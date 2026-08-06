# Synthetic AF bank — followups wave 8 (design)

Plan: [`plans/synthetic-af-bank-followups-wave8-plan.md`](../plans/synthetic-af-bank-followups-wave8-plan.md).
Wave 7: [`docs/synthetic-af-bank-followups-wave7-results.md`](synthetic-af-bank-followups-wave7-results.md).
Register: [`docs/followups.md`](followups.md).
Baseline: `develop` @ `e8eb8b0` (PR #183 merged, unreleased). Suite **3661**.

*Wave 7 measured that the seven `detectionBinning = 2` datasets gain **17–95 % of σ_focus** at their own factor —
the largest available win on the board — and then found its own gate: at binning 2 the bright tier drops below the
checked-in `recallHighMin = 0.90` on two of them. This wave's first item is that gate, and nothing downstream of it
runs until it is answered.*

---

## §0 — The three ordering decisions, taken here rather than halfway through

Wave 7's lesson list is mostly about *sequencing*: which arm invalidates which, and what a wave is allowed to move
at once. Three such decisions govern wave 8. All three are taken now, in writing, with the evidence that decided
them.

### 0.1 — F32's confirmation arm: DEFERRED AGAIN, and here is exactly what that costs

[F32](followups.md#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains)'s
confirmation arm has now been deferred across waves 5, 6 and 7. Deferring it a fourth time is a real cost and is
recorded as one — but the cost is **not** the one the deferral rule was written to prevent, and the difference is
measurable rather than rhetorical.

**What the rule protects.** The arm's subject is `D18_m24_deep_shed`, `D19_cygnus_deep_shed`,
`D20_m24_bright_control` (synthetic) plus `toml999`, `CWhiteFocus`, `uneven`, `muggsie`, `mccomiskey` (real). It is
comparable with wave 5's φ table **only while those eight runs' frames are bit-identical to what wave 5 measured.**
Any wave that re-renders one of them ends the option.

**Measured, not assumed: wave 8 cannot end it.** Every dataset's `expectedOptimal.detectionBinning`, read from the
bank's own `synthetic_meta.json`:

| detectionBinning | datasets |
|---|---|
| **2** | `D08`, `D09`, `D10`, `D12`, `D14`, `D15`, `D17` — F39(b)'s seven |
| **1** | `D01`–`D07`, `D11`, `D13`, `D16`, **`D18`, `D19`, `D20`** |

`D18` / `D19` / `D20` are binning 1 under either configuration, so **F39(b)'s adoption cannot touch F32's synthetic
half** — which is what [F39](followups.md#f39--the-harness-records-a-detection-binning-the-run-never-applied-and-7-datasets-have-never-run-at-theirs)'s
hand-off §5 already says, now confirmed from the files rather than from the note. F19(a)/(b) are product-side and
render nothing. F19(c) — the only item in the register that *would* re-render — is deferred out of this wave (§0.2).
The real bank is not touched at all. **So the arm is exactly as runnable and exactly as comparable after wave 8 as
it is today.**

**And wave 9 does not end it either.** F19(c) moves `SynthBankDerivations.MinExposureSeconds`, which re-derives the
exposure for the **8 datasets sitting on the 0.5 s floor** — `D01`–`D05`, `D07`, `D13`, `D14` (re-measured in wave 7;
the entry's original "12 of 17" has drifted). `D18` / `D19` / `D20` are not among them.

**So the option cost of this deferral, stated plainly:** the φ = 0.50 keep floor stays **default OFF** for one more
wave. That is the whole cost. No comparability is lost, and no arm becomes more expensive.

**Why not simply run it anyway.** Two reasons, and the second is the decisive one.

1. It is not a small arm. Wave 6 changed its shape — it now needs a **third** `--continue-rounds` arm — and its
   scope is *both full banks* (20 synthetic + 19 real) rather than wave 5's 8-run screening subset. That is ~117
   optimizations plus `bank-verify` on both banks, and it deserves its own design document rather than a section in
   someone else's.
2. **[F15](followups.md#f15--optimize---per-run-overwrites-each-runs-stored-settings) makes it actively harmful to
   bundle.** `optimize --per-run` writes `optimized_settings.json` back into every run folder it visits. A full-bank
   F32 arm would re-baseline the stored settings of all 20 synthetic run folders in the same wave that F39(b)'s
   adoption is trying to establish a clean, *stated* stored landing on seven of them. Two re-baselines of the same
   files in one wave is precisely the silent drift the F15 rule exists to prevent, and unpicking which arm owns
   which folder afterwards is the kind of provenance question
   [F30](followups.md#f30--a-stored-optimized_settingsjson-does-not-say-which-config-produced-it) exists because we
   have got wrong before.

**The trigger that ends the deferral, fixed here so this cannot recur indefinitely:** F32's confirmation arm runs
**before any wave that re-renders `D18` / `D19` / `D20` or that runs `optimize --per-run` over the full synthetic
bank for another purpose.** No such wave is currently scheduled. If none appears, it is **wave 9's item 1**.

### 0.2 — F19(c) is DEFERRED to wave 9; (a) and (b) ship here

F19's reopened entry lists three separable parts. (a) the trigger and (b) the saturated recommendation are
**product-side**: they change what the optimizer wizard computes and shows, and they move no rendered frame. (c) —
re-checking the `MinExposureSeconds = 0.5 s` floor — is **bank-side**: `SynthBankDerivations.cs:326`, consumed at
`:449-451`, and it is what clamps `D02`'s derived exposure. Changing it re-derives the exposure of the eight
floor datasets and therefore **re-renders them**.

**Why that cannot share a wave with F39(b)'s adoption.** `D14_cdk14_2563mm_e47` is on the 0.5 s floor **and** is one
of F39(b)'s seven. A wave that moves the exposure floor moves `D14`'s frames while the adoption arm is measuring
`D14`'s landing at a new detection factor — two variables on the same dataset, in the same wave, with one
before/after between them. That is exactly the trap wave 7 was structured to avoid and wave 6 refused, and the fact
that only one dataset overlaps makes it worse rather than better: the confound would be invisible in six of the
seven rows.

There is a second, independent reason: F39(b)'s adoption rests on the **13 binning-1 datasets returning
bit-identical** as a free control ([F41](followups.md#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)'s
shape). Seven of those 13 (`D01`–`D05`, `D07`, `D13`) are on the exposure floor. Re-rendering them destroys the
control that makes the adoption readable.

**So (c) is wave 9's, and it carries the re-render with it.** That is also the wave that must run F32's
confirmation arm first if it has not run by then (§0.1) — noting that (c)'s eight datasets do not include
`D18`/`D19`/`D20`, so the two are compatible in either order within that wave.

### 0.3 — F39(b)'s arm ordering is REVERSED from wave 7

Wave 7 ran the status-quo binning-1 arm **last** on purpose, so the bank's folders kept their pre-wave
configuration while nobody had taken the adoption decision. This wave takes the decision, so the ordering inverts:
**status-quo arm FIRST, adopted arm LAST**, so the folders keep the adopted landing. F39's hand-off §4 says this
verbatim; it is repeated here because it is an instruction to the *script*, not a conclusion, and a script written
from memory gets it backwards.

---

## §1 — F46: the gate. Is the bright-tier loss a units bug or a real cost?

[F46](followups.md#f46--detection-binning-buys-faint-stars-and-quietly-sells-bright-ones-to-the-shapesize-gates)
blocks F39(b). Scored against the checked-in per-class `recallHighMin = 0.90`, binning 2 puts two datasets below
their band — `D12_c14_585_afbin2` at **0.813** (L34) and `D15_cdk20_3454mm_e47` at **0.874** (L47) — and
[`synthetic-af-bank-expectations.json`](synthetic-af-bank-expectations.json)'s own rule forbids fixing a breach by
widening the band. So this is a gate, not an observation.

### 1.1 — What is already known, and what is NOT

Known, from wave 7's arm G1 (14 `golden eval` runs, `--params default --defocus-donut --match centroid
--match-radius 12`, `--settings` pinned):

| dataset | recall@all 1 → 2 | recall@high 1 → 2 | precision |
|---|---|---|---|
| `D12_c14_585_afbin2` | 0.562 → **0.675** | 0.879 → **0.813** | 1.000 / 1.000 |
| `D15_cdk20_3454mm_e47` | 0.764 → **0.917** | 0.931 → **0.874** | 1.000 / 1.000 |
| `D08_c11_2800mm` | 0.870 → **0.962** | 1.000 → 0.970 | 1.000 / 1.000 |
| `D10_rc16_3250mm_sparse` | 0.852 → **0.939** | 0.966 → 0.948 | 1.000 / 1.000 |

**What is NOT known is which gate eats the BRIGHT stars.** `golden eval`'s false-negative attribution is aggregated
over **all** tiers. On `D12` it accounts for 107 false negatives of which only 20 are bright, so reading the table
as an explanation of the bright-tier loss is an inference, not a measurement:

| gate | `D12` bin1 → bin2 | `D15` bin1 → bin2 |
|---|---|---|
| `LowSensitivity` | 114 → **34** | 52 → **0** |
| `NO CANDIDATE (structure gap)` | 4 → **23** | 0 → **5** |
| `TooDistorted` | 13 → 19 | 2 → 4 |
| `TooSmall` | 0 → **6** | 0 → **1** |
| `NotCentered` | 0 → **6** | 0 → 0 |
| `OnBorder` | 0 → **4** | 0 → **1** |
| `Contaminated` | 0 → **6** | 6 → **10** |
| `TooFlat` | 13 → 8 | 0 → 0 |
| ACCEPTED-elsewhere | 0 → **1** | 0 → 0 |

`TooLowHFR` appears in none of the fourteen runs, which is what already ruled out
[F38](followups.md#f38--the-minhfr-seed-trigger-compares-a-captured-pixel-vertex-against-a-binned-pixel-gate).

**And the obvious size story does not survive first contact with the numbers.** In-focus HFR in captured pixels
(`truthModel.hfrMin ÷ captureBinning`), and what it becomes in the binned raster at factor 2:

| dataset | captured HFR | binned HFR at ×2 | Δ recall@high |
|---|---|---|---|
| `D08` | 4.86 | 2.43 | −0.030 |
| `D12` | 5.10 | 2.55 | **−0.066** |
| `D17` | 5.30 | 2.65 | 0.000 |
| `D14` | 5.30 | 2.65 | +0.008 |
| `D09` | 5.33 | 2.67 | +0.011 |
| `D10` | 5.60 | 2.80 | −0.018 |
| `D15` | 5.95 | **2.97** | **−0.057** |

`D15` lands at 2.97 binned px — dead on `DetectionBinningResolver.TargetHfrPixels = 3.0`, the exact centre of the
band every pixel-unit knob is calibrated for — and loses more bright stars than `D08` at 2.43. **The loss is not
monotone in star size**, so "the stars got too small for the gates" cannot be the whole story, and possibly not any
of it. That is worth knowing before spending a sweep on it.

### 1.2 — The design intent, which the hypothesis has to argue against

`DetectionBinningResolver`'s class doc states the rule the whole feature is built on:

> every pixel-unit detector knob (`MinHFR`, `MinimumStarBoundingBoxSize`, `NoiseReductionRadius`,
> `StructureLayers`, …) is calibrated for an in-focus HFR near `TargetHfrPixels`. Pick the integer factor that
> brings the measured HFR closest to it.

So the knobs staying at their nominal values **inside the binned raster is the intent, not an oversight** —
`ApplyFactor` rescaling only `PixelScale` is consistent with that, because `PixelScale` is the one quantity that
must stay in sky units. A units-bug claim therefore has to say which specific knob's calibration does *not* follow
star size, and the sweep is what says it.

### 1.3 — Three hypotheses, each with a prediction that can be wrong

**H1 — a pixel-unit knob does not track star size.** `MinimumStarBoundingBoxSize` (5) and `StructureLayers` (4) are
the two candidate-formation knobs whose meaning the factor changes. *Predicts:* the lost bright stars are the
**smallest** ones, isolated, and a smaller `--min-box` (or a different layer count) recovers them.

**H2 — half the linear resolution BLENDS close pairs.** The golden's own policy separates stars at
`mergeSeparationHfrMultiple = 2.0` measured at native resolution; at factor 2 the detector resolves half as well, so
a close pair collapses into one candidate and the second golden box becomes a false negative. *Predicts:* the lost
bright stars are concentrated at **small nearest-neighbour separation**, and a bin-2 detection usually **exists**
within a match radius of them (matched to the neighbour). `D12`'s single `ACCEPTED-elsewhere` at bin2 and the
`Contaminated` rise on both datasets are the fingerprints this predicts.

**H3 — the cost is real.** Binning buys SNR with linear resolution and some bright-tier recall is simply the price.
*Predicts:* neither concentration appears, no knob sweep recovers the band, and the loss is spread across gates.

H1 and H2 are not exclusive; H3 is what remains if both are refuted.

### 1.4 — The arms, cheapest first, and what each one settles

**Arm P0 — the blend check. No run, no code, files already on disk.** For `D12` and `D15`, cross
`D:\hf_w7\golden\{ds}_bin{1,2}\attempt01\detected_f*.csv` (accepted detections with bbox and HFR) against the
per-image `*.golden.json` in the bank. Isolate the **lost-bright set**: golden boxes of confidence `high` matched at
bin1 and unmatched at bin2. For each, report nearest golden-neighbour distance, whether a bin2 detection sits within
one match radius, that detection's bbox, and distance to the frame border. This is the same discipline that made
wave 7's arm G1 decisive — read what the instrument already wrote before building another one.

**Arm P1 — `--min-box` sweep. No code;** `golden eval` already exposes the flag. `D12` and `D15` at
`--detection-binning 2`, `--min-box ∈ {2, 3, 4, 5}` (5 reproduces wave 7 exactly and is the arm's own control), then
`D08` and `D10` at whichever value wins. 10 evals, ~5 minutes.

**Arm P2 — `--structure-layers` sweep. No code.** Same two datasets at factor 2, `--structure-layers ∈ {3, 4, 5}`
(4 is the control). 6 evals.

**Arm P3 — per-tier FN attribution. Small code, CONDITIONAL.** Only if P0–P2 leave the mechanism unnamed. The FN
attribution loop in `GoldenEvalRunner` already has the golden box's confidence rank in hand one loop earlier; the
change is to key `FnByGate` by tier as well, so the report prints the attribution for the high tier separately.
That turns "recall@high fell 6.6 points" into "the bright stars went to *this* gate" — which is what triage needs
and what the aggregate table cannot say.

### 1.5 — Pre-registered rules, fixed before any arm runs

> **R1 (H1 — the knob).** H1 is CONFIRMED only if some setting of `--min-box` or `--structure-layers`, at
> `--detection-binning 2`, brings `recall@high` to **≥ 0.90 on BOTH `D12` and `D15`** while holding **precision
> ≥ 0.98** and **not reducing `recall@all` below its binning-2 value** (0.675 / 0.917). Anything less — including
> "it recovers `D12` but not `D15`" — REFUTES H1 as a sufficient explanation, and it is reported as refuted.

> **R2 (H2 — blending).** H2 is CONFIRMED only if, in the lost-bright set, **≥ 50 %** of stars have a golden
> neighbour within **2 × the frame's in-focus HFR** *and* a bin-2 detection within one match radius. The comparison
> group is the bright stars that were **kept** at bin 2 on the same frames; if their nearest-neighbour distribution
> is the same, the concentration is an artifact of crowding rather than of blending, and H2 is REFUTED.

> **R3 (the inertness control).** A sweep value equal to the shipped default must reproduce wave 7's numbers
> **exactly** (`D12` recall@high 0.813, `D15` 0.874). If it does not, the instrument moved between waves and no
> other number in this section is readable. This is [F41](followups.md#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)
> applied to a new binary, and it is checked FIRST.

> **R4 (too-good-to-be-true).** If a swept knob raises `recall@high` **and** `recall@all` **and** precision all at
> once on both datasets, that is an instrument failure until proven otherwise — the sweep is over
> candidate-formation knobs, where every real setting trades something. Wave 7's rule that a number agreeing too
> well with the hypothesis is also a fault, applied here.

### 1.6 — If H1 and H2 are both refuted: what a legitimate band re-derivation looks like

The expectations file's rule is not negotiable: *"A cell landing outside its band is a FLAG that gets triaged into
`docs/followups.md` — it is NEVER fixed by widening the band here."* A re-derivation is nonetheless legitimate
**for a stated reason**: the bands were calibrated at binning 1, and for these seven datasets binning 1 is a
configuration **the bank never intended** — their own `expectedOptimal.detectionBinning` is 2, and their exposures
were derived at binning 2 (`exposureDefinition` says so verbatim). A band fitted to a configuration the spec
disclaims is a band fitted to the wrong experiment.

If that is where this lands, the edit must satisfy all four of:

1. **State both directions.** Overall recall RISES on 7/7 while `recall@high` falls on 4/7, so a re-derivation
   TIGHTENS most cells and loosens two. A change that only loosens is not a re-derivation, it is the widening the
   rule forbids.
2. **Name the granularity problem rather than paper over it.** A per-class floor is the wrong instrument when one
   class holds datasets at two detection factors (`L47` holds `D07`/`D11` at factor 1 and `D10`/`D14`/`D15`/`D17`
   at factor 2). Either the band becomes per-factor, or `D12`/`D15` get a per-dataset override carrying its own
   reason.
3. **Make the trade visible in the file.** The file currently scores `recallHighMin` and `precisionMin` only, so a
   change that trades 6.6 points of bright recall for 15 points of overall recall reads as a pure regression. Add
   `recallAllMin` for the affected cells so the file records what was bought as well as what was sold.
4. **Keep it a separate, separately-argued commit** from the adoption itself, so a reader can revert one without
   the other.

---

## §2 — F39(b): adopting the derived detection binning

**Gated on §1.** If F46 lands on a fix, the adoption runs with the fix in place and the breach never occurs. If
F46 lands on H3, the adoption runs behind §1.6's band re-derivation, as a separate commit.

### 2.1 — The decision

Make the headless path **honour each run's derived detection binning factor by default**, so the bank tests the
axis it has always claimed to test. The evidence is wave 7's, and it is not marginal: overall recall up on 7/7
(+0.087 … +0.146) at precision 1.000, `LowSensitivity` false negatives collapsing 184 → 4 on the worst case, and
**σ_focus improving on 7/7 by 17 % to 95 %** — `D17` from 2.65 focuser steps of uncertainty to 0.12.

**What does NOT change, stated so the wave is not read as bigger than it is.** The **frames do not move**:
`detectionBinning` is detector-side and enters no render input (`GenerateSweep` takes centre / step / offsetSteps /
exposure). **The product is unaffected**: the live app already applies the user's factor through
`HocusFocusStarDetection.ApplyDetectionImageContext`. This is a harness/bank change on 7 of 20 datasets.

### 2.2 — Mechanism

Prefer flipping the default of `optimize --apply-run-detection-binning` (with an explicit opt-out) over removing the
discard at `OptimizationDiagnosticRunner.cs:443`. The flag deliberately applies **only** the binning; making the
whole per-run `Resolved` bundle authoritative would let a per-run settings file shadow the `--settings` file every
arm pins, which would break the comparability of every arm this project has run
([F42](followups.md#f42--every-build-directory-silently-gets-its-own-detector-settings-and-the-run-instructions-require-a-new-one-per-arm)).
The opt-out is what keeps every prior wave's arm reproducible on a current binary.

`golden eval` gets the same default and the same opt-out, so the two harnesses cannot disagree about what the bank
tests. `--detection-binning N` stays as the explicit override that wave 7 added.

### 2.3 — Arms and controls

- **Arm A — status-quo, FIRST.** `optimize --per-run` with the opt-out, over **all 20** datasets.
- **Arm B — adopted, LAST.** `optimize --per-run` at the new default, over all 20. F15: the run folders end holding
  arm B's landing, which is the intended state and is recorded as such in the results doc.
- **The free control.** The **13** binning-1 datasets must come back **bit-identical** between A and B. A
  difference there is a fault, not a result.
- **The second control.** The seven's arm-B numbers are compared against wave 7's arm G2 table (same datasets, same
  semantics, different binary). Divergence means something other than the binning moved.
- **`BaselineJ` identical across arms** on every run, [F41](followups.md#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)'s
  tell that the comparison is valid at all.
- **Scored against the checked-in bands**, not only against the other arm. That comparison, and nothing else, is
  what found this gate in the first place.

---

## §3 — F19: the trigger, and a recommendation that must admit it saturated

Wave 7's pre-registered arm E refuted the wave's own "no change" position: `D02_rich_135mm` gains **44.1 % of
σ_focus at 8×** its derived exposure, and `SensitivityIsAtFloor` is **false at every rung** (landed Sensitivity
9–49), so the exposure block is **never surfaced**. The gap is upstream of the `NTarget = 20` statistic.

### 3.1 — The question wave 7 did not ask, and it decides the shape of the fix

Widening the trigger only helps if, once surfaced, the block says something **true**. On `D02` at 0.5 s the field
carries 2746 stars above the gate, so `S_now` — the 20th-brightest accepted star's SNR — is large, the raw factor
`(TargetSensitivity / S_now)²` is small, and the recommendation would land on the
`ExposureIsNotTheLimit` branch. If that is what it does, then **surfacing the block would publish a wrong answer**,
and part (a) must not ship as "widen the trigger" alone.

This is currently an argument, not a measurement — and wave 7's own lesson is that an argument about mechanism can
be entirely correct about what it addresses and still address the wrong thing. So it gets an arm.

**Arm X — "what would the block say if it fired?"** The five exposure rungs of `D02` and `D16` are **still on disk**
(`D:\hf_w7\armE\t{0.5,1,2,4,8}`, 4.7 GB), so **no re-render is needed.** The harness records
`ExposureRecommendation` as `null` whenever `SensitivityIsAtFloor` is false — verified in
`D:\hf_w7\armE\opt_0.5_D02_rich_135mm\aggregate_summary.json` — so the instrument is one harness-side change:
compute and record the recommendation unconditionally, gating only the *display*, never the *measurement*. `D16` is
the control: its σ_focus minimum sits exactly at its derived 2 s, so the derivation is known correct there and the
recommendation should say so.

> **R5, fixed before Arm X runs.** At `D02`'s derived 0.5 s rung, with the run's own frames:
> - **If** the recommendation reports `ExposureIsNotTheLimit`, **or** asks for less than **2×** current, while the
>   8 s rung is measured 44 % better in σ_focus — then the statistic is wrong for this population, **part (a) does
>   not ship as a trigger widening**, and the wave ships the instrument plus (b) plus a new entry for
>   σ_focus-driven exposure advice.
> - **If** it asks for **≥ 4×** current, the statistic was right all along and only the trigger hid it; (a) is then
>   the whole fix and ships as a trigger widening.
> - Anything between 2× and 4× is a partial: the trigger widens, and the shortfall is filed.
>
> `D16` is the control in both branches: at its derived 2 s the recommendation must NOT ask for materially more,
> because more is measurably worse there (σ_focus 0.064 → 0.120 at 4 s). If it does, the instrument is wrong and
> `D02`'s reading is unusable.

### 3.2 — Part (b): saturation is a fact about the answer, not an answer

A recommendation whose arithmetic hit a bound must say so, in both places the project computes one.

**Product.** `ExposureRecommendation` already carries `WasCapped` / `CapWasRunRelative` for the two *upper* bounds
(`MaxExposureFactor = 4`, `MaxRecommendedExposureSeconds = 30`). The unreported case is the lower branch: when
`RawSeconds ≤ currentExposureSeconds`, `RecommendedSeconds` is returned as `currentExposureSeconds` **exactly** and
the verdict is `ExposureIsNotTheLimit`. "Your exposure is fine" and "the arithmetic asked for less than you are
already using, so this number is your own input handed back" are different statements, and only the second is what
happened. The derivation tooltip (`StarSignalCopy.DescribeExposureDerivation`) is where that belongs — background
in a tooltip, not in the paragraph, matching the sibling detection-binning block's own rule.

**Harness.** `SynthBankDerivations` clamps to `[MinExposureSeconds, MaxRecommendedExposureSeconds]` and reports the
clamped value as *the* derived exposure with a band of `[0.5, 0.5]`. On `D02` the arithmetic asked for far less than
0.5 s: the value carries **no information**, and the band's zero width is the tell that nothing was measured rather
than that everything agreed. `synth-bank --dry-run` must say which datasets' exposures saturated, because that list
is exactly wave 9's F19(c) work list. This is a reporting change only — it moves no frame and writes no new field
into a rendered dataset's `synthetic_meta.json`.

### 3.3 — Part (a): the trigger, in whichever shape R5 licenses

If R5's second branch fires: surface the exposure block when the recommendation has a **material ask** (at least one
`RoundExposureSeconds` ladder step above current), independent of where the Sensitivity gate landed. The
`LowSignalChartNote` stays gated on the Sensitivity floor — it makes a specific claim about the gate, and the
existing doc comment explains at length why it must not be re-pointed at a different condition.

If R5's first branch fires: the trigger is not widened, and the wave says so. Publishing a wrong recommendation to
more users is a worse outcome than publishing none, and the honest deliverable is the diagnosis plus the entry.

---

## §4 — F48: state the statistic over the cells that can move

Wave 7's arm S was rejected by a pre-registered rule whose statistic — the median σ_focus ratio over 26 cells — read
**1.0000** for a distribution with **6 cells > 20 % better and 2 cells > 20 % worse**, because **18 of the 26 ties
are STRUCTURAL**: `S0` converges in a single round on most datasets, so only round 0 is ever rendered and no arm can
differ.

**The verdict stands.** The rule was fixed before the arm ran and was applied as written; a pre-registered statistic
that turns out to be poorly matched to the data is a lesson for the next rule, not a licence to pick a better one
once the numbers are in. This item does not re-open it.

**What it does** is find out what arm S actually does, over the cells where the arms *can* differ — from
`D:\hf_w7\f18arms\{C,D,S}_S*\synth_validate_report.json`, which carries a `rounds` array per (dataset, scenario)
cell. Restrict to cells with **more than one rendered round**, then report n, the median ratio S/C and D/C over that
set, and the better/worse counts. Report the excluded count and *why* it was excluded, in the same table — a
denominator that quietly drops 18 of 26 cells is the same failure in the other direction.

No new runs. No new code in the plugin.

---

## §5 — Measurement discipline for this wave

The first five cost earlier waves real time; the last four are wave 7's, and one of them is what found F46.

| rule | how it binds here |
|---|---|
| **PIN `--settings` on every arm** ([F42](followups.md#f42--every-build-directory-silently-gets-its-own-detector-settings-and-the-run-instructions-require-a-new-one-per-arm)), and READ the `UseAdvanced=False` warning | every `golden eval` and `optimize` invocation; the warning's "overrides N advanced knob(s)" count must read **0** |
| **`BaselineJ` is a free control** ([F41](followups.md#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)); a number agreeing TOO WELL is also an instrument failure | §2.3's arm A/B check, and R4 |
| **Check whether a cheap instrument already exists** | P0 reads files already on disk; P1/P2 need no code; Arm X needs no re-render |
| **Change one thing; `--dry-run` the derived-parameter diff BEFORE rendering** | nothing renders this wave — the dry-run diff runs anyway, as the **inertness control** |
| **Count tests that DISCRIMINATE. Revert the fix, see which fail** | every code change in §1.4/§2.2/§3.2 |
| **WRITE THE FALSIFICATION RULE DOWN BEFORE THE ARM RUNS** | R1–R5 above, all fixed here |
| **A rule that passes by being INERT has not been validated** | R1 requires the band to be *reached*, not merely not-regressed; R5's branches are both actionable |
| **STATE THE STATISTIC OVER THE CELLS THAT CAN MOVE** | §4, and R2's comparison group |
| **SCORE AGAINST THE CHECKED-IN BANDS**, not just the other arm | §1.5, §2.3 |
| **On a red CI check** | verify the test COUNT ([F37](followups.md#f37--the-ci-test-host-crashes-natively-accessviolationexception-aborting-2000-tests-with-zero-failures)) **and** check githubstatus.com — wave 7 hit a real Actions `major_outage` that failed docs-only commits before any test ran |

**New findings are FLAGGED into `docs/followups.md`, not fixed inline.**
