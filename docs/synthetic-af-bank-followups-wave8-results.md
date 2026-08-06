# Synthetic AF bank — followups wave 8 (results)

Design: [`docs/synthetic-af-bank-followups-wave8-design.md`](synthetic-af-bank-followups-wave8-design.md).
Plan: [`plans/synthetic-af-bank-followups-wave8-plan.md`](../plans/synthetic-af-bank-followups-wave8-plan.md).
Wave 7: [`docs/synthetic-af-bank-followups-wave7-results.md`](synthetic-af-bank-followups-wave7-results.md).
Register: [`docs/followups.md`](followups.md).

*The wave opened on a gate: [F46](followups.md#f46--detection-binning-buys-faint-stars-and-quietly-sells-bright-ones-to-the-shapesize-gates)
blocked F39(b)'s adoption because binning 2 pushed two datasets below their checked-in `recallHighMin`. **Both
pre-registered F46 hypotheses were refuted, and the mechanism turned out to be a third thing neither of them
named** — found by reading the detector's own source comment after the measurements had ruled the other two out.*

## Status of this document

**Complete for every arm that ran.** Three pre-registered rules fired against what this wave set out believing,
and all three are reported as they landed. Nothing below is a projection.

| item | state |
|---|---|
| **F46** — the gate | **Mechanism identified: `StructureLayers`.** R1, R2 and R6 all REFUTED; R3/R4 passed. No blanket fix ships. See §1 |
| **F39(b)** — adoption | **Adopted**, with the two breaching cells left as FLAGS pointing at F46 — the expectations file's own rule, applied rather than worked around. See §2 |
| **F19** — trigger + saturation | **R5 FIRED ON BRANCH 1 — widening the trigger would publish a WRONG answer.** See §3 |
| **F48** — arm S re-score | **DONE, and the denominator alone decided wave 7's verdict.** See §4 |
| **F49** — new, from a FIELD report | The Star signal block has no remedy for a floored gate on a rich field. Filed, not fixed. See §5 |
| **F50** — new | A gate's false-negative count is an upper bound on what relieving it buys, not an estimate. See §5 |
| the re-render | **NONE.** Two independent inertness controls pass |
| F32's confirmation arm | **Deferred a fourth time, with the cost measured** (§0.1 of the design) — and wave 8 verifiably did not consume it |

## Headline

| what | result |
|---|---|
| **F46's gate** | **Answered — and by a mechanism neither pre-registered hypothesis named.** The knob is `StructureLayers`, whose à-trous residual is scaled in PIXELS while binning halves every structure's pixel extent. A binning-1 control discriminates: deeper layers HURT at bin 1 and HELP at bin 2 |
| …and its fix | **REFUTED by the five datasets that did not motivate it.** Measured on all seven, `layers += log2(factor)` improves `recall@high` on **3 of 7** and costs `D09`. On `D12`+`D15` alone it looks like a clean 2-for-2 win. **No blanket scaling ships** |
| **F39(b)** | **ADOPTED. σ_focus improves on 7 of 7 by 17.3–95.5 %**, and the 13 binning-1 datasets come back **bit-identical** — 0 control violations of 13 |
| …and a fact nobody had | **`D17`'s `BaselineJ` is exactly 0.000 at binning 1 and 0.9948 at its own factor.** [F20](followups.md#f20--below-minhfr-the-autofocus-objective-collapses-to-exactly-zero-with-no-diagnostic)'s collapse, on a dataset that has never once been run at the factor its own physics specifies |
| **F19** | **The pre-registered rule fired against the obvious fix for the second wave running.** With the recommendation finally measured on healthy-gate runs, `D02` reports `ExposureIsNotTheLimit` with a raw ask of **0.000 s** at every rung of a 16× range over which σ_focus improves 44 %. **Widening the trigger would publish a wrong answer** |
| **F19(b)'s by-product** | The saturated set, listed for the first time: **13 of 20**, not the 8 on record — including **two at the CEILING**, a direction nobody had counted, one of them **11× short** of its own derivation |
| **F48** | Over the 12 cells that CAN move, arm S beats the control by **10.2 %**; F18's rule needed >5 %. **The denominator alone decided wave 7's verdict** — which still stands |
| **F46's instrument** | Its first output **corrected F46's own filed account** of where the bright stars go |

---

## §1 — F46: it is `StructureLayers`, and neither hypothesis on the register named it

### The two pre-registered hypotheses were both refuted, by measurement

**R2 (blending) — REFUTED, and the refutation is what pointed at the answer.** The offline blend check
(`D:\hf_w8\p0\blend_check.py`, no detector run: it cross-references the detections and goldens wave 7 already
wrote) isolates the *lost-bright set* — golden boxes of confidence `high` matched at binning 1 and unmatched at
binning 2 — against the *kept-bright* comparison group on the same frames. The reproduced per-frame TP/FN match
wave 7's report exactly on every frame, so the matcher is faithful.

| | `D12_c14_585_afbin2` | `D15_cdk20_3454mm_e47` |
|---|---|---|
| lost-bright / kept-bright | 8 / 87 | 5 / 76 |
| median nearest golden neighbour, lost | **79.1 px** (15.5× in-focus HFR) | **828.9 px** (139× HFR) |
| fraction with a neighbour ≤ 2× HFR | **0.000** | **0.000** |
| fraction with ANY binning-2 detection within the match radius | **0.000** | **0.000** |
| **median golden bbox, lost vs kept** | **48×48** vs 36×36 | **66×66** vs 42×42 |

The lost stars are **isolated** — nothing is blending — and nothing was detected near them at all. **But the bbox
row inverts the other hypothesis:** the stars binning 2 loses are *larger* than the ones it keeps, several at
80×80 px, and on `D15` all but one sit at the sweep's outer frames. Those are heavily defocused **donuts**, not
small stars.

**R1 (a pixel-unit size knob) — REFUTED, with a mechanism worth recording.** `--min-box ∈ {2,3,4,5}` at binning 2
is **byte-identical on both datasets** — every recall figure, every tier. The flag is applied (the report's knob
line reads `MinBox=2`) and it does what it says: `REJECTED:TooSmall: 6` disappears from `D12`'s attribution. **Those
six stars are simply not recovered** — they fail the next gate instead (`TooDistorted` 19→22, `LowSensitivity`
34→37) and overall recall does not move by one star.

> **A gate's false-negative count is an UPPER BOUND on what relieving it buys, never an estimate.** The
> attribution names the FIRST gate a candidate hits; relieving it can just hand the candidate to the next one.
> Filed as a new followup, because every prior wave has read these tables the other way.

### The mechanism: the wavelet residual that removes "large structures" is scaled in PIXELS

`StarDetector`'s step 4 subtracts an à-trous B3-spline residual to erase large-scale structure such as nebulae.
Its own source comment predicts exactly this defect:

> *"If the pixel scale is very small or need a wide range for focus, **you may need to increase the number of
> layers to keep stars from being excluded**"*

The residual's scale is `2^layers` **pixels**. Detection binning **halves every structure's pixel extent** while
the layer count stays fixed, so a defocused donut that comfortably survived the subtraction at binning 1 is inside
the residual at binning 2 and is erased before candidate formation. That is precisely `NO CANDIDATE (structure
gap)` — the attribution category that appeared from nowhere at binning 2 (`D12` 4→23, `D15` 0→5) — and it is
precisely the *large, isolated, wing-frame* stars the blend check found.

`DetectionBinningResolver.ApplyFactor` rescales only `PixelScale`. `StructureLayers` is carried into the
half-resolution raster unchanged.

### The discriminating control: the same knob at binning 1

R4 required this before the result could be believed at all — a knob that improves `recall@high` *and*
`recall@all` on both datasets is an instrument fault until explained. The explanation has to be that the effect is
**binning-specific**, and it is:

**`recall@high`**

| `--structure-layers` | `D12` bin 1 | `D12` **bin 2** | `D15` bin 1 | `D15` **bin 2** |
|---|---|---|---|---|
| **4** (shipped) | 0.879 | 0.813 | 0.931 | 0.874 |
| 5 | 0.860 | 0.822 | 0.931 | 0.897 |
| 6 | 0.869 | **0.841** | 0.897 | **0.977** |
| 7 | 0.879 | 0.822 | 0.920 | **0.977** |
| 8 | 0.869 | 0.794 | 0.943 | **0.977** |

**`recall@all`** (precision is 1.000 in all 20 runs)

| `--structure-layers` | `D12` bin 1 | `D12` **bin 2** | `D15` bin 1 | `D15` **bin 2** |
|---|---|---|---|---|
| **4** (shipped) | **0.562** | 0.675 | **0.764** | 0.917 |
| 5 | 0.550 | 0.672 | 0.760 | 0.909 |
| 6 | 0.550 | **0.742** | 0.717 | 0.937 |
| 7 | 0.553 | 0.711 | 0.713 | **0.969** |
| 8 | 0.553 | 0.690 | 0.740 | **0.969** |

**At binning 1 the shipped 4 maximises `recall@all` on both datasets** (0.562 and 0.764) and **every deeper
setting is worse**, while `recall@high` is flat-to-noisy across the whole range (`D12` 0.860–0.879, `D15`
0.897–0.943) with no systematic gain — `D15`'s 0.943 at layers 8 is the one cell above the shipped value and it
comes with `recall@all` still down at 0.740. **At binning 2 both rise substantially** up to a peak. The knob's
optimum moves with the binning factor, which is the units claim stated as a measurement rather than as an
argument.

### The high-tier attribution, which CORRECTS what F46's entry says the bright stars die of

The aggregate attribution mixes every tier; on `D12` it explains 107 false negatives of which only 20 are bright.
The new high-tier-only table (shipped this wave, §1 "what shipped") measures it instead of inferring it — and it
does not say what F46's entry, reading the all-tier table, said it said:

**High-tier false negatives at binning 2, shipped `--structure-layers 4`:**

| gate | `D15_cdk20_3454mm_e47` | `D12_c14_585_afbin2` |
|---|---|---|
| `REJECTED:Contaminated` | **10** | 3 |
| `REJECTED:TooFlat` | 0 | **8** |
| `REJECTED:NotCentered` | 0 | 3 |
| `REJECTED:TooDistorted` | 0 | 2 |
| `NO CANDIDATE (structure gap)` | **0** | 2 |
| `REJECTED:OnBorder` | 1 | 1 |
| ACCEPTED-elsewhere | 0 | 1 |

**`D15`'s bright-tier loss is `Contaminated`, and its structure-gap count is ZERO.** F46's entry names
"`NO CANDIDATE (structure gap)`, `TooSmall`, `NotCentered`, `OnBorder`, and more `TooDistorted`" as where the
bright stars go — that list is read off the ALL-TIER table and is right about the faint tier and wrong about the
one the bands are scored on. `TooSmall` does not appear in the high tier at all, which is the same conclusion the
`--min-box` sweep reached independently.

**And at `--structure-layers 6` those ten `Contaminated` rejections go to ZERO** (`D15`), while `D12`'s `TooFlat`
falls 8 → 2 and `NotCentered` 3 → 0.

> **The gate is measured; the causal chain is an interpretation and is labelled as one.** The consistent reading:
> at binning 2 with a residual scale too close to the donut, the subtraction eats the donut's outer annulus, the
> surviving candidate's bounding box is wrong, and the local background box then samples the donut's own flux —
> which is what the contamination test is built to reject. Deeper layers keep the donut whole and the test stops
> firing. That explains why the *lost-bright set is the LARGE, isolated, wing-frame stars*, which is measured. The
> step from "structure layers" to "contamination" is not.

### R1's verdict, applied as written

> **R1 is REFUTED.** No setting of either knob brings `recall@high` to ≥ 0.90 on **both** datasets. `D15` clears
> comfortably from layers 6 (**0.977**, above even its binning-1 0.931); `D12` peaks at layers 6 (**0.841**) and
> falls away again at 7 and 8.

The rule was fixed before the arms ran and it is applied as written: the knob is **not a sufficient fix**. What it
*is* — a confirmed, binning-specific, mechanism-backed partial — is reported as that, and the residue on `D12`
is triaged separately rather than absorbed into a rounded-up claim.

**`D12` was already breaching at binning 1** (0.879 < 0.90), which
[F39](followups.md#f39--the-harness-records-a-detection-binning-the-run-never-applied-and-7-datasets-have-never-run-at-theirs)'s
hand-off recorded. So the adoption does not *create* `D12`'s breach; it deepens it by 0.066, of which the layer fix
returns 0.028.

### R6 — and this is why the rule was measured on all SEVEN rather than on the two that motivated it

Two datasets are enough to find a mechanism and nowhere near enough to derive a product rule from. R6 was fixed
before the sweep ran: *the dyadic rule `StructureLayers += round(log2(factor))` is adopted only if, across all
seven at factor 2, `layers+1` improves `recall@high` on a **majority** and regresses **none** by more than 0.01.*

**`recall@high` at binning 2** (precision 1.000 in all 28 runs; `layers 4` reproduces wave 7 exactly on all seven):

| dataset | **4** (shipped) | 5 (`+1`) | 6 (`+2`) | 7 | Δ at 6 |
|---|---|---|---|---|---|
| `D08_c11_2800mm` | 0.970 | 0.970 | 0.970 | 0.970 | — |
| `D09_c14_3800mm` | **0.989** | 0.978 | 0.978 | 0.967 | **−0.011** |
| `D10_rc16_3250mm_sparse` | 0.948 | 0.983 | **1.000** | 1.000 | +0.052 |
| `D12_c14_585_afbin2` | 0.813 | 0.822 | **0.841** | 0.822 | +0.028 |
| `D14_cdk14_2563mm_e47` | 0.992 | 0.992 | 0.992 | 0.990 | — |
| `D15_cdk20_3454mm_e47` | 0.874 | 0.897 | **0.977** | 0.977 | **+0.103** |
| `D17_cdk14_oiii5` | 1.000 | 1.000 | 1.000 | 1.000 | — |

> **R6 is REFUTED, on both of its clauses.** `layers+1` improves `recall@high` on **3 of 7** — not a majority —
> and `D09` regresses by **0.011**, over the 0.01 bar. `layers+2` fares no better on either clause (same 3 of 7,
> same `D09` cost). **No blanket scaling rule ships.**

**Had this been run on `D12` and `D15` alone it would have looked like a clean 2-for-2 win**, and a hard-coded
`StructureLayers += log2(factor)` would have shipped to every user on evidence drawn entirely from the two cells
that motivated it. The population that did not motivate the hypothesis is what refuted it.

### So what F46 actually is, stated as narrowly as the evidence supports

1. **`StructureLayers` IS a pixel-unit knob carried unscaled into a half-resolution raster.** Confirmed by
   mechanism (the à-trous residual's scale is `2^layers` px), by the source's own warning, and by a binning-1
   control that discriminates.
2. **Its effect is real but NOT uniform.** It dominates where a dataset's wing donuts are large relative to the
   residual scale (`D10`, `D12`, `D15`) and is absent where they are not (`D08`, `D14`, `D17`). It costs on `D09`.
3. **The default is what is wrong, not the code path.** `StarDetectorParams.StructureLayers = 4` is calibrated at
   binning 1, and every number in this section is measured at `--params default` — a configuration **no user keeps
   after optimizing**. `OptimizerVariable` already searches `StructureLayers` over `[1, 8]`, so a per-run
   `optimize` can and does find the right depth. What the bank measures here is the shipped *default*, which is
   exactly what a user gets before they ever open the wizard.
4. **Therefore the fix is not a scaling constant.** Filed with the measurement; the product change is deliberately
   not made this wave, for the same reason [F47](followups.md#f47--a-focus-recovery-step-can-be-placed-where-nothing-is-detectable-and-now-there-is-a-number-that-says-so)
   was not: it changes live detection behaviour and wants its own before/after.

### What shipped for F46

- **`golden eval` high-tier FN attribution** — the same table restricted to the tier the checked-in
  `recallHighMin` bands are scored on, plus a per-star `false_negatives_f<focuser>.csv` (position, size,
  confidence, disposition, gate). This is what corrected the entry's own account of where the bright stars go.
- The all-tier section is unchanged, so every prior report stays comparable.

---

## §2 — F39(b): adopted, and the two breaching cells are left as FLAGS

### The gate is resolved — by applying the expectations file's rule rather than working around it

F46 did not produce a fix that clears the band (§1), so the design's §1.6 anticipated a band re-derivation. **It is
not needed, and doing it would have been wrong.** The expectations file's rule reads:

> *"A cell landing outside its band is a **FLAG that gets triaged into `docs/followups.md`** — it is NEVER fixed by
> widening the band here."*

That rule does not block adoption. It prescribes exactly what this wave has done: **triage the breach**, which §1
does in full — mechanism named, binning-specificity established by a control, the population measured, and the
scaling rule refuted on its own pre-registered terms. And the bands are **documentation, not a gate**: nothing in
the codebase reads `synthetic-af-bank-expectations.json` (`plans/af-recommender-hardening-plan.md:36` calls them
*"aspirational bands … data that no code reads"*), so a breach flags rather than fails.

**So the bands are left untouched, and they are now telling the truth:** `D12` at 0.813 and `D15` at 0.874 say *the
shipped default `StructureLayers = 4` under-performs at detection binning 2 on rigs with large wing donuts* — which
is a true statement about the product that the user gets before they ever open the optimizer. Re-deriving the band
would have encoded a known defect into the file as an expectation. That is the outcome the rule exists to prevent,
reached from the direction nobody anticipated.

**`D12` was breaching before any of this** (0.879 at binning 1). Adoption deepens it; it does not create it.
**`D15`'s breach is new to adoption and is entirely explained**: `--structure-layers 6` takes it to **0.977**, above
even its binning-1 0.931, and the per-run optimizer already searches that knob over `[1, 8]`.

### The decision, and what it does not touch

`optimize --per-run` now applies each run's derived detection binning **by default**;
`--no-run-detection-binning` is the opt-out, and it exists so every arm this project has already run stays
reproducible on a current binary — rebuilding an old commit to get a control is not a control
([F41](followups.md#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)). Wave 7's
`--apply-run-detection-binning` is still accepted and is now a no-op, so its scripts keep working *and keep
meaning what they said*.

`golden eval` is **deliberately NOT re-based.** It is the instrument every prior wave's golden arm was measured
with, and silently defaulting it would re-baseline those comparisons rather than extend them. Instead it now
**states the disagreement**: when the invocation's factor differs from the run's derived one, the report says so
and names the source. The two harnesses cannot disagree *quietly*, which was the actual requirement.

Still applying **only** the binning, never the whole per-run `Resolved` bundle — that would let a per-run settings
file shadow the `--settings` file every arm pins
([F42](followups.md#f42--every-build-directory-silently-gets-its-own-detector-settings-and-the-run-instructions-require-a-new-one-per-arm)).

### The arms: 40 optimizations, all 20 datasets, controls read BEFORE results

**Control 1 — the free control. 0 violations of 13.** Every binning-1 dataset is **bit-identical** between arm A
and arm B, on `J`, `σ_focus`, `RecommendedStep` and the changed-parameter list. `D01` 0.991244 / 0.150999, `D18`
0.999669 / 0.009264, `D20` 0.999756 / 0.005400, and ten more. The adoption cannot touch a dataset whose derived
factor is 1, and now that is measured rather than argued from the code path.

**Control 2 — `BaselineJ` moves on exactly the seven, and nowhere else.** That is the *expected* direction here
rather than [F41](followups.md#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)'s failure
mode: the **seed itself is detected at a different factor**, so its `J` must move. It does, and it moves a long
way:

| dataset | `BaselineJ` A → B |
|---|---|
| **`D17_cdk14_oiii5`** | **0.000000 → 0.994830** |
| `D15_cdk20_3454mm_e47` | 0.893314 → 0.994703 |
| `D10_rc16_3250mm_sparse` | 0.865994 → 0.958929 |
| `D09_c14_3800mm` | 0.918546 → 0.993888 |
| `D08` / `D12` / `D14` | 0.952527 → 0.995175 / 0.939277 → 0.987378 / 0.997060 → 0.997901 |

**`D17`'s baseline was exactly ZERO at binning 1** — [F20](followups.md#f20--below-minhfr-the-autofocus-objective-collapses-to-exactly-zero-with-no-diagnostic)'s
collapse, the curve gated away entirely by `MinHFR` — **and is 0.9948 at its own factor.** So the adoption does not
merely improve seven landings; on one dataset it is the difference between having a baseline curve and not having
one. That is a fact about the bank nobody had, because nobody had ever run it at its own factor.

**Control 3 — arm B against wave 7's arm G2, on a different binary. Identical to six decimal places on all seven**,
`J` and `σ_focus` both (`D08` 0.996853 / 0.532429, `D15` 0.995176 / 0.055769, `D17` 0.995184 / 0.120325, …). Two
binaries, one pinned settings file, byte-identical landings. **This also serves as an inertness control on this
wave's own code**: the unconditional exposure-recommendation computation and the per-tier attribution perturb no
optimizer landing.

### The result

**σ_focus improves on 7 of 7, by 17.3 % to 95.5 %**, reproducing wave 7's arm G2 exactly:

| dataset | `J` A → B | σ_focus A → **B** | improvement |
|---|---|---|---|
| `D17_cdk14_oiii5` | 0.978540 → **0.995184** | 2.647226 → **0.120325** | **95.5 %** |
| `D15_cdk20_3454mm_e47` | 0.995187 → 0.995176 | 0.737956 → **0.055769** | **92.4 %** |
| `D09_c14_3800mm` | 0.991822 → **0.995053** | 2.401513 → **0.204834** | **91.5 %** |
| `D10_rc16_3250mm_sparse` | 0.978795 → **0.993513** | 2.398310 → **0.455925** | **81.0 %** |
| `D12_c14_585_afbin2` | 0.986530 → **0.996313** | 3.881744 → **0.957497** | **75.3 %** |
| `D08_c11_2800mm` | 0.994660 → **0.996853** | 1.060217 → **0.532429** | **49.8 %** |
| `D14_cdk14_2563mm_e47` | 0.998862 → 0.998597 | 0.269502 → **0.222772** | **17.3 %** |

**[F15](followups.md#f15--optimize---per-run-overwrites-each-runs-stored-settings), stated so the bank's state is
on the record:** arm B ran last, so **every run folder's `optimized_settings.json` now holds the adopted landing**.
On the 13 binning-1 datasets that is bit-identical to arm A's, so only the seven actually moved. This is the
intended end state, and it is the reverse of what wave 7 deliberately left behind.

---

## §3 — F19: the pre-registered arm fired against the obvious fix, for the second wave running

Wave 7's arm E refuted F19's "no change" position: `D02_rich_135mm` gains **44.1 % of σ_focus at 8×** its derived
exposure while `SensitivityIsAtFloor` is **false at every rung**, so the exposure block is never surfaced. The
obvious remedy — and the one F19's own next-step text leads with — is to **widen the trigger**.

**That only helps if the block, once surfaced, says something true. Nobody had checked, because the harness could
not.** It recorded `ExposureRecommendation` as `null` whenever the gate was healthy — reproducing the product's
blind spot instead of measuring it.

### The instrument: gate the display, never the measurement

One line in `OptimizationDiagnosticRunner.BuildAggregateRow`. The recommendation is now computed for **every** run;
`SensitivityIsAtFloor` is still recorded beside it, so every consumer that wants the product's trigger semantics
still has them. **No re-render:** wave 7's exposure rungs are still on disk at `D:\hf_w7\armE\t{0.5,1,2,4,8}`.

### Arm X, and R5's verdict

| dataset | rung | landed Sens | σ_focus | **S_now** | **raw** | **recommended** | `ExposureIsNotTheLimit` |
|---|---|---|---|---|---|---|---|
| **`D02_rich_135mm`** | **0.5 s (derived)** | 16.7 | 0.10927 | **991.8** | **0.000 s** | **0.50 s = current** | **True** |
| `D02_rich_135mm` | 1 s | 48.5 | 0.09125 | 1226.3 | 0.000 s | 1.00 s = current | True |
| `D02_rich_135mm` | 2 s | 49.0 | 0.09482 | 1864.4 | 0.000 s | 2.00 s = current | True |
| `D02_rich_135mm` | 4 s | 9.0 | 0.08182 | 2308.8 | 0.000 s | 4.00 s = current | True |
| `D02_rich_135mm` | **8 s** | 9.0 | **0.06109** *(−44.1 %)* | 1894.9 | 0.000 s | 8.00 s = current | True |
| `D16_esprit550_ha3` | 0.5 s | 0.0 | 0.35169 | 6.5 | 1.184 s | **1.50 s** | False |
| `D16_esprit550_ha3` | 1 s | 0.0 | 0.19762 | 10.3 | 0.946 s | 1.00 s = current | True |
| **`D16_esprit550_ha3`** | **2 s (derived)** | 0.0 | **0.06423** | 19.9 | 0.507 s | **2.00 s = current** | True |
| `D16_esprit550_ha3` | 4 s | 0.0 | 0.12034 | 43.5 | 0.211 s | 4.00 s = current | True |
| `D16_esprit550_ha3` | 8 s | 0.0 | 0.14828 | 89.1 | 0.101 s | 8.00 s = current | True |

**Read the `D02` block as a whole: the recommendation says "your exposure is fine" at every rung across a 16×
range over which σ_focus improves by 44 %.** It is not wrong at one operating point that a widened trigger might
have caught — it is wrong everywhere on the axis it is being asked about.

> **R5 FIRES ON BRANCH 1. At `D02`'s derived 0.5 s rung the recommendation reports `ExposureIsNotTheLimit` with a
> raw ask of 0.000 s** — on a run measured to gain 44 % of its focus precision from 16× that exposure. **Part (a)
> does NOT ship as a trigger widening.** Widening the trigger would have surfaced that answer to more users; it
> would have looked like a fix and would have been the same wrong number in a wider audience.

**The control passes on every rung, which is what makes `D02`'s reading usable.** `D16_esprit550_ha3` at 0.5 s —
*below* its derived value — correctly asks for **1.5 s**; at its derived 2 s it asks for nothing more, which is
right because that is exactly where its σ_focus minimum sits (0.06423); and at 4 s and 8 s, where σ_focus is
measurably *worse* (0.12034, 0.14828), it again asks for nothing more. **The recommender is right wherever it can
see, and blind where it cannot** — so the defect is the statistic's blindness, not its arithmetic. A control that
merely failed to fire would have proved nothing; this one fires correctly in the one place it should.

### And the magnitude finally has a number on it

F19 has always argued that `S_now` — the `NTarget = 20`-th brightest accepted star's SNR — saturates on a rich
field. Measured, against `TargetSensitivity = 10`:

**`S_now` on `D02` is 991.8 to 1864.4 — 100× to 186× the target.** So
`RawSeconds = 0.5 × (10 / 991.8)² = 5 × 10⁻⁵ s`. **The statistic is not slightly wrong on this population; it is
wrong by four orders of magnitude**, because the 20th-brightest star of a 2746-star field carries no information
about the faint stars that hold the V-curve's wings. And it saturates *harder* as exposure rises (992 → 1226 →
1864), so it can never converge toward asking for more.

### What ships for F19, and what does not

| part | outcome |
|---|---|
| the **instrument** | **SHIPS.** The recommendation is measured on every run; the display stays gated. This is what made the question answerable |
| **(b)** — saturation must be reported | **SHIPS, harness side.** `SynthBankDerivations` now records `exposureRawSeconds` and `exposureClamp` (`none`/`floor`/`ceiling`/`not-derived`) as FIELDS, says so in the definition text, and `synth-bank --dry-run` prints the saturated set. A field, not only prose, for the reason F39(a) added `DetectionBinningSource`: a reader diffs fields, nobody diffs a sentence |
| **(b)** — product side | **VERIFIED, NOT IMPLEMENTED — it was already correct.** `StarSignalCopy.DescribeExposureDerivation` already reports every clamp branch and says WHICH bound bound it (`CapLimitsRecommendation`, `CappedByAbsoluteLimit`), and the `ExposureIsNotTheLimit` branch already states the S/N that made the math derive nothing. No work was needed; reading it was the work |
| **(a)** — the trigger | **DOES NOT SHIP.** R5 branch 1. Recorded with its measurement |
| **(c)** — `MinExposureSeconds` | out of scope by design (§0.2) — bank-side, re-renders `D14` which the adoption arm is measuring |

**What F19 needs next is not a trigger and not `NTarget`.** It is a statistic that can see the wing frames — the
σ_focus / wing-star-count signal — and that is a new mechanism rather than a repair to this one. Filed.

### The saturated set, which nobody had ever listed — and it is bigger than F19 says

`synth-bank --dry-run` on this branch now prints it. **13 of the 20 datasets report a clamp rather than a derived
value**, and the ratios are not marginal:

| dataset | clamp | the solve asked for | reported | |
|---|---|---|---|---|
| `D02_rich_135mm` | floor | **0.001 s** | 0.5 s | **500× more than asked** |
| `D01_ultrawide_40mm` | floor | 0.003 s | 0.5 s | 167× |
| `D03_redcat_250mm` | floor | 0.003 s | 0.5 s | 167× |
| `D04_esprit_550mm` | floor | 0.004 s | 0.5 s | 125× |
| `D18_m24_deep_shed` | floor | 0.004 s | 0.5 s | 125× |
| `D20_m24_bright_control` | floor | 0.004 s | 0.5 s | 125× |
| `D19_cygnus_deep_shed` | floor | 0.008 s | 0.5 s | 62× |
| `D05_tec140_1000mm` | floor | 0.014 s | 0.5 s | 36× |
| `D14_cdk14_2563mm_e47` | floor | 0.055 s | 0.5 s | 9× |
| `D07_rc10_2000mm` | floor | 0.066 s | 0.5 s | 8× |
| `D13_apo200_1800mm` | floor | 0.264 s | 0.5 s | 2× |
| **`D17_cdk14_oiii5`** | **ceiling** | **40.4 s** | 30 s | 1.3× short |
| **`D10_rc16_3250mm_sparse`** | **ceiling** | **335.6 s** | 30 s | **11× short** |

**Three things this changes.**

1. **F19's own count was low, in two directions.** The entry says "the 0.5 s floor for 8 of 17"; measured, it is
   **11 at the floor** — those eight plus `D18`/`D19`/`D20`, which the 17-dataset count never included — **and 2 at
   the ceiling, which nobody had counted at all.** A saturation that runs *out of exposure* is the same defect from
   the other side and had no name.
2. **`D02` makes F19's case far more sharply than F19 does.** The arithmetic asks for **0.001 s**, gets 0.5 s, and
   wave 7 measured that dataset improving 44 % of σ_focus at **8 s** — four orders of magnitude above the ask.
   The derivation is not off by a factor on this population; it is measuring something unrelated to what the fit
   needs, exactly as arm X's `S_now = 991.8` says.
3. **`D18` / `D19` / `D20` are floor-saturated**, which is worth knowing before
   [F32](followups.md#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains)'s
   confirmation arm runs on them (§0.1): its entire synthetic half sits on exposures that are clamps rather than
   derived values. That does not invalidate the arm — every φ arm sees the same frames — but it belongs in its
   write-up.

**This list IS wave 9's F19(c) work list**, produced as a by-product of (b) rather than as a separate
investigation.

### The inertness control

`synth-bank --dry-run` over all 20 datasets, this branch vs a `develop` @ `e8eb8b0` build of the same binary.
**The derived-parameter line — `step*`, `exposure`, the band, `detectionBinning`, `donut` — is IDENTICAL on every
one of the 20.** Every other differing line is one of the 13 new `SATURATED` lines or the same clause appended to
that dataset's `exposure definition` prose. **Nothing this wave shipped moves a rendered frame or an expected
optimum.**

> **The filter had to be tightened, and wave 7's would have hidden this.** Wave 7's strip greps
> `detectionBinning=`, and the *prose* `exposure definition` line contains the substring
> `captureBinning=1×detectionBinning=1` — so the definition text rides into a filter meant for the parameter line.
> Reusing the previous wave's filter verbatim, which is normally the right instinct for a like-for-like control,
> here produced 26 diff lines that looked like movement and were not. The control is only as good as the thing it
> selects on.

---

## §4 — F48: the denominator, not the data, decided wave 7's verdict

Re-scored offline from `D:\hf_w7\f18arms\{C,D,S}_S*\synth_validate_report.json` (`D:\hf_w8\f48_rescore.py`). No
new runs, no plugin code. A cell is **movable** when more than one round was rendered — below that, only round 0
exists and no arm can differ.

| denominator | n | median σ_focus ratio **S/C** | S >20% better | S >20% worse |
|---|---|---|---|---|
| all scorable cells (**wave 7's**) | 28 | **1.0000** | 6 | 2 |
| **movable cells only** | **12** | **0.8977** | 6 | 2 |

**Excluded as structural ties: 16 of 28** — `S0` = 6, `S2` = 6, `S3` = 4. Those scenarios converge in a single
round on most datasets, so the arms are byte-identical by construction.

**Over the cells that can move, arm S is 10.2 % better than the control, and arm D is 1.0000 on all twelve.**
Wave 7's rule 2 was *"arm S ships instead of D only if it beats D by >5 % median"*. **On the movable denominator it
beats D by 10.2 %, so the rule would have FIRED and arm S would have shipped.** The data did not change; the
denominator did.

**The wave-7 verdict still stands, and this does not re-open it.** The statistic was fixed before the arm ran and
was applied as written; a pre-registered statistic that turns out to be poorly matched to the data is a lesson for
the next rule, not a licence to pick a better one once the numbers are in. What this re-score buys is the
quantified version of that lesson — **the choice of denominator was worth more than the entire effect being
measured** — and a sharper reading of arm D:

**Arm D is more inert than wave 7 reported.** Wave 7 said "byte-identical on 26 of 28 cells, bound in exactly one".
Restricted to the twelve cells where an arm *could* act, arm D is **1.0000 on all twelve** — its single binding
cell (`D16` S2) is a one-round cell that could not have differed anyway, and its σ_focus there is NaN. **Arm D
never acted on a cell where acting was possible.** The flag staying default OFF is, if anything, better supported
than wave 7 stated.

**Arm S's structure is real and unchanged:** the wins concentrate in `S1` (recovering from a too-narrow sweep —
`D05` 0.2296 → 0.0055, `D06` 0.0861 → 0.0154, `D15` 0.1622 → 0.0471, `D09` 0.0945 → 0.0406) and both losses are in
`S6` (step *and* exposure wrong — `D15` 0.0457 → 0.0978, `D16` 0.0651 → 0.1433). A narrower executed sweep helps
when the fit is being rebuilt and hurts when the run is also photon-starved.

---

## §5 — Found and flagged, not fixed

### F49 — a FIELD report, and it is F19's mirror image

Mid-wave, the user ran the shipped wizard twice on their own rig with the `Default` profile and got a landing at
**Sensitivity 15.667 → 0.000**, `StarClippingMultiplier` 6.750 → 0.250, stars per frame **834 → 5766**. The Star
signal block fired, correctly said *"Star brightness is not the problem: your brightest stars measure S/N 1438.6"*
— **and offered no remedy at all**, on a page whose contract is "diagnosis plus exactly one instruction".

Traced to source, it is structural. `StarSignalCopy.RemedyFor` has exactly three ranked branches and **all three
are exposure or binning remedies**. With `ExposureIsNotTheLimit` true, `IncreasesExposure` is false and branch 2's
`!ExposureIsNotTheLimit` is false, so **every branch falls through and it returns `string.Empty`**.

**The block's TRIGGER is the Sensitivity gate; its CONTENT is exposure-only.** On a rich, well-exposed field where
the optimizer *chose* a floor gate, it fires and has structurally nothing to say. And there is no lever to name
even if it did: F32's `MinDetectionKeepFraction` has **no XAML binding anywhere** — it is `--keep-floor` on the
harness only. (It would not have bound here regardless: it rejects landings keeping *fewer* stars than the seed,
and this one keeps ~7× **more**. The pathology is admission, not shedding — the other end of an axis F32 has never
measured.)

**Placed beside F19, the pair states the defect one level up:** F19 is the block staying **silent** on a population
that would benefit from what it knows; F49 is the block **speaking** to a population it cannot help. Any fix to
F19's trigger has to not deepen F49.

The same report also carries the step recommender widening **again**, 214 → 459, from a sweep that is too NARROW
rather than too wide — the arithmetic reproduces exactly from the screenshot's own focuser axis, and the cause is
that the sweep never reaches `3 × HFR_min`, so `FindHalfWidth` extrapolates on every run. Filed with F49.

### F50 — a false-negative gate count is an upper bound, not an estimate

Falling out of F46's refuted `--min-box` hypothesis: relieving `MinimumStarBoundingBoxSize` made `TooSmall: 6`
disappear from `D12`'s attribution and **recovered not one star** — the same six candidates failed the next gate
(`TooDistorted` 19→22, `LowSensitivity` 34→37) and every recall figure was byte-identical.

This is a **reading** defect, not a code defect, but it is not confined to prose: `GateRecommender` recommends a
threshold *per gate* from exactly these counts (`GateRecommender.cs:285` maps `RejectionGate.TooSmall` →
`MinimumStarBoundingBoxSize`). A recommender driven by a bound it treats as an estimate will loosen a gate for no
gain, and each loosening is a real precision cost somewhere else. Wave 7's F39(b) table, F46's entry and the
noise-clip sweep write-ups all read these counts the other way.

---

## Verification

**Full suite green: 3667 passed, 0 failed** (`develop` @ `e8eb8b0` was 3661 → **+6**).

**Discriminating counts, each confirmed by neutralizing the change and re-running — not asserted.**

| change | tests | verified by |
|---|---|---|
| `ExposureClamp.Classify` | **2 discriminating / 3 contract** | Replacing it with the plausible-but-wrong implementation — compare the CLAMPED output to the bounds, drop the `IsFinite` guard — fails **exactly** `Classify_ASolveLandingExactlyOnABoundIsNOTSaturated` and `Classify_AnUnsolvableConfigurationIsNotDerivedRatherThanClamped`. The other three pass under it, so they pin the contract rather than this defect, and are labelled that way |
| `ResolveRunDetectionBinningFactor` null-`Resolved` path | **2 discriminating** | Dereferencing `resolvedForRun` eagerly (what an implementation assuming a bundle is always supplied would do) fails both `…AnswersFromTheDatasetWithNoResolvedSettingsAtAll` and the existing `…ResolvesTo1WithNeitherSource` |

**And what is NOT unit-tested, said plainly rather than papered over.** Three of this wave's changes live in
`GoldenEvalRunner`, `OptimizationDiagnosticRunner` and `SynthBankRunner`, which the test project does not
source-link — linking them would pull in the catalog reader, the compositor and the detector. Their verification
is measurement, and it is not weaker for it:

- **the unconditional exposure recommendation** — arm X *is* the test: the field is non-null on healthy-gate runs,
  which is the entire behaviour change, and the `D16` control returns the right answer on all five rungs;
- **the high-tier FN attribution** — its first output contradicted the entry it was built to explain, which a
  passing assertion could not have done;
- **the F39(b) default flip** — two smoke runs printing the two different modes, then 40 arm runs in which the 13
  binning-1 datasets are bit-identical and the seven reproduce wave 7's independent measurement to 6 dp.

**Two inertness controls, both passing.** (1) `synth-bank --dry-run` derived parameters identical to `develop` on
all 20 datasets. (2) Arm B's landings identical to wave 7's arm G2 to six decimal places on all seven — so nothing
this wave shipped perturbs an optimizer landing either.

**Per [F37](followups.md#f37--the-ci-test-host-crashes-natively-accessviolationexception-aborting-2000-tests-with-zero-failures),
a red CI check is checked against the native test-host crash — verify the test COUNT — and against
githubstatus.com, before being read as a regression.** Wave 7 hit a real Actions `major_outage` that failed
docs-only commits in *Set up job* before any test ran.

---

## Reproduce

```
# F46 arm P0 -- the blend check. NO detector run: reads what wave 7 already wrote (~10 s)
python3 D:\hf_w8\p0\blend_check.py

# F46 arms P1/P2 -- the knob sweeps, no code (~3 min)
D:\hf_w8\p1\knob_sweep.sh

# F46 -- R4's obligation: the SAME knob at binning 1, which is what makes the effect binning-specific (~20 min)
D:\hf_w8\p2\layers_control.sh

# F46 -- R6: the scaling rule over ALL SEVEN, not just the two that motivated it (~10 min)
D:\hf_w8\p2\seven_sweep.sh

# F19 arm X -- what the block WOULD say. No re-render: wave 7's rungs are still on disk (~8 min)
D:\hf_w8\armX\arm_x.sh

# F39(b) -- the adoption arms. Status quo FIRST, adopted LAST (F15) (~40 min)
D:\hf_w8\adopt\adopt_arms.sh   &&   python3 D:\hf_w8\adopt\score_adopt.py

# F48 -- the re-score. No runs, no plugin code (~1 s)
python3 D:\hf_w8\f48_rescore.py

# The inertness control -- derived parameters, this branch vs develop @ e8eb8b0, all 20 datasets (~30 s)
D:\hf_w8\dryrun_diff.sh 'D:\hf_w8\exe\SynthBank\synthetic-bank-spec.json'
```

---

## Lessons

**1. The population that did NOT motivate a hypothesis is what tests it.** F46's mechanism was found on `D12` and
`D15`, where a `--structure-layers` change looks like a clean 2-for-2 win. Measured on all seven it improves 3 and
costs one, and the blanket rule is refuted. Two cells are enough to find a mechanism and nowhere near enough to
derive a product rule from — R6 existed only because that distinction was written down before the sweep ran.

**2. Both of this wave's pre-registered rules fired against the obvious answer, and neither cost more than an
hour.** R1 refuted the size-knob hypothesis by *inertness* — the knob moves stars between gates and recovers none.
R5 refuted "widen the trigger" by measuring what the widened trigger would have said. Wave 7's lesson was that
writing the rule down first is what makes a result readable as a refutation; wave 8's is that **the cheap
refutation usually arrives before the expensive confirmation**, so run it first.

**3. A control is only as good as what it selects on.** Reusing wave 7's derived-parameter filter verbatim — the
right instinct for a like-for-like control — produced 26 lines of apparent movement, because the `exposure
definition` prose contains the substring `detectionBinning=` that the filter greps for. The physics had not moved
at all. Check what the filter *matches*, not only that it is the same filter.

**4. Gate the display, never the measurement.** The single line that made F19 answerable had been gating the
*computation* on the product's own display condition, so the harness reproduced the blind spot it existed to
study. Any diagnostic that asks "what would the product have said here?" cannot be gated on the product's decision
to say it.

**5. An instrument built for one question answered a different one first.** The high-tier FN attribution was built
to explain a recall@high movement; the first thing it did was **correct F46's own filed account** of where the
bright stars go (`Contaminated`, not the structure gap; `TooSmall` nowhere at all). The all-tier table that entry
was written from is dominated by the faint tier on every long-focal-length dataset.

**6. Reporting a clamp produced a work list nobody had.** F19(b) was scoped as "say when the arithmetic
saturated". Printing it revealed **13 of 20** datasets saturating rather than the 8 on record — including **two at
the ceiling**, a direction the entry never counted, one of them **11× short** of its own derivation.
