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
**Status:** Open — **mechanism shipped behind flags, default OFF (wave 7)**; both open decisions closed; the
σ_focus arms RAN and returned a **null** — the bank cannot exercise this defect · found 2026-08-02 reproducing a
3800 mm sweep that yielded four dead frames

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

**DESIGNED AND SHIPPED BEHIND FLAGS 2026-08-06 (wave 7); the σ_focus arms are wired and OWED.** Spec:
[`docs/synthetic-af-bank-followups-wave7-design.md`](synthetic-af-bank-followups-wave7-design.md) §2.

`StepSizeRecommender.Recommend` takes an optional `SweepDetectability` (per-frame star counts, focuser positions,
recovery flags, `NHard`) and computes `half_width = min(W_3x, max(W_detect, floor))`. Absent ⇒ **byte-identical**,
pinned by a test, so one binary is both arms ([F41](#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)).
On this entry's own 3800 mm run: `min(6221, 5310) = 5310` → step **1517**. *(This entry quotes 1060–1330 for the
same run; that divides by 4–5, i.e. it is the EXECUTED-sweep figure — arm S gives 5310/4.375 = 1214. Both are unit
tests now, and separating them is why there are two arms.)*

**The two decisions this entry left open, both closed:**

- **(a) Should focus-recovery steps extend the sweep past the band?** *Yes for the 3× band — that is their purpose,
  and `JRun` already exempts them from its hard floor — but never past detectability.* Which of the two the SHIPPED
  default should be is settled by MEASUREMENT: sizing the base step for a conditional path costs **20% of the lever
  arm on every successful run** (divisor 3.5 → 4.375 at 4 offset + 1 recovery), and that is not obviously worth
  paying. So the recommendation now reports `MaxUsefulHalfSpan` = the outermost frame that measured anything, and
  arm S (`--step-size-for-executed-sweep`) is measured against arm D (`--step-detect-bound`) with a rule fixed in
  advance. **Clamping the engine's recovery-step placement to `MaxUsefulHalfSpan` is engine-side and is NOT done
  here** — filed as its own followup.
- **(b) What floor does `W_detect` need?** Two guards. Fewer than `MinFramesForDetectHalfWidth = 3` qualifying
  non-recovery frames ⇒ `W_detect` is **NaN (no bound), never 0** — a thin run has shown that it did not detect
  anything out there, not that nothing is detectable, and only the first reading is safe. And
  `MinHalfWidthSampledHalfSpanMultiple = 0.5`, **the mirror of the existing `MaxHalfWidthSampledHalfSpanMultiple =
  1.5`**: the recommender has always bounded how far one run may WIDEN and had no bound on how far it may NARROW.
  At 0.5 the worst one-run shrink is 0.57×, so a pathological run halves the step and converges over runs.

**Acceptance rules, pre-registered.** Arm D ships if median σ_focus over the bank is no worse than the control by
more than 2%, no dataset regresses more than 20%, and the A3 final-step assertion passes on at least as many cells.
Arm S ships instead of D only if it beats D by more than 5% median. `BaselineJ` identical across C/D/S before any
σ is read (F41's free check); and if arm D's `W_detect` equals the sampled half-span on most datasets it is
reporting the sweep's edge rather than detectability and the arm is void.

**`optimize` deliberately stays on the pre-F18 recommender**, so every F32/F35 arm's reported step remains
comparable; the arms run through `synth-validate`, which is the recommender's own convergence driver.

**A DEFECT IN THE RULE, caught by this entry's own pre-registered instrument check.** The check said: *if
`W_detect` equals the sampled half-span on most datasets it is reporting the sweep's edge rather than
detectability, and the arm is void.* It came back equal on **all 20** datasets. `W_detect` is bounded above by the
sampled half-span **by construction** — it is the outermost SAMPLED position that cleared `NHard` — so when no
frame falls below the floor, returning the sweep's own edge turns `min(W_3x, W_detect)` into *"never recommend a
sweep wider than the one you just took"* on every healthy run. That is a cap on WIDENING, a different rule, and one
the recommender already has. **Fixed: no starved frame ⇒ NaN ⇒ no bound** — "unmeasurable is not zero", at the
other end. The first arm run was discarded and re-run on the corrected binary.

**ARMS RUN 2026-08-06 (wave 7). The rule fires for arm D, against arm S — and arm D's pass is VACUOUS.**
28 (dataset, scenario) cells over `D05`/`D06`/`D09`/`D12`/`D15`/`D16` × S0/S1/S2 (+S3/S6 where applicable), three
arms, one binary, `--settings` pinned. Round-0 control: **0 violations**. Assertion verdicts **identical across all
three arms** (Pass 82 / Fail 2; the 2 are in arm C too — pre-existing S2 behaviour, F25/F34).

| rule (fixed in advance) | outcome |
|---|---|
| arm D ships if median σ_focus no worse than C by >2%, no dataset worse by >20%, A3 count not reduced | **FIRES** — median ratio 1.0000, 0 worse, counts identical |
| arm S ships instead of D only if it beats D by >5% median | **DOES NOT FIRE** — median 1.0000, and S is >20% WORSE on 2 cells |

**But arm D is byte-identical to the control on 26 of 28 cells.** The bound bound in exactly ONE cell (`D16` S2),
whose σ_focus is **NaN** (degenerate fit) — so its only active cell produced no readable acceptance metric.
**Zero** cells improved. The rule fires because the arm is INERT, not because it is good.

**Consequence: the mechanism ships, the flag stays default OFF.** This entry asked for bank validation with
σ_focus as the acceptance metric and the bank returned a **null**: at the derived exposures the detectability limit
is never reached (edge-frame headroom 3–2746 stars against a floor of 3), and this entry's own evidence came from a
real 3800 mm rig at 14 s whose wings went starless — which no dataset here reproduces. Turning it on for every
user on a vacuous pass would be reading a null as a green light. **Adoption needs a rig where the defect
reproduces**, and wave 7 shipped the instrument that identifies one (`StepSizeRecommendation.MaxUsefulHalfSpan`).

**What the bank cannot do, recorded as a property of the bank:** its exposure derivation targets `NTarget = 20`
stars over the gate on the MEDIAN frame, which on these fields implies ≥ `NHard` at the edge. A dataset that could
exercise this entry has to be built deliberately — starved on purpose — or borrowed from the real bank.

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

**Re-measured 2026-08-03 (F23 wave 1) — the manifestation is seed-dependent.** On the re-run D05 S2 fits at
**R² = −0.106** (still negative — no usable curve) and the recommender answers **140**, i.e. it *held* the step
rather than widening it to 240. The specific 4×→7× over-reach in the evidence above did not recur.

The code gap this entry names is untouched, so the entry stands: `Recommend` still has no fit-quality gate, and
whether a degenerate fit happens to hold or to widen is left to the noise realization. But the headline number
is not the typical case.

**Found while re-measuring — a fourth harness-calibration bug.** That same D05 S2 run is scored
`converged: true`, `stoppedReason: "converged (round applied nothing)"`, with `finalStepSize 140` against
`stepBehavioral 35` — four times too wide. Assertion A3 correctly FAILs it (`final step 140 outside [21,56]`),
but the convergence flag reads PASS. A degenerate fit produces a no-op recommendation, and the loop reads
"nothing changed" as "converged". This inflates convergence counts on exactly the runs that are most broken,
and belongs with the three calibration bugs already recorded in
[`docs/synthetic-af-bank-baseline-results.md`](synthetic-af-bank-baseline-results.md).

**Next step.** Gate the recommendation on fit quality. The R² is already in hand at the call site; a fit below
some floor should either hold the current step or shrink it, never widen it. Reproduce with
`synth-validate --datasets D05_tec140_1000mm --scenarios S2 --max-rounds 4`. Separately: make "converged" require
being inside the tolerance band, not merely unchanged.

**Wave 7 bounds the MAGNITUDE and leaves the gap (2026-08-06).**
[F18](#f18--step-size-is-sized-by-curve-geometry-alone-so-the-sweep-outruns-what-the-detector-can-see)'s
`W_detect` is measured and can never exceed what the sweep sampled, so `min(W_3x, W_detect)` bounds the 4×→7×
over-reach on any run whose star counts are populated. That is not this entry's fix: at R² = −0.223 the
recommender still ANSWERS rather than recognising it has no curve, and a degenerate fit that happens to be bounded
is still a degenerate fit being trusted. **The fit-quality gate, and the "converged means unchanged" half, are
both still owed.**

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

**Re-measured 2026-08-03 (F23 wave 1) — the LIVELOCK does not reproduce; the deferral does.** Same harness,
same datasets, `--max-rounds 4 --max-evals 120`:

| run | recorded evidence | re-measured |
|---|---|---|
| D08 S1 | step held at 21 for **4 rounds**, target 82 | 21 → 21 → 36 → **62**, converged in 3 rounds |
| D12 S6 | 35 → 60 → 60 → 74, target 141 | 35 → 60 → 60 → 80, still not converged at round 4 |

The binning-first deferral is still visible and still costs a round — D08 applies step 21 twice, and D12
applies 60 twice — but the step then updates and D08 converges. The unbounded stall does not recur. The
difference is that the landed Sensitivity varied round to round here (16.7 / 15.7 / 10 on D08) rather than
sitting at the floor, so the binning recommendation settled instead of being persistently wrong — which is
consistent with F26 being downstream of [F22](#f22--detection-binning-is-a-hard-threshold-on-a-measurement-that-under-reads-so-boundary-rigs-get-the-wrong-factor)/F23
rather than an independent defect.

Also measured: with the F23 marginal-SNR term enabled, **D12 S6 converges** (3 rounds, final step 103) where
shipping does not (4 rounds, final 80, not converged). So the objective fix helps this loop even though it
fails its own precision gates.

> **The "fails its own precision gates" half is REFUTED, re-scored at `/5` (2026-08-05, wave 5).** This was the
> single clause [F36](#f36--which-pre-wave-2-entries-actually-rested-on-the-broken-precision-metric-audited-and-it-is-none-of-them)
> left open across F1–F8/F18/F21/F25/F26. Arm (a)'s landings scored with `golden eval` (which applies the `/5`
> `TruthProtection` repair), against the control arm `H_A` on the same three datasets — the three arm (a) was
> recorded as failing the ≥ 0.90 precision gate on:
>
> | dataset | control `H_A` | **arm (a)** | arm (a) as recorded at `/3` |
> |---|---|---|---|
> | `D09_c14_3800mm` | 0.958 | **1.000** | 0.451 |
> | `D12_c14_585_afbin2` | 1.000 | **1.000** | 0.653 |
> | `D15_cdk20_3454mm_e47` | 0.968 | **1.000** | 0.531 |
>
> **Arm (a) clears the gate on all three, and beats the control on two of them.** The gate failure was entirely
> an artifact of the pre-[F31](#f31--synthetic-bank-precision-is-not-exact-the-golden-omits-real-stars-and-they-score-as-false-positives)
> metric. What the term actually cost is **recall** — 0.983 → 0.932 on D09, 0.942 → 0.900 on D12, 0.965 → 0.917
> on D15 — the same inversion F31 found: both mechanisms were suppressing *real detections*, not junk.
>
> So F26's convergence result stands **and** its caveat does not: the marginal-SNR term helped this loop without
> failing any precision gate. It remains "won't fix as written" under [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall)
> on F31's grounds — a precision defect one thirtieth the size of the artifact that hid it does not justify a
> term — and this clause is now closed rather than unverified. Reproduce: `D:\hf_w5\f26\run.sh`.

**Status revision.** The one-round deferral cost is real and worth the guard below; the "indefinitely" in this
entry's title is not supported by re-measurement and should be read as "for at least one round, unbounded in
principle".

**Next step.** Bound the deferral: if the binning recommendation has not changed the applied value for N
consecutive rounds, stop deferring and let the step update proceed. Fixing F22 would also dissolve this, but the
livelock is worth guarding against independently — any future oscillating recommendation would reproduce it.

**Explicitly NOT covered by wave 7's F18 work (2026-08-06).** F18, F21, F25 and F26 are the same component read
four ways, and a passing bank must not be read as four entries closed. This one is the wizard's binning-first
update ORDERING, downstream of [F22](#f22--detection-binning-is-a-hard-threshold-on-a-measurement-that-under-reads-so-boundary-rigs-get-the-wrong-factor),
not the recommender's arithmetic. Untouched, and the guard above is still owed.

### F23 — ~~The optimizer objective has no precision term, so it trades precision away for marginal recall~~
**Status:** **Won't fix as written** (2026-08-03, wave 2 — evidence base void; the real effect is ~1/5 the size and the axis is recall, see F32/F33) · found 2026-08-02

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

**Wave 1 executed 2026-08-03 — BOTH candidate mechanisms measured, both REJECTED.** Plan:
[`plans/af-recommender-hardening-plan.md`](../plans/af-recommender-hardening-plan.md); full results:
[`docs/f23-objective-precision-term-results.md`](f23-objective-precision-term-results.md). Three arms, one
binary, arm selected by flag; scored by `bank-verify` config A against a same-session control:

| arm | precision < 0.90 | recall@high drop > 0.02 | σ_focus worse > 20% |
|---|---|---|---|
| H — unmodified HEAD (control) | 8/17 | — | — |
| b — hard floor on the searchable Sensitivity range | **9/17** | | |
| a — `SMarginalSnr` objective term | **7/17** | 3 | 3 |

**(b) is worse than doing nothing.** Forcing Sensitivity to 6 breaks four datasets that were healthy — D13
0.985 → 0.860, D14 0.948 → 0.739, D04 0.977 → 0.859 — while helping three. Restricting the *domain* does not
remove the incentive to buy star count; the search simply loosens other gates to win the stars back, admitting
junk through a different door. This is the strongest available argument that the fix belongs in the objective.

**(a) works where it can fire, and is structurally escapable.** Real gains (D09 0.451 → **0.944**, D17 0.465 →
0.735, D10 0.547 → 0.814) but no gate cleared. The reason is not a mis-set constant: the gate guarantees
`sensitivity >= PeakResponse × StarClippingMultiplier`, and **both are searchable curated axes**, so the
optimizer can lift the statistic's own lower bound above the floor and make the penalty unable to fire while
the false positives remain. Measured: D12 landed at `1.0 × 6.25 = 6.25` and D15 at `1.0 × 6.75 = 6.75`, both
just past the 6.0 floor, precision stranded at 0.659 and 0.587. A floor of 8 would be escaped at 8, so the
budgeted retune round was deliberately not spent.

**Also learned:** D08 lands at Sensitivity 8 — above any floor, term legitimately inert — with precision
0.748. So a floored Sensitivity is **not** the only source of false positives, and this entry's mechanism
section describes part of the problem, not all of it.

`MarginalSnrStrength` therefore ships at **0** (inert; J bit-identical to before). The implementation is kept,
tested and flag-selectable so the next attempt starts from a measured position.

> ## ⚠ THE EVIDENCE BASE ABOVE IS VOID (2026-08-03, wave 2 — re-measured at `afbank-verify/5`)
>
> Every precision figure in this entry came from a metric that charged a false positive for each real star the
> golden policy had dropped ([F31](#f31--synthetic-bank-precision-is-not-exact-the-golden-omits-real-stars-and-they-score-as-false-positives)).
> Re-measured on the same landings:
>
> | | as recorded above | re-measured at `/5` |
> |---|---|---|
> | C0@nc2 precision, worst of 17 | 0.942 | **1.000** |
> | Config A precision, worst of 17 | 0.451 (D09) | **0.910** (D10) |
> | Config A datasets below 0.90 | 6 | **0** |
> | D09 "bought +0.034 recall for −0.542 precision" | — | +0.034 recall for **−0.042** precision |
>
> **The headline claim — "it gains a few points of recall and gives up half the precision" — is false.** The
> detector's real false-positive rate on this bank is 0–9%, and the reading is a measurement rather than a
> saturated metric: the null control (the same detections translated with wraparound) scores 0.000–0.169, and
> `truthViolations` is 0 on all 51 config rows.
>
> **What survives.** The *direction* is real and lands on exactly the datasets named here — D10 0.910,
> D09 0.958, D17 0.959, D15 0.968, every one a long-focal-length rig landing at Sensitivity 0. That is roughly
> one fifth the size of the artifact that hid it. Far too small to justify an objective term; not zero either.
> Both wave-1 mechanisms are correctly rejected, but for a reason the entry does not give: they were suppressing
> **real detections**. On D09 the control arm found 510 stars at 97.8% true precision where the term-on arm
> found 231.
>
> **And a term could not have helped anyway.** See [F32](#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains):
> `J` already sits at 0.98–0.999 before the search starts, so any new term competes for an exhausted fourth
> decimal place. That is the better explanation for why mechanism (a) produced real precision movement and still
> cleared no gate.
>
> **Where the problem actually is.** [F33](#f33--the-synthetic-bank-does-not-reproduce-the-real-banks-optimizer-failure-mode):
> on the REAL bank the optimizer drives Sensitivity *up* to 31–50 and sheds 45% of recall at the median, the
> opposite sign of the synthetic behaviour this entry describes. Recall, not precision, is the axis with a
> defect on it.

**Next step (revised again).** Do not build a precision term. Read F32 and F33 first: the objective's scale is
exhausted, and the failure mode this entry describes does not transfer to real rigs. The "second false-positive
source at healthy Sensitivity" (D08 at 8 with 0.748, D16 at 2.5 with 0.744) also dissolves — both score **1.000**
at `/5`. Earlier framings retained below as the record.

**Next step (original).** Add a precision-like term to the objective. It cannot be true precision on real data
(that is the whole problem), but two proxies are already available: the golden-independent
false-positive *proxies* the detector already collects, and — for tuning and regression — this bank,
where precision is exact. Any change wants scoring against **both** banks, since the synthetic one can
now measure exactly the quantity the real one cannot. Related: [F4](#f4--the-objective-has-no-sensor-model-term)
is the same shape of gap (a term the objective omits), and [F11](#f11--precision-is-a-lower-bound-on-runs-whose-faint-tier-was-budget-truncated--re-run-these-with-more-montages) is why this went unseen.

### F24 — ~~Donut detection costs precision even where donuts exist, and badly where they do not~~ → it costs RECALL where it is not needed
**Status:** Open — **remaining step ANSWERED (wave 5): do NOT neutralize the master's two defaults**; the fix is
a condition on star size, which routes into [F32](#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains).
Previously **restated** (2026-08-03, wave 2 — the precision claim is refuted; a recall claim replaces it) · found 2026-08-02 on the synthetic AF bank

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

> ## ⚠ REFUTED AND RESTATED (2026-08-03, wave 2)
>
> **The precision claim above is an artifact of the pre-[F31](#f31--synthetic-bank-precision-is-not-exact-the-golden-omits-real-stars-and-they-score-as-false-positives)
> metric and does not survive re-measurement.** This is the one wave-1 followup that was never re-measured,
> because the config-B prepass output no longer existed; it has now been re-derived and scored at
> `afbank-verify/5`.
>
> | dataset | ε | C0@nc2 | **B, as re-measured** | B, as this entry recorded it |
> |---|---|---|---|---|
> | **D13_apo200_1800mm** | **0** | 1.000 | **1.000** | 0.653 |
> | D14_cdk14_2563mm_e47 | 0.47 | 1.000 | **0.997** | 0.671 |
> | D12_c14_585_afbin2 | 0.34 | 1.000 | **0.997** | 0.506 |
> | D09_c14_3800mm | 0.34 | 1.000 | **1.000** | 0.448 |
>
> **Config B's precision never falls below 0.991 on any of the 17 datasets**, and on the ε=0 control it is a
> flat 1.000. There is no precision cost to find, so there is nothing for the "score the nine donut-gated axes"
> next step to attribute.

**What is actually true, measured at `/5`: donut detection costs RECALL where it is not needed, and buys a
large σ_focus improvement almost everywhere.** Config B against C0@nc2:

| dataset | ε | Δrecall@high | σ_focus C0 → B |
|---|---|---|---|
| D16_esprit550_ha3 | 0 | **−0.147** | 0.615 → 0.315 |
| D04_esprit_550mm | 0 | **−0.113** | 0.232 → 0.059 |
| D13_apo200_1800mm | 0 | −0.014 | **2.352 → 0.291** |
| D03_redcat_250mm | 0 | **+0.138** | 1.183 → 0.341 |
| D11_rc10_585_afbin2 | 0.47 | +0.030 | 0.101 → 0.444 |
| D09_c14_3800mm | 0.34 | +0.078 | 1.141 → 0.644 |

D13 is the sharpest reversal. This entry cited it as the case that proved donut detection fires on things that
are not donuts and does damage; re-measured, D13 keeps 1.000 precision, gives up 0.014 recall, and its AF fit
**improves eightfold** (σ_focus 2.352 → 0.291). That is [F5](#f5--donut-detection-halves-σ_focus-on-a-run-with-no-donuts)
happening again, not a defect. `donutEffect` over the 17 A/B pairs is now `donutHelpedAF: 8`,
`donutHurtSensor: 6` — still no clean win, but no longer the "costs precision everywhere" picture.

**Why it matters, restated.** Donut detection remains a bootstrap input gating nine optimizer axes with no
product signal recommending it ([F1](#f1--the-donut-heuristic-misses-small-donuts) is the heuristic missing
*small* donuts). The stakes on getting that signal right are unchanged — but the cost of enabling it wrongly is
**lost faint stars on a well-sampled refractor**, not admitted junk, and a fix aimed at the wrong one of those
would have been wasted.

**Next step.** Find what the recall loss on D16 and D04 is: both are unobstructed 550 mm refractors, so the
donut-gated relaxations should be inert on them and are not. Score the nine gated axes against ΔRECALL on those
two datasets. Note also that the two survivors of the original entry are untouched by all of this: C0 is *not*
broken on the synthetic donut datasets (unlike the real bank's `Panos`/`mufti`/`LinwoodFocus`), and there is
still no signal that recommends the master.

**Answered 2026-08-03 (wave 3): it is not any of the nine axes — it is what the MASTER TOGGLE turns on by
default.** Reading the two landings before running anything settled the framing: **the search moved none of the
nine.** D16 landed every one at its default; D04 moved only `DefocusDistortionSizeReference` 30 → 28.75, and
that one is provably inert (bit-identical recall). A per-axis ablation would have returned nine nulls.

Arms hold every parameter at defaults and vary only master-gated mechanisms. `golden eval`, ~40 s each,
precision **1.000** / FP **0** throughout:

| arm | D16 recall@high | D04 recall@high |
|---|---|---|
| master OFF | **0.821** (151/184) | **0.762** (16853/22109) |
| master ON | 0.793 | 0.723 |
| + structure boost 0 | **0.821** (all recovered) | 0.732 (23%) |
| + morph-close 1 | 0.799 (21%) | 0.758 (90%) |
| **+ both** | **0.821 (151/184)** | **0.762 (16853/22109)** |

Neutralizing both recovers the master-OFF recall **exactly — the same star counts, not merely the same ratio**,
on both datasets. The two mechanisms are:

1. **A silent +2 structure boost.** Master ON with `DefocusAwareStructure` OFF still applies
   `DonutDefaultStructureLayerBoost = 2` (`StarDetector.cs:610-615`), taking `StructureLayers` 4 → 6. More
   wavelet layers subtracted erases more small-star structure. Dominant on D16.
2. **A 5 px morph-close.** `DonutMorphCloseSize` **defaults to 5 and is neutral at 1**, so the master runs a
   morphological close that merges compact stars. Dominant on D04.

**Caveat:** these arms run at default shared params (NC 4.0), so −0.028 / −0.039 is the master's own cost, not
the −0.147 / −0.113 above (NC 2, against B's full landing). The master residue is a fifth to a third of it; the
rest is B's different landing on the *shared* axes.

**And the mechanism is [F32](#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains).**
Both are reachable by the search — `DonutMorphCloseSize` is a curated axis and `StructureLayerBoost` becomes
settable once `DefocusAwareStructure` flips on. The optimizer never neutralizes them because `J` does not pay
for recall. **This is not a missing knob; it is a saturated objective, measured on two specific rigs.**

**Remaining next step.** Consider defaulting the master's structure boost and morph-close to neutral on runs the
donut heuristic did not flag, or making the objective pay for recall. Both are behaviour changes, so per
[F33](#f33--the-synthetic-bank-does-not-reproduce-the-real-banks-optimizer-failure-mode) they must be scored on
**both** banks. Reproduce: `D:\hf_w3\run_f24_arms.sh` and `run_f24_arms2.sh`.

**ANSWERED 2026-08-05 (wave 5): do NOT default them to neutral. The wave-3 conclusion was drawn from two
datasets and does not generalize to twenty.** Five arms × all 20 synthetic datasets, `golden eval` at default
shared params with the master forced on, ~55 min total and **no code change** — the overrides already exist
(`GoldenEvalRunner.cs:461-479`). Precision is **1.000 on every arm and every dataset**.

**First, wave 3 reproduces exactly.** D16 masterOFF 0.821 → shipping 0.793, and `boost0` restores 0.821; D04
0.762 → 0.723, and `close1` restores 0.758. Neutralizing **both** recovers master-OFF recall on **19 of 20**
datasets (D07 exceeds it). The mechanism claim is confirmed, and now on the whole bank.

**But the master's two mechanisms are not a uniform cost — they are a rig-dependent trade:**

| direction | datasets | Δrecall (masterOFF − shipping) |
|---|---|---|
| master **HURTS** recall | 12 — D01–D05, D07, D10, D13, D16, D18, D19, D20 | +0.007 … **+0.067** |
| master **HELPS** recall | **3 — D06, D09, D14** | −0.005 … **−0.067** |
| no effect | 5 — D08, D11, D12, D15, D17 | 0.000 |

On all three of the datasets where it helps, `boost0` gives the gain back **exactly** (boost0 = masterOFF to
3 dp), so **the +2 structure boost is what buys it** and the morph-close is inert there. And the three are
`D06_sparse_1000mm`, `D09_c14_3800mm`, `D14_cdk14_2563mm_e47` — long focal length and sparse, i.e. **large
defocused stars**, which is precisely what a coarser wavelet residual exists to preserve. The story is coherent
in both directions: the boost saves big defocused stars from the subtraction and erases small-star structure, so
it helps where stars are large and hurts where they are small and well sampled.

**So the pre-registered criterion FAILS** (recall Δ ≥ −0.005 on *every* dataset): both-neutral costs
**−0.067 on D09**, −0.024 on D06 and −0.005 on D14. A blanket default change takes recall away from exactly the
rigs the feature was built for.

**What the fix actually is.** Not a default — a *condition*. The knob wants to depend on star size, and the
optimizer can already reach it (`DefocusAwareStructure` is a curated axis, and flipping it on makes
`StructureLayerBoost` settable, so `DefocusAwareStructure=true, boost=0` is a reachable neutral point). It never
goes there because `J` does not pay for recall — which is [F32](#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains),
and F32's constraint is the general form of this fix.

**Note the keep floor does NOT dissolve this.** The seed has the master OFF, and the master's own cost is
≤ 0.067 of recall — a candidate flipping it on stays feasible at any floor ≤ 0.9. F32 bounds catastrophic
shedding; it does not price a 4% one. The two entries are independent.

Reproduce: `D:\hf_w5\f24_arms.sh`, analysed by `D:\hf_w5\analyze_f24.py`.

> **The suspect D01/D02 rows of that table, resolved (wave 6).** `D02`'s frames turned out to be unaffected
> (bit-identical after the F44 fix), so its row never needed replacing. `D01`'s did, and re-running all five arms
> on the corrected field changes nothing that matters:
>
> | arm | D01 recall@high, old (truncated) | **D01, new (full field)** | TP, new | FP |
> |---|---|---|---|---|
> | `masterOFF` | 0.129 | **0.127** | 19039 | 0 |
> | `shipping` | 0.115 | 0.114 | 17636 | 0 |
> | `boost0` | 0.119 | 0.119 | 17988 | 0 |
> | `close1` | 0.126 | 0.125 | 19004 | 0 |
> | `both` | 0.129 | **0.127** | 19039 | 0 |
>
> The master still **hurts** on D01 (masterOFF − shipping = +0.014 old, +0.013 new), `both` still recovers
> master-OFF recall exactly, and precision is still 1.000 with zero false positives. The wave-5 verdict turned on
> D06/D09/D14 — none of which was ever suspect — and is untouched.

### F19 — The exposure recommendation is decided by the 20 brightest stars, so a rich field can never earn one
**Status:** **(a) refuted (wave 8) · (b) DONE (wave 8) · (c) RESOLVED "the floor stays", free, no re-render
(wave 9) · THE REMAINDER IS FIXED (wave 9): the wing rejected fraction, chosen by a rule fixed before it, and the
reason both earlier fixes failed is now a THEOREM about the gate.** Population check across both banks still owed
· found 2026-08-02 deriving expected-optimal exposures for the synthetic AF bank

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

> **These two numbers have DRIFTED and are no longer what the derivation produces (re-measured 2026-08-06, wave 7).**
> `synth-bank --dry-run` over all 20 datasets on `develop` @ `761b9b1`:
>
> | claim as filed | measured today |
> |---|---|
> | the 0.5 s floor for **12 of 17** | **8 of 17** (`D01`–`D05`, `D07`, `D13`, `D14`) |
> | `D16_esprit550_ha3` derives **0.5 s** | **2 s** (band 1.079–3.102 s) |
>
> The cause is **not identified** — it lies somewhere between this entry's filing (2026-08-02) and now, and the
> candidates are [F44](#f44--the-synthetic-camera-queries-the-catalog-for-at-most-one-cell-so-a-wide-field-is-rendered-starless-outside-a-small-central-patch)'s
> catalog-query fix (which changes on-frame star counts on exactly the wide fields this claim rests on) and the
> derivation's own move to a median-across-sweep-frames statistic. Recorded rather than chased: the entry's
> ARGUMENT is unaffected — a 3 nm Hα field 64× down on flux earning 2 s is still "sized by field richness rather
> than by photon starvation" — but its specific numbers must not be quoted again without re-measuring. It also
> moves `D16` above the `MeaningfulExposureAboveFloorSeconds` gate, so it now qualifies for scenario S3, which it
> did not when this entry was written.

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

**DECIDED 2026-08-06 (wave 7): `NTarget = 20` STAYS, and here is the position.** Full reasoning in
[`docs/synthetic-af-bank-followups-wave7-design.md`](synthetic-af-bank-followups-wave7-design.md) §1. Three legs:

1. **The recommendation inverts the objective's own knee.** `S_stars` saturates at `nMedian >= NTarget`; past 20
   stars per frame the optimizer stops rewarding star count at all, so an exposure recommendation sized from a
   fainter population would be buying stars the objective is indifferent to. `Recommend` reads `NTarget` from the
   caller's `ObjectiveConstants` rather than a literal, so an objective retune moves both together.
2. **The named alternative is PINNED BY THE GATE and cannot work as a control signal.** "The SNR at the star count
   the fit actually consumes" is the faintest accepted star's SNR. A candidate is accepted iff its gate statistic
   exceeds `max(Sensitivity, StarDetector.InertSensitivityBound)`, and this advice is surfaced ONLY when
   `Sensitivity <= 1.0` (`OptimizationSummary.HasLowStarSignal`) — so the binding bound is the inert one, **1.5**
   at shipped defaults. A longer exposure does not raise that number; it admits MORE faint stars whose faintest
   again sits at the bound. `t·(TargetSensitivity/1.5)² = 44·t` therefore saturates `MaxExposureFactor = 4` on
   every run, forever, and never converges. The measured variable would be a fixed point of the actuator's own
   gate rather than a measurement of the sky.
3. **The half of this entry that is a real defect already has its own mechanism, and it post-dates the entry.**
   PR #159 shipped THREE verdicts, not one: `ExposureIsNotTheLimit`, `StarCountIsTheLimit` (a fixed ×2 PROBE with
   a stopping rule) and `StarFieldIsExhausted`. The "are there enough stars?" question is answered there, by a
   probe rather than a formula, precisely because whether more exposure reveals more stars depends on a luminosity
   function one sweep cannot measure. What is left of this entry — a rich field whose WING frames are starved — is
   a sweep-geometry defect owned by [F18](#f18--step-size-is-sized-by-curve-geometry-alone-so-the-sweep-outruns-what-the-detector-can-see),
   and the product already says so for the adjacent signal (`FlatRejectedCount` is documented as *"narrow the
   sweep, never expose longer"*).

**What can still refute this, pre-registered.** The position makes a claim about the world: *a field that already
carries 20 bright stars gains nothing material from more exposure*. **Arm E** tests it — `D16_esprit550_ha3` (this
entry's own poster child) and `D02_rich_135mm` rendered at 0.5/1/2/4/8 s, fixed detector settings, σ_focus per
rung. **Rule fixed in advance: if any longer rung improves σ_focus by more than 20% relative to the
derived-exposure rung on either dataset, this entry REOPENS.**

> ## THE RULE FIRED. THE "no change" POSITION ABOVE IS REFUTED, AND THIS ENTRY IS OPEN AGAIN (2026-08-06, wave 7).
>
> `D02_rich_135mm` — a **rich** wide field, 2746 stars still above the gate at the sweep's outermost frame, whose
> derived exposure is the **0.5 s clamp floor**:
>
> | exposure | σ_focus (current) | σ_focus (optimized) | J best | min stars/frame |
> |---|---|---|---|---|
> | **0.5 s (derived)** | 0.23211 | **0.10927** | 0.99633 | 23 |
> | 1 s | 0.19828 | 0.09125 (+16.5%) | 0.99720 | 34 |
> | 2 s | 0.18119 | 0.09482 (+13.2%) | 0.99708 | 58 |
> | 4 s | 0.16754 | **0.08182 (+25.1%)** | 0.99770 | 63 |
> | 8 s | 0.15238 | **0.06109 (+44.1%)** | 0.99853 | 37 |
>
> σ_focus improves **monotonically** on the current-settings column and by **44% at 8×** on the optimized one.
> A rich field demonstrably DOES gain from more exposure, and the shipped recommender says nothing about it.
>
> **And the diagnosis is sharper than this entry's own framing.** `SensitivityIsAtFloor` is **False at every
> rung** — the landed Sensitivity is 9–49, nowhere near the floor — so the exposure block is **never surfaced at
> all** on this dataset. The `NTarget = 20` statistic is not merely answering the wrong question here; on this
> population it is **never evaluated**, because the block's TRIGGER (`OptimizationSummary.HasLowStarSignal`,
> `Sensitivity <= 1.0`) gates it out first. The gap is upstream of the statistic.
>
> **The 20-star derivation is NOT uniformly wrong, which matters for the fix.** `D16_esprit550_ha3` — the
> narrowband case this entry opens with — has its σ_focus MINIMUM exactly at its derived 2 s (0.06423), and gets
> **worse** at 4 s (0.12034) and 8 s (0.14828). So the derivation nails D16 and under-serves D02. The
> distinguishing feature is that **D02's derived exposure sits at the `MinExposureSeconds = 0.5 s` clamp floor** —
> the arithmetic asked for less than the minimum, i.e. it saturated and carried no information — while D16's is a
> free solution inside the band.
>
> **The confound, declared before the arm ran and still true:** more exposure adds stars AND measures the existing
> ones better; this arm cannot separate them. It does not need to. The question was *"should a rich field ever
> earn an exposure recommendation?"* and the answer is **yes**.
>
> **Next step, and it is not "change NTarget".** (a) The trigger, not the statistic, is what silences this
> population — decide whether the exposure block should also fire when the landed Sensitivity is healthy but
> σ_focus is poor, or when the derived exposure saturated at a clamp. (b) A recommendation whose arithmetic hits
> either clamp should report that it saturated rather than reporting the clamp as an answer. (c) Re-check
> `MinExposureSeconds = 0.5 s`: on `D02` it is the binding constraint and it is 16× below what the fit wants.
> Reproduce: `D:\hf_w7\remaining_arms.sh` (Arm E), scored by `D:\hf_w7\score_armE.py`.

> ## AND (a) — "widen the trigger" — IS ALSO REFUTED, by its own pre-registered test (2026-08-06, wave 8).
>
> Widening the trigger only helps if the block, once surfaced, says something TRUE. Nobody had checked, because
> **the harness could not**: `OptimizationDiagnosticRunner` recorded `ExposureRecommendation` as `null` whenever
> the gate was healthy, reproducing the product's blind spot instead of measuring it. Wave 8 computes it for every
> run — **gate the DISPLAY, never the MEASUREMENT** — and re-ran wave 7's five exposure rungs, which are still on
> disk, so **no re-render**.
>
> **Rule R5, fixed before the arm ran:** *if at `D02`'s derived rung the recommendation reports
> `ExposureIsNotTheLimit`, or asks for less than 2× current, the statistic is wrong for this population and (a)
> does NOT ship as a trigger widening.*
>
> | dataset | rung | σ_focus | **S_now** | **raw ask** | recommended | `ExposureIsNotTheLimit` |
> |---|---|---|---|---|---|---|
> | `D02_rich_135mm` | **0.5 s (derived)** | 0.10927 | **991.8** | **0.000 s** | 0.50 s = current | **True** |
> | `D02_rich_135mm` | 1 s | 0.09125 | 1226.3 | 0.000 s | = current | True |
> | `D02_rich_135mm` | 2 s | 0.09482 | 1864.4 | 0.000 s | = current | True |
> | `D02_rich_135mm` | 4 s | 0.08182 | 2308.8 | 0.000 s | = current | True |
> | `D02_rich_135mm` | **8 s** | **0.06109** (−44.1 %) | 1894.9 | 0.000 s | = current | True |
>
> **THE RULE FIRES.** The recommendation says "your exposure is fine" at **every rung across a 16× range** over
> which σ_focus improves 44 %. It is not wrong at one operating point a wider trigger might have caught — it is
> wrong everywhere on the axis it is being asked about. **Surfacing it would have published that answer to more
> users and looked like a fix.**
>
> **The control passes on every rung, which is what makes the reading usable.** `D16_esprit550_ha3` at 0.5 s
> (below its derived value) correctly asks for **1.5 s**; at its derived 2 s — exactly where its σ_focus minimum
> sits (0.06423) — it asks for nothing more; and at 4 s and 8 s, where σ_focus is measurably worse (0.12034,
> 0.14828), it again asks for nothing more. **The recommender is right wherever it can see and blind where it
> cannot.**
>
> **And the magnitude finally has a number.** Against `TargetSensitivity = 10`, `S_now` on `D02` measures **991.8
> to 2308.8 — 100× to 231× the target** — so `RawSeconds = 0.5 × (10/991.8)² = 5 × 10⁻⁵ s`. The statistic is not
> slightly wrong on this population; it is wrong **by four orders of magnitude**, and it saturates HARDER as
> exposure rises (992 → 1226 → 1864 → 2309), so it can never converge toward asking for more.
>
> **(b) is DONE, and half of it needed no code.** Harness: `SynthBankDerivations` now records
> `exposureRawSeconds` and `exposureClamp` (`none`/`floor`/`ceiling`/`not-derived`) as **fields**, states the
> saturation in the definition text, and `synth-bank --dry-run` prints the saturated set — which is (c)'s work
> list. A field rather than only prose, for the reason [F39](#f39--the-harness-records-a-detection-binning-the-run-never-applied-and-7-datasets-have-never-run-at-theirs)(a)
> added `DetectionBinningSource`: a reader diffs fields, nobody diffs a sentence. **Product: already correct** —
> `StarSignalCopy.DescribeExposureDerivation` already reports every clamp branch and names which bound bound it.
> Verified, not implemented.
>
> **What is left is not a trigger and not `NTarget`:** a statistic that can see the WING frames — σ_focus, or the
> wing-frame star count — which is a new mechanism rather than a repair to this one. **(c) is deferred to wave 9**
> with its re-render (moving `MinExposureSeconds` re-derives 8 datasets including `D14`, which wave 8's F39(b)
> adoption arm was simultaneously measuring).
> Reproduce: `D:\hf_w8\armX\arm_x.sh`.
>
> ### The saturated set, listed for the first time — and THIS ENTRY'S COUNT WAS LOW IN BOTH DIRECTIONS
>
> (b)'s reporting change produced it as a by-product. **13 of 20 datasets report a clamp rather than a derived
> value**, measured by `synth-bank --dry-run`:
>
> | clamp | datasets | the solve asked for → reported |
> |---|---|---|
> | **floor** (11) | `D02` **0.001 s → 0.5 s (500×)**, `D01`/`D03` 0.003, `D04`/`D18`/`D20` 0.004, `D19` 0.008, `D05` 0.014, `D14` 0.055, `D07` 0.066, `D13` 0.264 | all reported as 0.5 s |
> | **ceiling** (2) | **`D10_rc16_3250mm_sparse` 335.6 s → 30 s (11× SHORT)**, `D17_cdk14_oiii5` 40.4 s → 30 s | reported as 30 s |
>
> - This entry says *"the 0.5 s floor for 8 of 17"*. Measured: **11 at the floor** — those eight plus `D18`/`D19`/
>   `D20`, which the 17-dataset count never included.
> - **And 2 at the CEILING, which nobody had counted at all.** Running *out* of exposure is the same defect from
>   the other side and had no name until (b) printed it.
> - **`D02` makes this entry's case far more sharply than the entry does:** the arithmetic asks for **0.001 s**,
>   gets 0.5 s, and arm E measured that dataset improving 44 % of σ_focus at **8 s** — four orders of magnitude
>   above the ask.
> - **`D18`/`D19`/`D20` are floor-saturated**, which belongs in
>   [F32](#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains)'s
>   confirmation-arm write-up: its entire synthetic half sits on clamps rather than derived exposures. It does not
>   invalidate that arm (every φ arm sees the same frames) but it must be stated there.
>
> **This list IS (c)'s work list**, produced by (b) rather than by a separate investigation.

> ## THE REMAINDER IS FIXED (2026-08-07, wave 9), AND THE REASON BOTH EARLIER FIXES FAILED IS A THEOREM
>
> ### The gate floor theorem
>
> `StarDetector.InertSensitivityBound` proves every candidate reaching the Sensitivity gate satisfies
> `sensitivity > PeakResponse × EffectiveClipMultiplier`, and acceptance additionally requires it to exceed
> `StarDetectorParams.Sensitivity`. So **every accepted star's gate statistic strictly exceeds
> `EffectiveSensitivityGate = max(Sensitivity, InertSensitivityBound)`** — and therefore **ANY order statistic
> over accepted stars, at ANY rank, on ANY subset of frames, is bounded below by that gate.**
>
> **Corollary: when the optimizer lands a gate at or above `TargetSensitivity` (10), `ExposureIsNotTheLimit` is
> true BY CONSTRUCTION, whatever the sky contains.** `D02_rich_135mm` lands its effective gate at **16.7 / 50.0 /
> 34.3 / 10.0 / 14.0** across a 16× exposure ladder over which σ_focus improves **48 %**.
>
> **This is why both earlier fixes were refuted by their own pre-registered tests.** Wave 7's "change `NTarget`"
> and wave 8's "widen the trigger" each moved the rank or the display and left the POPULATION untouched. It is
> strictly stronger than wave 7's leg 2, which established only that the *faintest accepted star* is pinned by the
> gate; the theorem says every statistic over accepted stars is. **And it killed wave 9's own first candidate
> family before a line of it was implemented** — all four candidates were order statistics over accepted stars.
>
> ### What ships: the WING REJECTED FRACTION
>
> The candidates the gate **rejected** on the sweep's wing frames are the only population in a run that is not
> floored by the gate, and they are exactly the stars a longer exposure can convert.
> `ExposureRecommendation.WingRejectedFraction` = `rejected / (rejected + accepted)` pooled over the outer third
> of the non-recovery frames by distance from the fitted focus; `WingIsShedding` at ≥ 0.20.
>
> - **Pooled over a wing SET, not a worst frame** — which answers this class's own documented objection that a
>   worst-frame rule "would hand the entire recommendation to whichever single frame had a passing cloud".
> - **A PROBE (×2), not a formula**, following `StarCountProbeFactor`'s precedent: the run records HOW MANY
>   candidates were rejected out there, not what SNR they sat at, so there is nothing to derive a magnitude from.
>   **Fabricating one was measured and rejected** — a `(1/(1−f))²` form asked **1.25×** on `D16` at exactly the
>   exposure where its σ_focus is minimised, which is the control the whole statistic has to pass.
> - **It WIDENS rather than replaces:** `max(existing, probe)`. Both halves are load-bearing.
> - **NaN, never 0**, when the run cannot be placed on the wing axis. "We could not look" and "we looked and
>   nothing was shedding" must not be the same number, because the caller turns one of them into an instruction.
> - **Inert unless the data supports it:** a caller that does not populate the per-frame wing axis is
>   byte-identical to before.
>
> ### RULE W, fixed BEFORE the statistic was written, and applied to this binary's own ladder
>
> Validated on wave 7's exposure ladder, still on disk at `D:\hf_w7\armE\t{0.5,1,2,4,8}`. **No re-render.**
>
> | | W1 `D02`@0.5 s ≥ 2× | W2 `D16`@2 s < 1.25× | W3 converges | W4 `D16`@0.5 s asks | verdict |
> |---|---|---|---|---|---|
> | shipped statistic | ✗ 1.00× | ✓ | ✓ | ✓ | fires on NEITHER |
> | wing fraction, `(1/(1−f))²` magnitude | ✓ 4.00× | ✗ 1.25× | ✗ | ✗ | fires on BOTH |
> | wing headroom vs the gate | ✓ 2.00× | ✓ | ✗ | ✗ | — |
> | wing probe ALONE | ✓ 2.00× | ✓ | ✓ | ✗ | — |
> | **`max(shipped, wing probe)`** | **✓ 2.00×** | **✓ 1.00×** | **✓** | **✓ 3.00×** | **ADOPTED** |
>
> **Five of seven candidates failed, three of them AFTER passing W1** — the clause that looks like the whole point.
> On `D02` the adopted statistic asks 2× at 0.5 / 1 / 2 s and goes **silent at 4 s**, which is exactly where this
> binary's σ_focus minimum sits (0.05208).
>
> **The ladder MOVED between binaries** ([F54](#f54--f39bs-default-flip-moves-a-landing-at-a-resolved-factor-of-1-where-it-is-documented-as-a-no-op)),
> so the rule was applied to this binary's own σ values rather than to wave 7's — after checking that **all four
> clauses' premises survive on both ladders**. `D02` gains 48 % here against wave 7's 44 %; `D16`'s minimum is at
> its derived 2 s on both, and 4 s / 8 s are worse on both.
>
> **The verdict is reproduced END TO END by the shipped code**, not only by the offline scorer: re-running the ten
> rungs with `ExposureRecommender` itself gives the same four passes.
>
> **Tests: 8, four discriminating**, each confirmed by neutralizing — delete the probe ⇒ W1/W3/W4 fail; `max()` ⇒
> plain assignment ⇒ W4 alone fails; fire on any rejection ⇒ W2 alone fails. That also caught an overclaim in one
> of this wave's own test comments: the F28 conjunct is **redundant by construction** and fails nothing when
> removed, so both the code and the test now say so.
>
> **Still owed: the §3.5 population check** — the verdict measured across all 20 synthetic datasets and the real
> bank. It is blocked behind F32's confirmation arm, structurally rather than by scheduling: `optimize --per-run`
> writes back into the bank's run folders ([F15](#f15--optimize---per-run-overwrites-each-runs-stored-settings))
> and there is no flag to suppress it, so a population pass while that arm is in flight would race it on every
> folder. Reproduce: `D:\hf_w9\wing2\`, `D:\hf_w9\wing\score_wing.py`.

> ## (c) RESOLVED 2026-08-07 (wave 9): THE FLOOR STAYS, THE CEILING STAYS, AND NOTHING RE-RENDERS
>
> (c) was filed as expensive — moving `MinExposureSeconds` re-derives and re-renders 11 datasets. **The check
> that decides whether to spend that is free, and it was run first.** Rule F19c, fixed before it ran: *(c)
> resolves as "the floor stays" unless a floor-clamped dataset can be shown, from data already on disk, to have
> its σ_focus minimum BELOW 0.5 s.*
>
> **The floor. Every one of the 11 clamped datasets asks for LESS than 0.5 s**, so a lower floor moves all eleven
> **down** — and the one dataset with a measured exposure ladder moves the other way: wave 7's arm E has `D02`
> improving **44 % of σ_focus at 8 s**, four orders of magnitude above its 0.001 s ask. **Lowering the floor makes
> `D02` worse; raising it is not what a floor is for** (a floor stops an absurdly short exposure, it does not
> supply an exposure the derivation failed to find). **The floor is the symptom, not the defect.**
>
> **And the free check produced a number the entry never had.** `synth-bank --dry-run` on the wave-9 binary
> reproduces the saturated set exactly (**13 of 20**, 11 floor + 2 ceiling), and printed beside the on-frame star
> count the 20th-brightest is drawn from it says what this entry has always ARGUED:
>
> | dataset | on-frame stars | raw ask | clamp |
> |---|---|---|---|
> | `D10_rc16_3250mm_sparse` | **26** | **335.6 s** | ceiling |
> | `D13_apo200_1800mm` | 177 | 0.264 s | floor |
> | `D07_rc10_2000mm` | 428 | 0.066 s | floor |
> | `D14_cdk14_2563mm_e47` | 640 | 0.055 s | floor |
> | **`D17_cdk14_oiii5`** | **799** | **40.4 s** | **ceiling** |
> | `D03` / `D20` / `D05` / `D02` | 969 / 1151 / 2346 / 3590 | 0.003 / 0.004 / 0.014 / **0.001** s | floor |
> | `D19` / `D04` / `D01` / `D18` | 4914 / 7480 / 19201 / **27084** | 0.008 / 0.004 / 0.003 / 0.004 s | floor |
>
> **Spearman ρ(on-frame star count, raw ask) = −0.68 over the 13.** The two CEILING datasets are the two
> sparsest-or-faintest; all eleven FLOOR datasets are the rich ones. *"Sized by field richness rather than by
> whether the stars the fit depends on are above the noise"* is no longer an argument — it is a rank correlation
> over the whole bank, in both directions at once.
>
> **The one exception is the honest one, and it is this entry's own poster child.** `D17_cdk14_oiii5` has 799
> on-frame stars — more than four of the floor-clamped datasets — and still asks for 40.4 s, because an OIII
> filter genuinely starves it. So richness is not the WHOLE story; it is simply the dominant term, and the
> statistic has no way to tell the two apart. That is the wing-statistic problem, not a clamp problem.
>
> ### The CEILING, which had never been examined
>
> Separable from the floor and **not obviously the same defect** — at the floor the arithmetic asks for less than
> any sane exposure; at the ceiling it asks for more than an AF sweep can spend.
>
> - **Product: `MaxRecommendedExposureSeconds = 30 s` STAYS. Verified, not changed.** `D10`'s 335.6 s over a
>   9-point sweep is ~50 minutes of pure integration and `AutoFocusEngineOptions.AutoFocusTimeout` would kill the
>   run. The cap is doing its job, and `StarSignalCopy.DescribeExposureDerivation` already names *which* bound
>   bound it (F19(b) verified that).
> - **Bank fidelity: `D10` and `D17` are RENDERED at 30 s while their own physics asks 335.6 / 40.4 s.** Two of
>   the twenty datasets are deliberately photon-starved relative to their derivation and no write-up had ever said
>   so. **Recorded, not re-rendered:** a `D10` rendered at 335 s would be a dataset no user could capture.
> - **Rule CEIL, fixed in advance:** the ceiling moves only if a dataset's σ_focus is measured to improve
>   materially between 30 s and its raw ask. No such ladder exists, and rendering one would re-render the bank
>   [F32](#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains)'s
>   confirmation arm was running on. **Deferred with a stated price**, which is the thing wave 8's §0.1 got wrong
>   by deferring on a prediction instead.
>
> **So (c) costs nothing and re-renders nothing, and the wave's item 1 was worth running for its own sake rather
> than as a prerequisite.** Reproduce: `D:\hf_w9\dryrun_w9.txt`.

### F20 — Below `MinHFR` the autofocus objective collapses to exactly zero, with no diagnostic
**Status:** Done (parts 1 and 2) · found 2026-08-02 running `optimize --per-run` over the
synthetic AF bank

> **Where this stands after wave 3.** The two halves of this entry's fix have diverged and the entry should not
> be read as half-closed by accident:
>
> - **Part 2, "seed out of the plateau": DONE** via [F35](#f35--minhfr-should-be-seeded-from-the-sweep-wings-and-neither-available-hfr-statistic-can-size-it).
>   D01 and D02 go from `FinalJ` exactly 0 to real landings; 14/17 synthetic and 19/19 real landings are
>   bit-identical to the control.
> - **Part 1, "report it": DONE (wave 4)** — and the blocker this entry named was not the blocker.
>   `UndersampledStarsChartNote` now states the measured in-focus HFR, the gate it fell under, and the binning
>   that relates them, as an italic warning above the focus curve (`Optimization/DataTemplates.xaml`), plus
>   `FrameTooLowHFRCounts` through the optimizer.
>
>   **The `*Bounds` framing was a red herring.** The seam that reads `LowSensitivity`/`TooFlat`
>   (`RunEvaluationLoader.cs:334-335`) reads **int properties** — `Metrics?.LowSensitivity ?? 0` — and
>   `StarDetectorMetrics.TooLowHFR` is already exactly such an int (`IStarDetector.cs:755`), already incremented
>   unconditionally (`StarDetector.cs:1895`), already merged across threads (`IStarDetector.cs:832`), and
>   **already rendered in `AutoFocus/DataTemplates.xaml:1062-1074` as "HFR Too Low"**. Whether a gate owns a
>   `*Bounds` rect list has nothing to do with reachability at that seam. So the project invariant about new
>   `StarDetectorMetrics` fields was **already satisfied**, and the real work was the optimizer-side hop
>   (`FrameDetectionResult` → `RunEvaluationMetrics`) — four edit sites across three files, not a detector change.
>
>   Deliberately **not** folded into the "Star signal" block: its doc comment (`VM:227-248`) scopes it to the
>   *Sensitivity* gate, and this is a different gate with a different remedy. It is a sibling note instead, and
>   both can be visible at once. The trigger is `MinHfrSeed.IsBelowGate`, shared with the seed so the message and
>   the seeding decision cannot drift apart.
> - **The real-bank claim below is disproven** (see the correction under Evidence). What reproduces on real rigs
>   is the *symptom*, not the cause.

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

**It also reproduces on the REAL bank (2026-08-03).** `BaselineJ` — the objective at shipped defaults — is
exactly **0.0000** on `Panos` and `LinwoodFocus`. Both are the same silent null result D01/D02 produce
synthetically, on real frames from real rigs, and both are runs whose recall@≥12 *improves* under the optimizer
(+0.031 and +0.089), for the reason F32 gives: they have headroom to climb. So this is not a synthetic-bank
artifact of the render, and the affected population is not hypothetical.

> **Corrected 2026-08-03 (wave 3), twice over.**
>
> **(a) "The only two runs of nineteen" is wrong — it is four.** `SorenVance` and `lumos` also carry
> `BaselineJ` exactly 0.0000. They split, too: `SorenVance` climbs 0 → **0.9701** while `lumos` stays 0 → 0, so
> the cold-start plateau is **sometimes** escapable — which the two-run framing hid.
>
> **(b) NONE of the four is the defect this entry describes, and that is the bigger correction.** The claim
> above — that they are "the same silent null result D01/D02 produce synthetically" — was tested directly by the
> [F35](#f35--minhfr-should-be-seeded-from-the-sweep-wings-and-neither-available-hfr-statistic-can-size-it)
> seeding run over all 19 real runs. **The seed fired on zero of them**, because all four have a fit vertex
> *above* the 1.2 gate, and all 19 landings came back bit-identical to the control. Their `J = 0` comes from
> having almost no stars at all — `lumos` and `SorenVance` detect **zero** at C0, `Panos` 33 and `LinwoodFocus`
> 13 across nine frames — not from stars being rejected for measuring too small.
>
> **Two different defects present identically** as `currentJ=0 bestJ=0 hard-floor FAIL`: the `MinHFR` gate
> emptying the curve's core (synthetic D01/D02) and a frame with no detectable signal (real `lumos`/`SorenVance`).
> Only the first is a detector-configuration problem. So this entry's strongest claim — that the affected
> population is not hypothetical because it reproduces on real rigs — **is not supported**; what reproduces on
> the real bank is the *symptom*, not the cause. The synthetic bank is currently the only place the real defect
> is known to exist.

**Next step.** Two parts, and the second is the substantive one.
1. *Report it.* When a large fraction of accepted candidates are rejected by `MinHFR` specifically, say so and
   name the pixel-scale / focal-length combination. The counts are already collected — but **not where the
   optimizer can see them**: `CollectRejectedCandidateDiagnostics` records never reach the optimizer's
   evaluation path ([F27](#f27--the-optimizer-cannot-reach-the-rejected-candidate-diagnostics-an-approved-spec-says-it-can)).
   The nine per-gate `*Bounds` rect lists *are* reachable at the same seam that reads
   `LowSensitivity`/`TooFlat`, and one of them is `TooLowHFR`… except it is not: `*Bounds` covers eight of
   `RejectionGate`'s twelve constants and `TooLowHFR` is one of the four with none. So this is reporting **plus**
   one new counter, not reporting alone.
2. *Seed out of the plateau.* Before optimizing, if the median in-focus HFR is at or below `MinHFR`, seed
   `MinHFR` beneath it (the measured HFR is available from the same in-focus record
   `DetectionBinningResolver` already consumes) so the search starts somewhere with a gradient. Score any change
   on **D01–D03** of the synthetic bank, where the correct answer is known and current recall is 0.135 / 0.475 /
   0.400.

   **Measured 2026-08-03 — see [F35](#f35--minhfr-should-be-seeded-from-the-sweep-wings-and-neither-available-hfr-statistic-can-size-it), which changes this step in two ways.**
   (a) The trigger as written is **circular** — D01's in-focus frame detects zero stars, so there is no median
   in-focus HFR to read. It has to come from the sweep WINGS, which are richly populated (816–2002 stars/frame)
   and already fit at R² = 0.911. (b) The success criterion here is wrong: lowering the gate moves D01's recall
   only **0.135 → 0.165**, because `TooLowHFR` accounts for just 1877 of ~38500 missed stars while the structure
   map never proposes 18801 of them. What it *does* do is put 5 stars back on the vertex frame at `MinHFR` 0.5,
   which clears the `NHard` = 3 hard floor and is the entire reason `FinalJ` is 0. **Score this fix on "the hard
   floor passes", not on recall** — and do not expect it to lift the W-class band.

   **The risk to design against.** A seeded `MinHFR` is a knob the search may not be able to climb back out of:
   the objective is flat in its neighbourhood on exactly these rigs, which is why the search never moves it
   today. Seed it to a value *measured* from the frames, not to a permissively low one, and re-check that a rig
   which does NOT need the seed (D05, whose search finds 1.45 unaided) is left alone. Compounding factor from
   [F22](#f22--detection-binning-is-a-hard-threshold-on-a-measurement-that-under-reads-so-boundary-rigs-get-the-wrong-factor),
   **with its direction corrected in wave 4**: below ~1.1 px the measurement *over*-reads (the pixelization
   floor, ~0.7 px), it does not under-read, so it makes an undersampled rig look further from this cliff than it
   is rather than closer. The rig is still gated — `MinHFR` defaults to 1.2 and the floored measurement sits
   below it — but any seed sized from that measurement inherits an understatement of how far the gate must come
   down, which is the bias F35 exists to route around.

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

**Wave 7 bounds this entry's SYMPTOM and does not touch its cause (2026-08-06).**
[F18](#f18--step-size-is-sized-by-curve-geometry-alone-so-the-sweep-outruns-what-the-detector-can-see)'s
`MinHalfWidthSampledHalfSpanMultiple = 0.5` caps how far one run may narrow the sweep, so a 143.6 → 12.1 collapse
can no longer land as a 41 → 3 step in one round. But nothing has diagnosed WHY `FindHalfWidth`'s outward search
returned 12.1 on a fit at R² = 1.0000, and a bound on a wrong answer is not an explanation. **This entry's own
next step — instrument `FindHalfWidth` on the two saved `D17` sweeps and confirm or refute the coarse-walk
hypothesis — is still owed**, and the entry stays Open.

### F22 — Detection binning is a hard threshold on a measurement that under-reads, so boundary rigs get the wrong factor
**Status:** Open · found 2026-08-02 running the synthetic bank's S0 control

`DetectionBinningResolver.RecommendFromHfr` is `clamp(round(hfr / 3), 1, 4)` — a hard threshold with its 1→2
boundary at **4.5 px**. The HFR it is given is the detector's *measured* in-focus HFR, which departs from the
optical HFR by 2–10% usually but by up to **38%** in the cases that matter. Rigs whose true HFR sits near 4.5 px
therefore land on the wrong side.

> **The "under-reads" in this entry's title is only half the story (recorded 2026-08-04, wave 4).** The departure
> is **bimodal**, not a systematic under-read — see the measurement below. It under-reads on the large-HFR rigs
> this entry is about, and *over-reads*, sometimes enormously, on small-HFR rigs. Both halves matter, and they
> matter to different entries.

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

**Confirmed as an F23 symptom (2026-08-03) — measured with a toggle, not inferred.** The marginal-SNR
false-positive term built in F23 wave 1 is switchable (`--marginal-snr-strength`) and changes *only* the
objective, so the same frames and the same seed can be scored with the optimizer landing at Sensitivity 0 or
above it. `D17_cdk14_oiii5` scenario S0:

| objective | landed Sensitivity | measured in-focus HFR | binning recommended |
|---|---|---|---|
| term OFF (shipping) | 0.0 | **3.27 px** | 1 |
| term ON | 7.0 | **4.66 px** | **2** |

The 4.5 px threshold sits between the two readings, so the Sensitivity landing *alone* flips the factor — a
30% shift in the measured HFR from nothing but admitted noise. The same signature appears on
`D12_c14_585_afbin2` S6 round 2 (Sensitivity 0 → 6 moves the in-focus HFR 2.99 → 4.45, +49%) and on
`D08_c11_2800mm` S0. This is the causal chain the design spec asserted, now measured: **noise blobs admitted
at a floored Sensitivity pull the in-focus HFR median down, and on a boundary rig that flips the binning
factor.**

The term does **not** ship (it fails its acceptance gates — see [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall)),
so F22 still reproduces in shipping behaviour. What is settled is the *attribution*, and with it that
calibrating the HFR bias would have been the wrong fix — the bias is not a property of the measurement, it is
a property of what the optimizer chose to detect.

**And the departure is BIMODAL, which reverses its sign on the rigs F20 is about.** Measured-vs-optical in-focus
HFR across all 17 datasets (originally from the AF-recommender-hardening design spec, PR #167, which was closed
as superseded — this is the part of it that survived re-measurement):

| regime | datasets | measured vs optical |
|---|---|---|
| HFR ≳ 1.8 px | D05–D15, D17 | **−4% to −23%** (under-reads) |
| HFR ≲ 1.1 px | D01–D04, D16 | **+26% to +234%** (OVER-reads) |

The over-read at small HFR is the **pixelization floor**: a star whose flux lands in essentially one pixel still
measures a few tenths of a pixel, so the measurement cannot follow the optics down. `D01_ultrawide_40mm`'s
optical vertex is **0.231 px** and it measures **0.77 px**. That is independently corroborated three ways: the
bank's own truth model carries `hfrMinEffective = 0.7` for D01 (`synthetic_meta.json`), the derivations use a
0.70 px floor for R2, and [F35](#f35--minhfr-should-be-seeded-from-the-sweep-wings-and-neither-available-hfr-statistic-can-size-it)'s
live pre-search fit independently read **0.762 px** against the same 0.238 px truth.

**Next step.** Two candidates, not mutually exclusive. (a) ~~Calibrate out the bias~~ — **ruled out twice over**:
the departure is not systematic (it tracks the Sensitivity landing) *and* it is not even one-signed (it inverts
below ~1.1 px). (b) Add hysteresis or a dead band around the 4.5/7.5 px boundaries so a marginal rig does not
flip between sessions, and say "borderline" in the UI rather than presenting a coin flip as a recommendation.

> **Correction to this entry's own compounding note (wave 4).** It previously read "the same under-reading pushes
> small-HFR rigs toward the `MinHFR` cliff." That is **backwards in direction**: below ~1.1 px the measurement
> *over*-reads, so it makes a rig look FURTHER from the gate than it is. The real compounding with
> [F20](#f20--below-minhfr-the-autofocus-objective-collapses-to-exactly-zero-with-no-diagnostic) is subtler and
> worse: the over-read is floored near 0.7 px while `MinHFR` defaults to **1.2**, so an undersampled rig is still
> gated — the measurement simply **understates how far below the gate it really sits**. That is exactly why F35
> concluded the fit may only TRIGGER the seed and must never SIZE it.

### F27 — The optimizer cannot reach the rejected-candidate diagnostics an approved spec says it can
**Status:** **Done** (2026-08-03, wave 2 — the spec was never committed; record corrected and the seam documented in code) · found 2026-08-03 implementing [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall)

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

**Resolved 2026-08-03 by correcting the record, not the code.** Every code claim above was re-verified line by
line and holds at HEAD. Two things changed the resolution:

1. **The spec was never merged.** `docs/af-recommender-hardening-design.md` is absent from `develop`; it exists
   only on the unmerged branch `ghilios/af-recommender-hardening-design` (`8b7867c`). Three merged documents
   linked to it, so every one of those links dangles for anyone not sitting on that branch. They now point at
   [`plans/af-recommender-hardening-plan.md`](../plans/af-recommender-hardening-plan.md), the design of record on
   `develop`. (Worth stating precisely: a first pass at this entry said the spec "was never committed and no
   revision of it exists", which is wrong in the direction that would have justified rewriting it from
   scratch.)
2. **The plan already had it right.** Its "signals already available" table reads `CollectRejectedCandidateDiagnostics
   records | no — RejectedCandidates never reaches HocusFocusStarDetectionResult`. So the false claim lived only
   in the uncommitted spec, and the committed artifact contradicted it correctly the whole time. Wave 1 was
   written from the version that was wrong.

**Not plumbed, deliberately.** Adding `RejectedCandidates` to the optimizer's contract buys per-rejection detail
nothing currently needs, at a per-detection allocation cost in the hot loop. The seam is instead **documented
where someone would look for it** (`Interfaces/IStarDetector.cs`, on `RejectedCandidateRecord`), so the next
person to assume the optimizer can see these records is told otherwise by the type itself rather than by a
design document that may not survive. The `*Bounds` rect lists are noted there too, with the caveat the original
entry left out: they carry geometry only — no measured value, no threshold — so they answer a strictly weaker
question and are not a drop-in substitute.

**Also fixed:** the `AllBoundsLists()` doc comment said "seven" and "an eighth in the future" while the helper
returns **nine** — it had drifted when `TooElongatedBounds` and `BloomSuppressedBounds` were added. The
`RejectedCandidateRecord` comment repeated the same stale count, and the true relationship is more lopsided than
either number suggests: `RejectionGate` has **twelve** constants, the nine `*Bounds` lists cover **eight** of
them (TooSmall, OnBorder, HFRAnalysisFailed and TooLowHFR have none), and the ninth — `SaturatedBounds` —
corresponds to no gate at all, because saturated stars are KEPT and masked during the PSF fit rather than
rejected. So "the `*Bounds` lists cover most gates" was making the gap look smaller than it is.

### F28 — `LowSensitivity` reads exactly zero precisely when the Sensitivity gate has collapsed
**Status:** **Done** (2026-08-03, wave 2 — discriminator now gated on the gate being able to reject; user-visible) · found 2026-08-03 implementing [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall)

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

**Fixed 2026-08-03 (wave 2).** The discriminator is now conditional on the gate being able to reject at all.
`StarDetector.InertSensitivityBound(p)` = `PeakResponse × MinEffectiveClipMultiplier(p)` is computed from the
run's own params — never hard-coded, because both factors are searched axes and F23 wave 1 measured landings at
`StarClip` 6.25 and 6.75 where the bound is over 4× the default. `ExposureRecommendation` gains
`InertGateBound` and `GateIsProvablyInert`; an inert gate now falls to the **probe** (which ships with its own
stopping rule) rather than to a verdict of exhaustion nothing in the run supports, and the derivation tooltip
says why the rejection count carries no information.

**This is user-visible**, and narrowly so: signal-sufficient runs where every frame is short of the star-count
target AND the gate sat below its own inert bound move from "no exposure offered" to a 2× probe. The rig that
motivated the zero-rejections test is untouched — it rejected 5 candidates at 2 s, so its gate was demonstrably
live, and the two populations are disjoint by construction (`gateIsProvablyInert` requires
`gateRejectedCount == 0`). An observed rejection refutes the derivation and wins.

The entry's closing warning stands and is worth repeating: **never use this counter as a false-positive
signal.** The pathological landing produces its cleanest possible value.

### F30 — A stored `optimized_settings.json` does not say which config produced it
**Status:** **Done** (2026-08-03, wave 2 — provenance block added, schema 3) · found 2026-08-03 pinning the [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall) baseline

**Retraction first.** This entry was originally filed as "the published V2 config-A landings do not
reproduce". **That was wrong, and the error was mine, not the tool's.** Re-running `optimize --per-run` at
HEAD reproduces the V2 config A *exactly*: the same six datasets at `BrightnessSensitivity` 0.0 (D09, D10,
D11, D12, D15, D17 — precisely the design spec's Evidence-1 list) and identical `bank-verify` precision on
**all 17 datasets**, to every published digit. C0@nc2 likewise reproduces exactly. The optimizer, the bank,
the detector and the scoring chain are all reproducible.

**What actually happened.** The `optimized_settings.json` copies sitting in each dataset's `attempt01/` were
compared against the published **config A** table and found not to match — 5 at Sensitivity 0.0 instead of 6,
D15 at 10.0 instead of 0.0. They did not match because **they were config B's landings**, written by the
`--donut` prepass that ran last (each carried `DefocusAwareDonutDetection: true`, which is the tell, and which
was visible in the file the whole time). Nothing was irreproducible; the artifact was simply misattributed.

**The real gap, which survives.** `OptimizedStarDetectionSettings` records `CreatedAtUtc`, `RunCount`,
`BaselineJ`, `FinalJ` and `SchemaVersion` — but nothing that identifies **which invocation produced it**. Since
[F15](#f15--optimize---per-run-overwrites-each-runs-stored-settings) has the prepass overwrite these files in
place, a bank folder accumulates landings from whichever run went last, and there is no way to tell config A's
from config B's except by inferring it from `DefocusAwareDonutDetection` — an inference that is only valid
while `--donut` is the only thing that varies between prepasses. It stops being valid the moment two arms
differ by anything else, which is exactly what F23 wave 1 did (three arms differing by objective constants and
search domain).

**Why it matters.** The misattribution cost real time and produced a wrong followup entry that was committed
twice before the control arm disproved it. A one-line provenance field would have made it impossible.

**Fixed 2026-08-03 (wave 2).** `OptimizedStarDetectionSettings` gains an optional `Provenance` block
(`Producer`, `CommandLine`, `SettingsFingerprint`, `ProducerVersion`) and the schema goes to **3**. The
fingerprint hashes the harness settings' *semantic* content — the parsed option bag, key-sorted, plus the
resolved pixel-scale inputs — not the file's bytes, so re-exporting or re-indenting a settings file does not make
a landing look as though it came from a different configuration.

**Degrades in both directions.** The block is omitted from the JSON entirely when null, so a snapshot written by
the shipping plugin is byte-identical to before; a v1/v2 file still loads with `Provenance` null, which reads as
**unattributable** and must never be read as "matches me"; and the tilt wizard's `CaptureDetectionSettings` —
which snapshots *live* settings rather than an optimizer result — deliberately keeps it null, because stamping a
producer on it would be a lie. `Clone()` deep-copies it: `MemberwiseClone` is shallow, so without that every copy
would alias one instance.

The underlying overwrite ([F15](#f15--optimize---per-run-overwrites-each-runs-stored-settings)) is unchanged — a
bank folder still accumulates whichever prepass went last. What changes is that the survivor now says so.

### F31 — Synthetic-bank precision is NOT exact: the golden omits real stars, and they score as false positives
**Status:** **Done** (2026-08-03, wave 2 — repaired, validated against a null control, and everything re-baselined at `afbank-verify/5`) · found 2026-08-03 verifying the F23 wave-1 result · **INVALIDATED F23's evidence base**

`docs/synthetic-af-bank-baseline-results.md` headlines the synthetic bank with "Golden precision (D06,
`golden eval`) **1.000** — 205 TP, **0 FP**. Exact, not a lower bound." That claim does not generalise. Measured
against each frame's own `*.truth.json`, **96% of the false positives the bank reports are real rendered stars.**

**Evidence.** `D09_c14_3800mm`, config A as landed by an unmodified-HEAD control arm, all 9 frames, 12 px
centroid match:

| | count |
|---|---|
| detections | 510 |
| scored FP against `*.golden.json` | 280 |
| of those, a **real truth star** within 12 px | **269 (96.1%)** |
| genuinely spurious | **11** |

The 269 break down by truth tier as **198 `omitted`** and **71 `unresolved`**. Re-scored against truth, the
four datasets that drove the F23 finding all sit at 0.95–1.00 precision on **every** arm:

| dataset | true precision (control / floor / term) | golden-scored (what F23 used) |
|---|---|---|
| D09_c14_3800mm | **0.978** / 1.000 / 1.000 | 0.525 / 0.991 / 1.000 |
| D10_rc16_3250mm_sparse | **0.946** / 1.000 / 0.993 | 0.634 / 0.933 / 0.974 |
| D11_rc10_585_afbin2 | **1.000** / 1.000 / 1.000 | 0.871 / 1.000 / 0.967 |
| D12_c14_585_afbin2 | **1.000** / 1.000 / 1.000 | 0.899 / 0.736 / 0.905 |

**Mechanism.** `GoldenFromTruth` tiers truth stars by native **peak-pixel** SNR (`GoldenFromTruth.cs`, thresholds
`goldenHighSnr` 20 / `goldenMediumSnr` 10 / `goldenLowSnr` 5 / `goldenUnresolvedSnr` 3.5 in
`SynthBank/synthetic-bank-spec.json`). Below 3.5 a star is tiered `omitted` and appears in **neither** `stars`
**nor** `unresolved` — so `GoldenMatch.ExcludeUnresolved` (`GoldenEvalRunner.cs:261-266`) cannot protect it, and
`bank-verify` does not call `ExcludeUnresolved` at all. Defocus destroys peak-pixel SNR while leaving integrated
flux intact, so the golden evaporates toward the sweep wings: D08 holds **81 golden stars at focus and 9 at the
extreme frame**, against 123–126 truth stars per frame throughout.

**Coordinate alignment was null-tested** before believing this: detections match truth at 100% as-is, 2.3% at
0.5× or 2× scale (chance), 0% under a 300 px shift.

**Why it matters — this inverts F23.** F23's headline is "C0 precision never drops below 0.942; config A reaches
0.451, so the optimizer trades precision away for marginal recall." The real mechanism is the opposite: C0 at
`Sensitivity` 10 detects only bright stars, all of which are in the golden; config A at `Sensitivity` 0 detects
**many more real but faint stars**, which the golden omits, and the metric charges every one as a false positive.
The control arm detects **510** stars on D09 at **97.8%** true precision where the term-on arm detects **231** at
100% — so both F23 wave-1 mechanisms were suppressing *real detections*, not junk.

**Why it matters — this also inverts the bank's selling point.** Precision against the synthetic golden is a
**lower bound**, for exactly the reason [F11](#f11--precision-is-a-lower-bound-on-runs-whose-faint-tier-was-budget-truncated--re-run-these-with-more-montages)
gives on the real bank: the reference is incomplete below the tier cut. The synthetic bank's claim to measure
precision *exactly* is what justified building it, and as implemented it does not hold.

**Re-scored with the repair in place (afbank-verify/4).** Control arm, config A, 17 datasets: the `/3`
golden-only metric gave 0.451–0.992 with 8 datasets below 0.90; the repaired metric gives **0.982–1.000**,
none below 0.90. The detector's real false-positive rate on this bank is **0–1.8%** at every configuration
tested. The residual is real rather than noise — the only four datasets short of 1.000 are D09 (0.991),
D17 (0.986), D15 (0.988) and D10 (0.982), i.e. the long-focal-length rigs landing at Sensitivity 0 plus the
sparse field. That is F23's predicted effect at roughly **1/30th** the size of the artifact that masked it.

**A caution for whoever re-baselines.** The first cut of the repair sized protection by the star's light
footprint (2·HFR, ~40 px on a wing donut). That removed the bias and replaced it with **saturation**:
precision read 1.000 on all 17 datasets for all three arms — which looks like a clean result and measures
nothing. Protection is now the match radius exactly. Before trusting a re-baseline, check that precision
still SPREADS across datasets; all-1.000 means the metric is saturated again, not that the detector is
perfect.

**Closed 2026-08-03 (wave 2), with the instrument validated before anything was baselined against it.**

**The validation came first, and it was cheap.** `golden eval` writes `detected_f<focuser>.csv` per frame, so 30
saved configurations (D09/D13/D17 at nine Sensitivity values, plus D10/D11/D12) could be re-scored **offline**
under four policies with the detector never running again. Re-implementing `GoldenMatch` in Python reproduced
the published C# `/3` numbers exactly — D09 s0 0.8037 vs 0.804, D17 s0 0.6974 vs 0.697, D13 s0 0.8504 vs 0.850 —
which is what made its other columns believable. Minutes of work; it would have caught this before wave 1 began.

What it showed:

- **The metric is NOT saturated.** A null control (the same detections translated with wraparound — count and
  clustering preserved, correspondence destroyed) scores **0.000–0.012**. The 2·HFR saturation this entry warns
  about would have shown up as a high null. It does not.
- **Three independent policies agree to within 0.006** at 0.98–1.00: the as-implemented `/4` metric, a variant
  whose protection predicate is exactly the matching predicate, and direct truth scoring.
- **The `/3` violation, counted:** detections charged as a false positive while sitting within the match radius
  of a real rendered star number 52/318 on D09 s0, 79/337 on D17 s0, 139/1075 on D13 s0. Under `/4`, **0 on all
  30 configurations**.
- **A residual asymmetry in the `/4` repair, always flattering.** `GoldenMatch.Covers` excludes on
  `centre-in-box OR IoU(box, det.bbox) > 0`, so the *detection's own bounding box* dilates every protection box
  and a wide donut detection is protected well past the match radius — despite the class comment claiming
  protection is "exactly as generous as matching". Measured: D09 s0 reads 1.0000 under the implemented predicate
  against 0.9909 under the symmetric one. Small (≤0.011), systematic, one-directional.

**Shipped as `afbank-verify/5`.** Truth protection now uses the centroid predicate matching itself uses (the
golden's own `unresolved` boxes keep `Covers`, since that is the real bank's path and must not move), and every
config row now reports `precisionNull`, `truthViolations` and `scoredFraction`. Next-step (1) — score directly
against truth — was measured and found to agree with the repaired golden+protection scoring to within 0.006, so
it was not worth a second scoring path; next-step (2) is moot.

**Next-step (3) is done.** The V3 matrix in
[`synthetic-af-bank-baseline-results.md`](synthetic-af-bank-baseline-results.md), a new measured
[`synthetic-af-bank-baseline.json`](synthetic-af-bank-baseline.json), re-derived `precisionMin` bands in
[`synthetic-af-bank-expectations.json`](synthetic-af-bank-expectations.json) (the old 0.95/0.98 were fitted to
the broken metric and were *not* carried forward), and [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall)
/ [F24](#f24--donut-detection-costs-precision-even-where-donuts-exist-and-badly-where-they-do-not--it-costs-recall-where-it-is-not-needed)
both re-measured — F23 void as written, F24 refuted and restated as a recall finding.

**The lesson, stated so it outlives the bug.** Reproducibility validated nothing here. Every check confirmed
determinism, and a deterministic pipeline reproduces a systematic error perfectly; the bias lived in the
reference all the checks shared. What caught it was scoring against an *independent* source. And a metric can
fail in two directions — biased, then saturated — which is why `precisionNull` now ships alongside every
precision figure rather than being something a reader has to think to ask for.

### F32 — `J` is saturated near 1.0, so the optimizer trades enormous recall for numerically trivial gains
**Status:** Open — **mechanism shipped default OFF (wave 5)**; the confirmation arm was **re-validated as the right
experiment (wave 6)** against a restarts-only alternative and is still owed. The
entry's own premise is corrected below: on 5 of 7 binding runs there was no trade to bound, the search was
merely stuck · found 2026-08-03 re-reading the wave-1 real-bank control arm

The objective's landings are not close calls. Across the 17 scorable real-bank runs, `optimize --per-run` gives
up a **median 0.243 of recall@SNR≥12** to gain a **median ΔJ of +0.0125** — and the worst cases are far starker
than the median.

**Evidence.** `bank-verify --runs "D:\\Autofocus Bank" --opt-a` (wave-1 control arm, `afbank-verify/3`, 19 runs,
0 failed), against each run's own `BaselineJ`/`FinalJ` from its stored landing:

| run | recall@≥12 C0 → A | Δrecall | ΔJ | landed Sensitivity |
|---|---|---|---|---|
| `toml999` | 0.819 → 0.357 | **−0.462** | **+0.0002** | 33.3 |
| `muggsie` | 0.879 → 0.512 | −0.368 | +0.0039 | 17.2 |
| `CWhiteFocus` | 0.810 → 0.327 | −0.483 | +0.0042 | 50.0 |
| `uneven` | 0.931 → 0.319 | −0.613 | +0.0046 | 31.2 |
| `bobp` | 0.580 → 0.495 | −0.086 | +0.0041 | 10.0 |

`toml999` is the entry's clearest statement: **two ten-thousandths of `J` bought with 46 points of recall.** At
`BaselineJ` values of 0.98–0.999 there is almost no headroom left, so every remaining move is a rounding error in
the objective and a catastrophe in the star list. The search is behaving correctly; the scale it is climbing has
run out.

**Why it matters.** This is upstream of [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall)
and of [F4](#f4--the-objective-has-no-sensor-model-term). A precision term, a sensor term, or any other new term
added to a `J` that already sits at 0.998 will be competing for the same exhausted fourth decimal place. It also
explains why F23's wave-1 mechanism (a) could produce real precision movement and still clear no acceptance gate:
the term was fighting for headroom that does not exist. And it reframes the wizard's own presentation — a landing
reported as an improvement over the current settings is, on most runs, an improvement too small to mean anything
while the change in what gets detected is enormous.

**Next step.** Before adding any further term to `J`, measure its dynamic range on both banks: the distribution of
`FinalJ − BaselineJ` over the runs, and the recall/precision movement per unit `J`. If the trade rate is what
these numbers say, the fix is to rescale or re-anchor the objective (or gate acceptance on the *magnitude* of the
improvement) rather than to add a term. Cheap to check: the numbers above come from files already on disk.

**Measured 2026-08-03 (wave 3).** Done, from files already on disk, over all three arms. `BaselineJ` and
`FinalJ` come from each run's stored landing; recall from the `/5` synthetic verify and the `/3` real one —
valid because `recallHigh` derives only from `match.Pairs` (`BankVerifyRunner.cs:488`) and the `/5` repair
touched only the false-positive list, so the real bank's *precision* is void but its *recall* is not.

| arm | median `BaselineJ` | median Δ`J` | median Δrecall@≥12 | **median trade rate** (Δrecall per unit Δ`J`) |
|---|---|---|---|---|
| synthetic A | 0.952 | +0.0101 | **0.000** | **0.00** |
| synthetic B | 0.992 | +0.0021 | −0.014 | **−1.01** |
| **real A** | 0.977 | +0.0126 | **−0.243** | **−17.32** |

Three things follow, and the third is the one that changes the plan:

1. **The headline number reproduces exactly.** Median Δrecall on the real bank is −0.243, as recorded above.
2. **The worst case is far worse than "0.0002 for 46 points".** Expressed as a rate, `toml999` gives up **3018
   points of recall per unit of `J`**; `uneven` −134, `CWhiteFocus` −116, `muggsie` −94. The fourth decimal is
   not a rounding error, it is the entire remaining scale.
3. **The two banks disagree in SIGN, not just in magnitude** — the synthetic bank's median trade rate is
   **0.00**, i.e. config A costs the synthetic bank no recall at all while costing the real bank a quarter of
   it. So the synthetic bank cannot measure this defect, and
   [F33](#f33--the-synthetic-bank-does-not-reproduce-the-real-banks-optimizer-failure-mode)'s rule applies to
   any proposed rescaling as much as to a new term. **A candidate objective change must move the real-bank
   trade rate, and the synthetic bank cannot tell you whether it did.**

Reproduce: `D:\hf_w3\f32_dynrange.py` (reads `hf_w2/verify_v5`, `hf_f23/verify_real_H`, and the `H_A` / `B_A`
/ `H_real_A` landings; no detector run).

**SHIPPED 2026-08-05 (wave 5) as an acceptance constraint — and the arms overturn this entry's own framing.**
`OptimizerSettings.MinDetectionKeepFraction` rejects a candidate keeping less than φ of the SEED's accepted stars
(min over runs) **ahead of** the `j > bestJ` compare at all three accept sites. `J` is never multiplied or
re-anchored, so landings stay comparable to every prior arm. Default null; **inertness measured against the
parent commit with settings pinned: bit-identical**. Multi-pass callers pin round 0's seed totals (at φ=0.5,
three `--continue-rounds` would otherwise reach 0.125 of where the user started).

**32 optimizations, φ ∈ {0.30, 0.50, 0.75} + a feature-OFF control, one binary, `--settings` pinned
([F42](#f42--every-build-directory-silently-gets-its-own-detector-settings-and-the-run-instructions-require-a-new-one-per-arm)).
`BaselineJ` identical across all four arms of every run ([F41](#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)'s
tell), so the comparison is valid.**

**The headline is not what this entry predicted: on most binding runs the constraint IMPROVES `J` while keeping
2–4× more stars.** At φ = 0.75, **5 of 7** binding runs land at a higher `J` than the unconstrained search found:

| run | keep off → φ=0.75 | Δ`J` | σ_focus vs unconstrained |
|---|---|---|---|
| `CWhiteFocus` | 0.429 → **1.819** | **+0.00139** | **0.156×** |
| `uneven` | 0.353 → 0.937 | +0.00165 | 0.785× |
| `D19_cygnus_deep_shed` | 0.600 → 1.276 | +0.00024 | 0.364× |
| `toml999` | 0.397 → **1.163** | +0.00054 | 0.959× |
| `D18_m24_deep_shed` | 0.511 → **1.819** | +0.00009 | 1.337× |
| `muggsie` | 0.612 → 0.805 | −0.00234 | 1.676× |
| `mccomiskey` | 0.095 → 0.771 | −0.03097 | 2.812× |

**A constrained maximum cannot exceed an unconstrained GLOBAL maximum**, so this proves the unconstrained search
**was not finding the global optimum**. The shedding corner is substantially a **greedy trap**, not the rational
purchase this entry and wave 4 concluded it was — the mechanism `RevertNeutralAxes` already documents, one level
up: Phase A grids Sensitivity × StarClip with Sensitivity as the OUTER loop, the winning StarClip is discovered
in a shedding row, and strict `j > bestJ` freezes it there. **"It is genuinely buying a much better fit with the
stars it discards" is true relative to the baseline and false as a claim about the best available trade.**

**Inertness measured 12 times, 9 bit-identical** — D20 at all three floors, D19 and muggsie at two each, toml999
and uneven at φ=0.30. The 3 that changed are the pre-registered caveat (a landing can be feasible while a
candidate *visited on the way* was not); two changed for the better, one by −0.0001 of `J`.

**Exact recall on the synthetic arms: precision 1.000 and FP 0 at every floor** — every star won back is real.
D18 recall@high 0.607 → **0.945** at φ=0.50; D19 0.968 → 0.984 with **2×** the detections at φ=0.75; **D20
identical to the last detection at all four arms**. Note φ=0.75 **overshoots on D18** — its effective gate
collapses to 0.234, the Sensitivity-floor pathology reached from the other side.

**Recommended floor: φ = 0.50**, by the rule fixed in advance (*the smallest φ meeting the criteria*). φ=0.30
does not address the defect (only `mccomiskey` binds, and it pays). **Adoption still needs the confirmation arm**
— both full banks plus `bank-verify` for real-bank recall, since the efficacy criterion could only be evaluated
by keep-% proxy here. Ships default OFF until then. Reproduce: `D:\hf_w5\f32_arms.sh`,
`D:\hf_w5\scorecard.py`, `D:\hf_w5\score_f32_synth.sh`.

**CONFIRMATION ARM RE-VALIDATED AS THE RIGHT EXPERIMENT (wave 6), against a pre-registered alternative.** Before
spending ~6 h, the competing hypothesis was tested: if the unconstrained search is merely *stuck*, any RESTART
should recover the floor's gain with no floor at all. `--continue-rounds` already is that mechanism — each round
re-seeds from the prior best with a **fresh** curated set, resetting the pattern-search stride. **Arm R:** the same
8 runs, `--continue-rounds 2`, **no keep floor**, same binary, `--settings` pinned.

**First, the control.** Arm R's round 0 is **identical to wave 5's feature-OFF arm on all 8 runs, to 6 dp** —
0.997993 / 0.998476 / 0.996368 / 0.997195 / 0.994025 / 0.999822 / 0.999557 / 0.999766. Two waves, two binaries,
one pinned settings file, byte-identical landings. [F41](#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)'s
check passing this cleanly is what makes the rest of the table readable.

| run | round 0 (= wave-5 OFF) | **Arm R final** | Δ from restarts | wave-5 φ=0.75 | Δ from the floor | **restarts recover** |
|---|---|---|---|---|---|---|
| `toml999` | 0.997993 | 0.998068 | +0.000075 | 0.998536 | +0.000543 | **13.8%** |
| `CWhiteFocus` | 0.998476 | 0.998650 | +0.000174 | 0.999867 | +0.001391 | **12.5%** |
| `uneven` | 0.996368 | 0.996745 | +0.000377 | 0.998014 | +0.001646 | **22.9%** |
| `D18_m24_deep_shed` | 0.999822 | 0.999855 | +0.000033 | 0.999914 | +0.000092 | **35.9%** |
| `D19_cygnus_deep_shed` | 0.999557 | 0.999557 | 0 | 0.999800 | +0.000243 | **0%** |
| `muggsie` | 0.997195 | 0.997195 | 0 | 0.994854 | **−0.002341** | floor HURT |
| `mccomiskey` | 0.994025 | **0.997394** | **+0.003369** | 0.963058 | **−0.030967** | floor HURT |
| **`D20` (control)** | 0.999766 | 0.999768 | +0.000002 | 0.999766 | 0 | — |

**Three findings, and they are not the same finding.**

1. **The greedy trap is CONFIRMED as a fact, not an inference.** With no constraint whatsoever, simply restarting
   improves `J` on **5 of the 7 binding runs** (all but `muggsie` and `D19`; the `D20` control moves 2×10⁻⁶,
   i.e. not at all). A converged global optimum cannot be improved by re-seeding from itself, so
   round 0 demonstrably was not one. This is the same phenomenon as
   [F8](#f8--optimizer-landings-are-not-reproducible-across-invocations) — a landing is a property of the
   trajectory, not of the objective.
2. **But restarting is NOT a substitute for the floor.** On the five runs where the floor helped, restarts recover
   **0–36% (median 13.8%)** of the floor's gain. A restart changes the *trajectory*; the floor changes the
   *feasible set*, and it reaches a region three restarts do not find. **So the pre-registered rule fires:
   recovery < 50% on 5 of 5 → keep the confirmation arm as designed, at φ = 0.50.**
3. **The floor's two failures are exactly where restarts do best.** `mccomiskey` — the worst floor case at −0.031 —
   *gains* +0.0034 from restarts alone, and `muggsie` (−0.0023 under the floor) is untouched by them. The two
   mechanisms are complementary rather than competing, so **the confirmation arm should carry a third
   `--continue-rounds` arm** rather than being floor-vs-nothing. That is a change to the experiment, and it costs
   one more arm, not six hours more.

> **Instrument caveat found while reading Arm R, and it would have inverted the result.** The end-of-run
> `detections kept vs seed` line reads **≈ 1.0 in every multi-round run** (`toml999` 1.00069, `CWhiteFocus`
> 1.00259) — which looks like "the unconstrained search stopped shedding entirely" and is nothing of the sort.
> `DetectionKeepBaselineTotals` is pinned to round 0's seed **only when a floor is set** (`:690`), so with no floor
> each round's keep is measured against *that round's own seed* — the last round barely moves, so the ratio is ~1.
> Round 0's true keep is wave 5's control column (0.397, 0.429, 0.353, 0.612, 0.095). **A diagnostic whose meaning
> silently changes with an unrelated flag is worse than an absent one**; the pinning should apply to the readout
> whether or not a floor is in force. Reproduce: `D:\hf_w6\f32_armR.sh`.

**Wave 7 checked whether it was about to invalidate this arm, and it did not (2026-08-06).** Wave 7's three items
were expected to share a bank re-baseline, which would have made a confirmation arm on the new frames
incomparable with wave 5's φ table. Measured instead of assumed: **F19 decided "no change"** (so the exposure axis
of the derivation does not move) and **F39(b) needed no re-render at all** (the factor is detector-side; the
frames' exposures were already derived at binning 2). Nothing in wave 7 has re-rendered a frame, so `D18` / `D19`
/ `D20` — this arm's entire synthetic half — are bit-for-bit what wave 5 and wave 6 measured, and its other five
runs are on the real bank, which wave 7 never touches. **The arm is runnable and comparable TODAY.**

**When F18's re-render does happen, the rule for this arm is fixed in advance:** the datasets whose `step*` moves
get their pre-wave frames preserved (`D:\hf_w7\oldframes`, as wave 6 did) and the arm runs on those; an arm run
on the NEW bank must re-run wave 5's φ arms on that bank rather than compare across it. That is
[F41](#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary) applied to frames instead of to
binaries.

### F33 — ~~The synthetic bank does not reproduce the real bank's optimizer failure mode~~ → it does now
**Status:** Done (part 1 wave 3, part 2 wave 4) · found 2026-08-03 re-reading the wave-1 arms side by side

> **Both parts are shipped.** Part 1 (report the *effective* gate) landed in wave 3 as
> `StarDetector.EffectiveSensitivityGate`. Part 2 (decide whether the bank grows a shedding class) was decided
> **yes** in wave 4, and the class was generated and **validated against a criterion fixed before generation** —
> `D18`/`D19` shed at trade rates −53.4/−40.4 inside the real bank's regime, while the `D20` control comes back
> at **+44.9**. The title's claim no longer holds: the synthetic bank now reproduces the regime, so the
> "never the synthetic alone" rule it imposed on objective changes is satisfiable rather than blocking.

On the synthetic bank the optimizer drives `BrightnessSensitivity` **down** to its 0.0 floor and detects *more*.
On the real bank it drives Sensitivity **up**, often to the top of the range, and detects far *fewer*. Those are
opposite behaviours, and the synthetic bank was built to study the first one.

**Evidence.** Landed Sensitivity and detection counts, same wave-1 control arm, same binary:

| bank | landings | detections C0 → A |
|---|---|---|
| synthetic (6 of 17 datasets) | Sensitivity **0.0** (D09, D10, D11, D12, D15, D17) | up |
| real: `CWhiteFocus` | **50.0** | 1807 → **534** |
| real: `standard_example1` | **34.3** | 602 → **177** |
| real: `toml999` | **33.3** | 677 → **228** |
| real: `uneven` | **31.2** | 406 → **133** |
| real: `mccomiskey` | 0.0, but **StarClip 10.0** (the maximum) | 3606 → **43** |

`mccomiskey` is the instructive one: Sensitivity reads 0.0, which looks like the synthetic pathology, but the
effective gate is `PeakResponse × StarClip` — the *same escape route* F23 wave 1 measured on D12 and D15, here
operating as the shedding mechanism rather than the loosening one. Reading the Sensitivity axis alone
misclassifies this run.

> **Corrected 2026-08-03 (wave 3), and it is more general than one run.** The `0.75 × 10 = 7.5` above used the
> *default* `PeakResponse`; this landing's own `StarPeakResponse` is **0.98**, so its effective gate is
> **9.81**. Both inputs are searched axes, so the gate must always be computed from the landing's own params —
> the same trap the entry warns about, one level down. And `mccomiskey` is not alone: **`caboose`** also lands
> Sensitivity 0.0 with an effective gate of **5.05**, and on the synthetic bank **2 of the 6** "Sensitivity 0.0"
> landings F23 cites are not floor landings either (**D11 → 2.36**, **D12 → 2.10**; D09/D10/D15/D17 are genuine,
> all below the 1.5 inert bound). So this is a systematic reading error, not one odd run.
>
> **Shipped:** `StarDetector.EffectiveSensitivityGate(p)` = `max(Sensitivity, InertSensitivityBound(p))`,
> reported alongside every quoted landing Sensitivity — `optimized_settings.json` (derived, get-only, no schema
> bump), `optimize`'s console + `optimize_summary.txt` + `aggregate_summary.*`, and `bank-verify`'s per-config
> `effectiveSensitivity`. The `afbank-verify` schema is deliberately **not** bumped: the field is additive and
> derived, no number changes, and the /4 and /5 bumps were for changes that made numbers non-comparable.

**Why it matters.** [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall)'s
framing — "the optimizer drives `BrightnessSensitivity` to its 0.0 floor" — is a **synthetic-bank-only**
description. Any fix designed and accepted against the synthetic bank alone is being tuned on the opposite sign of
the effect it needs to correct on real rigs, which is how wave 1's arm (a) came to be a wash on the real bank
while showing real movement on the synthetic one. It also bounds the bank's claim: the synthetic bank measures
precision exactly (that is real and now verified), but it does not currently exhibit
[F4](#f4--the-objective-has-no-sensor-model-term)'s star-shedding at all.

**Next step.** Two parts. (1) Report the *effective* gate `max(Sensitivity, PeakResponse × StarClip)` wherever a
landing's Sensitivity is quoted, so a `mccomiskey`-shaped landing is not read as a floor landing. (2) Decide
deliberately whether the bank should grow a dataset class that reproduces the shedding regime — and until it does,
score every candidate objective change on **both** banks, never the synthetic one alone.

**Part 2 DECIDED (wave 4): yes, and the mechanism is not what this entry assumes.** The synthetic bank does not
fail to shed because its *render* differs from real frames. Measured with `golden eval` at ~40 s per arm, the
detector's response to the gate is much the same at any density:

| dataset | recall@high, Sensitivity 10 | Sensitivity 50 | high-tier stars surviving at 50 |
|---|---|---|---|
| `D04_esprit_550mm` (dense) | 0.762 | **0.208** | **4591** |
| `D16_esprit550_ha3` (sparse) | 0.821 | **0.130** | **24** across 9 frames |

Precision is **1.000 and FP is 0 on every arm** — everything shed is a real star. What density changes is not
whether the detector *will* shed but whether the optimizer can **afford** to: D04 keeps thousands of stars and
still clears `NHard = 3` on every frame, so the search can climb to a shedding operating point and score it; D16
falls to ~2.7 stars/frame, `J` goes to 0, and the search is repelled. The synthetic bank's median min-frame
detection count is **7** against the real bank's **37**, and only 2 of 17 datasets clear 100.

So the missing ingredient is **headroom above the hard floor**, and it is a spec-level property — density is set
by `limitingMagnitude` and pointing against a real Gaia/ASTAP catalog, not by generator code.

**Shipped:** three rows in `synthetic-bank-spec.json` — `D18_m24_deep_shed` (M24 at limiting mag 15.5, 27084
on-frame stars), `D19_cygnus_deep_shed` (a second optic and field so no finding rests on one geometry), and
`D20_m24_bright_control` (the same rig and field at limiting mag 12.0: dense enough to have the headroom, but
bright-dominated, so there is no faint near-threshold tail whose membership changes with focus). **The control
was designed before the experiment** — wave 3's D05 lesson — and it isolates the faint tail from mere density: if
D20 sheds too, the faint-tail mechanism is refuted and density alone explains the regime.

**Acceptance criterion, fixed before generation so it cannot be moved afterwards:** a row earns its place only if
at config A it reaches median trade rate **≤ −10** recall-points per unit `J` with **keep% < 80%**, at precision
**≥ 0.99**. D20 must NOT meet it.

**Measured — the class PASSES and the control separates cleanly:**

| dataset | landed Sens | recall C0 → A | Δrecall | Δ`J` | trade rate | keep% | criterion |
|---|---|---|---|---|---|---|---|
| `D18_m24_deep_shed` | 32.83 | 0.736 → 0.607 | −0.129 | 0.00242 | **−53.4** | **48.8%** | **MEETS** |
| `D19_cygnus_deep_shed` | 16.67 | 0.983 → 0.968 | −0.015 | 0.00037 | **−40.4** | 59.2% | **MEETS** |
| **`D20_m24_bright_control`** | 15.67 | 0.946 → **0.960** | **+0.014** | 0.00031 | **+44.9** | **94.8%** | **does not meet** |

Precision is 1.000 on all three. **D20's trade rate is positive** — the optimizer *gained* recall on the
bright-dominated control while raising its gate. So the mechanism is decided rather than assumed: all three
fields have the headroom to shed, and only the two with a faint near-threshold tail actually do. **Density
supplies the headroom; the faint tail supplies the motive.**

Note all three raised Sensitivity above the default, control included — a landing that moves the gate is not by
itself evidence of shedding, and an arm read off landed parameters alone would have misclassified D20.

**So [F32](#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains)'s
both-bank requirement is now satisfiable**: the synthetic bank finally contains the regime a candidate objective
change has to be shown to move.

**Why this is worth spending anything on:** only the synthetic bank knows the **true optimal focuser position**
(`optimalFocuserPosition` is a spec input). So only it can answer whether shedding bought *real focus accuracy*
or merely a smaller **self-reported** σ from a fit with fewer, better-behaved points — the crux of F32 and F4,
currently unanswerable on either bank.

### F38 — The `MinHFR` seed trigger compares a captured-pixel vertex against a binned-pixel gate
**Status:** Done (wave 4) · found 2026-08-04 checking F20 part 1's trigger before reusing it for user-facing copy

`MinHfrSeed.Resolve(fitVertexHfr, currentMinHfr)` compared two numbers that are not in the same space. The gate
fires **inside the binned raster** — `if (star.HFR <= p.MinHFR)` at `StarDetector.cs:1894`, over the `BinMean`
output — while every HFR the detector *reports* has already been scaled back to source pixels at `:805-808`
(`ScaleToSourcePixels` multiplies HFR by the factor, `CvImageUtility.cs:818`). That rescaled value is what flows
to `AverageHFR` → the hyperbola → `BestFit.Minimum.Y`. Nothing divided the factor back out.

**The entry's own doc comment defended the wrong half.** `MinHfrSeed` argued the comparison was safe because
"from the software-binning stage onward the whole pipeline runs in binned pixels" (`StarDetector.cs:483-486`).
That sentence is about the **right-hand** operand, and its very next clause says the other half: "GateAndMeasure­Internal
scales every pixel-space output back to source pixels before returning, **so callers never see binned units**."
`MinHfrSeed` is exactly such a caller. The detector is careful about this elsewhere — `RejectedCandidateRecord`'s
`MeasuredValue`/`ThresholdValue` are deliberately *not* rescaled because they are "the gate's own comparison pair"
and "must stay in the same space" (`StarDetector.cs:847-857`). This was a gate comparison pair that was not.

**Direction: missed seeds only, never spurious.** `{h ≤ G} ⊂ {h ≤ N·G}`, so every seed that fired was one the
correct rule also fires. Sound but incomplete — no risk of lowering a gate that did not need lowering.

**The silent window**, at the shipped `MinHFR = 1.2` and `N = 2`: a captured vertex in **(1.2, 2.4]** px. The
rig's near-focus frames really are being emptied by `TooLowHFR`, but the *wing* frames still fit, so
`BestFit.Minimum.Y` is finite and the rule takes the **"the rig's stars clear the gate; leave it alone (the D05
case)"** branch on a rig that emphatically does not. It then prints nothing, because the seed simply never fires.

**Reach — and why no F35 number is invalidated.** TestApp `optimize` never sets `DetectionBinning`
(`ApplyAfContext` writes only PixelScale/Region/ModelPSF/paths), so it runs at the `StarDetectorParams` default
of **1**, where both spaces coincide. The entire F35 evidence base — 17 synthetic datasets, 19 real runs, the D01
firing, the D05 control — was produced at N = 1 and stands unchanged. The bug is **latent headless and ACTIVE in
the wizard**, which stamps the user's real profile/per-filter factor via `ApplyDetectionImageContext`
(`HocusFocusStarDetection.cs:573-574`) — i.e. it bites the actual product, on exactly the undersampled population
F20/F35 exist to rescue.

**Shipped:** `Resolve` and the new shared `MinHfrSeed.IsBelowGate` take the factor explicitly, so the mixed-unit
comparison cannot recur silently, and F20 part 1's message reuses `IsBelowGate` rather than re-deriving it.
`MinHfrSeedTests` had **zero binning coverage**, which is why this survived wave 3; it now has four discriminating
cases, confirmed failing with the division reverted.

**Lesson.** The first version of that coverage was five tests of which only **one** failed when the fix was
reverted — the other four asserted behaviour identical with and without it. Wave 3's rule ("a regression test that
passes either way is worth nothing") applies to a *set* of tests too: count the ones that discriminate, not the
ones you wrote.

**The BANK can finally exercise this fix (2026-08-06, wave 7).** This entry's population is a run whose
`DetectionBinning > 1`, and until wave 7 the harness had never produced one —
[F39](#f39--the-harness-records-a-detection-binning-the-run-never-applied-and-7-datasets-have-never-run-at-theirs)(b)
was exactly that gap. `optimize --apply-run-detection-binning` now makes the seven `detectionBinning = 2` datasets
detect at 2, so a bank-level regression assertion for the captured-vs-binned comparison became possible for the
first time — and **it was added in the same wave**: the whole (1.2, 2.4] silent window at factor 2, plus the four
real bank in-focus HFRs pinning that honouring the factor did NOT quietly start seeding the entire binning-2
population. **3 discriminating** (reverting the division fails them) **/ 5 guards**.

**A related hypothesis this entry did NOT explain, checked and refuted.** Wave 7's binning arm shows `recall@high`
falling on 4 of 7 datasets at factor 2, which looks exactly like this entry's mismatch (a gate in binned space
effectively doubling `MinHFR` in captured pixels). `golden eval`'s false-negative attribution reports **no
`TooLowHFR` at all** in any of the fourteen runs: the loss is candidate formation and the shape/size gates. Filed
separately as [F46](#f46--detection-binning-buys-faint-stars-and-quietly-sells-bright-ones-to-the-shapesize-gates).

### F39 — The harness records a detection binning the run never applied, and 7 datasets have never run at theirs
**Status:** Open — part (a) done (wave 6); **part (b) MEASURED IN FULL (wave 7, and it needed no re-render):
recall up on 7/7 AND σ_focus up 17–95% on 7/7**; the adoption re-baseline is a wave of its own · found 2026-08-04
checking whether the banks differ in binning

Every `harness_settings.json` in the synthetic bank says `"DetectionBinning": "Bin2"`; every real run says
`Bin1`. That looks like a systematic difference between the banks that could explain F33 outright. **It is not,
because the value is inert on the headless path** — but two real defects sit underneath it.

**1. The recorded value is not the value used.** Nothing reads `harness_settings.json`'s `DetectionBinning` back
into `StarDetectorParams`. `BuildStarDetectorParams` / `BuildDefaultStarDetectorParams` never map it; only
`ApplyDetectionImageContext` does, and the headless runners never call it. `OptimizationDiagnosticRunner.cs:443`
calls `HarnessSettingsStore.ResolveForRun(...)` and **discards the returned value** — it is a pure write
side-effect. So every `optimize --per-run` and every `golden eval` over both banks ran at the default of **1**,
and the file on disk claims otherwise. Anyone reproducing a run from that file gets the wrong binning.

**2. The derivation never ran, on any dataset.** `ResolveForRun` derives the factor from
`RecommendFromHfr(inFocusHfrPixels)` only when the run has an `autofocus_report_Region0.json` carrying a fitted
minimum. No synthetic bank folder has one, so all 17 fell to "kept from base" and inherited `Bin2` from a profile
export — every one of them says so in its own `DerivedNotes`: *"DetectionBinning kept from base (no fitted
in-focus HFR for this dataset)"*.

And it is **circular on exactly the datasets that matter most**: the derivation needs a fitted in-focus HFR, and
D01/D02 have none *because the `MinHFR` gate zeroed the curve* — the very defect F20/F35 are about.

**3. The consequence: an axis of the bank has never been exercised.** `expectedOptimal.detectionBinning = 2` for
**D08, D09, D10, D12, D14, D15, D17** — seven datasets — and all seven have only ever been scored at 1.
`D08_c11_2800mm`'s spec description calls it *"the first detectionBinning=2 dataset (in-focus HFR crosses the
`DetectionBinningResolver` threshold)"*. It has never been one. Note this is also the population
[F38](#f38--the-minhfr-seed-trigger-compares-a-captured-pixel-vertex-against-a-binned-pixel-gate) would bite on,
so the bank cannot currently regression-test that fix either.

**Why it matters.** No prior measurement is invalidated — the factor was a uniform 1 across every arm, so all
A/B comparisons remain internally valid, and this is a provenance and coverage defect rather than a numbers
defect. But it means the bank silently does not test what it says it tests, and a file that records settings a
run did not use is worse than one that records nothing.

**Next step.** Deliberately **not fixed inline**: making the headless path honour a per-run binning changes every
synthetic number at once and would re-baseline the bank mid-wave. Two separable pieces. (a) Stop writing a
derived-looking value that was not derived — either omit the field or mark it `kept-from-base` in the file
itself, not only in `DerivedNotes`. (b) Decide whether the seven `detectionBinning = 2` datasets should be run at
their expected factor, which is a re-baseline and needs its own arm.

**PART (a) DONE 2026-08-06 (wave 6); part (b) DEFERRED, deliberately.** `ResolveForRun` now writes
`DetectionBinningSource` — `derived-from-in-focus-hfr` or `kept-from-base` — as a **field** rather than only as
prose inside `DerivedNotes`. A reader diffs fields; nobody diffs a sentence, which is why seventeen files could
say "kept from base" in prose while presenting `"DetectionBinning": "Bin2"` next to sixteen genuinely exported
values. 2 tests, both discriminating.

**Part (b) is not paired with wave 6's re-baseline on purpose.** Wave 6 re-rendered `D01`/`D02` and the whole
value of that pass is that **exactly one thing changed** — the star field, with every derived parameter (step,
exposure, binning, donut) provably identical. Honouring a per-run binning would have moved a second variable on
7 other datasets in the same wave, and the two effects could not then be separated. It needs its own arm, with
its own before/after, on a bank nobody is simultaneously re-rendering.

**PART (b) MEASURED 2026-08-06 (wave 7) — and it needed NO re-render at all.** The premise that it would was
wrong, and reading one stored field is what showed it. `D08_c11_2800mm`'s own `synthetic_meta.json` says:

> `exposureDefinition`: *"Exposure t solving snr(t) = ... = 10 for the 20th-brightest of 123 on-frame stars ... **at
> binning=2 (captureBinning=1×detectionBinning=2)**, clamped to [0.5, 30] s"*

The bank's exposures for these seven **were already derived assuming binning 2** while every run scored them at 1,
and `detectionBinning` enters no render input (`GenerateSweep` takes centre/step/offsetSteps/exposure). Honouring
the factor therefore REMOVES an inconsistency rather than creating a new configuration.

**Arm G1 — `golden eval --detection-binning 1` vs `2`, `--params default --defocus-donut`, `--match centroid
--match-radius 12`, `--settings` pinned. 14 evals, ~6 minutes, no optimizer.** (The flag is new; `golden eval`
already exposed every other detection override, so this was one `Int(...)` line routed through
`DetectionBinningResolver.ApplyFactor` plus the per-run `PixelScale × factor` the detector itself applies.)

| dataset | recall@all 1 → **2** | recall@high 1 → **2** | precision | `LowSensitivity` FN 1 → **2** | other FN 1 → 2 |
|---|---|---|---|---|---|
| `D08_c11_2800mm` | 0.870 → **0.962** | 1.000 → 0.970 | 1.000 / 1.000 | 41 → **3** | 0 → 9 |
| `D09_c14_3800mm` | 0.761 → **0.906** | 0.978 → **0.989** | 1.000 / 1.000 | 49 → **7** | 7 → 15 |
| `D10_rc16_3250mm_sparse` | 0.852 → **0.939** | 0.966 → 0.948 | 1.000 / 1.000 | 15 → **1** | 2 → 6 |
| `D12_c14_585_afbin2` | 0.562 → **0.675** | 0.879 → 0.813 | 1.000 / 1.000 | 114 → **34** | 30 → 73 |
| `D14_cdk14_2563mm_e47` | 0.841 → **0.987** | 0.984 → **0.992** | 0.999 → **1.000** | 184 → **4** | 6 → 11 |
| `D15_cdk20_3454mm_e47` | 0.764 → **0.917** | 0.931 → 0.874 | 1.000 / 1.000 | 52 → **0** | 8 → 21 |
| `D17_cdk14_oiii5` | 0.875 → **0.990** | 1.000 → 1.000 | 1.000 / 1.000 | 26 → **2** | 0 → 0 |

1. **The physics-derived factor is right and the bank can finally say so.** Overall recall improves on **7 of 7**
   (+0.087 … +0.146) at **zero** precision cost — the single false positive anywhere in the set disappears.
2. **The mechanism is measured, not inferred.** `golden eval`'s FN attribution names it: `LowSensitivity`
   rejections collapse (**184 → 4** on `D14`). Binning raises the per-binned-pixel SNR, so the Sensitivity gate
   stops eating faint stars. Nothing about the gate changed.
3. **It is not free, and the price lands on the BRIGHT tier** — see the new followup below.

**Shipped:** `golden eval --detection-binning N`; `optimize --apply-run-detection-binning` (opt-in, absent ⇒
bit-identical, and it applies ONLY the binning — making the whole per-run `Resolved` authoritative would let a
per-run file shadow the `--settings` every arm pins, F42); and
`HarnessSettingsStore.ResolveRunDetectionBinningFactor`, which prefers the dataset's physics-derived
`synthetic_meta.json` value over the settings file's (`kept-from-base`, part (a)'s field) and **prints which
source it used**. 4 tests.

**ARM G2 — `optimize --per-run` with and without the flag. This is the largest effect in wave 7.**
**σ_focus improves at binning 2 on 7 of 7 datasets, by 17% to 95%:**

| dataset | `J` bin1 → **bin2** | σ_focus bin1 → **bin2** | σ improvement |
|---|---|---|---|
| `D17_cdk14_oiii5` | 0.97854 → **0.99518** | 2.64723 → **0.12033** | **+95.5%** |
| `D15_cdk20_3454mm_e47` | 0.99519 → 0.99518 | 0.73796 → **0.05577** | **+92.4%** |
| `D09_c14_3800mm` | 0.99182 → **0.99505** | 2.40151 → **0.20483** | **+91.5%** |
| `D10_rc16_3250mm_sparse` | 0.97880 → **0.99351** | 2.39831 → **0.45592** | **+81.0%** |
| `D12_c14_585_afbin2` | 0.98653 → **0.99631** | 3.88174 → **0.95750** | **+75.3%** |
| `D08_c11_2800mm` | 0.99466 → **0.99685** | 1.06022 → **0.53243** | **+49.8%** |
| `D14_cdk14_2563mm_e47` | 0.99886 → 0.99860 | 0.26950 → **0.22277** | **+17.3%** |

`J` improves on 6 of 7 (`D14` flat at −0.0003). **σ_focus is what autofocus is for**, and on `D17` it goes from
2.65 focuser steps of uncertainty to 0.12 — a factor of 22. Seven datasets have been scored for their entire
existence at a factor their own physics says is wrong, and it cost between a sixth and nineteen twentieths of
their focus precision. Prior waves' numbers on these seven stay internally valid (the factor was a uniform 1
across every arm) but were measured on a configuration the bank did not intend.

**[F15](#f15--optimize---per-run-overwrites-each-runs-stored-settings) handled deliberately:** the arms were
ordered so the **status-quo binning-1 arm ran LAST**, so the bank's run folders still hold their pre-wave
landings. Verified from the files — `Provenance.CommandLine` reads `--out D:\hf_w7\g2\bin1\…`
([F30](#f30--a-stored-optimized_settingsjson-does-not-say-which-config-produced-it) doing its job). Re-baselining
seven datasets on a decision nobody has taken is exactly the silent drift that rule exists to prevent.

**The adoption decision is NOT taken here.** Acting on this means re-baselining the seven, which is a wave of its
own with its own before/after on a bank nobody is simultaneously re-rendering. What wave 7 delivers is the
measurement and the flag.

**Still owed: the ADOPTION wave.** Wave 7 delivered the measurement and the flag; it did not take the decision.
Everything below is what that wave needs, so it does not have to re-derive it.

**1. The decision to take.** Should the seven be scored at their derived factor permanently — i.e. does
`--apply-run-detection-binning` become the default on the headless path (or the resolved value stop being
discarded at all)? The evidence says yes on recall and emphatically yes on σ_focus, and the counter-evidence is
[F46](#f46--detection-binning-buys-faint-stars-and-quietly-sells-bright-ones-to-the-shapesize-gates).

**2. What changes, and what does not.** The frames do NOT move (`detectionBinning` enters no render input — §3.3),
so **no re-render**, and no `--dry-run` diff is needed to scope one. The **product is not affected either**: the
live app already applies the user's factor through `ApplyDetectionImageContext`. This is a harness/bank change
only, on 7 of 20 datasets. The other 13 are binning 1 under either configuration, which makes them a **free
control — they must come back bit-identical**, and an adoption arm that moves one of them has a fault, not a
result ([F41](#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)'s shape).

**3. It BREACHES two checked-in expectation bands, and that is the real gate.** Scored against
[`docs/synthetic-af-bank-expectations.json`](synthetic-af-bank-expectations.json)'s per-class `recallHighMin`
(both L34 and L47 are 0.90), using wave 7's arm-G1 numbers under config B:

| dataset | class | floor | recall@high bin1 | **bin2** | verdict at bin2 |
|---|---|---|---|---|---|
| `D12_c14_585_afbin2` | L34 | 0.90 | 0.879 | **0.813** | **BREACH** (and it already breached at bin1) |
| `D15_cdk20_3454mm_e47` | L47 | 0.90 | 0.931 | **0.874** | **BREACH** (bin1 was fine) |
| `D08` / `D09` / `D10` / `D14` / `D17` | — | 0.90 | — | 0.948–1.000 | ok |

That file's own rule is explicit: *"A cell landing outside its band is a FLAG that gets triaged into
docs/followups.md — it is NEVER fixed by widening the band here."* So **adoption cannot proceed by relaxing the
band**. Either F46 is understood first and the bright-tier loss is reduced, or the breach is triaged on its
merits and the band is re-derived with a stated justification — which is a legitimate move (the bands were
calibrated at binning 1, a configuration the bank did not intend) but a deliberate, separately-argued one.

**Note the direction is not uniform**: overall recall RISES on all seven while `recall@high` falls on four, so a
re-derivation would tighten some bands and loosen others. Both halves have to be stated.

**4. Ordering, so the adoption arm is the one the bank keeps.**
[F15](#f15--optimize---per-run-overwrites-each-runs-stored-settings): `optimize --per-run` rewrites
`optimized_settings.json` into the run folders. Wave 7 ran the status-quo arm LAST on purpose; an adoption wave
must run it **FIRST** and the binning-2 arm last, then record the mapping in its results doc.

**5. What it does NOT disturb.** `D18`/`D19`/`D20` are not among the seven, so
[F32](#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains)'s
confirmation arm is unaffected by this adoption — the question that dominated wave 7's ordering does not recur
here. Prior waves' A/B comparisons on the seven also stay internally valid (the factor was a uniform 1 across
every arm of every wave); what changes is that future numbers are not comparable to them, which is
[F41](#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary) again and wants the usual
re-run-rather-than-compare-across treatment.

**6. Reproduce the wave-7 measurement it builds on:** `D:\hf_w7\f39b_golden.sh` (arm G1, recall/precision, ~6 min)
and the G2 block of `D:\hf_w7\remaining_arms.sh` (σ_focus / `J`). Per-dataset outputs in `D:\hf_w7\golden` and
`D:\hf_w7\g2`.

**PART (b) ADOPTED 2026-08-06 (wave 8).** `optimize --per-run` now applies each run's derived detection binning
**by default**; `--no-run-detection-binning` is the opt-out, and it exists so every arm this project has already
run stays reproducible on a current binary — rebuilding an old commit to get a control arm is not a control
([F41](#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)). Wave 7's
`--apply-run-detection-binning` is still accepted and is now a no-op, so its scripts keep working *and keep
meaning what they said*. Arms ran **status-quo FIRST, adopted LAST** per §4 above, so the bank's run folders hold
the adopted landing.

**`golden eval` is deliberately NOT re-based.** It is the instrument every prior wave's golden arm was measured
with, and silently defaulting it would re-baseline those comparisons rather than extend them. Instead it now
**states the disagreement**: when the factor it is scoring at differs from the run's derived one, the report says
so and names the source. The two harnesses cannot disagree *quietly*, which was the actual requirement.

**The §3 gate is resolved by applying the expectations file's own rule rather than working around it.**
[F46](#f46--detection-binning-buys-faint-stars-and-quietly-sells-bright-ones-to-the-shapesize-gates) was triaged in
full (mechanism named, binning-specificity established by a control, scaling rule refuted on its own pre-registered
terms) and produced no fix that clears the band. The file says a cell outside its band *"is a FLAG that gets
triaged into `docs/followups.md`"* — it does not say adoption is blocked, and the bands are documentation rather
than a gate (nothing in the codebase reads that file). **So the bands are left untouched and are now telling the
truth:** `D12` at 0.813 and `D15` at 0.874 say *the shipped default `StructureLayers = 4` under-performs at
detection binning 2 on rigs with large wing donuts*, which is a true statement about the product. Re-deriving the
band would have encoded a known defect into the file as an expectation — the outcome the rule exists to prevent,
reached from the direction nobody anticipated. Note `D12` was **already** breaching at binning 1 (0.879), so
adoption deepens that breach rather than creating it; `D15`'s is new to adoption and is fully explained
(`--structure-layers 6` takes it to 0.977).
Reproduce: `D:\hf_w8\adopt\adopt_arms.sh`.

### F40 — The settings handoff shipped in wave 4 had never once been written to disk
**Status:** Done (wave 5 — backfilled and now exercised) · found 2026-08-05 backfilling it

Wave 4 shipped `hocusfocus_star_detection.json`, the `StarDetectionSettingsExport` envelope that makes a bank
landing importable by the NINA UI. A filesystem scan of `D:\` and the user profile before the wave-5 backfill
found **zero files of that name anywhere**.

**It is not a bug in the writer.** `WriteSettingsHandoff` is called from the one `WriteOptimizedSettings` site
with a non-null `baseOptions`, and the format has unit coverage (`OptimizedLandingExportTests`). The cause is
ordering: wave 4's own arms — including the `optA` acceptance run on D18/D19/D20 — ran **before** the handoff was
committed, and no `optimize` pass has run since. Wave 4's write-up says as much in its last line ("the envelope
appears on the next `optimize` pass over a run"), which is correct and reads much weaker than the headline
"**Shipped.** Every landing is now stored in a form the app can import and replay with".

**Why it matters, and it generalizes past this file.** A feature can be written, unit-tested, merged, and
described as shipped while never having *executed* in the environment it exists for. Unit tests prove the mapping;
they do not prove a file arrives on disk. The distance between "the code that writes it is correct" and "it has
been written" is exactly one arm that nobody ran.

**Fixed (wave 5):** `TestApp bank-export-settings --runs <bank-root> [--apply]` converts each folder's existing
`optimized_settings.json` in place, with no optimizer run — so F15 is never touched. **42 landings backfilled
(20 synthetic + 22 real), 0 failed**, every one round-trip verified through `DiffKnobs` *before* being written.
That backfill is the format's first end-to-end exercise outside unit tests.

### F41 — A prior wave's control arm is not a control for a later wave's binary
**Status:** Open (recorded as a standing rule) · found 2026-08-05, twelve minutes into the first wave-5 arm

Wave 5's C0 acceptance criterion was "the feature-OFF landing must be bit-identical to `hf_f23/H_real_A`", the
wave-1 control arm. On `toml999` it failed across nine knobs — Sensitivity 33.3 → 16.7, StarClip 3.5 → 6.875,
MaxDistortion 0.10 → 0.45 — which reads as a serious regression in the change under test.

**It is not one, and the same output says so.** `BaselineJ` also differs, **0.99784 → 0.98348**. `BaselineJ` is
the *current settings*' score: no search is involved in producing it, so a search-side change cannot move it. A
changed `BaselineJ` can only mean the objective or the detector changed — and between wave 1 and wave 5 they
changed at least five times (`5115885` marginal-SNR default, `c2db33e` inert-gate fix, `7b5a695` effective gate,
`238623d` MinHFR seeding/reporting, plus PR #174's bimodal HFR).

**The rule.** An arm directory records what a *particular binary* landed. It is a valid baseline only for
comparisons against **that same binary**. For "is my change inert", build the **immediate parent commit**; for
"what did my change do", use **this binary's own feature-OFF arm**. Reusing an older wave's arm silently measures
every intervening merge and attributes it to the change under test.

**The tell is free and worth checking first.** `BaselineJ` (and any other search-independent quantity) should be
identical between two arms of the *same* binary. If it is not, the binaries differ and no knob comparison between
them means anything — check that single number before reading a diff as a regression.

### F42 — Every build directory silently gets its OWN detector settings, and the run instructions require a new one per arm
**Status:** **Done (wave 6)** — per-user default path, loud bootstrap, and a `UseAdvanced=False` warning that names
the overridden knobs · found 2026-08-05 chasing a `BaselineJ` gap that turned out not to be the code under test

`HarnessSettingsStore.DefaultPath()` is `Path.Combine(AppContext.BaseDirectory, "harness_settings.json")` — the
file sits **next to the exe** — and `ResolveAt` **bootstraps one from the live NINA profile** when it is absent.
Meanwhile the AF-bank run instructions say to build each arm to a separate `-o` directory, because the exe is
file-locked while a run is in progress.

Those two facts compose into a silent confound: **every new build directory bootstraps a fresh settings file from
whatever the profile happens to hold at that moment**, so two arms built minutes apart can run different
detectors. Measured across three wave-5 build dirs:

| option | `exe2` | `exe_base` |
|---|---|---|
| `LocallyAdaptiveBinarization` | True | **False** |
| `ModelPSF` | True | **False** |
| `UseOptimizedSettings` | False | **True** |
| `DetectionDebugMode` | False | **True** |
| `PixelSizeMicrons` / `FocalLengthMm` | 3.8 / **NaN** | 3.76 / 688.0 |

`UseOptimizedSettings = True` alone changes what `BuildStarDetectorParams` returns for the BASELINE — the "before"
every improvement is measured against. `LocallyAdaptiveBinarization` changes candidate formation outright. Each
bootstrap also stamps a differently-named profile snapshot (`Default-2026-08-05T10:54:36`), which is the visible
tell in the run's own log: *"Settings: … (exported … from profile 'Default-…')"*.

**The irony is the point.** This store exists precisely to stop profile state leaking into runs — its own comment
says "a profile-sourced seed is mutable machine state nothing records, and `TryLoad("")` picks whichever profile
is ACTIVE — two runs of the same data minutes apart were seeded from different telescopes." The *bootstrap* path
reintroduces exactly that, and the build-to-a-separate-directory workflow guarantees it fires.

**What it does and does not invalidate.** An arm set run from ONE build directory is internally valid — every arm
shares the file, so a flag remains the only difference (this is true of wave 5's own F32 and F24 arms). What is
invalid is any comparison ACROSS build directories, which is every cross-wave and every
before/after-a-code-change comparison — the ones [F41](#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)
is about. The two findings are the same hazard from two directions.

**Next step.** Pass `--settings <one fixed path>` on every arm, and make the bootstrap loud: print a WARNING when
a settings file is created rather than loaded, since that is the moment an arm silently stops being comparable to
its predecessor. Consider defaulting the path to a fixed per-user location rather than `AppContext.BaseDirectory`,
so a new build directory inherits instead of bootstrapping.

**FIXED 2026-08-06 (wave 6).** All three, plus one the entry did not know about:

1. **The default path now INHERITS.** `DefaultPath()` = an existing file beside the exe (unchanged for any arm
   directory that already has one) → otherwise `%LOCALAPPDATA%\HocusFocusHarness\harness_settings.json`. A fresh
   `-o` directory therefore picks up the same file the last one used instead of minting a new detector. The
   decision is a pure function (`ResolveDefaultPath`) so it is tested without a filesystem.
2. **The bootstrap is loud** — `WARNING` on stderr plus `Logger.Warning`, naming the profile and saying outright
   that the run is not comparable to any earlier arm using a different file.
3. **A `UseAdvanced = False` settings file now warns, and names the knobs it is lying about.**
   `StarDetectionOptions.InitializeOptions` ends in `ConfigureSimpleSettings()`, which in Simple mode runs
   `DerivePresetSettings()` and **overwrites ~16 advanced knobs** from the three `Simple_*` presets — so editing
   `NoiseClippingMultiplier` or `MinHFR` in a pinned file does *nothing*, silently. This is the wave-5 probe set
   where four supposedly different configurations returned identical star counts, promoted from an anecdote in
   [F43](#f43--the-optimizer-wizard-refuses-to-start-unless-the-default-settings-already-produce-a-usable-curve) to
   a check. The overridden keys are MEASURED (hand the file's own bag to a throwaway options instance, diff it
   afterwards) rather than hard-coded, so the list cannot drift from what the class does.

**Checked, and wave 5 is NOT invalidated by (3):** `D:\hf_w5\pinned_settings.json`'s advanced values coincide with
the Typical presets (`NoiseClip 4`, `StarClip 2`, `StructureLayers 4`, `Sensitivity 10`, `MinHFR 1.2`,
`MinBox 5`, `NoiseReductionRadius 3+1`), so the diff is empty and every wave-5 arm ran the detector its file
describes. The hazard was real and did not fire. **Nor is any wave-5 comparison affected by (1)**: those arms
pinned `--settings` explicitly, which bypasses `DefaultPath()` entirely.

7 tests, of which **4 discriminate** (confirmed by neutralizing the three behaviours and re-running: exactly those
4 fail); the other 3 are labelled guards — the beside-the-exe precedence, Advanced-mode silence, and the
agrees-with-the-presets case.

### F43 — The optimizer wizard refuses to start unless the DEFAULT settings already produce a usable curve
**Status:** **Done (wave 5)** for the Live path; **replay DECIDED (wave 6) — extend, but not before the
`BaselineJ = 0` presentation question is answered**, so the implementation is still open · found 2026-08-05 from a
user report on a 40 mm rig

`SeedFitIsUsableAsync` (`StarDetectionOptimizerWizardVM.cs:2869`, called at `:2541`) evaluates the **default seed
params** once and refuses to optimize unless some run yields a finite σ(focus) over ≥ 3 positions. On a Live
sweep it retries exactly one way — widening the focus-recovery exemption to drop starless *outermost* positions —
and then errors out with *"The captured sweep does not produce a usable focus curve at the default detection
settings."*

**This is circular.** The tool exists to find settings that build a usable curve, and it declines to run unless
the settings it has not optimized yet already build one. The guard tests **one point** in a search space the
optimizer is free to explore; `MinHFR` alone spans [0.1, 5.0] against a default of 1.2.

**Measured on `D01_ultrawide_40mm`** — the bank's own 40 mm rig, `golden eval --params default`, per-frame
accepted stars across the sweep:

| focuser | 5964 | 5973 | 5982 | 5991 | **6000 (focus)** | 6009 | 6018 | 6027 | 6036 |
|---|---|---|---|---|---|---|---|---|---|
| `MinHFR = 1.2` (default) | 816 | 1670 | 1773 | 11 | **0** | 5 | 1748 | 1672 | 815 |
| `MinHFR = 0.30` (F35 floor) | 816 | 1670 | 2463 | 204 | **5** | 187 | 2469 | 1672 | 815 |
| `MinHFR = 0.10` (search floor) | 816 | 1670 | 2463 | 208 | **6** | 190 | 2469 | 1672 | 815 |

> **Re-measured on the F44-corrected D01 (wave 6).** The table above was taken on a spatially truncated field; the
> corrected one is 2.3× richer everywhere and **the core is still empty**, which is the whole point of the entry:
>
> | focuser | 5964 | 5973 | 5982 | 5991 | **6000 (focus)** | 6009 | 6018 | 6027 | 6036 |
> |---|---|---|---|---|---|---|---|---|---|
> | `MinHFR = 1.2` (default) | 1859 | 3758 | 4157 | 13 | **0** | 11 | 4182 | 3761 | 1867 |
> | `MinHFR = 0.30` | 1859 | 3758 | 5784 | 476 | **7** | 438 | 5786 | 3761 | 1867 |
> | `MinHFR = 0.10` | 1859 | 3758 | 5784 | 478 | **7** | 443 | 5786 | 3761 | 1867 |
>
> Thousands of stars in the wings, **zero at focus at the default gate**, and lowering the gate buys 7 — against
> `NHard = 3`. The rescue is exactly as thin on a correct field as it looked on a truncated one, so the entry's
> "genuinely thin on the rigs it exists for" framing stands unaltered. The old rows reproduce **exactly** on the
> wave-6 binary, so the two tables differ only by the frames.

The wings detect thousands of stars; the **core of the curve is empty**. That is F20's signature — rejections
concentrated on the INNER frames — and at 40 mm it is expected: in-focus stars are ~1 px, so the `MinHFR` gate
removes precisely the frames the vertex is fitted from.

**And `MinHFR` is the ONLY axis that rescues it.** `MinimumStarBoundingBoxSize` 5 → 3 changes nothing (still 0 at
focus); `StructureLayers` 4 → 2 makes it strictly worse (0 at *both* central positions). So the single knob that
un-blocks this rig class is the one the guard's refusal prevents the search from ever touching.

**Why [F35](#f35--minhfr-should-be-seeded-from-the-sweep-wings-and-neither-available-hfr-statistic-can-size-it)
does not already cover this.** F35's `MinHfrSeed` is applied inside `OptimizeAsync` (`:3076`) — **after** this
guard — and its trigger is `BestFit.Minimum.Y <= MinHFR`, i.e. it needs a **fitted vertex**. When the gate has
destroyed the fit there is no vertex, `seedFitVertexHfr` is NaN, and the rule correctly declines. So F35 rescues
"the vertex sits under the gate" (D01/D02 headless, which still fit) and **not** "the gate destroyed the fit",
which is the strictly worse case and the one users hit.

**The error message's own advice is unreachable too.** It suggests "the recommended detection binning" — but that
recommendation is derived from a fitted in-focus HFR ([F39](#f39--the-harness-records-a-detection-binning-the-run-never-applied-and-7-datasets-have-never-run-at-theirs)),
which does not exist for exactly these runs. The remedy offered requires the thing whose absence caused the
error.

**FIXED 2026-08-05 (wave 5).** Before refusing, `TryRescueWithLowerMinHfrAsync` probes a **lowered gate** —
`MinHfrSeed.SeedFloor`, then the curated variable's own `Lower` bound, least-aggressive first. The ladder is read
from `OptimizerVariable.CreateCuratedSet` rather than written as constants, so the guard can never admit a rig on
a gate the search is not allowed to reach, nor refuse one it could have rescued because a constant drifted. If a
probe makes the curve fittable, the run proceeds **and the rescued gate becomes the seed** via the existing
`OptimizerSettings.MinHfrSeedFloor` — F35's mechanism, triggered by *feasibility* instead of by a vertex.
`OptimizeAsync` takes the **lower** of the two floors, since both only ever lower the gate and either may be
absent.

The rescue is **not silent**: `MinHfrRescueNotice` tells the user which gate failed and what it was lowered to,
because the wizard is then reporting results from a gate they did not choose — and on a short focal length that
is the setting they most need to know about.

Three things the fix deliberately does **not** do. It does not relax the bar to "the counts look healthy": the
probe asks only whether a curve is *determinable*, because the rescue is genuinely thin on the rigs it exists for
(6 stars on D01's in-focus frame against `NHard = 3`) and any richer bar re-rejects exactly that population —
raising the counts from there is the search's job. It does not mutate the caller's seed (probes run on a clone;
callers reuse one `StarDetectorParams`, and an in-place write is the wave-3 seed leak). And it does not remove
the refusal: a sweep no probed gate can fit is still refused, which is what the guard was written for.

**LIVE only, and the replay case is left open on purpose.** Replay gates on the user's CURRENT settings because a
saved run exists only because those settings could already focus — a decision locked by
`SeedGuard_ReplayMode_GatesOnBaseline`, which caught a first version of this fix that extended the probe to both
modes. Probing the seed there would quietly convert replay into a seed-gated path. **Open question:** whether a
saved run whose current settings cannot fit deserves the same rescue. The circularity argument applies equally;
the counter-argument is that such a run should not have been captured. Not changed as a side effect of this one.

> **REPLAY DECIDED (wave 6): extend the rescue, but not as a drive-by — the premise it rests on is measurably
> false, and the change needs a UI answer the wizard does not have yet.**
>
> *The premise.* `SeedFitIsUsableAsync` gates replay on `r.Baseline` (`:2882`) because "a saved run exists because
> those settings could already focus". Three populations falsify that, and all three are real today:
>
> | population | why the premise fails | evidence |
> |---|---|---|
> | **failed AF runs** | `KeepFramesForReview` saves sweeps that did NOT focus — a failed AF is precisely what a user brings to the optimizer | `KeepFramesForReview = True` in the shipped AF options |
> | **settings changed since capture** | the wizard's whole purpose is changing detection settings; "current" need not be what captured the run | — |
> | **runs never captured by this profile at all** | bank folders and other people's data are loaded routinely | the live app's own `LastSelectedLoadPath` read `D:\SyntheticAutofocusBank\D01_ultrawide_40mm\attempt01` — a dataset that yields **zero** in-focus stars at the default gate |
>
> So the asymmetry is not "live is the hard case and replay is the easy one"; it is that replay refuses a case it
> demonstrably receives. `D01` is the counterexample in the bank itself.
>
> *What to implement.* Keep the baseline gate FIRST — when the current settings fit, nothing changes, so the
> premise case stays exactly as it is. Only on the refusal path, probe the same `TryRescueSeedAsync` ladder that
> Live uses, and refuse only when no probed gate produces a **scorable** curve. `SeedGuard_ReplayMode_GatesOnBaseline`
> then needs rewriting rather than deleting: its contract becomes *"replay refuses when neither the baseline nor any
> probed gate can fit"*, and it must still fail when the ladder is removed.
>
> *Why this is NOT shipped in wave 6.* A replay whose baseline cannot fit has `BaselineJ = 0` — measured directly:
> every `D01`/`D02` arm this wave printed `Current settings J: 0`. The wizard's headline is an improvement
> *percentage against the baseline*, and a percentage against zero is not a number. So the change needs a decision
> about what the summary says when there is no baseline to improve on ("no usable curve at your current settings"
> rather than a ratio), which is UI work with its own review. Implementing the guard without it would replace a
> confusing refusal with a confusing result.

**EXTENDED 2026-08-05, same session, after the reporter tried it and it STILL failed.** The first fix was right
about the circularity and wrong about the bar. Lowering `MinHFR` made the reporter's curve **fittable** but not
**scorable**: frames still fell under the objective's `NHard` floor, so `J` was identically 0 and the search had
no gradient. Handing the search that seed is indistinguishable, to the user, from refusing outright — the wizard
appears to run and produces nothing.

**Measured on the reporter's own 61 MP sweep** (`FOCALLEN 40.0`, `XPIXSZ 3.76` → **19.4 arcsec/px**, 4 s, gain
100, step 250, `FOCPOS 25000` = true focus). Accepted stars per position:

| seed | 23750 | 24000 | 24250 | 24500 | 24750 | **25000** | 25250 | 25500 | 25750 | 26000 | 26250 | min | `J` |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| defaults (`MinHFR` 1.2) | 0 | 2 | 2 | 6 | 103 | **0** | 95 | 3 | 1 | 2 | 0 | 0 | **0** |
| `MinHFR` 0.3 (first fix) | 0 | 2 | 2 | 6 | 103 | **14** | 95 | 3 | 1 | 2 | 0 | 0 | **0** |
| + `Sensitivity` → 0 | — | — | — | — | — | — | — | — | — | — | — | 0 | **0** |
| + `NoiseClippingMultiplier` → 1.0 | — | — | — | — | — | — | — | — | — | — | — | **3511** | **0.2477** |

(The last two rows are reported by their minimum because the point is the `NHard` floor, which is what `J`
turns on; the per-position detail is in `D:\hf_w5\user40mm\*/optimize_summary.txt`.)

Two things that only measurement would have given: **`MinHFR` is real but not sufficient** (0 → 14 stars at
focus, still unscorable), and **the binding gate is `NoiseClippingMultiplier`, not `Sensitivity`** — relaxing the
acceptance gate alone leaves `J` at 0. From the `MinHFR`-only seed, **80 evaluations could not move `J` off 0**;
from the relaxed seed the optimizer converged immediately (`J` 0.246 → 0.262, min 43 stars).

**So the rescue now walks a LADDER and its bar is SCORABLE, not merely fittable:** `MinHFR` → `SeedFloor`, then
its search-space lower bound, then `+ Sensitivity` lower, then `+ NoiseClippingMultiplier` lower — least
aggressive first, every bound read from `OptimizerVariable.CreateCuratedSet` so the guard can never seed the
search outside what it may reach. Acceptance is `JTotal > 0`, computed with the wizard's own
`objectiveConstants`, so "the search has something to climb" is decided by the SAME number the search maximizes.
The winning configuration becomes the fresh pass's **seed**.

**Two false trails worth recording, both killed by checking the instrument rather than the theory.** D01, the
bank's own 40 mm dataset, looked like the obvious proxy and is not one: its sweep never reaches the 30 px
candidate size where the defocus-aware distortion relaxation engages, so `--defocus-gates` is **bit-identical**
there while the reporter's frames are far more defocused. And four probe runs returned **identical** star counts
across four supposedly different configurations — the profile had `UseAdvanced = False`, so Simple mode was
ignoring every knob being set. Identical results across different inputs is the tell that the instrument, not
the subject, is the thing being measured.

**Still open for this rig class.** The bank has no dataset resembling it (40 mm at heavy defocus, signal-starved
short exposure): D01 is 40 mm but nowhere near that defocus, so nothing regression-tests this population. And the
step size is its own problem — at 250 the usable band is about one step wide (103 stars at 24750, **0** at 25000,
95 at 25250), which is [F18](#f18--step-size-is-sized-by-curve-geometry-alone-so-the-sweep-outruns-what-the-detector-can-see).

Tests: 5, of which **3 fail** when the fix is removed (2 for the probe, 1 for the scorable bar); the other two are
labelled guards (inertness on a healthy sweep, and the floor-combination helper).

### F44 — The synthetic camera queries the catalog for at most ONE CELL, so a wide field is rendered starless outside a small central patch
**Status:** **Done (wave 6)** — code fixed wave 5, both rows re-baselined wave 6 and `suspect` cleared; **`D01` was
the only affected row** ·
found 2026-08-05 from a user report: "why is only a portion of the sensor getting rendered with stars?"

`StarFieldCompositor` computes the field correctly — `DiagonalFovDegrees × 1.05` — and hands it to
`AstapCatalogReader.Query`, which calls `AstapCellGeometry.FindAreas`. That method does two things which are
right for ASTAP and wrong for a renderer:

1. **It clamps the field**: `var fov = Math.Min(fovRadians, MaxFovRadians(partitioning))`, where `MaxFovRadians`
   is **5.142857°** (1476-cell) or **9.53°** (290-cell) — i.e. exactly one cell width.
2. **It then samples only the FOUR CORNERS** of that clamped box and returns the ≤4 cells they fall in. Its own
   summary says so: *"the 1–4 cells whose union covers the square field of view"*.

Both are a faithful port of ASTAP's `find_areas` (the doc comment says "reference behavior"), and both are
correct **for plate-solving**, where fields are a few degrees and 4 corner cells genuinely cover them. They are
not correct for rendering a wide field.

**Measured on the reporting rig** (9576 × 6388 at 3.76 µm, `FOCALLEN 40.0`):

| quantity | value |
|---|---|
| frame | **48.5° × 33.4°** |
| diagonal FOV the compositor requests | **59.67°** |
| what `FindAreas` clamps it to | **5.142857°** |
| clamp factor | **11.6×** |
| union of the ≤4 selected cells | at most ~10.3° × 10.3° |

So the catalog is queried over roughly a 10° patch of a 48.5° × 33.4° frame, and everything outside it renders
**starless** — which is exactly the bounded rectangle of stars the reporter saw, sitting in an otherwise empty
(noise-only) sensor.

**Why nothing caught it.** The compositor warns only when the query returns **no** stars
(`StarFieldCompositor.cs:303`); a partially-covered field returns plenty, so no warning fires and the frame looks
plausible. **The bank does exercise it — on two rows — and nothing checked.** `D01_ultrawide_40mm`'s own spec
description even names the hazard: *"capped at mag<=10.5 so the ~39deg diagonal FOV catalog query (design risk
R5) stays tractable"*. R5 was recorded as a **tractability** risk (too many stars); the actual failure was the
opposite — the query silently returns too few, over too small a patch.

### Affected bank datasets — SUSPECT, refresh required

Diagonal FOV × 1.05 against the cap, computed over all 20 rows (sensor dimensions from `SensorRegistry`,
binning applied). The verdict is the same under either partitioning (5.14° or 9.53°):

| dataset | sensor | FL | diagonal FOV | requested | × over the 5.14° cap | status |
|---|---|---|---|---|---|---|
| **`D01_ultrawide_40mm`** | IMX571 | 40 mm | **38.91°** | 40.85° | **7.9×** | **SUSPECT — refresh** |
| **`D02_rich_135mm`** | IMX571 | 135 mm | **11.95°** | 12.55° | **2.4×** | **SUSPECT — refresh** |
| `D03_redcat_250mm` (next widest) | IMX533 | 250 mm | 3.66° | 3.85° | 0.7× | ok |
| the remaining 17 | — | ≥ 250 mm | ≤ 2.94° | ≤ 3.09° | ≤ 0.6× | ok |

**Only D01 and D02 are affected**, and both must be **re-rendered in the next wave** once the query is fixed.
Everything from D03 down sits comfortably inside one cell and is unaffected.

**What this does and does not invalidate on those two rows.** The stars that ARE rendered are rendered correctly
— right PSF, right defocus, right photometry — so results about **gate thresholds and HFR-versus-focus behaviour**
survive. What does not survive is anything reading **counts, recall, precision, or position**: the field is
spatially truncated, so star totals are low by an unknown factor and the surviving stars occupy one patch of the
sensor.

Specifically at risk, and to be re-checked after the refresh:

- **[F35](#f35--minhfr-should-be-seeded-from-the-sweep-wings-and-neither-available-hfr-statistic-can-size-it)'s
  headline validation rests on exactly these two rows** — "D01 and D02 go from `FinalJ` exactly 0 to a real
  landing". The *mechanism* (the `MinHFR` gate zeroing an undersampled rig's fit, and seeding rescuing it) is a
  threshold result and should hold; the `FinalJ` values and star counts are measured on a truncated field.
- **[F43](#f43--the-optimizer-wizard-refuses-to-start-unless-the-default-settings-already-produce-a-usable-curve)**
  cites D01's per-frame counts (0 stars at focus, ~1700 in the wings). Its conclusion was independently confirmed
  on the reporter's own real 40 mm frames, so it stands, but the D01 figures are indicative rather than exact.
- The **wave-5 [F24](#f24--donut-detection-costs-precision-even-where-donuts-exist-and-badly-where-they-do-not--it-costs-recall-where-it-is-not-needed)
  arms** covered all 20 datasets; the D01 and D02 rows of that table are suspect. The verdict is unaffected — it
  turned on D06/D09/D14, none of which are.
- Any **sensor-model, tilt, or region-based** result derived from D01/D02, since those read star *position*.

**Consequences beyond the missing stars.** The rendered field is not just sparse but *spatially truncated*, so
anything that reads position — the aberration inspector's sensor model, tilt calibration, region-based AF, the
golden-set geometry — sees a synthetic frame whose stars occupy one corner-ish patch of the sensor. Any result
derived from a wide-field simulator frame is suspect until this is fixed.

**FIXED 2026-08-05 (wave 5).** `AstapCellGeometry.FindAreasCovering` enumerates **every** cell intersecting a
cone of the requested radius, walking the band table directly: per dec band, the RA half-span comes from the
spherical law of cosines evaluated at both band edges and at the cone centre, padded by one whole cell each side.
`AstapCatalogReader.Query` uses it **only when the field exceeds the cap** — below it the two agree by
construction (a box at most one cell across touches at most four cells, and its corners hit all four), so every
existing narrow-field result stays bit-identical and `FindAreas` remains an untouched, honest ASTAP port.

**Measured on `D01_ultrawide_40mm`, re-rendered:**

| | truth stars on frame | x extent | y extent | sensor grid occupancy |
|---|---|---|---|---|
| before | 8107 | 431 – 5921 | **741 – 2965** | **125/256 (49%)** |
| after | **19212** | −1 – 6249 | **0 – 4176** | **256/256 (100%)** |

Half the sensor was empty; it now fills edge to edge with 2.4× the stars.

Tests: an independent brute-force oracle (dense sphere sampling, no shared code with the routine under test)
asserts no cell inside the cone goes unqueried, across six pointings including RA-wrap and both poles, on both
partitionings; plus whole-sky, filename-encoding and supersets-the-reference checks. A `touchesPole` fast path
was written, measured against those tests, found to change nothing (`MaxRaHalfSpan` already returns π there) and
**removed** rather than left as an untested branch.

**RE-BASELINED 2026-08-06 (wave 6) — and only ONE of the two rows was ever affected.** Both were re-rendered into
the bank with the fixed query. Every derived parameter is unchanged on both (step 9 / 6, exposure 0.5 s,
`detectionBinning` 1, donut off, identical sweep positions), so the star field is the *only* thing that moved:

| row | in-focus truth stars | x extent | y extent | 16×16 grid occupancy | frames |
|---|---|---|---|---|---|
| `D01` before | 8107 | 431–5921 | **741–2965** | **125/256 (49%)** | — |
| `D01` after | **19212** | −1–6249 | 0–4176 | **256/256 (100%)** | changed |
| **`D02` before** | **3594** | −2–6247 | −1–4176 | **256/256 (100%)** | — |
| **`D02` after** | **3594** | −2–6247 | −1–4176 | **256/256 (100%)** | **BIT-IDENTICAL** |

**`D02_rich_135mm` was never affected**: its re-rendered FITS are byte-for-byte identical to the old ones, and its
golden star set is the same set (3536 stars, 25 unresolved, reordered only — the new query enumerates cells in a
different order). **So no number derived from D02 ever needed re-measuring**, including F35's D02 landing.

**The suspect criterion over-predicted, and this is the useful correction.** The table above marked D02 suspect on
*diagonal FOV ÷ one-cell cap* = 2.4×, which is a proxy for the real question: *do the ≤ 4 corner cells `FindAreas`
returns cover the field?* Cells are equal-area, so at `D02`'s **Dec +61.45°** one cell spans ≈ 1/cos(61.45) ≈ 2.1×
more RA degrees than at the equator, and the four corner cells covered an 11.95° field comfortably. **The cap ratio
is not the criterion; cell coverage at the field's declination is.** A ratio-based rule flags rows that are fine
(D02) and would keep flagging them forever. D01 at 7.9× is far enough over that no declination saves it.

`suspect` is now cleared on both rows, so `synth-bank` no longer warns. What was re-measured on D01, and what it
changed, is in [`docs/synthetic-af-bank-followups-wave6-results.md`](synthetic-af-bank-followups-wave6-results.md).

### F35 — `MinHFR` should be seeded from the sweep WINGS, and neither available HFR statistic can size it
**Status:** Done (wave 3) · found 2026-08-03 answering "how far can `MinHFR` safely come down?" for
[F20](#f20--below-minhfr-the-autofocus-objective-collapses-to-exactly-zero-with-no-diagnostic)

**Shipped 2026-08-03** — `MinHfrSeed.Resolve` + `OptimizerSettings.MinHfrSeedFloor`, applied at the engine seam
ahead of θ0. Two of this entry's own premises were wrong and are corrected below.

Synthetic bank, 17/17, `EXIT=0` where the wave-1 arm exited 3. The seed fires on exactly **three** datasets —
D01 (fit vertex 0.762 px), D02 (0.749) and D03 (1.015), i.e. precisely the undersampled rigs this entry and F20
name:

| dataset | seed? | `MinHFR` new / control | `FinalJ` new / control |
|---|---|---|---|
| `D01_ultrawide_40mm` | **YES** | 0.100 / 1.200 | **0.99018 / 0.00000** |
| `D02_rich_135mm` | **YES** | 0.550 / 1.200 | **0.99656 / 0.00000** |
| `D03_redcat_250mm` | **YES** | 0.300 / 0.450 | 0.99549 / 0.99486 |
| the other 14 | no | **identical** | **identical to 5 dp** |

> **RE-MEASURED 2026-08-06 (wave 6) on the F44-corrected frames — the headline survives intact.** `D01`'s field
> was spatially truncated when the table above was produced ([F44](#f44--the-synthetic-camera-queries-the-catalog-for-at-most-one-cell-so-a-wide-field-is-rendered-starless-outside-a-small-central-patch));
> `D02`'s, it turns out, was not (its re-rendered frames are bit-identical). Re-run on **one binary**, with the
> control produced by the new `optimize --no-min-hfr-seed` rather than by an older commit ([F41](#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)):
>
> | dataset | frames | fitted vertex | `FinalJ` seed OFF | `FinalJ` seed ON | landed `MinHFR` |
> |---|---|---|---|---|---|
> | `D01` | **old (truncated)** | 0.7621 | **0.000000** | 0.990358 | 0.100 |
> | `D01` | **new (full field)** | 0.7712 | **0.000000** | **0.991551** | 0.100 |
> | `D02` (frames bit-identical) | — | 0.7488 | **0.000000** | 0.996510 | 0.3625 |
> | `D03` | — | 1.0154 | 0.995880 | 0.996543 | 0.300 |
> | **`D05` control** | — | 1.7882 | **0.999706** | **0.999706** | 1.48125 |
>
> **"`FinalJ` exactly 0 → a real landing" is unchanged on a field with 2.4× the stars**, which is what a
> gate-threshold result should do. `BaselineJ` is identical between the two arms of every dataset (F41's free
> check), and `D05` is bit-identical seed-on vs seed-off — the boring control that caught the wave-3 seed leak,
> still boring.
>
> **The drift is bounded by a dataset that could not move.** `D02`'s frames are provably identical to wave 3's, so
> its `FinalJ` 0.99656 → 0.996510 is **pure wave-3→wave-6 binary drift: 5×10⁻⁵**. `D01`'s old-frames landing moved
> 0.99018 → 0.990358 (+1.8×10⁻⁴) — same order. So `D01`'s **+1.2×10⁻³** from the re-render is roughly 6× the
> drift and is attributable to the frames, not the binary.

**D01 and D02 go from `FinalJ` exactly 0 to a real landing — the whole of F20's defect — and 14 of 17 landings
are bit-identical to the control**, including D05 at 1.450. On every rig the change was not designed for it is a
no-op, not a small perturbation. Zero datasets that did not trigger landed at the seed constant.

**Real bank: 19/19 landings bit-identical to the control, seed fired on ZERO runs** (per F33's "both banks"
rule). Exactly inert, not "within noise". It also disproves F20's claim that its real-bank runs are the same
defect — see the correction in [F20](#f20--below-minhfr-the-autofocus-objective-collapses-to-exactly-zero-with-no-diagnostic).
**The fix is real and measured, but its real-bank population on this bank is empty**, so the user-facing benefit
is unproven on real frames until an undersampled short-focal-length rig enters the bank. Do not claim otherwise.

Two things the run adds. **`BaselineJ = 0` is not by itself evidence of this defect**: D17 reads 0 like D01/D02,
but its vertex is above the gate, the seed correctly declines, and its landing is unchanged — it was already
escaping on its own. And **the censoring bias is visible but survivable**: D01's fit read 0.762 px against a
0.238 truth vertex (3.2× high) and still cleared 1.2, which is exactly why a *trigger-only* use of the fit works
where a *sizing* use cannot — `0.8 × predicted` would have given 0.61 and re-gated the rig completely.

> **The validation run caught a bug in the fix, and the D05 control is what caught it.** The first bank pass
> read 17/17 hard-floor PASS — and was wrong. D05 landed `MinHFR` **0.300** (the seed constant) despite a
> 1.804 px truth vertex that must never trigger; 14 of 17 datasets landed at exactly 0.300 while the log printed
> the seeding line exactly **once**. Cause: `OptimizeAsync` wrote `seed.MinHFR` **in place**, and callers reuse
> one `StarDetectorParams` — TestApp builds its context once *outside* the per-dataset loop
> (`OptimizationDiagnosticRunner.cs:306` vs `:426`), and the wizard passes a live reference to `runs[0].Seed`.
> So D01's genuine firing permanently re-gated the other sixteen, each of which then saw a seed already at the
> floor and correctly declined to trigger. **One real firing, sixteen silent ones, order-dependent results.**
> Fixed by cloning the seed; the regression test was confirmed to FAIL without the fix. Note the first unit test
> asserted `seed.MinHFR == SeedFloor` — it codified the bug and passed. **D05 exists purely to be boring, and a
> boring reading was the only thing that separated a working fix from one that had re-gated the whole bank.**

> **Correction 1 — the trigger statistic is not available where this entry says it is.** `BestFit.Minimum.Y` is
> "already read by the wizard as `baselineInFocusHfr`" only **after** the search, in `BuildSummaryAsync`
> (`StarDetectionOptimizerWizardVM.cs:3115`). The shared engine seam cannot see it at all:
> `RunEvaluationMetrics` carries the vertex **X only** (`BestFocusPosition`, `RunEvaluationData.cs:817-818`) and
> has no `Minimum.Y` field of any kind. Resolved by splitting the rule — the **caller** computes the trigger
> (the wizard and TestApp `optimize` both already hold a pre-search `RunEvaluationResult`), the **engine**
> applies the clamp. `synth-validate` and `tilt` fit no curve before optimizing, so they deliberately do not opt
> in and stay bit-identical.
>
> **Correction 2 — step 3 rests on a grid that does not exist, so it is NOT a fix.** `Continuous` is not
> quantized: `OptimizerVariable.Quantize` is *identity + clamp* for it (`:83-92`), and `InitialStep` is the
> **initial pattern-search stride**, which Phase B halves on every non-improving sweep down to
> `InitialStep × StepFloorFraction` = **0.03125**. So the axis already resolves to ~0.03 px — the "0.45 → 0.20
> is a 2.25× jump" framing describes only the first descent stride. And on the motivating rigs it is moot in
> both directions: `MinHFR` is absent from Phase A (`CoarseGrid` is `Sensitivity × StarClippingMultiplier` only,
> `:299-303`), so it moves solely through strictly-improving Phase-B moves — which on D01/D02's flat `J` never
> happen **at any stride**. Retuning `InitialStep` would perturb every rig that *does* have a gradient while
> doing nothing for the ones this entry is about. **Not shipped, deliberately.**

[F20](#f20--below-minhfr-the-autofocus-objective-collapses-to-exactly-zero-with-no-diagnostic)'s proposed fix —
"if the median in-focus HFR is at or below `MinHFR`, seed `MinHFR` beneath it" — **cannot be implemented as
written, because it is circular.** On `D01_ultrawide_40mm` the in-focus frame detects **zero** stars at the
default gate, so there is no median in-focus HFR to read. The measurement that would trigger the fix is the one
the gate has destroyed.

**The wings break the circularity, and cost nothing.** Far from focus the PSF is large, so those frames are
unaffected by the gate and richly populated — D01's four outer frames carry **816–2002** accepted stars each at
`MinHFR` 1.2. The hyperbola fit already succeeds on them (**R² = 0.911**), and `BestFit.Minimum.Y` is already
computed and already read by the wizard as `baselineInFocusHfr`. So the trigger is available today with no new
measurement: *fit the curve from whatever frames produce stars, and compare the predicted vertex against
`MinHFR`.*

**But the fit must TRIGGER the adjustment, not SIZE it — both available statistics are biased the same way.**

| statistic | reads | truth | error |
|---|---|---|---|
| wing-only hyperbola vertex (frames with ≥100 stars) | **0.548 px** | 0.238 px | **2.3× high** |
| median measured HFR of survivors at focuser 5991 | **1.33 px** | 0.606 px | **2.2× high** |
| median measured HFR of survivors at focuser 6009 | **1.68 px** | 0.606 px | **2.8× high** |

The second and third are **left-censored at `MinHFR` itself** — only stars measuring above the gate can be
seen, so the surviving sample's median is bounded below by the very knob being tuned. This is structurally the
same trap [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall)
wave 1 hit, where the marginal-SNR statistic was left-censored at the Sensitivity gate. The first is a
different mechanism (a hyperbola's vertex parameter is the one the wings constrain worst) with the same sign.

**Both biases understate how far the gate must come down**, so a plausible rule like `MinHFR = 0.8 × predicted`
gives 0.44 on D01 — which still gates its true 0.238 px vertex completely. The fix would have looked applied and
changed nothing.

**How low is safe: measured, and the answer is "as low as you like".**
`golden eval --params default --min-hfr <M>`, exact precision against synthetic truth, 8 values from 1.2 to 0.1:

recall@high per dataset, **FP = 0 in all 32 configurations**:

| `MinHFR` | D01 (40 mm) | D02 (135 mm) | D03 (250 mm) | D05 control (1000 mm) | D01 vertex-frame stars |
|---|---|---|---|---|---|
| 1.2 (default) | 0.129 | 0.451 | 0.393 | 0.985 | **0** |
| 0.9 | 0.152 | 0.507 | 0.535 | 0.985 | **0** |
| 0.7 | 0.160 | 0.567 | 0.581 | 0.985 | 2 |
| **0.5** | 0.164 | 0.588 | 0.585 | 0.985 | **5** |
| 0.35 | 0.164 | 0.594 | 0.585 | 0.985 | 5 |
| 0.25 | 0.165 | 0.595 | 0.585 | 0.985 | 5 |
| 0.15 | 0.165 | 0.595 | 0.585 | 0.985 | 5 |
| 0.1 | 0.165 | 0.595 | 0.585 | 0.985 | 6 |

> **The D01 column re-measured on the F44-corrected frames (wave 6).** The old-frames column above reproduces
> **exactly, to three decimals, on the wave-6 binary** — a free F41 check that this detector has not drifted for
> this measurement — and the corrected field changes the conclusion in no respect:
>
> | `MinHFR` | D01 old (truncated) | **D01 new (full field)** | FP, both | D01 vertex-frame stars, old → new |
> |---|---|---|---|---|
> | 1.2 | 0.129 | **0.127** | 0 | 0 → **0** |
> | 0.9 | 0.152 | 0.150 | 0 | 0 → 0 |
> | 0.7 | 0.160 | 0.157 | 0 | 2 → 4 |
> | 0.5 | 0.164 | 0.161 | 0 | 5 → 6 |
> | 0.35 | 0.164 | 0.162 | 0 | 5 → 7 |
> | 0.25 – 0.15 | 0.165 | 0.162 | 0 | 5 → 7 |
> | 0.1 | 0.165 | 0.162 | 0 | 6 → 7 |
>
> **Zero false positives at every value on the corrected field too, and the gain still saturates by 0.5.** Recall
> is fractionally *lower* because the golden grew with the field (TP 8253 → **19039**, and the stars the fix added
> are predominantly faint edge-of-frame ones), not because detection got worse: the wing frames go from ~816/1670
> accepted stars to ~1859/3758. The claim this table exists to support — *how low is safe* — is unchanged.

**Zero false positives at every value on every dataset**, and the recall gain saturates by 0.5 at the latest.
`MinHFR` is a second line of defence — hot-pixel filtering is separate and enabled by default — and on this bank
it is not carrying any of the load. **0.25–0.35 captures all the available gain on every class.**

**`D05_tec140_1000mm` is the control that makes a GLOBAL seed defensible.** Its in-focus HFR is 1.77 px, well
clear of every gate value tested, and its recall is **flat at 0.985 from 1.2 all the way to 0.1** — not one star
gained or lost. A rig that does not need the seed is provably unperturbed by it, so the adjustment does not have
to be conditioned on first detecting the wide-field case (which is the detection the gate has already
prevented). Without this row the seeding rule would need a trigger it cannot evaluate.

**The largest gain is D03, not D01.** `D03_redcat_250mm` moves **0.393 → 0.585 (+0.192)** against D01's +0.036.
The fix helps *marginally* undersampled rigs most, not the extreme one — D01 is dominated by candidate-formation
losses that this gate does not touch (see the attribution table below). Anyone scoping this work off D01 alone
will both underestimate the benefit and misattribute where it lands.

**The mechanism is the hard floor, not recall — and that reframes F20.** Lowering the gate moves D01's recall
only 0.129 → 0.165. What it actually does is put stars back on the **vertex frame**: 0 → 2 at 0.7, and **0 → 5
at 0.5**, which is the first value that clears the objective's `NHard` = 3 stars-per-frame requirement. That is
the whole of `FinalJ = 0.00000`. So the fix is real and worth shipping, but its success criterion is *"the hard
floor passes and the search gets a gradient"*, **not** *"recall recovers"*.

**And `MinHFR` is only 5% of D01's recall problem.** False-negative attribution at the two extremes:

| gate | at `MinHFR` 1.2 | at 0.1 |
|---|---|---|
| NO CANDIDATE (structure gap) | **18801** | **18800** |
| TooSmall | 6398 | 6398 |
| LowSensitivity | 6301 | 6301 |
| TooDistorted | 2387 | 2387 |
| NotCentered | 2228 | 2228 |
| **TooLowHFR** | **1877** | **1** |

`TooLowHFR` is 1877 of ~38500 missed golden stars. Removing it entirely leaves every other gate untouched.
**The W class's recall is lost in candidate FORMATION** — the structure map never proposes 49% of them, and
`MinimumStarBoundingBoxSize` (default 5 px) rejects another 17% as `TooSmall` — so anyone expecting the
`MinHFR` fix to lift D01 toward the 0.90 W-class band will be disappointed, and the band stays missed for a
reason this entry does not address.

**Why it matters.** F20 is the strongest surviving finding on this bank and its fix was one circular statistic
away from being unimplementable. The censoring point generalises past `MinHFR`: **any gate whose threshold is
tuned from the surviving sample is tuning against a distribution it truncated.** That has now bitten twice
(Sensitivity in F23, HFR here), which makes it a review question rather than a coincidence.

**Next step.** Three pieces.
1. Seed `MinHFR` when `BestFit.Minimum.Y` (from the wing-populated fit) sits at or below it — **trigger only**.
2. Size the seed from **pixel scale and sampling**, not from either censored statistic. The measured floor is
   0.25–0.35 px with no precision cost on this bank; validate on D01–D03 where the truth vertex is known
   (0.238 / 0.280 / ~0.97 px). Note the gate is `star.HFR <= p.MinHFR`, inclusive, so the seed must be strictly
   below the HFR to be kept.
3. Widen the search where it matters: `OptimizerVariable`'s `MinHFR` axis is `Continuous(0.1, 5.0, step 0.25)`,
   so from 1.2 the reachable grid is 0.95 → 0.70 → 0.45 → 0.20. Only 0.20 clears D01, it is four steps away with
   `J` flat the whole way (F20's cold-start plateau), and a 0.25 step is far too coarse in a sub-pixel regime —
   0.45 → 0.20 is a 2.25× jump. A seed makes the walk unnecessary; a finer low-end step makes it survivable.

**The risk to design against, unchanged from F20:** the objective rewards star count, so nothing pulls a seeded
`MinHFR` back up. It is a knob the search cannot climb out of, which is why the seed must come from geometry
rather than from a measurement the gate itself shaped.

### F36 — Which pre-wave-2 entries actually rested on the broken precision metric: audited, and it is none of them
**Status:** Done (wave 3) · raised 2026-08-03 as "F1–F8, F18, F21, F25, F26 have not been re-verified"

After [F31](#f31--synthetic-bank-precision-is-not-exact-the-golden-omits-real-stars) voided every `/3` precision
number and [F23](#f23--the-optimizer-objective-has-no-precision-term-so-it-trades-precision-away-for-marginal-recall)/[F24](#f24--donut-detection-costs-precision-even-where-donuts-exist-and-badly-where-it-does-not)
were re-measured, the open question was whether the *other* entries measured on those same landings needed the
same treatment. That was an inference from what changed, not a check. **It has now been checked, entry by entry.**

**Result: no entry's conclusion depends on the repaired precision metric.** Every one of F1–F8, F18, F21, F25 and
F26 rests on σ_focus, recall, R², or landed parameter values — and `recallHigh` derives only from `match.Pairs`
(`BankVerifyRunner.cs:488`), which the `/5` repair never touched. One clause is the exception, below.

| entry | rests on | verdict |
|---|---|---|
| F1, F2 | donut-flag thresholds (`frac`, HFR, bbox) | unaffected |
| F3 | recall@≥12 on the real bank | unaffected — but see below |
| F4 | σ_focus, `J`, sensor-model stars/R² | unaffected |
| F5, F6, F18, F21, F25 | σ_focus, R², curve geometry | unaffected |
| F7 | σ_focus, and recall "essentially unchanged" | unaffected |
| F8 | landed Sensitivity/StarClip corners | unaffected — but see below |
| F26 | binning/convergence, **plus one precision clause** | ~~one clause unverified~~ → **verified and REFUTED at `/5` (wave 5)** |

**The one genuinely unverified clause.** F26 states that with the F23 marginal-SNR term enabled "D12 S6 converges
… even though it **fails its own precision gates**." Those gates were the wave-1 `/3` ones, and F23's real
precision effect turned out to be roughly one fifth of the artifact that hid it. The convergence result stands;
the "fails its precision gates" half is not evidence until re-scored at `/5`.

> **Closed 2026-08-05 (wave 5): re-scored, and the clause is REFUTED.** Arm (a) scores precision **1.000** on all
> three datasets it was recorded as failing the ≥ 0.90 gate on (D09, D12, D15), beating the control on two. The
> full table and what it cost instead (recall) are in [F26](#f26--a-stuck-binning-recommendation-starves-the-step-update-indefinitely).
> **This audit's own verdict is unchanged** — no entry's conclusion depended on the repaired metric — and the one
> exposure it identified has now been measured rather than left as a caveat. Cost: six `golden eval` arms, four
> minutes.

**Two entries are changed by [F33](#f33--the-synthetic-bank-does-not-reproduce-the-real-banks-optimizer-failure-mode)'s
effective-gate correction instead — a different repair than the one being audited for.**

- **F8** reads three landings as `sens 0 / clip 0.25`, `sens 0 / clip 6.875`, `sens 30.1 / clip 3.44` and calls
  them non-reproducible. By **effective** gate they are further apart still — roughly **0.19**, **5.2** and
  **30.1** — so the entry's conclusion is not merely intact but understated. Two landings that read as the same
  "sens 0" corner are enforcing gates 27× apart.
- **F3** cites `mccomiskey` A shedding to recall 0.079 as evidence `Wtie` does not prevent star-shedding. That
  run's gate is now known to be **9.81**, not the floor its Sensitivity 0.0 suggests, which identifies the
  shedding *mechanism* (the clip escape route) the entry left unnamed.

**Why it matters.** The blanket assumption — "these were measured on suspect landings, so they are all suspect" —
would have cost a full re-measurement pass and found nothing. The actual exposure was one clause in one entry.
Recording the audit so it is not re-opened on the same inference next wave.

### F45 — The Grubbs test rejects the IN-FOCUS point of a near-perfect curve, and the blind walk then buys an extra exposure
**Status:** Open · found 2026-08-06 (wave 6) from a real 40 mm simulator run · **mechanism reproduced offline
from the run's own report**, not inferred

**The observation.** A blind sweep logged its queue decision against a minimum its own data does not support:

```
21:47:08 Enough left trend points (4) with an established minimum (25015) to queue remaining right focus points up to 25075
```

`AutoFocusEngine.cs:1485` sets `targetMaxFocuserPosition = trendlineFit.Minimum.X + (failedRightPoints +
offsetSteps) · stepSize`. `TrendlineFitting.Minimum` is **not a fitted vertex** — NINA core's
`TrendlineFitting.Calculate` sets it to `argmin(Y + ErrorY)` over the points it is HANDED. The run's own report
(`2026-08-05--21-47-43--90d513b9….json`, step 15, `offsetSteps` 4) makes the anchor look plainly wrong:

| position | HFR | error | `Y + ErrorY` |
|---|---|---|---|
| **25000** | 0.6941 | 0.1736 | **0.8677 ← argmin of the measured set** |
| 25015 | 0.7709 | 0.2459 | 1.0168 |

with `CalculatedFocusPoint = 24999.996` and hyperbolic R² = **0.99928**. Anchored at 25000 the target is 25060,
which had already been sampled ⇒ the walk stops; anchored at 25015 it is 25075 ⇒ **one extra step and one extra
exposure**, on every run that hits this.

**The mechanism, measured.** Feeding the report's own points back through the engine's fitting loop
(`WeightRegularization.Regularize` → `AlglibHyperbolicFitting` → `MathUtility.RejectionTest` → drop → refit,
`MaxOutlierRejections = 1`, confidence 0.99, `WeightedHyperbolicFitEnabled`):

| set | rejected | resulting `Minimum.X` |
|---|---|---|
| the 10 points present at the decision (24925–25060) | **25000** | **25015** |
| the final 11 points | **25000** | 25015 |
| the same 10 points, rejection disabled | — | **25000** |

**So `Minimum` is computed on the POST-REJECTION set, and the point rejected is the in-focus one.** σ is
regularized but untouched here (the floor is 0.2 × median = 0.023; no σ is that small), so regularization is not
the cause.

**And the rejection itself is the more serious half.** The weighted residuals the Grubbs test ranks, on a fit with
R² = 0.9994:

| position | residual (px) | weighted | Grubbs z |
|---|---|---|---|
| **25000** | **+0.0345** | +0.1987 | **2.5804** ← rejected |
| 24940 | +0.0116 | +0.1934 | **2.5277** ← also over the limit |
| 24970 (next highest) | −0.0321 | −0.1728 | 1.1714 |
| every other point | ≤ 0.034 | ≤ 0.14 | ≤ 0.84 |

The limit at N = 10, confidence 0.99 is **2.4821**; the scale is `median = −0.0569`, `MAD = 0.0990`. Two points
clear the limit and the larger wins. The scale is the **MAD of the weighted residuals** (`MathUtility.cs:163`) —
deliberately robust, and correctly so against a gross outlier — but on an *excellent* fit the residuals are both
tiny and tightly clustered, so the MAD collapses and ordinary scatter reads as a 2.58σ outlier. The rejected
point's residual is **0.0345 px against its own measurement error of 0.174 px**: it is consistent with the curve
to a fifth of its own error bar, and is discarded as an outlier anyway.

**Why it matters, beyond one exposure.**
- The discarded point is the **most informative one in the sweep** — the in-focus measurement the whole run exists
  to obtain. Here it costs little (the hyperbola vertex is pinned by 10 other points, and it still landed at
  24999.996); on a sparser sweep, or a rig where the near-focus frames are the only ones with stars, discarding it
  is not free.
- A **robust scale estimator applied to a near-perfect fit inverts its own purpose**: the better the fit, the
  smaller the MAD, and the more aggressively the test fires. The guard is loosest exactly where it is needed and
  tightest where it is not.
- The blind walk's cost is deterministic and per-run: one exposure, on a rig class whose exposures are the
  expensive part.

**Next step, and what NOT to do.** Three separable questions, deliberately not answered here:
(a) should the queue anchor be a *fitted* vertex (`HyperbolicFitting.Minimum.X`, or `Intersection`) rather than a
post-rejection argmin data point — note `Minimum` is also the anchor for the left/right trend split, so changing
its meaning is not local; (b) should `RejectionTest`'s MAD scale carry a **floor tied to the points' own measured
σ**, so a point cannot be an outlier while sitting well inside its own error bar; (c) should the walk's
`while (rightMostPosition < targetMaxFocuserPosition)` compare with a half-step tolerance, which bounds the
symptom without touching either fit. **(b) changes every AF fit in the product** and must be measured on the bank
before it is contemplated — the AF-bank σ_focus/R² arms are the instrument.

**Reproduce (offline, no rig, ~1 min):** feed the eleven `MeasurePoints` above through
`WeightRegularization.Regularize` → `AlglibHyperbolicFitting.Create(…, TiltedHyperbola, pts, stepSize: 15,
useWeights: true)` → `MathUtility.RejectionTest(pts, fit.Fitting, 0.99, BuildResidualWeights(pts, true))`, then
`new TrendlineFitting().Calculate(remaining, "STARHFR").Minimum.X`. Run log: `20260805-214043`.

### F46 — Detection binning buys faint stars and quietly sells bright ones to the shape/size gates
**Status:** Open · found 2026-08-06 (wave 7) running the F39(b) binning arm · **measured with attribution, not inferred**

Scoring the seven `detectionBinning = 2` datasets at their own factor for the first time
([F39](#f39--the-harness-records-a-detection-binning-the-run-never-applied-and-7-datasets-have-never-run-at-theirs)(b))
improves overall recall on **all seven** — and moves `recall@high` the WRONG WAY on **four** of them:

| dataset | recall@all 1 → 2 | **recall@high 1 → 2** | non-`LowSensitivity` FN 1 → 2 |
|---|---|---|---|
| `D12_c14_585_afbin2` | 0.562 → 0.675 | 0.879 → **0.813** | 30 → **73** |
| `D15_cdk20_3454mm_e47` | 0.764 → 0.917 | 0.931 → **0.874** | 8 → **21** |
| `D08_c11_2800mm` | 0.870 → 0.962 | 1.000 → **0.970** | 0 → **9** |
| `D10_rc16_3250mm_sparse` | 0.852 → 0.939 | 0.966 → **0.948** | 2 → **6** |

**Where they go, from `golden eval`'s own FN attribution.** At factor 1 the misses are almost entirely
`LowSensitivity` (41 / 49 / 15 / 114 / 184 / 52 / 26 across the seven). At factor 2 those collapse (3 / 7 / 1 / 34
/ 4 / 0 / 2) and a different set appears that was **absent or near-absent at factor 1**: `NO CANDIDATE (structure
gap)`, `TooSmall`, `NotCentered`, `OnBorder`, and more `TooDistorted`. That is the trade named exactly: **binning
buys SNR with linear resolution**, and at half the resolution a compact star can fall out of candidate FORMATION
or out of a shape gate that is calibrated in pixels.

**Why it matters.** `DetectionBinningResolver` recommends the factor from measured in-focus HFR alone, and its
whole rationale is that pixel-unit knobs are calibrated for HFR near 3 px. The recommendation is delivered as an
unqualified improvement, and on `D12` it costs 6.6 points of recall on the brightest tier — the stars the AF fit
weights most. Nothing in the product tells the user that half of a knob's effect points the other way.

**Not F38.** The obvious explanation is
[F38](#f38--the-minhfr-seed-trigger-compares-a-captured-pixel-vertex-against-a-binned-pixel-gate)'s space
mismatch: `MinHFR` gates inside the binned raster, so factor 2 would effectively gate at 2.4 captured px. The
attribution refutes it — **`TooLowHFR` appears in none of the fourteen runs.**

**This BLOCKS [F39](#f39--the-harness-records-a-detection-binning-the-run-never-applied-and-7-datasets-have-never-run-at-theirs)(b)'s
adoption wave, which is what raises it from an observation to a gate.** At binning 2 the bright-tier loss puts
**two datasets below their checked-in `recallHighMin = 0.90` band**: `D12_c14_585_afbin2` at **0.813** (L34) and
`D15_cdk20_3454mm_e47` at **0.874** (L47). `synthetic-af-bank-expectations.json`'s own rule forbids fixing that by
widening the band, so adopting the correct binning factor requires either reducing this loss or triaging the
breach on its merits with a stated re-derivation.

**Next step.** Two separable questions. (a) Are `MinimumStarBoundingBoxSize` and the structure-map layers
calibrated for the BINNED raster, or are they carrying captured-pixel values into a half-resolution image? They
are the two knobs whose units the factor changes, and `ApplyFactor` rescales only `PixelScale`. (b) Should the
binning recommendation be presented with its cost — "+13 points of overall recall, −6.6 on the bright tier" — or
should the factor be chosen to maximize something that sees both? Cheap to start: re-run the arm with
`--min-box` swept, which `golden eval` already exposes and which needs no code.
Reproduce: `D:\hf_w7\f39b_golden.sh`; per-dataset outputs in `D:\hf_w7\golden`.

**ANSWERED 2026-08-06 (wave 8): question (a) is YES — the knob is `StructureLayers`, not `MinimumStarBoundingBoxSize`
— and NO blanket scaling ships, because the correction does not generalize.** Full working in
[`docs/synthetic-af-bank-followups-wave8-results.md`](synthetic-af-bank-followups-wave8-results.md) §1.

**First, this entry's own account of WHERE the bright stars go is corrected.** The list above (`NO CANDIDATE
(structure gap)`, `TooSmall`, `NotCentered`, `OnBorder`, more `TooDistorted`) was read off the ALL-TIER
attribution, which on `D12` explains 107 false negatives of which only 20 are bright. Wave 8 shipped a
**high-tier-only** attribution and a per-star `false_negatives_f<focuser>.csv`, and the bright tier says something
else. At binning 2, shipped defaults:

| gate, HIGH TIER only | `D15` | `D12` |
|---|---|---|
| `REJECTED:Contaminated` | **10** | 3 |
| `REJECTED:TooFlat` | 0 | **8** |
| `NO CANDIDATE (structure gap)` | **0** | 2 |
| `REJECTED:TooSmall` | **0** | **0** |

**`D15`'s bright-tier loss is `Contaminated`, and its structure-gap count is zero.** `TooSmall` does not appear in
the bright tier at all.

**`MinimumStarBoundingBoxSize` is REFUTED, and inertly.** `--min-box ∈ {2,3,4,5}` at binning 2 is **byte-identical**
on both datasets. The flag works — `TooSmall: 6` disappears from `D12`'s all-tier table at `--min-box 2` — but
those six stars are **not recovered**: they fail the next gate instead (`TooDistorted` 19→22, `LowSensitivity`
34→37). *A gate's FN count is an UPPER BOUND on what relieving it buys, never an estimate* — filed as
[F50](#f50--a-false-negative-gate-count-is-an-upper-bound-on-what-relieving-that-gate-buys-not-an-estimate).

**Blending was also refuted** (`D:\hf_w8\p0\blend_check.py`, no detector run): the lost bright stars are ISOLATED —
median nearest golden neighbour 79 px on `D12` and 829 px on `D15`, i.e. 15× and 139× the in-focus HFR — and there
is no binning-2 detection within a match radius of any of them. But that arm produced the clue: **the lost stars
are LARGER than the kept ones** (`D15` 66×66 vs 42×42, several at 80×80), concentrated at the sweep wings. Big
defocused donuts, not small stars.

**The mechanism.** `StarDetector` step 4 subtracts an à-trous B3-spline residual to erase large-scale structure,
and its scale is `2^layers` **pixels**. Binning halves every structure's pixel extent while the layer count stays
fixed, so a donut that survived the subtraction at binning 1 is inside the residual at binning 2 and is erased
before candidate formation. The source's own comment predicts it: *"if the pixel scale is very small … you may
need to increase the number of layers to keep stars from being excluded."* `ApplyFactor` rescales only
`PixelScale`.

**The binning-1 control discriminates:** at binning 1 the shipped `StructureLayers = 4` is at or above the optimum
(every deeper setting is worse on `recall@all`, on both datasets); at binning 2 deeper is strictly better up to a
peak. `D15` goes 0.874 → **0.977** at layers 6, above even its binning-1 0.931.

**And the rule was refuted by the population that did not motivate it.** Measured on all seven (`R6`, fixed before
that sweep ran): `StructureLayers += log2(factor)` improves `recall@high` on **3 of 7** — not a majority — and
costs `D09` 0.011. `layers+2` fares no better. **Had this been run on `D12`+`D15` alone it would have looked like a
clean 2-for-2 win** and a hard-coded scaling would have shipped to every user on evidence drawn entirely from the
two cells that motivated it.

**So the defect is the DEFAULT, not the code path** — and every number here is measured at `--params default`, a
configuration no user keeps: `OptimizerVariable` already searches `StructureLayers` over `[1, 8]`, so a per-run
`optimize` finds the right depth. The product change is deliberately not made; like
[F47](#f47--a-focus-recovery-step-can-be-placed-where-nothing-is-detectable-and-now-there-is-a-number-that-says-so)
it changes live detection behaviour and wants its own before/after.

**Question (b) is still open** and is now the more interesting half.
Reproduce: `D:\hf_w8\p0\blend_check.py`, `D:\hf_w8\p1\knob_sweep.sh`, `D:\hf_w8\p2\layers_control.sh`,
`D:\hf_w8\p2\seven_sweep.sh`.

### F47 — A focus-recovery step can be placed where nothing is detectable, and now there is a number that says so
**Status:** Open · found 2026-08-06 (wave 7) closing F18's open decision (a)

[F18](#f18--step-size-is-sized-by-curve-geometry-alone-so-the-sweep-outruns-what-the-detector-can-see) measured a
3800 mm run whose focus-recovery step landed at 8850 — **4.4× the minimum HFR, and past the last position that
detected anything** (5310). Wave 7 closed F18's decision (a) as *recovery steps may extend past the 3× band —
that is their purpose, and `JRun` exempts them from its hard floor — but never past detectability*, and shipped
the measurement half: `StepSizeRecommendation.MaxUsefulHalfSpan`, the outermost non-recovery frame that still
cleared `NHard`.

**Reported, not enforced.** Nothing clamps where `AutoFocusEngine` places a recovery step. Sizing the base step
for the recovery path instead was rejected as the default because it costs **20% of the lever arm on every
successful run** (divisor 3.5 → 4.375 at 4 offset + 1 recovery) to bound a path that only executes when a run has
already failed — a trade wave 7 settles by measurement rather than by argument.

**Why it matters.** A recovery step beyond `MaxUsefulHalfSpan` buys no measurement at any step size: it is one
exposure, one focuser move, and a frame that contributes nothing but a starved-frame rejection. On the rig F18
measured it is where all the flat-topped rejections came from.

**Next step.** Clamp the recovery step's placement to `MaxUsefulHalfSpan` when the value is finite, leaving it
unchanged when it is not (an unmeasurable bound is absent, never zero — the same rule F18's `W_detect` uses). This
is engine-side, changes live AF behaviour, and wants its own before/after; it is deliberately not bundled with
F18's recommender change.

### F48 — The executed-sweep step sizing is BIMODAL (6 cells better, 2 worse), and the median hides both
**Status:** Open · found 2026-08-06 (wave 7) running F18's arm S · **the pre-registered rule was applied as written and rejected it**

F18's arm S sizes the step for the points the sweep ACTUALLY visits (offset + focus-recovery per side) rather than
the offset steps alone. Over 26 scorable (dataset, scenario) cells it is:

| vs the control | cells |
|---|---|
| **>20% BETTER** | **6** — `D05` S1 0.2296 → **0.0055**, `D15` S1 0.1622 → **0.0471**, `D09` S1 0.0945 → **0.0406**, `D16` S3 0.1332 → **0.0465**, `D06` S1 0.0861 → **0.0154**, `D05`/`D06` others |
| within ±20% | 18 |
| **>20% WORSE** | **2** — `D15` S6 0.0457 → 0.0978, `D16` S6 0.0651 → 0.1433 |

**Median ratio: 1.0000.** F18's rule 2 ("arm S ships only if it beats arm D by >5% median") therefore did not
fire, and arm S does not ship. That verdict stands — the rule was fixed before the arm ran, and a pre-registered
statistic that turns out to be poorly matched to the data is a lesson for the next rule, not a licence to pick a
better statistic once the numbers are in.

**But the median is the wrong statistic here and that is worth fixing for next time.** 18 of the 26 ties are
STRUCTURAL: S0 converges in a single round on most datasets, so only round 0 is ever rendered and no arm can
differ. A median over a set dominated by forced ties reports "no effect" for a distribution with six clear wins
and two clear losses in it.

**Why it matters.** The wins are concentrated in the ×0.25-step scenarios (S1), i.e. runs recovering from a
too-narrow sweep, and the losses in S6 (step AND exposure both wrong). That is a real, structured signal — a
narrower executed sweep helps when the fit is being rebuilt and hurts when the run is also photon-starved — and
the median threw it away.

**Next step.** Two separable pieces. (a) Re-score arm S on the cells where the arms CAN differ (more than one
round rendered), which needs no new runs — the reports are on disk at `D:\hf_w7\f18arms`. (b) For any future
arm rule, state the statistic over the cells that can move, and say up front how many cells are expected to be
structural ties; a rule whose denominator is mostly ties cannot fire in either direction.
Reproduce: `D:\hf_w7\f18_arms.sh`, scored by `D:\hf_w7\score_f18.py`.

**PART (a) DONE 2026-08-06 (wave 8), offline, no new runs — and the denominator alone decided the verdict.**

| denominator | n | median σ_focus **S/C** | S >20 % better | S >20 % worse |
|---|---|---|---|---|
| all scorable cells (wave 7's) | 28 | **1.0000** | 6 | 2 |
| **movable cells only** (>1 round rendered) | **12** | **0.8977** | 6 | 2 |

Excluded as structural ties: **16 of 28** — `S0` = 6, `S2` = 6, `S3` = 4.

**Over the cells that can move, arm S beats the control by 10.2 %.** F18's rule 2 was *"arm S ships only if it
beats arm D by >5 % median"*, and arm D is **1.0000 on all twelve movable cells** — so on the movable denominator
**the rule would have FIRED and arm S would have shipped.** The data did not change; the denominator did.

**The wave-7 verdict still stands and this does not re-open it** — the statistic was fixed before the arm ran and
was applied as written. What this buys is the quantified lesson: **the choice of denominator was worth more than
the entire effect being measured.**

**It also sharpens arm D.** Wave 7 reported "byte-identical on 26 of 28, bound in exactly one". Restricted to the
cells where an arm COULD act, **arm D is 1.0000 on all twelve** — its one binding cell (`D16` S2) is a one-round
cell that could not have differed anyway, and its σ_focus there is NaN. **Arm D never acted on a cell where acting
was possible.** [F18](#f18--step-size-is-sized-by-curve-geometry-alone-so-the-sweep-outruns-what-the-detector-can-see)'s
flag staying default OFF is better supported than wave 7 stated, not worse.

Part (b) — state the statistic over the cells that can move — is now a standing rule in the wave design template.
Reproduce: `D:\hf_w8\f48_rescore.py`.

### F51 — "Capture a new sweep and optimize" is gated on the EXPOSURE recommendation, so it hides exactly when the run needs re-running — and it would not carry the new step size anyway
**Status:** Open · found 2026-08-06 (wave 8) from the same field session as
[F49](#f49--the-star-signal-block-fires-on-a-floored-gate-but-every-remedy-it-owns-is-an-exposure-remedy-so-a-rich-well-exposed-field-gets-a-diagnosis-with-no-instruction) ·
mechanism confirmed in source

The user's session, from the NINA log — four wizard runs, each one told to widen by the previous one:

| run | step | exposure | recommended next | outcome |
|---|---|---|---|---|
| 12:30 | 100 | 2 s | **214** | accepted, ~38 min |
| 13:10 | 214 | 2 s | **459** (*"capped by this sweep's width"*) | accepted at **Sensitivity 0.000**, ~52 min |
| 14:03 | 459 | **5 s** | 474 | good landing, Sensitivity 2.5, ~30 min |
| 14:36 | 459 | 2 s | 482 | **~2 hours** |

**The user had to Accept a landing they did not want, twice, in order to re-run with a wider sweep.** Their words:
*"Ideally that shows up as an option to re-run entirely with the updated value without having to accept or go back
(which is what happened here)."*

**The action they wanted EXISTS — `CaptureNewSweepCommand` — and was hidden.**

```csharp
public bool ShowCaptureNewSweep =>
    HasExposureBlock && lastRunWasLive && !IsUseCurrentMode
    && (SelectedSummary?.ExposureAdvice?.IncreasesExposure ?? false);
```

On run 2 the first three conjuncts were true (the gate was floored at 0.000, the run was live, the mode was
Optimize) and **`IncreasesExposure` was false** — the exposure row read *"2 s (unchanged; measured star S/N
1438.6; target 10)"*. So the button was hidden. **That is [F19](#f19--the-exposure-recommendation-is-decided-by-the-20-brightest-stars-so-a-rich-field-can-never-earn-one)
on live hardware:** `S_now = 1438.6` against a target of 10 is the same saturation wave 8 measured on `D02`
(991.8 vs 10). F19's defect does not merely silence a *recommendation* — it removes the *button*.

**And even had it been visible it would not have solved this.** `RunLiveAttemptAsync` overrides only
`OverrideAutoFocusExposureTime` (plus focus-recovery steps) and takes everything else from
`autoFocusEngine.GetOptions()`, i.e. **the profile's step size**. So the re-capture path carries the exposure
recommendation and **not the step-size recommendation** — the one that was actually asking to change on every run
of this session. There is no re-run-at-the-recommended-step path at all; Accept-then-restart is the only one, which
is exactly what the user did.

**Why it matters.** The step recommender is explicitly a converge-over-runs mechanism (its own copy says *"re-run
auto-focus to refine"*), and the wizard has no affordance for the iteration it prescribes. Each cycle costs a full
sweep plus an optimization — here 30–120 minutes — and forces the user to write a landing they may not want into
their profile to get it. Note run 2's Accept is how `Sensitivity 0.000` reached the profile in the first place,
which is why runs 3 and 4 both show `Current` Sensitivity 0.000.

**Next step.** (a) Gate `ShowCaptureNewSweep` on *any* material recommendation — exposure **or** step size — rather
than on `IncreasesExposure` alone. (b) Make the re-capture apply the recommended **step size** as well as the
exposure, or say plainly that it will not. (c) Neither should require Accept: re-capturing is not adopting.
Reproduce: NINA log `20260806-122836-3.3.0.1048.73484-202608.log`; frames in `E:\AutoFocusSaves\`.

**ALL THREE SHIPPED 2026-08-07 (wave 9), and (a) turned out to have TWO causes rather than one.** The entry
names `IncreasesExposure`; the property also required `HasExposureBlock`, which is
`OptimizationSummary.HasLowStarSignal` — **Sensitivity ≤ 1.0**. So the button was hidden by the exposure
condition on run 2 *and* by the BLOCK's own condition on runs 1, 3 and 4, whose gates were healthy and which
therefore never had a re-run affordance at all. The entry's table records those three runs recommending 214, 474
and 482 with no way to act on any of them, and nothing in the entry had noticed the second gate.

| part | what shipped |
|---|---|
| **(a)** | `ShowCaptureNewSweep` = `lastRunWasLive && !IsUseCurrentMode && (IncreasesExposure ‖ StepSizeOrOffsetChanged)`. The `HasExposureBlock` conjunct is **gone**, and the row **moved out of the Star signal block** into the Auto-focus block — beside the step-size and offset rows, which are the values it now carries. The two MODE conditions stay (Replay has no rig; use-current has nothing to re-tune), because those are fixed for the life of the Summary |
| **(b)** | `ApplyRecaptureGeometry(options, stepSize)` in `RunLiveAttemptAsync`, applied **before** `ApplyFocusRecovery`. Set from the SELECTED summary by `CaptureNewSweepAsync` and cleared on **every** exit path, so an ordinary Live Start is byte-identical |
| **(b), the "or say plainly" half** | shipped **as well as** carrying it: `CaptureNewSweepCarriesText` states the geometry the next sweep will use (`"step size 214 → 459 … at the exposure below"`) and that nothing is written to the profile. A sweep geometry the user cannot see is how this defect stayed invisible for a whole session |
| **(c)** | already true and now reachable — the command persists nothing and Accept remains the only writer. What forced the Accepts was (a) hiding the control, not (c) |

**The house rule survives in both directions**, which is what the row's relocation had to preserve:
`StarSignalCopy`'s Live sentence naming the button fires on `increases && live && !useCurrent`, which is a strict
subset of the new visibility — so the copy can never name a hidden button. And in the new step-only state, where
the Star signal block is not on screen at all, the row carries its own sentence.

> **CORRECTION, caught by an existing test on the full-suite run.** The first version of (b) also carried the
> recommended **offset steps**, and `CaptureNewSweep_UsesTheSnapshottedRecoverySteps_NotTheLiveBox` failed with a
> re-capture widened to **5** where the recovery snapshot alone gives **1**. It is wrong for two independent
> reasons, and the test found both: `StepSizeRecommender` derives its step from the desired half-width over the
> **current** points-per-side, so the recommended STEP already expresses the whole geometry change at the existing
> offset — carrying the offset too widens the sweep twice; and `ApplyFocusRecovery` **owns** the offset axis and
> ADDS to whatever it is handed. **The re-capture now carries the step size and nothing else**, and the copy says
> so rather than promising a sweep it does not take.

**Tests: 3 discriminating on `ApplyRecaptureGeometry`** (carries the step; complete no-op at non-positive, so the
Start path is unchanged; composes with `ApplyFocusRecovery` without double-widening — which is the assertion the
correction above turned into a permanent guard).

**Still open, and it is the entry's real subject:** the step recommender asking to widen on **every** run because
the sweep never reaches `3 × HFR_min`. (a)–(c) make the iteration cheap; they do not make it terminate. That is
[F21](#f21--stepsizerecommenders-half-width-is-not-stable-against-noise-even-at-r--10000)/F49(c).

### F52 — A two-hour optimization logs ONE line and offers no cost context, and the search is not cost-aware
**Status:** **(a) and (b) DONE 2026-08-06 (wave 8); (c) DONE 2026-08-07 (wave 9) once F19's remainder
landed; (d) open** · found 2026-08-06 (wave 8) from the same field session · partly measured, partly **unmeasurable after
the fact, which is the finding**

Run 4 (step 459, 2 s) ran from 14:38:11 to past 16:30 — **over two hours**. Run 3, on the **same step size, same
sweep geometry, same 11 frames, same rig, same night, differing only in exposure (5 s)**, took ~30 minutes.

**Between `Loaded saved AF run …` at 14:38:11 and the end of the session there is exactly ONE log line.** Nothing
records how many evaluations ran, which phase it was in, or how much of the budget remained. Asked *"should I
abort and try again with a longer exposure?"*, neither the log nor the UI can answer.

**What IS measured, on the user's own frames** (`optimize --max-evals 1`, one settings file per arm, `UseAdvanced=True`
so the Simple-mode presets do not overwrite the knobs — [F42](#f42--every-build-directory-silently-gets-its-own-detector-settings-and-the-run-instructions-require-a-new-one-per-arm)):

| arm | config | wall |
|---|---|---|
| cheap | `StructureLayers 4`, `DefocusAwareStructure off`, `NR 3` | **33 s** |
| expensive | `StructureLayers 6`, `DefocusAwareStructure ON`, `NR 5`, hotpixel-thresholding off — **the config run 4 actually landed on** | **47 s** |

Only one of the two evaluations differs between arms, so the differing evaluation roughly **doubled**. Corroborated
on the synthetic bank, where the scaling is stark — `D15_cdk20_3454mm_e47`, 9 frames, binning 1:

| `StructureLayers` | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|
| seconds | 27 | 46 | **117** | 254 | **526** |

**≈2× per additional layer.** The à-trous residual is recomputed at `2^layers`, and `DefocusAwareStructure` adds
`StructureLayerBoost` on top of that.

**A hypothesis that was checked and is REFUTED: the floored gate costs nothing.** `Sensitivity 0 / StarClip 0.25 /
PeakResponse 0.55` vs `10 / 2 / 0.75` on the same frames measured **42 s against 42 s**. The acceptance gates are
LATE filters — they discard candidates after formation and PSF fitting — so admitting 5766 stars instead of 834
does not cost time. The obvious explanation for the slowness is wrong.

**So ~2× is attributed and the remaining ~2× is NOT**, and it cannot be recovered after the fact. The available
circumstantial evidence is the landings themselves: run 4 moved **13** parameters including four structural/
expensive axes (`StructureLayers`, `DefocusAwareStructure`, `NoiseReductionRadius`, `HotpixelThresholdingEnabled`)
while run 3 moved **9** and touched none of them. More axes explored ⇒ more evaluations. **Headless at a FIXED
budget the ordering even inverts** — 120 evals took 108 s on the 2 s frames and 150 s on the 5 s frames — which
rules out per-evaluation cost as the whole story and points at evaluation COUNT.

**Why it matters.** The objective is blind to cost: two candidate configurations scoring identically can differ by
an order of magnitude in wall time, and nothing prefers the cheap one or reports that the search has moved into an
expensive region. A user watching a progress bar for two hours has no way to know that a 2.5× longer exposure would
likely have finished in a quarter of the time.

**What the wizard ALREADY surfaces while it runs, checked before proposing to add any of it:** `Phase`, a
`current/total` counter (`SetProgress`), and live `ProgressSeedJ` / `ProgressBestJ` / `ProgressSeedSigma` /
`ProgressBestSigma` — so seed-vs-best `J` AND σ are already on screen. `CancelCommand` exists, so there is a real
control for any abort advice to name (the house rule at `ShowOptimizeAgainAtRecommendedBinning`: never describe an
action whose control is hidden). **What is absent is not the progress readout — it is TIME and WHY.**

**Next step, in three separable pieces. (b) and (c) are the user's actual request and they have different
readiness.**

**(a) DONE 2026-08-06 (wave 8).** `StarDetectionOptimizer` emits one INFO line every **10 completed evaluations**
(`SearchContext.ProgressEvaluationInterval`), carrying phase, evaluation count against the budget, elapsed,
seconds-per-evaluation and the incumbent `J` — plus the cost note when one applies. Verified on the field session's
own frames:

```
Optimizer progress: phase 'CoarseGrid',    evaluation 20/60, elapsed 00:00:23, 1.2s/evaluation, best J 0.998372
Optimizer progress: phase 'PatternSearch', evaluation 50/60, elapsed 00:00:41, 0.8s/evaluation, best J 0.99849
```

Cache hits are excluded from the rate (they cost nothing and would flatter it), and the evaluator is timed rather
than the surrounding bookkeeping, so `s/evaluation × remaining` is a number that means something.

**(b) DONE 2026-08-06 (wave 8).** Three facts under the progress bar, none needing a statistic that does not
already exist:

1. **The RATE and a bound on what is left** — `"0.8 s per step · at most 4 min more, usually much less"`.
   **Elapsed was already there** and is deliberately not repeated: `ProgressCountElapsedText` renders
   `X / Y (M:SS)` one line above and re-raises every second. Checking that first is what kept this from shipping a
   duplicate. The remaining figure is stated as an **upper bound**, because the evaluation budget is a cap the
   search usually stops well short of (`OptimizerSettings.MaxEvaluations` records a bank-wide convergence study
   finding it self-terminates before 400 on most runs) — a plain ETA would read as a promise and would usually be
   far too long.
2. **Which knob made it expensive**, quantified against **this run's own seed**: *"Searching at 8 structure layers
   (2 deeper than this run started at), which costs roughly 4x per evaluation."* Derived from
   `StarDetector.EffectiveStructureLayers` — extracted in this wave as the single source of truth so the readout
   and the detector's step 4 cannot drift — against the ~2×-per-layer scaling measured above. Absent entirely when
   the search is in a cheap region, because a readout that always says "expensive" says nothing.
3. **What Cancel costs** — *"Cancel stops the search only. Nothing is written to your profile unless you click
   Accept, and the frames already captured stay on disk."* Verified against the code: `Cancel()` only cancels the
   token, and `Apply` is reachable only from Accept. The frames clause appears on live runs only.

**Tests: 5 discriminating + 1 guard**, each confirmed by neutralizing the change and re-running — including one
correction found that way: the "stays cheap" test originally claimed to discriminate against an absolute-keyed
note and did not, because its seed sat at the shipped default of 4 where the two implementations coincide. Its
seed now turns the donut master on (effective depth 6, `StructureLayers` still 4) so the absolute implementation
reports "2 deeper" for a search that has not moved, and the test fails as claimed.

> **(c) SHIPPED 2026-08-07 (wave 9), on the statistic it was waiting for.**
> [F19](#f19--the-exposure-recommendation-is-decided-by-the-20-brightest-stars-so-a-rich-field-can-never-earn-one)'s
> remainder landed as `ExposureRecommendation.WingIsShedding` — the fraction of candidates the gate rejected on the
> sweep's WING frames, which is the one population in a run not floored by the gate. **The separation this entry
> drew is kept exactly: facts about COST need no new statistic and are already shown; ADVICE needs one.**
>
> `StarDetectionOptimizerWizardVM.SearchExposureAdvice` is computed in `ComputeBaselineJAsync` — i.e. from the
> **SEED evaluation, which runs BEFORE the search** — so the answer to *"should I abort and try again with a
> longer exposure?"* was available in the first minute rather than after two hours, which was the complaint.
>
> It is **absent when the wings are healthy** (a note that always fires says nothing), it **names Cancel**, which
> exists, and it says **what Cancel costs in the same sentence**, because not knowing that is why the user sat
> through the two hours. It quotes **no recommended exposure**: the wing probe is a probe precisely because the
> rejected candidates' SNRs are not recorded, and the Summary's exposure row is where a number belongs once the
> run finishes.
>
> **Tests: 4, three discriminating** — and the multi-run one is CORRECTED. Its first version paired a shedding run
> with a healthy one, which the aggregation skips entirely, so "worst" and "average" were identical and it passed
> under a deliberately-averaged implementation. It now pairs two shedding runs (0.75 and 0.30) and fails as
> claimed.

**(c) ~~Recommend whether to abort and re-run at a longer exposure~~ — was BLOCKED ON
[F19](#f19--the-exposure-recommendation-is-decided-by-the-20-brightest-stars-so-a-rich-field-can-never-earn-one),
and shipping it before that would be actively harmful.** This is the piece the user asked for by name. It cannot
be built on the existing exposure statistic: on this very rig that statistic reported *"2 s (unchanged; measured
star S/N 1438.6; target 10)"*, and wave 8's arm X measured the same saturation on `D02` (`S_now` 991.8 against a
target of 10, raw ask **0.000 s**, while σ_focus improves 44 % at 8×). **An abort-and-re-expose prompt derived
from `S_now` would tell precisely the users who need a longer exposure that theirs is already fine** — F19's
defect promoted from a silent omission into an active instruction. What (c) needs is F19's outstanding half: a
statistic that can see the WING frames (their star counts / σ contribution) rather than the 20th-brightest star of
the whole field. Until then (b)'s facts are honest and (c)'s advice is not.

**(d) Consider a cost term or an eval-time budget.** The search should not spend its budget in a region that is 4×
the price for a fourth-decimal gain — the same shape as
[F32](#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains), in wall
time instead of recall.
Reproduce: `D:\hf_w8\field\compare_runs.sh`, `D:\hf_w8\field\cost_*.json`, `D:\hf_w8\field\gate_*.json`.

### F56 — The à-trous wavelet residual convolves a DENSE kernel that is 99.5 % zeros, and that IS the `StructureLayers` cost curve
**Status:** Open · found 2026-08-07 (wave 9) answering a question about GPU/SIMD builds ·
**ANALYSIS, NOT MEASUREMENT — no benchmark was run, because F55 forbids CPU load beside the sequential arm**

`StarDetector` step 4's structure-removal residual is the detector's dominant cost.
[F52](#f52--a-two-hour-optimization-logs-one-line-and-offers-no-cost-context-and-the-search-is-not-cost-aware)
measured it on `D15_cdk20_3454mm_e47`: **27 / 46 / 117 / 254 / 526 s** for `StructureLayers` 4→8, *"roughly a
doubling per layer"*, and shipped a UI note explaining that cost to users.

**The doubling is an implementation artifact.** `CvImageUtility.GetB3SplineFilter(L)` builds a 1-D kernel of
length `(1 << (L+2)) + 1` in which **exactly five taps are non-zero** — the B3-spline `0.0625 / 0.25 / 0.375 /
0.25 / 0.0625` spaced `2^L` apart — and its own comment states the intent: *"Rather than copy the matrix to
convolve it, we can pad the separated filter with zeroes."* `Cv2.SepFilter2D` then performs a **dense** separable
FIR over the whole kernel.

| layer | kernel taps | useful taps | wasted multiply-adds |
|---|---|---|---|
| **4** (shipped default) | 65 | 5 | **13×** |
| 6 | 257 | 5 | **51×** |
| 8 | 1025 | 5 | **205×** |

Cost is linear in kernel length, and the kernel doubles per layer — which reproduces F52's measured curve exactly.

**"À trous" MEANS "with holes", and the algorithm exists to be O(1) per layer**: convolve 5 taps at stride `2^L`.
OpenCV's `sepFilter2D` has no dilation parameter, which is presumably why the zero-padding was chosen, but a
hand-written strided 5-tap separable pass has no such limit and is trivially vectorizable.

**Predicted, NOT measured:** layer 8 falls to roughly the cost of layer 1 — order **20–100×** on this stage —
and the `StructureLayers` cost curve F52 documents largely disappears. **Nothing here is confirmed**; the
arithmetic above is a static reading of the kernel and OpenCV's semantics.

**Why it matters beyond speed.** (a) `OptimizerVariable` searches `StructureLayers` over `[1, 8]`, so every
optimizer run pays this, hundreds of times — it is a large share of the two-hour runs F52 was filed about.
(b) [F46](#f46--detection-binning-buys-faint-stars-and-quietly-sells-bright-ones-to-the-shapesize-gates) showed
deeper layers are the RIGHT answer at detection binning 2, and the cost is what makes that expensive to adopt.
(c) It reframes F52(d): a cost term in `J` would be penalising an artifact rather than physics.

**Next step.** (a) Replace the zero-padded kernel with a strided 5-tap separable convolution (two passes, or one
`filter2D` per axis with an explicit gather). (b) **Verify by EQUIVALENCE, not by eye** — it is the same
arithmetic, so `StarDetectorEquivalenceTests` plus a bank re-score must come back bit-identical (or explain any
delta as border handling: the current code uses `BorderTypes.Reflect`, and a strided implementation must match
that at every layer). (c) Only then benchmark, and only with **nothing else running** (F55).
**This supersedes any argument for a custom SIMD or CUDA OpenCV build as the first move**: the stock native
build already dispatches AVX2/AVX-512, and no SIMD width recovers a 205× algorithmic waste.

### F55 — `optimize` is NOT reproducible when several instances run at once, and the SEED evaluation is what moves
**Status:** Open · found 2026-08-07 (wave 9) when the confirmation arm's own pre-registered control fired ·
**this voids the wave-9 F32 arm and constrains every future arm's design**

Wave 9 ran F32's confirmation arm with a **fan-out of 4** — four `optimize --per-run` processes on distinct bank
folders — to bring ~28 h of sequential compute down to ~7 h. The design pre-registered a free control for exactly
this: arm A re-measures the 8 runs the sequential gate had already pinned.

> **RULE G fired. 2 of the 8 do not reproduce.**
>
> | run | sequential gate | arm A, fan-out 4 |
> |---|---|---|
> | `toml999` | **0.997993** | **0.995784** |
> | `D18_m24_deep_shed` | **0.999822** | **0.999882** |
> | the other six | *(exact)* | *(exact)* |

**And the second control says where it comes from.** `BaselineJ` is the objective of the run's CURRENT settings —
**a single evaluation of the pinned seed, with no search involved**. The three arms differ only in `--keep-floor`
and `--continue-rounds`, neither of which touches that evaluation, so it must be identical across all three. On
**6 of 39 runs it is not**:

| run | A | B | C |
|---|---|---|---|
| `D16_esprit550_ha3` | 0.988555 | **0.979173** | **0.979173** |
| `D10_rc16_3250mm_sparse` | 0.958929 | **0.957201** | **0.957201** |
| `D17_cdk14_oiii5` | 0.993062 | **0.994830** | 0.993062 |
| `D14_cdk14_2563mm_e47` | 0.997901 | **0.997500** | 0.997901 |
| `mufti` | 0.957087 | **0.957603** | **0.957603** |
| `D15_cdk20_3454mm_e47` | 0.994703 | 0.994703 | **0.994474** |

**The gaps are far too large to be float-summation order** (`D16` moves by 0.0094), and the pattern is sporadic
rather than per-arm — A is the odd one out on three runs, B on two, C on one. This is
[F41](#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)'s tell firing at scale, and
[F8](#f8--optimizer-landings-are-not-reproducible-across-invocations) one level deeper: not "a landing is a
property of the trajectory", but **the seed evaluation itself is not a function of (frames, settings) alone.**

**Two causes EXCLUDED by measurement, not by argument.** Re-running `toml999` today at arm A's exact invocation:

- **Not the F15 folder state.** Run **sequentially and alone**, on folders that then held arm A's landing, it
  returns **0.997993** — the gate's value. What a previous arm left in the run folder does not decide this.
- **Not a build or settings difference.** Same binary, same pinned file, same `[1/1] optimizing attempt01`, same
  `PixelScale 0.73944`, same resolved binning factor.

**One four-way concurrent trial did NOT reproduce the deviation** (`toml999` returned 0.997993 again with three
other `optimize` processes in flight). **That is not evidence against the fan-out**, and it is recorded here so it
is not read as such: the effect appears on ~15 % of runs, so a single trial has no power to exclude it. Waves 5–8
all ran their arms **sequentially** and all reported bit-identical controls; wave 9 is the first to fan out and
the first to lose them.

**What it costs.** The arm ran for **7 h 10 m** across three arms and 117 optimizations and **cannot be read
against wave 5's φ table**. RULE G was written as *"a partial reproduction is a failure, not a warning — the
eight are one instrument"*, and it is applied as written: **no φ verdict is published from this arm.**

> ### CONFIRMED 2026-08-07 by a 40-SECOND reproducer, and it is BIMODAL
>
> Rather than spend ~28 h to find out, the question was asked directly: **is the seed evaluation a pure function
> of (frames, settings)?** `D:\hf_w9\det\determinism_probe.sh` runs `optimize --per-run --max-evals 1` — so the
> search does nothing and `BaselineJ` is the whole measurement — on `D16_esprit550_ha3`, the arm's biggest mover,
> **five times sequentially and then five times at once**, each repeat on its own copy of the run folder reset to
> an identical state. The banks are never touched.
>
> | phase | `BaselineJ` |
> |---|---|
> | **5× SEQUENTIAL** | `0.9791727071693058` five times — **identical** |
> | **5× CONCURRENT** | `0.9885546719484486` ×3 and `0.9791727071693058` ×2 — **two values in one batch** |
>
> **Those are exactly arm A's 0.988555 and arm B/C's 0.979173.** The probe reproduces the entire 7-hour
> discrepancy from identical inputs in 40 seconds.
>
> **Three things follow.**
>
> 1. **Sequential execution is deterministic** (5 of 5, and the wave-9 gate independently reproduced wave 5's
>    eight values across two waves and two binaries). **Waves 5–8 stand**, and the ~28 h sequential re-run is
>    valid — it is now RUNNING.
> 2. **The defect is BIMODAL, not drift.** Two discrete attractors, not a spread — so it is a race between two
>    code paths, not floating-point summation order. That also means it can flip a *sequential* run if the machine
>    is busy for another reason, which is why this outranks the re-run in importance.
> 3. **A 40-second reproducer exists**, which is the thing that was actually missing. Any candidate fix is now
>    testable in a minute instead of a wave.
>
> **Excluded by reading the source (no runs — nothing may execute beside the sequential arm):** there is no
> OpenCL/`UMat` path at all; `AlglibHyperbolicFitting`'s parallel candidate-model pass writes into **indexed
> arrays** and resolves consensus by intersection + `Array.FindIndex`, so `Parallel.For` completion order cannot
> change it (the comments say so deliberately); and there is no load-sensitive timeout in the
> detection/evaluation/fit path — the only `Stopwatch` there is trace-only. **The mechanism is still open.**
> Degree-of-parallelism knobs derive from `Environment.ProcessorCount`, which does not change when other
> PROCESSES run, so the cause is more likely a shared cross-process resource or a genuine order dependence
> somewhere below the fit.

**Next step.** (a) ~~Re-run the arm sequentially~~ — **RUNNING** as of 2026-08-07 10:18Z, ~28 h.
(b) **Find the nondeterminism — this now outranks (a) in value, because the reproducer makes it cheap and
because a bimodal race can flip a sequential run too.** Bisect with `determinism_probe.sh`: it is 40 s per trial. (c) Until (b), **treat concurrent `optimize` as invalid for any arm whose
conclusion rests on comparing landings**, and say so in the run instructions beside
[F42](#f42--every-build-directory-silently-gets-its-own-detector-settings-and-the-run-instructions-require-a-new-one-per-arm)'s
settings-pinning rule. (d) Add a cheap standing guard: have `optimize` print `BaselineJ` where a scripted arm can
diff it, so this control is free on every future arm rather than only when someone writes it down.
Reproduce: `D:\hf_w9\f32_arms.log`, `D:\hf_w9\score_f32.py`, `D:\hf_w9\ctl_seq_toml999.log`,
`D:\hf_w9\ctl_conc_toml999.log`.

### F54 — F39(b)'s default flip MOVES a landing at a resolved factor of 1, where it is documented as a no-op
**Status:** Open · found 2026-08-07 (wave 9) chasing [F53](#f53--wave-8s-arm-x-does-not-reproduce-from-wave-8s-own-exe-because-the-arm-ran-on-an-earlier-build-of-it)'s
remainder · **measured and reproducible; the MECHANISM is not identified and is not claimed**

Wave 8 flipped `optimize --per-run`'s default to apply each run's derived detection binning, and recorded the
opt-out as leaving prior arms reproducible. On `D:\hf_w7\armE\t0.5\D02_rich_135mm` — **one binary, one pinned
settings file, one set of frames, and a resolved factor of 1** — the two paths land in different places:

| | landed gate | σ_focus | `bestJ` |
|---|---|---|---|
| default (flip ON), factor **1** from `synthetic_meta.json` | **33.333** | **0.10063** | 0.996486 |
| `--no-run-detection-binning` | **16.667** | **0.10927** | 0.996328 |

The second row reproduces wave 7's arm E and wave 8's arm X **exactly**, which is what makes this the explanation
for F53's residue rather than a second unexplained thing.

**Everything the harness prints about the seed is IDENTICAL between the two runs** — `Sensitivity=10`,
`StarClippingMultiplier=2`, `NoiseClippingMultiplier=4`, `StructureLayers=4`, and the per-run
`PixelScale 5.74486 arcsec/px (frame header)`. The only difference is the two `DetectionBinningResolver.ApplyFactor(_, 1)`
calls the flip enables.

**Why that is surprising.** `ApplyFactor(p, 1)` reads as the identity when `p.DetectionBinning` is already 1:
`unbinned = PixelScale / max(1, 1)`, then `PixelScale = unbinned × 1`. `StarDetectorParams.DetectionBinning`
**defaults to 1** (`IStarDetector.cs:495`), so the 0→1 hypothesis is dead. And
`HocusFocusStarDetection.ApplyDetectionImageContext` **overwrites** `detectorParams.DetectionBinning` from the
options override at detect time anyway, so the field the resolver writes is not even the one the detector uses.

**What is NOT the cause**, each excluded rather than assumed:

- **Not concurrency** — the two runs bracket each other under identical load, and a third control (F53) matched a
  different binary to 6 dp with three `optimize` processes in flight.
- **Not this wave's code** — `D:\hf_w8\exe` and wave 9's `exe2` agree with each other on the flip-ON value.
- **Not `EffectiveStructureLayers`** — wave 8's F52(a) extraction is a faithful one (identical branch order and
  expressions), and its `CostNoteFor` calls `Materialize`, which **clones** the seed and has no side effects.

**Why it matters, and it is not academic.** Wave 8's adoption control reported *"every binning-1 dataset is
bit-identical between arm A and arm B … 0 control violations of 13"* — **the same comparison this entry runs, with
the opposite result.** The two differ in one respect worth chasing first: wave 8's control ran on the BANK folders
while this runs on wave 7's rendered `armE` copies, and those copies already contain an `optimized_settings.json`
written by a previous `--per-run` pass ([F15](#f15--optimize---per-run-overwrites-each-runs-stored-settings)),
which `HarnessSettingsStore.ResolveForRun` reads. If a per-run settings file can change what the flip does, that is
the F42 shadowing concern arriving through a different door.

**This wave's F32 arm is NOT affected, and that is measured rather than assumed.** RULE G reproduced all 8
comparability runs to 6 dp **with the flip ON, on the bank folders**, against wave 5's values.

**Next step.** (a) Reproduce on a BANK folder with and without the flag, to separate "the flip moves landings" from
"a stale per-run `optimized_settings.json` moves landings". (b) If it is the per-run file, that is F15 causing a
measurement error rather than merely destroying provenance, and it raises F15's priority sharply. (c) Either way,
`ApplyFactor` at factor 1 should be provably inert or should not be called — a normalization step that changes an
answer is worse than no normalization.
Reproduce: `D:\hf_w9\wing\ctl_nobin.log` vs `D:\hf_w9\wing\opt_0.5_D02_rich_135mm.log`.

### F53 — Wave 8's arm X does not reproduce from wave 8's own `exe`, because the arm ran on an EARLIER build of it
**Status:** Open · found 2026-08-07 (wave 9) re-running arm X's exact command to build the wing instrument ·
**the reproduce line is stale, and nothing in the artifact says so**

Wave 8's F19 arm X is recorded with `Reproduce: D:\hf_w8\armX\arm_x.sh`, which invokes
`D:\hf_w8\exe\TestApp.exe`. **Running that script's exact command on that exact binary today does not reproduce
the write-up's numbers.**

| | `D02_rich_135mm` @ 0.5 s, `--max-evals 120`, same pinned settings |
|---|---|
| wave 8's arm X log | `bestJ = 0.996328`, effective gate **16.667**, σ_focus 0.10927 |
| `D:\hf_w8\exe` **today** | `bestJ = 0.996486`, effective gate **33.333**, σ_focus 0.10063 |
| wave 9's `exe2` (this branch) | `bestJ = 0.996486` — **identical to the line above** |

**The tell is in the log, and it is unambiguous.** Wave 8's arm X log contains **no `detection binning (F39b)`
line at all**; both of today's runs print one. That line is unconditional once wave 8's own F39(b) default flip
landed, so **arm X was executed against a build of `D:\hf_w8\exe` that predates the flip**, and the directory was
rebuilt later in the wave. The artifact directory keeps only the LAST build, so the script and the numbers it
produced now disagree with nothing recording that they should.

**Three things this is NOT.**

1. **Not concurrency.** The control above was run with three `optimize` processes already in flight and matched a
   run of a different binary to six decimal places. If a fan-out perturbed landings, these two could not agree.
2. **Not this wave's code.** `D:\hf_w8\exe` and wave 9's `exe2` — different binaries, one of them predating every
   line of wave 9 — give the same answer.
3. **Not `ApplyFactor`.** `DetectionBinningResolver.ApplyFactor(p, 1)` is the identity when `p.DetectionBinning`
   is already 1 (`unbinnedPixelScale = PixelScale / 1`, then `PixelScale = unbinnedPixelScale × 1`), which the
   pinned settings guarantee. That is also why wave 8's own adoption-arm control found the 13 binning-1 datasets
   bit-identical. The flip is a marker for WHICH BUILD ran, not the cause of the movement.

**Does it overturn wave 8's F19 verdict? No, and the reason is worth stating.** Arm X's finding was that `D02`
reports `ExposureIsNotTheLimit` with a raw ask of 0.000 s at every rung. Both landings put the EFFECTIVE gate
(16.667 and 33.333) far above `TargetSensitivity = 10`, and every accepted star's gate statistic strictly exceeds
that gate — so `ExposureIsNotTheLimit` is true by construction in both. **R5 fired for a structural reason, not a
numerical one, so a different landing reaches the same verdict.** What is void is the reproducibility of the
specific table, not its conclusion.

**Why it matters.** [F41](#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary) says a prior
wave's control arm is not a control for a later wave's binary. This is the sharper case: **a prior wave's arm is
not a control for its OWN recorded binary either**, when the arm directory is a build output that later steps in
the same wave overwrite. Every `Reproduce:` line in `docs/` that names a `D:\hf_w*\exe` inherits this.

**Next step.** (a) Stamp the build into the run: have `optimize` print the informational version / commit of the
binary it is running, so a log says which build produced it. (b) Until then, treat a `D:\hf_w*\exe` reproduce line
as naming a COMMAND, not a result — re-derive the numbers rather than quoting them across waves. (c) Prefer
per-arm build directories that are never rebuilt mid-wave, which
[F42](#f42--every-build-directory-silently-gets-its-own-detector-settings-and-the-run-instructions-require-a-new-one-per-arm)
already asks for on a different ground.
Reproduce: `D:\hf_w9\wing\ctl_w8bin.log` against `D:\hf_w8\armX\opt_0.5_D02_rich_135mm.log`.

### F50 — A false-negative gate count is an UPPER BOUND on what relieving that gate buys, not an estimate
**Status:** Open · found 2026-08-06 (wave 8) refuting F46's `MinimumStarBoundingBoxSize` hypothesis · **a reading
error, not a code defect — but every prior wave has read these tables the other way**

`golden eval`'s false-negative attribution names the **first** gate a missed golden star hits. Wave 8 relieved one
of them and measured what came back:

| `D12_c14_585_afbin2` at binning 2 | `--min-box 5` (shipped) | `--min-box 2` |
|---|---|---|
| `REJECTED:TooSmall` | **6** | **0** |
| `REJECTED:TooDistorted` | 19 | **22** |
| `REJECTED:LowSensitivity` | 34 | **37** |
| **recall@all / recall@high** | 0.675 / 0.813 | **0.675 / 0.813** |

The six `TooSmall` rejections vanish and **not one star is recovered** — they fail the next gate instead. The
result is byte-identical on every tier.

**Why it matters.** Wave 7's F39(b) table, F46's entry, and the noise-clip sweep write-ups all read a gate's FN
count as "what this gate is costing us". It is not: it is the count of stars that reach that gate first, which
bounds the gain from above and can be an arbitrarily loose bound. A candidate that is too small is usually also
distorted and faint — the gates are correlated because they are all measuring the same marginal blob.

**Why it matters MORE than a reading habit.** `GateRecommender` recommends a threshold *per gate* from exactly
these counts (`GateRecommender.cs:285` maps `RejectionGate.TooSmall` → `MinimumStarBoundingBoxSize`). A
recommender driven by a bound it treats as an estimate will happily loosen a gate for no gain — and each such
loosening is a real precision cost on some other frame.

**Next step.** Attribute a false negative to the SET of gates it would have to clear, not to the first one — the
detector already evaluates them in sequence, so the cheap version is "re-run with this gate disabled and see what
comes back", which is what wave 8 did by hand. Until then, treat every FN-by-gate number in
`docs/` as an upper bound and say so where it is quoted.
Reproduce: `D:\hf_w8\p1\knob_sweep.sh` (the `p1_minbox*` arms).

### F49 — The Star signal block fires on a floored gate but every remedy it owns is an EXPOSURE remedy, so a rich, well-exposed field gets a diagnosis with no instruction
**Status:** Open · found 2026-08-06 (wave 8) from a **field report on the shipped `Default` profile**, not from the
bank · mechanism confirmed in source

The user ran the optimization wizard twice on their own rig. The second landing: **Sensitivity 15.667 → 0.000**,
**StarClippingMultiplier 6.750 → 0.250**, stars per frame **834 → 5766** (≈ 7×), σ_focus 6.12 → 0.55. The Star
signal block fired and said:

> *"Brightness Sensitivity is at the bottom of its range (0.000): this focus result rests on low-confidence
> detections. Star brightness is not the problem: your brightest stars measure S/N 1438.6, meeting the default S/N
> target of 10. Brightness Sensitivity is low, so far fainter candidates are being admitted below them."*

…and then stopped. **No remedy sentence at all**, on a page whose whole contract is "diagnosis plus exactly one
instruction". The user's words: *"there's no guidance regarding how to get a better optimization with a non-zero
sensitivity."*

**Mechanism — this is structural, not a missing string.** `StarSignalCopy.RemedyFor` has exactly three ranked
branches, and **all three are about exposure or binning**:

| # | remedy | gate |
|---|---|---|
| 1 | change the detection binning factor first | `DetectionBinningDiffers && IncreasesExposure` |
| 2 | auto-focus through a broadband filter instead | `HasRecommendation && !ExposureIsNotTheLimit && !IncreasesExposure` |
| 3 | get frames at the longer exposure | `IncreasesExposure` |

On this run `ExposureIsNotTheLimit` is **true** (S/N 1438.6 against a target of 10; the row reads "2 s
(unchanged)"), so `IncreasesExposure` is false and branch 2's `!ExposureIsNotTheLimit` is false. **Every branch
falls through and the method returns `string.Empty`.**

**Why it matters.** The block's TRIGGER is `OptimizationSummary.HasLowStarSignal` — the Sensitivity gate alone —
while its CONTENT is exposure-only. So on a **rich, well-exposed field where the optimizer CHOSE a floor gate**,
the trigger fires, the copy correctly says brightness is not the problem, and then the block has structurally
nothing to offer. Worse, **there is no user lever even if it had one to name**:
[F32](#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains)'s
`OptimizerSettings.MinDetectionKeepFraction` has **no XAML binding anywhere** — it is `--keep-floor` on the harness
only — and the search's Sensitivity lower bound is not exposed either. (Note the keep floor would not have bound
here in any case: it rejects candidates keeping LESS than φ of the seed's stars, and this landing keeps ~7× MORE.
The pathology is admission, not shedding — the *other* end of the same axis, which F32 has never measured.)

**This is the exact MIRROR of [F19](#f19--the-exposure-recommendation-is-decided-by-the-20-brightest-stars-so-a-rich-field-can-never-earn-one).**
F19 is the block staying **silent** on a population that would benefit from what it knows; this is the block
**speaking** to a population it cannot help. Both say the same thing one level up: **the block's trigger and the
block's content are keyed to two different quantities**, and any fix to F19's trigger must not deepen this.

**Also in the same report, and separable: the step recommender widened again, from a NARROW sweep.** 214 → **459**,
labelled *"capped by this sweep's width; re-run auto-focus to refine"*. The arithmetic checks out from the
screenshot's own focuser axis — 11 points at spacing 214 ⇒ sampled half-span 1070; `MaxHalfWidthSampledHalfSpanMultiple
= 1.5` ⇒ 1605; 1605 / 3.5 = 458.6 → **459** — so the cap BOUND, i.e. the recommender wanted more still. The cause
is visible in the curve: HFR runs from ≈1.9 px at minimum to ≈3.5 px at the edges, so the sweep never reaches
`3 × HFR_min ≈ 5.7 px` and `FindHalfWidth` extrapolates on every run. It will therefore ask to widen on **every**
run until the sweep actually reaches 3×, and **nothing on the page says that this terminates, or after how many
runs**. This is the same instability
[F21](#f21--stepsizerecommenders-half-width-is-not-stable-against-noise-even-at-r--10000) names, approached from
the narrow side rather than [F25](#f25--from-a-far-too-wide-sweep-the-step-recommender-widens-it-further-instead-of-recovering)'s
wide side, and the user experienced it as a runaway rather than as convergence.

> ### (a) SHIPPED 2026-08-07 (wave 9) — and the branch is narrower than this entry proposed, on purpose
>
> `RemedyFor` gains a fourth, last-ranked branch for the floored-gate + `ExposureIsNotTheLimit` state:
>
> > *"The gate was lowered to admit more candidates, not because star brightness was missing. Set Brightness
> > Sensitivity by hand in the star detection options and run this wizard again in "use current settings" mode to
> > compare that landing against this one."*
>
> **It names only controls that EXIST**, per the house rule at `ShowOptimizeAgainAtRecommendedBinning`.
> `BrightnessSensitivity` is bound in `OptionsDataTemplates.xaml`
> (`StarDetectionOptions.BrightnessSensitivity`); `MinDetectionKeepFraction` is **not named**, because it has no
> XAML binding anywhere — verified, not assumed.
>
> **It REQUIRES a measurement, which this entry's proposed wording did not.** The sentence CLAIMS that brightness
> was not what was missing, and that is knowable only from `ExposureIsNotTheLimit` (S_now ≥ the default target).
> On a run with no derivable recommendation at all — too few usable frames, no per-star SNRs, an unknown exposure
> to scale from — all that is known is that the gate is floored, so those runs keep their diagnosis-only body.
> **Asserting it there would break the entry's own "no star-poor claim without evidence" rule from the opposite
> direction**, and two existing golden tests caught exactly that on the first attempt.
>
> **Tests: 4, three of them discriminating.** The pre-existing whole-body golden for this state is the sharpest —
> it asserted a string that ENDED after the admission sentence, which is the defect written down as an
> expectation. Plus: the remedy is last-ranked and does not displace an available exposure remedy; an unmeasured
> run gets no gate remedy; and the copy names no control that does not exist.

**Next step.** Three separable pieces. **(a)** ~~Give the floored-gate + `ExposureIsNotTheLimit` state a remedy of
its own~~ — **DONE, see above**; the honest one names the gate, not the exposure. **(b)** Decide
whether a user-facing floor on the search's Sensitivity (or F32's keep fraction, exposed) is the right lever, since
today there is none. **(c)** When the step recommendation is capped by the sweep width, say what it is converging
TOWARD (`3 × HFR_min`) and that the cap means "partial step, this will take another run" — the exposure
recommender's own `MaxExposureFactor` copy already sets that precedent verbatim ("Run again to refine").
Reproduce: field report, `Default` profile, 2026-08-06; wizard screenshot in the wave-8 thread.

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

### F34 — `synth-validate` scored a stalled run as converged, at a step 4× outside the band its own assertion failed it on
**Status:** **Done** (2026-08-03, wave 2) · found 2026-08-03 re-measuring [F25](#f25--from-a-far-too-wide-sweep-the-step-recommender-widens-it-further-instead-of-recovering)

The fourth harness-calibration bug on this bank, and the third in the convergence-predicate family. The round
loop set `stoppedReason = "converged (round applied nothing)"` unconditionally, and `ScenarioTerminal.Converged`
was then derived by string-matching that reason for a `"converged"` prefix.

**Evidence.** `D05_tec140_1000mm` S2:

```
converged: true
stoppedReason: "converged (round applied nothing)"
finalStepSize: 140     stepBehavioral: 35     deltaStepVsExpected: +105
assertions: A3 verdict=FAIL  "final step 140 outside [21,56] = [0.6,1.6]x step_behavioral (35)"
```

Two verdicts contradicting each other inside one JSON object.

**Mechanism.** `StepSizeRecommender.Degenerate` **holds the current step** whenever the fit is unusable — no
`Fitting`, a non-finite vertex, a non-positive minimum HFR, or the 3× band never crossed. The driver applies a
step only when it differs, so a degenerate fit produces `AppliedAnything == false` that is byte-identical to the
recommender genuinely agreeing. The loop read "nothing changed" as success, which inflates convergence counts on
exactly the runs that are most broken.

**Fixed.** A no-op round still STOPS the loop — the recommender is not going to move on its own — but it counts
as convergence only when the step is inside the tolerance band, and reports `stalled` otherwise. The band moved
into `ConvergenceBand` so both stop branches share one definition, and it is a strict subset of A3's `[0.6,1.6]×`
sharing its lower bound, so the two verdicts cannot contradict each other again. `Converged` is now set
explicitly at each stop site instead of parsed out of prose, and `stepToleranceBand` is recorded on the terminal
so the claim is checkable from the report alone.

**Verified on the motivating case.** `synth-validate --datasets D05_tec140_1000mm --scenarios S2 --max-rounds 4`
re-run after the fix:

```
converged:         false
stoppedReason:     "stalled (round applied nothing, but step 140 is outside the 14 tolerance band of
                    step_behavioral 35) — a no-op recommendation from a degenerate fit, not convergence"
finalStepSize: 140    stepBehavioral: 35    stepToleranceBand: 14
assertions:    A3 verdict=FAIL  "final step 140 outside [21,56] = [0.6,1.6]x step_behavioral (35)"
```

Same run, same step, same A3 failure — the report no longer contradicts itself, and the terminal now publishes
the band its verdict was reached with.

**Why it is worth a numbered entry.** Four calibration bugs have now been found on this harness — three in this
family — every one by someone happening to look rather than by anything failing. A harness that reports its own
success is load-bearing for every conclusion drawn from it; see also the `truthViolations` /
`precisionNull` guards added to `bank-verify` for the same reason ([F31](#f31--synthetic-bank-precision-is-not-exact-the-golden-omits-real-stars-and-they-score-as-false-positives)).

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

**It happened again in wave 3, on both banks at once (2026-08-03).** The F35 validation ran
`optimize --per-run` over `D:\SyntheticAutofocusBank` and `D:\Autofocus Bank`, so every run's in-place
`optimized_settings.json` in **both** banks is now the wave-3 landing. Nothing comparative was lost this time —
the wave-1 control arms live in their own `--out` directories (`hf_f23\H_A`, `hf_f23\H_real_A`) and are
untouched — but that was luck of workflow, not a property of the tool. **Any validation pass silently
re-baselines the banks' stored settings**, and the only reason this one was harmless is that the comparison
never read them.

Note the interaction that makes this sharper than it looks: `bank-verify --opt-a/--opt-b` and
`golden eval --params optimized` both read the **run folder** copy by default. So a prepass and a later scoring
run that were meant to be independent can silently share an arm.

**Next step.** Consider writing only to `--out` unless a flag opts into updating the run folder, or snapshot the
previous file alongside it.

### F37 — The CI test host crashes natively (`AccessViolationException`), aborting ~2000 tests with zero failures
**Status:** Open · found 2026-08-04 merging [PR #170](https://github.com/ghilios/hocus-focus/pull/170)

CI's `Run unit tests` job died with
`Fatal error. System.AccessViolationException: Attempted to read or write protected memory` — a **native crash of
the test host process**, not a test failure. The run reported `Passed: 1389, Failed: 0` out of a suite of
**3371**, so roughly 2000 tests never executed, and the job failed on exit code 1 rather than on any assertion.

**It is provably not caused by the code under test.** Two runs on the same branch, minutes apart:

| run | commit | contents | result |
|---|---|---|---|
| `30873901415` | `a29f6df` | **every code change in the PR** | **success** |
| `30874276311` | `b9095fa` | `a29f6df` **+ two markdown files** | **AccessViolationException** |
| `30874276311` (re-run) | `b9095fa` | unchanged | **success** |

A docs-only delta cannot cause a native memory violation, and re-running the identical commit passed. So this is
non-deterministic and environmental.

**The preserved evidence.** The failed attempt's TRX artifact survives (`8879261120`, attempt 1). It contains
1390 results — **1389 `Passed`, 0 `Failed`**, one `NotExecuted` (`SavedRuns_Benchmark`, skipped by design). The
last tests to complete finished at `03:35:05.622` and the host died at `03:35:06.004`. Tests run in parallel, so
the last-completed test is **not** necessarily the one that crashed — the TRX cannot identify the culprit, which
is exactly why the next step below is needed.

**The prime suspect is the coverage profiler, not the tests.** `.github/workflows/tests.yml:38` runs with
`--collect "XPlat Code Coverage"`, attaching the coverlet profiler to a test host that loads
**OpenCvSharp native** (`OpenCvSharpExtern.dll`). An instrumenting profiler over heavy native interop is a
well-known source of `AccessViolationException`, and it is the clearest difference between CI and local: local
full-suite runs use no `--collect` and passed **3371/3371 twice consecutively** on the same commit.

**Do not conflate this with the known flaky test.** The recorded flake
(`SendAsync_WritesOnABackgroundThread`, EAT serial transport) is an **assertion failure in one test**; this is a
**process crash with no failing assertion**. Different signature, and treating them as one thing would hide
whichever is real.

**One local observation that may or may not belong here.** During the same session, one local full-suite run
reported a single failure while two `optimize --per-run` bank passes were saturating the CPU. Its name was not
captured, and it did not recur across four subsequent full runs. Whether it is this defect, the EAT flake, or a
third thing is **unknown** — recorded so the next occurrence is not read as the first, not as evidence of a link.

**Why it matters.** A green suite is the merge gate. A failure mode that aborts two thirds of the suite while
reporting zero failures is the worst shape for a gate: it is indistinguishable at a glance from a real
regression, it costs a re-run every time, and — the real risk — **a crash that lands early enough would let a
genuine regression through in the ~2000 tests that never ran**, because nothing reports them as unexecuted.

**Next step.** Add `--blame-crash` to the CI test invocation so the run produces a sequence file and a crash
dump naming the test that was executing, and keep the existing `if: always()` artifact upload so it survives.
Then test the profiler hypothesis directly by running CI once **without** `--collect "XPlat Code Coverage"`; if
the crash stops reproducing, move coverage to a separate job so a coverage-only defect cannot fail the merge
gate. Cheap to start: the crash has now been seen once in CI, so the first step is instrumentation, not a fix.
