# Wave 27 — pre-registration: is the truth correction SOUND, and why is it INVISIBLE?

**Vessel decision.** PR #195 MERGED; wave 27 opens `ghilios/synthetic-af-bank-followups-wave27` fresh off
`develop @ e19a779`.

**Status:** pre-registration. Every rule, threshold, population, bar and validity gate below is fixed BEFORE any
of the wave's own statistics are read. Written by the pre-registration agent; the controller executes.

---

## 0. THE HEADLINE, AND IT IS A CORRECTION TO THE BACKLOG

**Backlog item 1 — *"Re-measure precision against `*.truth.json`, not `*.golden.json`, across the bank"* — is
DONE. It shipped on 2026-08-03, ten days before it was written into the backlog as the top item.** Wave 27 does
not do it. What wave 27 does instead is measure whether the shipped correction is **sound**, fix the fact that it
is **invisible**, and correct three documents that describe it as open.

The proof is short and every step is on disk:

1. **`Joko.NINA.Plugins/TestApp/SynthBank/TruthProtection.cs`** exists at `develop @ e19a779`, added by `aaf26e8`
   (2026-08-03 14:56 −0400), *"fix(bank-verify): stop charging false positives for real stars the golden dropped
   (F31)"*, refined by `867d41c` and `5c382a1`.
2. It is wired into **both** harnesses: `GoldenEvalRunner.cs:301-307` and `BankVerifyRunner.cs:465,483`.
   `golden eval` is the instrument that produced the owner's results table.
3. It was **active in the published run**. `/mnt/d/hf_w25/table18/D08_c11_2800mm.log` reads
   `OVERALL: TP=308 FP=11 FN=7 precision=0.966 recall=0.978`. The owner's table's own `D08` row (`prec 0.966`,
   `recall@all 0.978`) is arithmetically `308/(308+11) = 0.9655` and `308/315 = 0.9778`. Unprotected, the same
   detections give `308/(308+150) = 0.673`. **The published 0.966 requires FP = 11, which requires protection.**
4. **[F31](followups.md) is `Done`, closed 2026-08-03**, and its own entry already records the direct-truth
   comparison the backlog asks for: *"Next-step (1) — score directly against truth — was measured and found to
   agree with the repaired golden+protection scoring to within 0.006, so it was not worth a second scoring
   path."*

### 0.1 What the 91.3 % number actually measured

`/mnt/d/hf_w26/pd08/F31_truth_rescore.txt` re-derived false positives **from the golden's `stars` list alone**,
ignoring both `GoldenMatch.ExcludeUnresolved` and `TruthProtection`, and got **150**. `golden eval`'s actual `FP`
field on the *same detections* is **11**. So wave 26 measured the **pre-repair** metric and compared it against
the **post-repair** published number.

**The full decomposition of the 150** — and it resolves into *three* quantities, not two:

| | count | what it is |
|---|---|---|
| golden-`stars`-false | **150** | wave 26's number: the pre-repair `/3` metric |
| − excluded by the golden's own `unresolved` boxes | **50** | `GoldenMatch.ExcludeUnresolved`, shipped long before F31 |
| − excluded by `TruthProtection` (`omitted` / `merged-into`) | **89** | F31's repair |
| **= `golden eval`'s published `FP`** | **11** | the genuine wing-frame junk |
| *(separately)* within 12 px of **ANY** truth star | **137** | a **third** quantity, neither of the other two |

**Correction, in place: the junk count is 11, not 13.** Two of the "13" sit inside a golden `unresolved` box, and
`golden eval`'s `FP = 11` is the correct figure. Wherever this series has asserted 13 — including
`docs/synthetic-af-bank-results-table.md`'s *"13 genuine junk detections"* — it is wrong by two.

**And the correction STRENGTHENS [F83](followups.md)'s caveat 3 rather than weakening it:** none of the 11 has a
truth star within 12 px, so they are genuine junk and `D08`'s `+0.034` precision cost is **real**.

This is **[F68](followups.md) part 1 in its purest form — three different fields, all called "false positive"** —
and it is **[F79](followups.md)'s shape again**: the same wrong reading reachable by more than one route, where
only a scorer-versus-product comparison separates them. It is registered as such in §9.

### 0.2 The owner's deliverable carries a false banner

`docs/synthetic-af-bank-results-table.md` §"READ THE PRECISION COLUMN AS A LOWER BOUND" says D08's *"true
precision is nearer 0.997 than the 0.966"* and that *"every 1.000 in the column is a floor rather than a
ceiling."* **All of that is wrong.** The column is already truth-corrected; `0.966` is D08's honest precision;
and its 11 surviving false positives are the genuine wing-frame junk the same document correctly identifies two
paragraphs later. Correcting it is §8, and it is owed unconditionally.

---

## 1. THE QUESTION WAVE 27 ACTUALLY ASKS

> **The correction is applied. Is it SOUND — or has it replaced a biased metric with a SATURATED one?**

`TruthProtection` grants immunity from false-positive scoring to any detection landing within the match radius
(12.0 px) of an `omitted` or `merged-into` truth star. Some of that immunity is **correspondence** and some of it
is **chance**, and the denser the field the more of it is chance.

This is not a hypothetical failure mode. **`TruthProtection.cs`'s own class doc records having already hit it
once**: the first cut sized protection by the star's light footprint (`2·HFR`, ~40 px on a wing donut) and

> *"saturated the metric: precision read 1.000 on all 17 datasets for all three F23 arms, because a 40 px box
> around every faint truth star launders genuine noise blobs too. A metric that cannot separate configurations
> is as useless as one that is biased, just in the other direction."*

The match-radius box was chosen to fix that. **Nobody has ever checked whether it succeeded on the dense
datasets** — and the dense datasets are extraordinarily dense (§4.2). Across `table18`, **FP = 0 on 19 of 20
datasets**, the sole exception being `D08` at 11. A metric reading a perfect score almost everywhere is exactly
the symptom the class doc warns about.

### 1.1 Consequences, both directions, stated before the data

| if protection is… | then |
|---|---|
| **SOUND** | the precision column is honest as published; `D08`'s `0.966` stands; the banner comes off with no replacement caveat; **[F83](followups.md)'s decision proceeds on real numbers** |
| **SATURATED** | the precision column is an **UPPER** bound on the dense fields — *the opposite error from the one the banner claims* — every `1.000` there is uninformative rather than excellent; and **F83's "`J` has no precision term" becomes largely moot on this bank, because there would be no precision signal to put in `J`** |

Both are results the owner needs. Neither is the answer the wave is hoping for.

---

## 2. WHERE THE CONTROLLER'S FRAMING IS WRONG

The controller's first two messages framed the wave as a truth re-score with a peak-vs-integrated-SNR
discriminator. The controller has since self-corrected most of this. What follows is the full accounting,
including the parts still standing that this design rejects.

### 2.1 The rival-causes framing (a)/(b)/(c) was the wrong shape, and the right shape was already in source

The brief asked for a rule distinguishing **(a) OMISSION** (the reference is an incomplete list) from
**(b) MIS-SPECIFICATION** (the reference's *statistic* is wrong for defocused sources) from **(c) COINCIDENCE**.

**(a) and (b) are not rival causes. They are the same fact at two levels of description**, and the golden's own
source says so. The golden is not a hand-made list; it is derived from truth by a declared policy, and the
policy's `omitted` bucket at `GoldenFromTruth.cs:744-748` carries the literal reason *"a detection here should
count as a false positive"*. The reference is incomplete **because** the policy's threshold dropped those stars.
Asking which of the two caused the 91.3 % is asking whether water is wet or H₂O.

**The genuinely diagnostic question is a fourth one the brief did not name, and it is decidable from source at
zero compute:**

> **(d) MIS-ASSIGNED SIDE.** `GoldenFromTruth.cs` contains two doc comments that contradict each other.
> `SyntheticTier.Unresolved` is *"neither a required find nor a false positive"*; `SyntheticTier.Omitted` is *"a
> detector reporting something at its location should be scored as a false positive"*. Both describe stars the
> policy judged too faint to demand. **There is no principled reason a star at peak SNR 3.4 is a false positive
> while one at 3.6 is neither.** `unresolvedSnr` is a *visibility* threshold, and visibility bears on **recall**,
> not on falsity. A rendered star is not a false positive at any SNR. The defect was a **recall-side floor wired
> into the precision side**.

That is exactly what `TruthProtection` fixed, and the fix is one concept, not a new statistic. **So no new
detectability boundary is needed for precision, and introducing one would re-import the very arbitrary threshold
that caused the defect.**

### 2.2 The integrated-SNR discriminator is rejected, and the project already measured why

The proposed discriminator — detected-but-omitted stars separating from undetected-omitted stars on an
integrated-SNR axis while overlapping on peak SNR — is **rejected on three independent grounds**:

1. **It is algebraically degenerate within a frame.** For a source of total flux `F` and binned peak fraction
   `p`, the golden's own statistic is `SNR_peak = F·p/σ` and the uniform-aperture matched filter is
   `SNR_int = F·√p/σ = SNR_peak/√p`. Within one frame the defocus — and therefore `p` — is near-constant, so
   `SNR_int` is a **monotone transform** of `SNR_peak` and every rank statistic (AUC included) is *identical* by
   construction. Any separation the discriminator found would come from pooling across frames, i.e. it would be
   measuring *which frame*, not *which star*.
2. **The project has already measured it and it does not work.** `docs/golden-tier-plausibility-design.md` §2 is
   titled *"What was ruled out: integrated SNR"* and concludes: *"A 3 px spike at 12σ peak has integrated
   significance ≈ 12·√3 ≈ 20. A 36 px donut spread at 0.25σ/px has ≈ 8. Integrated SNR is the statistically
   correct ranking and it still puts the spike on top… **No purely significance-based statistic can fix this.**"*
   That design **supersedes** F16's proposal by name. Re-running it would be re-treading a fenced result.
3. **At extreme defocus it over-claims catastrophically.** On `D08` frame 00, `TruthIndex 0` has
   `FluxElectrons = 28192`, `KernelPeakFraction = 0.000716`, `PeakSnr = 11.58`. Inverting the policy's own
   arithmetic gives `σ_bg = 1.742 e⁻`, hence `SNR_int = 11.58/√0.000716 = 433`. A 3.5σ cut on `SNR_int` would
   declare essentially **all 126** stars on that frame "detectable by physics" — on a frame where the detector
   found **10 and 4 of them were junk**. It fails exactly as badly in the opposite direction from the statistic
   it was meant to correct.

### 2.3 The donut mechanism is REFUTED on the one dataset that has been read, and the controller's own table refutes it

The controller's second message already conceded this; the design records the refutation because it constrains
wave 28. `/mnt/d/hf_w26/pd08/F31_truth_rescore.txt` shows the golden-false-but-real detections **peak at best
focus** (38 of 38 at `f14000`, where `KernelPeakFraction` is at its *maximum* and the donut argument is at its
weakest), while the **junk is 11 of 11 on the two extreme wing frames (3 at `f13672`, 8 at `f14328`) and zero on
the seven interior frames** — the corrected count of §0.1, and the split is `golden eval`'s own per-frame `FP`
column.

A defocus-donut mechanism predicts the real-but-omitted share should *rise* toward the wings. It goes to
**zero**. The donut argument cuts both ways — at extreme defocus the golden under-lists *and* the detector
under-finds, and what is left over is junk. **The dominant mechanism is the peak-vs-integrating statistic at all
defocus, not defocus itself**, and §2.1 shows that too is downstream of the mis-assigned side.

### 2.4 The chance-coincidence prior does not generalise from D08, and this is the wave's whole point

Wave 26 priced coincidence at **0.30 expected on D08** and called it "nearly dead". `D08` carries **126** truth
stars on a 6248×4176 frame — **4.8 stars per megapixel**, the second-sparsest dataset in the bank.
`D18_m24_deep_shed` carries **27 219** on the same frame geometry: **1 043 per megapixel, a factor of 217**.
Generalising a coincidence rate from `D08` to the bank is the single most dangerous inference available here, and
the brief's framing invited it. §4.2 makes the density spread a first-class part of the satisfiability analysis.

### 2.5 The recall half is NOT wave 27's, and the reason is in source, not in scheduling

The brief asked whether wave 27 should also re-denominate recall. **No — and not merely for budget.**
`TruthProtection.cs` states its scope explicitly: *"Recall is deliberately untouched … a protected star is not
promoted to a required find."* Recall's denominator is the golden's `stars` list at tier ≥ high, which is
untouched by anything in this wave. **Backlog item 8 (wide-field recall, `recall@high` 0.183 on the 40 mm rig) is
therefore fully open and completely unaffected by wave 27's verdict, in either direction.** Pulling it in would
add a second, independent question to a wave whose first question has a reachable FAIL end — and would require
choosing new tier boundaries *with the data in hand*, which is precisely what the [F14](followups.md) fence
forbids. **Priced in §10; assigned to wave 28.**

### 2.6 Item (D) — re-scoring 7 datasets at `--detection-binning 2` — is IN wave 27, on a measured price

The controller first ranked (D) into wave 28, then withdrew that ranking after measuring the rate. **I agree with
the withdrawal, and I would have argued for it anyway on the second ground below.**

* **The price collapsed by an order of magnitude.** `golden eval` is **6 s** (`D06`) to **12 s** (`D08`) per
  9-frame cell on B15 — against the charter's nearest rate, `optimize --per-run --max-evals 250` at ~5.2 m/run,
  **~30× more**. Seven cells is **~2 minutes**, not "a small arm". §10 adds the measured rate as a labelled row,
  because the charter's §5 budget table has **no `golden eval` row at all** and the next wave would otherwise
  price a golden-eval arm off the `optimize` rate — [F21](followups.md)'s recorded error, in the other direction.
* **`git diff --name-only 29665a62..HEAD -- '*.cs'` is 0 lines** (`/mnt/d/hf_w27/prov_b15_to_head_cs.txt`):
  B15's tree and HEAD are **C#-identical**. So a factor-2 re-score on B15 is **fully comparable with `table18` at
  zero build cost**, which removes the "different instrument" objection I would otherwise have raised — the only
  thing that changes is the one flag under examination.

**What survives of my original objection, and it becomes a design constraint rather than a deferral:** (D) *is*
a different instrument from (B)'s population, so **(D) is scored separately, as `RULE D27`, and never folded into
`RULE T27`.** (B) reads `table18` and only `table18`. §5.7.

---

## 3. WHAT WAVE 27 SHIPS AND MEASURES

| half | what | binary? | gate reach | price |
|---|---|---|---|---|
| **(A) VISIBILITY** | `golden eval` emits `scoringMode`, `protectedStars`, `scoredFraction` — mirroring `bank-verify`, which already reports them | **yes, a build** | **NO** — see §6.4 | ~75 m |
| **(B) SOUNDNESS** | `RULE T27` — is the correction sound or saturated, across 20 datasets, 180 frames | **no** — pure Python on artifacts on disk | n/a | ~95 m |
| **(C) THE RECORD** | three documents corrected in place | no | n/a | ~20 m |
| **(D) THE BINNING ROW** | `RULE D27` — re-score the 7 `derived=2, scored=1` datasets at their own factor on B15 | **no** — B15 is on disk and C#-identical to HEAD | n/a | ~25 m (2 m of it compute) |

**If the wave overruns, (A) is cut first.** (B) carries the finding, (C) is owed unconditionally and nearly free,
(D) is now the cheapest compute in the wave at ~2 minutes, and (A) is a reporting improvement that will still be
true next wave — and is the only item needing a build. Stated here so the decision is pre-registered rather than
made under time pressure.

---

## 4. RULE T27 — the soundness rule

### 4.1 Statistics, all three fixed here

Per dataset `d`, pooled over its 9 frames.

Let, per frame `f`:

* `D(f)` = the detections — the rows of `detected_f<F>.csv`, read as the **`cx,cy` centroid columns**, not the
  `x,y` bounding-box origin ([F68](followups.md) part 5; `GoldenMatch` centre-mode tests the detection's centre
  against the golden rect, so `cx,cy` is the live copy).
* `TP(f)` = detections paired to a golden `stars` rect by `GoldenMatch.Match` in centre mode.
* `U(f)` = detections removed by `GoldenMatch.ExcludeUnresolved` against the golden's `unresolved` rects, using
  the product's **`Covers`** predicate (centre-in-box **OR** IoU(rect, detection bbox) > 0 — the dilated one;
  F31 records this asymmetry as *"small (≤0.011), systematic, one-directional"* and the product deliberately
  keeps it for `unresolved`).
* `R(f)` = the **at-risk pool** = `D(f)` minus `TP(f)` minus `U(f)`. These are the detections that would be false
  positives if protection did not exist.
* `P(f)` = the members of `R(f)` protected by `TruthProtection` — within **12.0 px** (Euclidean, centroid
  predicate) of a truth entry whose `Tier ∈ {omitted, merged-into}`, positioned at
  **`BinnedCenterX`/`BinnedCenterY`** (never `Star.CxPixels`/`CyPixels` — §5.1).
* `FP(f)` = `R(f)` minus `P(f)`.

| # | statistic | definition | why |
|---|---|---|---|
| **S1** | `A_all(d)` | mean over the 9 frames of the fraction of frame area covered by the **union** of (golden `stars` rects ∪ golden `unresolved` rects ∪ 12 px discs about the protection catalogue) | the probability that a **uniformly random** detection cannot be scored a false positive. Analytic, **needs no null**. This is the saturation statistic |
| **S2** | `precisionNull(d)` | precision recomputed with the **detections** wraparound-translated by `TruthProtection.ShiftForNullControl` and the reference left in place | **the product's own shipped null**, already reported on all 51 `bank-verify` config rows under a `precisionNullMax` gate. F31's measured sound range is **0.000–0.012** |
| **S3** | `chanceProtected(d)` | `Σ_f \|P_null(f)\| ÷ Σ_f \|R_null(f)\|` under the same translation — the fraction of the shifted at-risk pool the **unshifted** catalogue still protects | how much of the correction survives when correspondence is destroyed — i.e. how much of it is coincidence |

**Reported alongside, not thresholded:** `A_golden(d)` and `A_protOnly(d)` (S1's decomposition, so the reader
can see whether coverage is the reference doing its job or the correction over-reaching); `scoredFraction(d) =
Σ(|TP| + |FP|) ÷ Σ|D|` (the size of the invisible correction — half (A)'s motivation, measured); `precision_prot`
and `precision_unprot` (the published bracket); and the chance-corrected point estimate
`precision_corr = Σ|TP| ÷ (Σ|TP| + Σ|FP| + chanceProtected·Σ|P|)`.

**S3 is the statistic the brief's `excess` form would have got wrong, and catching it is why this section exists.**
The natural-looking statistic *"what fraction of granted protections survive the null"*,
`(p_obs − p_null)/(1 − p_null)`, is **degenerate on this population**: FP = 0 on 19 of 20 datasets, so
`p_obs = |P|/|R| = 1` on 19 of 20, and the expression collapses to `(1 − p_null)/(1 − p_null) ≡ 1` **whatever the
data say**. It is an unsatisfiable clause and it would have passed silently. S3 avoids it by asking the question
of the pool rather than of the granted set.

### 4.2 Bars, and why each is reachable at BOTH ends

All three bars are **0.25**, deliberately: one number, one meaning — *"a decorrelated or random object scores a
quarter as well as the real one."*

| bar | fires when | inherited from |
|---|---|---|
| **SATURATED-BY-AREA(d)** | `A_all(d) ≥ 0.25` | — (analytic) |
| **SATURATED-BY-NULL(d)** | `precisionNull(d) ≥ 0.25` | **F31's published sound range 0.000–0.012** — the bar is >20× the demonstrated sound regime and is **not fitted to anything in this wave** |
| **CHANCE-DOMINATED(d)** | `chanceProtected(d) ≥ 0.25` | — |

**Reachability, from reference characterisation only (truth counts and frame geometry; none of S1/S2/S3 has been
read):**

| dataset | truth stars, frame 00 | frame (binned px) | stars / Mpx | 12 px discs alone would cover ≤ |
|---|---|---|---|---|
| `D18_m24_deep_shed` | **27 219** | 6248×4176 | **1 043** | **0.47** |
| `D01_ultrawide_40mm` | **19 267** | 6248×4176 | 738 | **0.33** |
| `D04_esprit_550mm` | 7 525 | 6248×4176 | 288 | 0.13 |
| `D19_cygnus_deep_shed` | 4 961 | 9576×6388 | 81 | 0.04 |
| `D02_rich_135mm` | 3 604 | 6248×4176 | 138 | 0.06 |
| … | … | … | … | … |
| `D08_c11_2800mm` | 126 | 6248×4176 | 4.8 | 0.002 |
| `D10_rc16_3250mm_sparse` | **26** | 3008×3008 | **2.9** | **0.001** |

* **The FAIL end (SATURATED) is reachable.** `D18`'s protection discs alone reach 0.47 of the frame before a
  single golden `stars` rect is added, and `D01` carries **~12 265 golden `stars` boxes per frame**
  (110 384 across 9 frames, from its own `table18` log's `TP + FN`) whose half-widths grow with defocus.
* **The PASS end (SOUND) is reachable.** `D10` has 26 truth stars over 9.05 Mpx; its coverage cannot exceed
  ~0.001 by arithmetic, so it **cannot** fire any bar. Fifteen of the twenty datasets sit below 100 stars/Mpx.
* **Neither branch is foreclosed by the population.** The bank spans a factor of **360** in areal density
  (`D10` 2.9 → `D18` 1 043 stars/Mpx), which is why a per-dataset verdict is meaningful and a bank mean would be
  worthless. **The bank mean is therefore forbidden by this design (§4.5).**

**Declared:** the bars are informed by the reference's density spread, which is a property of the renderer's
catalogue and the frame geometry — *reference characterisation*. **None of S1, S2 or S3 has been computed on any
dataset at pre-registration time.** The bars are fixed here and may not move.

### 4.3 The null construction — USE THE ONE THAT ALREADY SHIPS, and bound its single-draw variance

**An earlier draft of this design specified a displacement null from scratch. That draft was wrong and is
withdrawn**: `TruthProtection.ShiftForNullControl` (`TruthProtection.cs:264-289`) already implements exactly this
control, already ships, and is already reported on all 51 `bank-verify` config rows. *The cheapest instrument is
the one already printed.* Inventing a second one would have produced a statistic that could not be compared with
the product's.

**The shipped construction, adopted verbatim.** The **DETECTIONS** are wraparound-translated by
`(NullShiftX, NullShiftY) = (317, 211)` px — `Wrap(v, extent) = ((v % extent) + extent) % extent` — and re-scored
against the **unshifted** reference. Note the convention carefully: the product shifts the *detections*, not the
catalogue. **This design adopts the product's convention exactly**, because §6.3's `S27-1` compares the two
routes' numbers and a convention mismatch would make that comparison meaningless. (On a torus the two are
equivalent for a translation-invariant statistic, but the golden rects have finite extent and the frame is not
literally periodic, so "approximately equivalent" is not good enough for a byte-level route comparison.)

**Is it adequate at `D01`'s ~12 265 golden stars per frame? — the controller asked, and the answer is
"decorrelation yes, variance no."**

| property | verdict | reasoning |
|---|---|---|
| **decorrelates** | **YES, everywhere** | 317 px is **26×** the 12.0 px match radius and far larger than any PSF the bank renders; it is 16.5 % of the narrow axis on the smallest frame (1920×1080) and 3.3 % on the largest (9576×6388) — large enough to destroy correspondence, small enough that no object leaves the frame |
| **not grid-aligned** | **YES** | 317 and 211 are both prime and neither divides any frame dimension in the bank |
| **preserves count, density, clustering** | **YES** | wraparound creates and destroys nothing |
| **single draw** | **NO — this is the inadequacy** | one offset is a **sample of size 1**. At `D10`'s 2.9 stars/Mpx `precisionNull` is a near-zero binomial and one draw is plenty; at `D01`'s 738 and `D18`'s 1 043 it is a **high-variance single draw**, and nothing in the shipped control can distinguish *"this offset landed unluckily"* from *"the field is saturated"*. `bank-verify`'s `precisionNullMax` maxes over **config rows**, not over offsets, so it does not close this |

**Resolution, fixed here.** The **product** keeps its single shipped offset — wave 27 does **not** change shipped
null behaviour, because that would change every `bank-verify` number in the series. The **independent Python
scorer** computes the same statistic at **`K = 4` offsets**, the product's plus three more:

```
Δ1 = (317, 211)   <- the product's, verbatim      Δ3 = (317, -211)
Δ2 = (-317, -211)                                 Δ4 = (-317, 211)
```

All four are the same magnitude (so the decorrelation argument transfers unchanged), all four are sign variants
(so no new constant is invented and nothing is tuned), and `Δ1` is shared with the product so **`S27-1`'s route
comparison is exact at `Δ1`**.

* **The rule reads the value at `Δ1`** — the product's own offset — so the verdict is a statement about the
  control that actually ships.
* **The max − min spread over the four** is reported and NOT thresholded, as the single-draw variance bound the
  product cannot give. **If the spread on any dataset exceeds that dataset's own `Δ1` value, the shipped null is
  recorded as UNDER-POWERED on that dataset** and the dataset becomes a named caveat rather than a verdict input.
  That is a finding about the product's control, and it is the honest form of the controller's question.

**`A_all` (S1) is computed by rasterising every rect and disc onto a fixed lattice of stride 4 px, offset
(2, 2)** — deterministic, no sampling seed, no null needed. The bank's smallest golden box is `w = 2·⌈4⌉ = 8 px`
(`MinBoxHalfWidthPixels = 4.0`), so a minimal box receives 4 lattice probes in expectation; the estimator is
unbiased in expectation with variance negligible against a 0.25 bar. The stride is fixed here and may not be
tuned after the data. `A_all` is the **mechanistic explanation** for whatever `precisionNull` reports, and the
two must agree in direction — a high `precisionNull` with a low `A_all` would mean the null is measuring
something other than coverage, and is called out.

**`A_all` (S1) is computed by rasterising every rect and disc onto a fixed lattice of stride 4 px, offset
(2, 2)** — deterministic, no sampling seed. The bank's smallest golden box is `w = 2·⌈4⌉ = 8 px`
(`MinBoxHalfWidthPixels = 4.0`), so a minimal box receives 4 lattice probes in expectation; the estimator is
unbiased in expectation with variance that is negligible against a 0.25 bar. The stride is fixed here and may not
be tuned after the data.

### 4.4 Populations — [F68](followups.md)'s five parts, per clause

| clause | population (ARTIFACT) | population (FIELD, and WHICH COPY) | statistic | aggregation | empty-set answer |
|---|---|---|---|---|---|
| **T27-V0** reproduction | the 20 `/mnt/d/hf_w25/table18/<DS>.log` files, paths **from the manifest only** | the `OVERALL:` line's `TP=`, `FP=`, `FN=` — the **product's** fields, not the frames CSV's (both exist; the CSV is per-frame and would need re-summing, which is a second route and therefore a second bug) | exact integer equality | per dataset; verdict is a **count of datasets**, `20 of 20` required | a missing/unparsable `OVERALL:` line ⇒ that dataset is **COULD-NOT-LOOK**, named, and T27 is `T-UNEVALUATED` |
| **T27-V1** coordinates | the 180 `*.truth.json` + the 180 `detected_f*.csv` | `BinnedCenterX/Y` **vs** `Star.CxPixels/CyPixels` — both copies read, deliberately, and compared | match rate at 12.0 px | per dataset | a frame with 0 detections contributes 0/0 and is excluded from that dataset's rate, counted in `frames_no_detections` |
| **T27-V3** pairing | the 180 manifest rows | the sidecar's own `focuserPosition` **field**, never the filename | equality with the manifest's `<F>` | count | any row unpaired ⇒ `T-UNEVALUATED` |
| **S1** `A_all` | 180 frames | golden `stars[]` + `unresolved[]` rects (`x,y,w,h`); truth `BinnedCenterX/Y` where `Tier ∈ {omitted, merged-into}` | covered lattice fraction | **mean over the 9 frames** (frames within a dataset have identical area, so the mean is exact) | a frame with an empty catalogue ⇒ `A = 0` for that frame, and it still counts in the mean |
| **S2** `precisionNull` | 180 frames | as S1 plus `D(f)` | `ΣTP_null ÷ (ΣTP_null + ΣFP_null)` | **pooled counts, then the ratio** | `ΣTP_null + ΣFP_null = 0` ⇒ `precisionNull = 0.0` by definition (no detection could be scored), reported with a `†` |
| **S3** `chanceProtected` | 180 frames | as S1 plus `R(f)` | `Σ\|P_null\| ÷ Σ\|R\|` | **pooled counts, then the ratio** | `Σ\|R\| = 0` ⇒ **`n/a`**; the dataset leaves S3's denominator and the denominator prints as `k of 19` |

**Aggregation, stated once and binding everywhere:** every dataset-level ratio is **pooled counts then ratio**,
never a mean of per-frame ratios. The bank's frames differ in detection count by more than an order of magnitude
within a single dataset (`D08`: 10 detections at the wing, 119 at focus), and a mean-of-ratios would be dominated
by the sparse wing frames. **The bank-level statement is a COUNT OF DATASETS crossing a bar, never a mean of
dataset ratios** — the density spread of §4.2 makes a bank mean meaningless.

### 4.5 The verdict population: 19 datasets, D08 excluded, and the count is COMPUTED

**`D08_c11_2800mm` is NOT BLIND.** Its per-frame truth-match table is published in
`/mnt/d/hf_w26/pd08/F31_truth_rescore.txt` and quoted verbatim in the owner's results table; its `table18`
`OVERALL:` line is quoted in §0 of this design. Under the [F14](followups.md) / [RULE D20](waves22+-handoff-prompt.md)
fence, **no verdict may be harvested from a population already read.**

* **Verdict population: the 19 datasets other than `D08`.** The literal `19` is **COMPUTED** by the scorer as
  `len(manifest_datasets) − len(NOT_BLIND)` where `NOT_BLIND = {"D08_c11_2800mm"}` is a named constant, and is
  **asserted equal to 19** at run time. It is never typed into an output string.
* **`D08` is carried as a labelled, reported-not-thresholded consistency check.** Its S1/S2/S3 are printed in the
  same table under a `[not blind]` marker and are excluded from every count.
* `D08` **does** participate in `T27-V0` (reproduction) and `T27-V1` (coordinates) — those are validity gates on
  the *scorer*, not on the wave's hypothesis, and a scorer that cannot reproduce a published log is broken
  regardless of blindness.

### 4.6 The verdict tree

Counts below are over the **19-dataset verdict population**. `SAT(d)` ⟺ `SATURATED-BY-AREA(d)` **or**
`SATURATED-BY-NULL(d)`. `CHANCE(d)` ⟺ `CHANCE-DOMINATED(d)`.

| # | condition | verdict | what the owner gets |
|---|---|---|---|
| 1 | any of `T27-V0`, `T27-V1`, `T27-V3`, `T27-V4` fails | **`T-UNEVALUATED`** | nothing is claimed. **Do NOT repair the rule and re-score** — a scorer that cannot reproduce the published numbers cannot be trusted to measure a null on them, and the F14 fence applies to the repair-and-harvest pattern |
| 2 | gates pass; `SAT` count **= 0** and `CHANCE` count **= 0** | **`T-SOUND`** | the precision column is honest as published. The banner comes off with no replacement caveat. F83 proceeds on real numbers |
| 3 | gates pass; `1 ≤ SAT + CHANCE ≤ 4` (union of the named sets) | **`T-SOUND-WITH-NAMED-EXCEPTIONS`** | the column is honest except on the named datasets, which are annotated **in place** in the results table as uninformative, each with its `A_all` / `precisionNull` / `chanceProtected` printed |
| 4 | gates pass; `SAT` count **≥ 5** | **`T-SATURATED`** | the precision column is an **UPPER** bound on the dense fields. Every `1.000` there is uninformative. F83's precision-term question becomes largely moot on this bank. The results table's precision column is re-annotated, and `TruthProtection` is recommended (costed, **not shipped**) a density guard |
| 5 | gates pass; `CHANCE` count **≥ 5** and `SAT` count **< 5** | **`T-CHANCE-DOMINATED`** | the protections are largely coincidental. The correct precision lies between `precision_unprot` and `precision_prot` and neither is publishable alone; the wave publishes the **bracket** and `precision_corr` per dataset |
| 6 | every dataset in the verdict population is COULD-NOT-LOOK | **`T-COULD-NOT-LOOK`** | named per dataset with its reason, emitted **before any field read** (§7) |

**Predicted answer, recorded now so the rule can be wrong** ([RULE R24](waves22+-handoff-prompt.md)'s lesson —
a rule with a predicted answer is a rule that can be wrong):

> **Prediction P-W27.** `T-SOUND-WITH-NAMED-EXCEPTIONS`, with the exceptions drawn from
> `{D18_m24_deep_shed, D01_ultrawide_40mm}` and at most one other. Mechanism: coverage scales with areal density,
> the bank spans 360× in density, and only the top two datasets have the density to reach a 0.25 bar on the disc
> term alone. **If `D04_esprit_550mm` or `D02_rich_135mm` also fires, the mechanism is not density alone and the
> results owe a second explanation.** If `D10`/`D06` fire, the scorer is wrong and `T27-V0` should have caught
> it.

---

## 5. VALIDITY GATES

### 5.1 `T27-V1` — COORDINATE SPACE AND BINNING. **Get this wrong and every number in the wave is wrong.**

**The decision, fixed here.** The 12.0 px match radius is expressed in **captured (binned) pixels** — the space
in which the FITS, the golden boxes, `detected_f*.csv` and `BinnedCenterX/Y` all live. Detections are matched
against **`BinnedCenterX`/`BinnedCenterY`**, never against `Star.CxPixels`/`CyPixels`.

Justification, from source and from the published logs:

* `GoldenFromTruth.Build` transforms every star to captured pixels (`ToBinnedCoordinate`, `(c+0.5)/bin − 0.5`)
  and emits `GoldenStarBox` in that space; `TruthProtection.BuildProtectionBoxes` reads `d.BinnedCenterX/Y`
  directly.
* `truthModel.captureBinning` is **2** on exactly the two `*_afbin2` datasets and **1** on the other eighteen —
  **the count 2 is COMPUTED from the 20 `synthetic_meta.json` files at run time, never typed.**
* `table18` scored **every** dataset at `DetectionBinning = 1` (the logs' own
  `NOTE: … derived detection binning is 2 … but it is being scored at 1`), so **no software-binning rescale is in
  play anywhere in this wave.**

**The gate, with its FAIL end run and printed, not hypothesised.** For every dataset the scorer matches `D` to
the full truth list at 12.0 px **twice** — once on `BinnedCenterX/Y`, once on `Star.CxPixels/CyPixels` — and:

| clause | requirement |
|---|---|
| **V1-a** | on the **18** `captureBinning = 1` datasets the two match rates are **bit-identical** (`repr()` equality). The transform is the identity at `bin = 1`; any difference means the reader is wrong |
| **V1-b** | on the **2** `captureBinning = 2` datasets (`D11_rc10_585_afbin2`, `D12_c14_585_afbin2`) the binned rate exceeds the native rate by **≥ 0.30 absolute** |

**V1-b's native arm IS the demonstrated FAIL end** — it is executed on real artifacts and its (low) rate is
printed beside the (high) binned rate in the same table. A gate whose FAIL end is a paragraph is not a gate.

### 5.2 `T27-V2` — the detection-binning discrepancy: DECIDED, and deliberately NOT repaired

The `table18` logs record `derived = 2, scored = 1` on **7 of 20** datasets (`D08, D09, D10, D12, D14, D15,
D17`) and `derived = 1, scored = 1` on the other 13. **The count 7 and the dataset list are COMPUTED from the
logs' own `NOTE:` lines at run time, never typed.**

**Decision, fixed before the data: `RULE T27` is scored on `table18` AS PUBLISHED, at factor 1, and the
discrepancy is NOT repaired inside T27.**

1. All 20 were scored the same way, so the precision column is **internally comparable** — which is all
   `RULE T27`'s question needs.
2. The detections on disk are the ones the owner's table was built from. Re-scoring at factor 2 produces a
   **different table**, not a check on this one.
3. Changing the instrument inside a soundness wave would confound T27's verdict with the change.

The factor-2 re-score is a **separate rule on a separate population** — `RULE D27`, §5.7 — and its result may not
be substituted into T27's inputs. §4.6's verdict is additionally **reported split 13 / 7** so the two rules can
be read against each other.

On those 7 rows the owner's table's **precision/recall** and its **binning** columns are measured under
contradictory assumptions: precision/recall at software factor **1**, the binning recommendation from wave 25's
S1 arm at factor **2** (a different instrument, already labelled as such in the table's own header). `RULE D27`
exists to say whether that matters.

### 5.3 `T27-V0` — REPRODUCTION, and its FAIL end is the (A) measurement

The scorer, reading paths **only** from the manifest, must reproduce `table18`'s published per-dataset `TP`,
`FP`, `FN` **exactly**, on **20 of 20**.

* **PASS end:** exact integer equality, 20 of 20.
* **FAIL end, demonstrated on real artifacts:** the same scorer invoked `--protection off` must **differ** from
  the published numbers on ≥ 1 dataset. This is not a contrived mutant — **it is simultaneously the FAIL-end
  demonstration and the measurement of the correction's size**, which is half (A)'s justification. One
  computation, two duties.
* If `T27-V0` fails, the verdict is `T-UNEVALUATED` and the wave stops. Fixing an outright bug in the *reader* is
  permitted once and must be recorded; if it still fails, **that is the finding**.

`T27-V0` is what checks this design's reading of `GoldenMatch.Match` / `ExcludeUnresolved` / `ExcludeProtected`
semantics. Nothing else in the wave needs to assume they were read correctly.

### 5.4 `T27-V3` — SIDECAR PAIRING

Every manifest row's `detected_f<F>.csv` pairs with a `*.truth.json` and a `*.golden.json` whose
**`focuserPosition` field** (from the sidecar, never parsed from the filename) equals `<F>`. **Assert 180 of
180.** FAIL end: a self-test fixture mutates one row's focuser and the check must refuse.

### 5.5 `T27-V4` — FINGERPRINT, class 4 (this wave's denominators)

Taken **before** any analysis, per *a control written after the arm is not a control*:

* `/mnt/d/hf_w27/fp_table18_BEFORE.txt` — 400 files (20 × [9 `detected_f*.csv` + 9 `false_negatives_f*.csv` +
  `golden_eval.txt` + `golden_eval_frames.csv`]) plus the 20 `.log` files.
* `/mnt/d/hf_w27/fp_sidecars_BEFORE.txt` — 180 `*.truth.json` + 180 `*.golden.json` + 20 `synthetic_meta.json`.

Both taken with `-print0 | sort -z | xargs -0 sha256sum` — **`xargs` splits on the space in `/mnt/d/Autofocus
Bank`, and a fingerprint written the naive way once recorded 20 landings instead of 42.** Population sizes are
asserted **inside** each file. Matching `*_AFTER.txt` files are written at the wave's close and **kept**. Any
difference ⇒ the wave's denominators moved under it ⇒ `T-UNEVALUATED`.

### 5.6 `T27-V5` — THE PATH TRAP. `table18`, not `table`.

**`/mnt/d/hf_w25/table/` also exists, also holds 20 datasets × 9 frames, and is WRONG.** It sourced params from
`D:\SyntheticAutofocusBank\<DS>\attempt01\` instead of `D:\hf_w18\seedA0\<DS>\attempt01\optimized_settings.json`
and gives different numbers (`D01` `recall@all` **0.209** against the published **0.123**). A scorer pointed at
it would produce a **plausible, wrong wave**. This is [F74](followups.md)'s path trap wearing a different hat.

**The guard:** the manifest driver asserts, for every one of the 20 logs, that the `params:` line contains the
literal `D:\hf_w18\seedA0\` **and** that the root basename is `table18`; and the scorer asserts the same on the
manifest header before reading a single field. The near-miss root is named in the manifest header as
`NEAR_MISS_ROOT=/mnt/d/hf_w25/table` so a future reader cannot rediscover it by accident.

### 5.6a `RULE R27` — REPRODUCIBILITY of the population. **ADOPTED, and it is a DECLARED DEVIATION.**

**`RULE R27 = R-IDENTICAL`.** B15 regenerates the entire published `table18` population byte for byte:
**20 cells, 420 files, 0 differences, 0 could-not-look** (`/mnt/d/hf_w27/repro_all_score.txt`). Driver
`repro_all_w27.sh` self-tests PASS with its comparator demonstrated in **both** directions; scorer
`score_repro_w27.py` self-tests **6 of 6** with `--out` mandatory and refusing; paths flow through
`repro_all_manifest.tsv` (20 of 20 rows) and are rebuilt nowhere.

**This ran BEFORE pre-registration, and the design says so rather than presenting it as registered.** The
deviation is declared, not laundered:

* **Why adopting it is legitimate.** `R27` is a **determinism control on the instrument**, not a test of the
  wave's hypothesis. It reads no statistic in T27's verdict tree and could not have been steered by knowing the
  answer — a byte-comparison of a regenerated population has exactly one interesting outcome and the wave's
  hypothesis is indifferent to it. This is the `Q26-V0`/`Q26-V5` shape.
* **Why it still matters that it was early.** *A self-test that runs AFTER the thing it guards is not a guard* —
  here the reverse risk applies, so the honest statement is that `R27` was **opportunistic rather than
  pre-registered**, and the results document must say so in the same sentence it quotes `R-IDENTICAL`.
* **Its FAIL end IS demonstrated** — the controller reports the comparator shown failing in both directions in
  `repro_all_w27.sh`'s own self-test, which is what makes `0 differences` a measurement rather than a
  tautology. *A control that cannot fail is not a control.*

**What `R27` buys the wave, and it is load-bearing for two other rules:**

1. Half (B) may treat the 180 `detected_f*.csv` as a **stable population** rather than a historical accident.
2. **`S27-4` becomes attributable.** Without `R27`, a byte difference between B16 and `table18` could be the
   treatment *or* run-to-run noise. With `R-IDENTICAL` in hand, **any difference is the treatment.** This is the
   exact inverse of wave 19's `R-DETERMINISTIC` complaint (*"the two build trees were C#-identical, so it
   measures the noise floor and not whether a code change can move a landing"*) — there the identity was the
   defect; here the identity is the baseline and the difference is the treatment.

### 5.7 `RULE D27` — the factor-2 re-score (item D)

**Question.** On the 7 datasets whose *derived* detection binning is 2, does scoring at the optimizer's own
factor move the owner's published precision/recall?

**Population.** The 7 datasets named by the `table18` logs' own `NOTE:` lines — `D08, D09, D10, D12, D14, D15,
D17` — **the list and the count 7 COMPUTED from those lines, never typed**. 7 × 9 = **63 frames**, asserted.
Re-scored on **B15** (`/mnt/d/hf_w25/exe`, `TestApp.dll` sha256 `2fb0c8fd…`), **no build**, into
`/mnt/d/hf_w27/table18_b2/`, deriving the invocation from `/mnt/d/hf_w25/table_arm18_w25.sh:62` — **and running
the derivation pre-flight on the result**, because that is exactly the `sed`-derivation path
[F80](followups.md) exists for.

**Verdict population: the 6 datasets other than `D08`** (`D08` is in the 7 and is not blind). The **6 is
COMPUTED** as `len(D27_POP) − len(NOT_BLIND ∩ D27_POP)`.

**Statistic.** Per dataset: `Δprecision` and `Δrecall@all` and `Δrecall@high`, factor 2 minus the published
factor 1, from each run's own `OVERALL:` line.

**Bar.** `MOVED(d)` ⟺ `|Δprecision| ≥ 0.01` **or** `|Δrecall@all| ≥ 0.01`. 0.01 is the last digit the owner's
table prints, so a move below it cannot change a published number; a move at or above it can.

| condition (over the 6-dataset verdict population) | verdict |
|---|---|
| `D27-V1` or `D27-V2` fails | **`D-UNEVALUATED`** |
| gates pass; `MOVED` count = 0 | **`D-NO-CHANGE`** — the 7 rows stand as published; the derived/scored discrepancy is cosmetic and the log's `NOTE:` can be read as informational |
| gates pass; `1 ≤ MOVED ≤ 6` | **`D-MOVED`** — the named rows are re-stated in the results table at the optimizer's own factor, with both values printed and the factor labelled on each |

**`D27-V1` — the `PixelScale` gate, with a demonstrated FAIL end.** `GoldenEvalRunner.cs:258-261` sets
`p.PixelScale = effectivePixelScale × detectionBinningFactor`; the recorded gotcha is that omitting the factor
produces a **plausible wrong answer**. The gate: each factor-2 log must print the `; detectionBinning 2` suffix
on its `pixelScale:` line **and** must NOT print the `derived … but it is being scored at 1` NOTE.
**FAIL end, run and printed:** one dataset (`D09_c14_3800mm`) is additionally re-run **without**
`--detection-binning`, and the NOTE must reappear and the `pixelScale:` line must differ. ~10 s.

**`D27-V2` — the coordinate-collapse guard.** The golden boxes are in captured pixels and are unchanged by
software binning. If detections came back in half-scale coordinates, essentially none would match. Require
`recall@all ≥ 0.25` on all 7 at factor 2. **Reachable both ways:** the published factor-1 values on these 7 are
0.978 / 0.949 / 0.965 / 0.705 / 0.641 / 0.961 / 0.875, all far above 0.25, so PASS is expected; and a coordinate
break would drive them to ~0, so FAIL is reachable by the exact defect the gate exists for.

**Blindness.** `D08`'s factor-1 numbers are published; its factor-2 numbers are **not**, so `D08` may be
*measured* here but is excluded from the verdict count and printed `[not blind]`.

---

## 6. HALF (A) — the shippable harness defect

### 6.1 The defect — and it is FOUR disclosures, not one

> **`bank-verify` carries four truth-related honesty disclosures. `golden eval` — the instrument that produced
> the owner's published results table — carries NONE of them.**

| disclosure | `bank-verify` | what it is | `golden eval` |
|---|---|---|---|
| `scoringMode` + `protectedStars` | `:287-306`, `:344` | whether truth protection was applied at all, and how much was available | **absent** |
| `precisionNull` | `:478-489`, `:513` | the chance null. Its own comment: *"A metric reading 1.000 with a null near 0 is measuring; one reading 1.000 with a high null is **saturated**, which is how the first cut of the F31 repair failed."* | **absent** |
| `truthViolations` | `:470-474`, `:517-521` | a scored FP sitting on a real rendered star — *"has an exact answer and the answer must be 0"* | **absent** |
| `scoredFraction` | `:510-512` | how much of the detection set the precision ratio actually judged — *"at Sensitivity 0 that reaches ~57 % on D17"* | **absent** |

`GoldenEvalRunner.WriteReports` is **not even passed the truth dispositions**. So every number in
`docs/synthetic-af-bank-results-table.md` was produced by a truth-protected instrument that cannot say it was
truth-protected, cannot say what chance alone would have scored, cannot say whether any FP sits on a real star,
and cannot say what fraction of the detections it judged. **That is the whole of wave 26's misreading,
mechanised.** A grep of `/mnt/d/hf_w25/table18/*.log`, `golden_eval.txt` and `golden_eval_frames.csv` for
`protected` / `scoringMode` / `precisionNull` returns **zero hits**.

**A null WAS generalised — but only in the sibling.** `precisionNull`'s wraparound ships in `bank-verify` and is
reported on all 51 config rows under a `precisionNullMax` gate. The separate *coordinate-alignment* null (F31's
100 % / 2.3 % / 0 %) is **`D09` only and has never been re-run**. `golden eval` implements **neither**. **So the
owner's 20 rows have never been null-tested.** That is the gap, stated precisely, and it is why half (B) exists.

### 6.2 The change — a PORT, not a design

All four are already written, already tested and already shipped in the sibling. Port them into
`GoldenEvalRunner` — console line, `golden_eval.txt`, `golden_eval_frames.csv` — reusing `bank-verify`'s wording
verbatim so the two harnesses cannot drift apart. Honour the house rule stated one line from the change site,
`GoldenEvalRunner.cs:669`: *"Report the mode actually used — a hardcoded label here silently misattributes every
stored report."*

**Plus one doc-comment-only fix, and the reason it is doc-comment-ONLY is a preservation constraint.**
`GoldenFromTruth.cs:34-36` (`Omitted` — *"a detector reporting something at its location should be scored as a
false positive"*) flatly contradicts `:76-84` (`UnresolvedSnr` — *"scoring a detection there as a false positive
would be punishing a detector for something the reference itself cannot certify either way"*). That contradiction
is §2.1's mis-assigned side, in source.

Worse, `:744-748` **writes the wrong version into every golden sidecar's `Reason` field**, so the pre-repair
policy is sitting on disk in all **180** `*.golden.json` in the bank — and it is the single most likely thing
that actually misled wave 26.

**Decision, fixed here: fix the two XML doc comments; do NOT change the `Reason` string generation, and do NOT
regenerate the sidecars.** Three reasons, in this order:

1. **Regenerating 180 goldens collides with fingerprint class 2 preservation** and would invalidate every
   precision/recall number this series has published. That is a catastrophic price for a diagnostic string.
2. Changing only the *generator* string would make future sidecars disagree with the 180 on disk — a silent
   divergence, which is the same class of defect being fixed.
3. Nothing reads `Reason` programmatically; it is a debugging aid.

The on-disk hazard is instead **registered as a named caveat** (§9) so the next reader of a `*.golden.json`
`Reason` field is warned in the register rather than misled by the file.

### 6.3 `RULE S27` — and the ship is NOT what the rule decides

Per the charter's *"ship unconditionally where the ship is not what the rule decides"*: the reporting gap is
established from **source** (one harness reports it, the other does not) and confirmed by a **grep of the
published logs returning zero**. Its correctness does not depend on T27's verdict. **(A) ships unconditionally,
under a correctness rule, not a value rule.**

| clause | requirement | FAIL end |
|---|---|---|
| **S27-1** | the four new fields appear in **all three** sinks on all 20 datasets, and their values equal `score_t27_w27.py`'s **independently computed** `protectedStars` / `scoredFraction` / `precisionNull` (at `Δ1`) / `truthViolations`, on **20 of 20** | a disagreement is a **finding about one of the two routes**, named, and (A) does not ship until the wave says which is right. This is [F79](followups.md)'s lesson made structural: two routes to the same statistic, compared, **and never assumed to compute the same statistic** |
| **S27-2** | every new NUnit test shown **RED against a named mutant** — `M-S1` deletes the `protectedStars` accumulation; `M-S2` hard-codes `scoringMode = "golden"`; `M-S3` returns the unshifted detections from the null path | a mutant that does not turn its test red means the test does not reach the code |
| **S27-3** | `TestAppOutputAsciiTests` green — the new console output must be ASCII | — |
| **S27-4** | **do-no-harm, provable by BYTES.** B16 re-runs all 20 `table18` cells. Required: every `detected_f*.csv` and `false_negatives_f*.csv` **byte-identical** to `table18`'s (**360 of 360**), and `TP`/`FP`/`FN` unchanged on **20 of 20**. Only the new fields may differ | if any byte of a detection artifact moves, or any of the 60 counts moves, the change was not report-only and **does not ship**. *A reporting change that moves a score is a bug*, and this makes that statement falsifiable rather than argued |
| **S27-5** | `truthViolations = 0` on **20 of 20** | `bank-verify`'s own comment: this *"has an exact answer and the answer must be 0"*. A non-zero value means a detection was charged as an FP while sitting on a real rendered star — the exact defect F31 repaired — and is a **product finding**, not a scoring nuisance |

**Two controls, and the wave needs BOTH because they answer different questions.** `T27-V6` (§5.6a) establishes
*"B15 reproduces `table18`"* — determinism of the instrument. `S27-4` establishes *"B16 equals B15 modulo the new
fields"* — do-no-harm of the treatment. **Without the first, a `S27-4` difference is unattributable**: it could
be the change or it could be run-to-run noise. Wave 19's `R-DETERMINISTIC` was a **null arm** precisely because
its two trees were C#-identical and it therefore measured the noise floor instead of a treatment. **Here the
inverse holds and it is a gift: `git diff --name-only 29665a62..HEAD -- '*.cs'` is 0 lines
(`/mnt/d/hf_w27/prov_b15_to_head_cs.txt`), so B15's tree and HEAD are C#-identical and B16 = B15 + exactly (A)
and nothing else. The difference IS the treatment** — the cleanest two-binary provenance this series has had.

### 6.4 Does the gate REACH this code? **No — and here is the check that does.**

A report-only change in `GoldenEvalRunner` is in no `optimize` search path; `OptimizationDiagnosticRunner` never
calls `GoldenEvalRunner`. Naming the 42-minute gate beside it would be **claiming a control the wave does not
have**. The checks that DO reach it are `S27-2`'s two mutant-killed tests and `S27-4`'s before/after log on a
named dataset.

**But wave 26's honest-skip conditions do not all hold here** — wave 27 *builds*, which trips reversal condition
1 (*"any C# changes"*). So the gate is **owed** unless the reversal is discharged on a principled, checkable
basis. This design pre-registers that basis:

> **`Q27-V1` — the reach-scoped discharge.** The gate is owed when the C# diff touches code the gate's own path
> can reach. Compute `git diff --name-only <B15 tree>..HEAD -- '*.cs'` (**reading B15's tree hash out of
> `/mnt/d/hf_w25/binary_provenance_w25.txt`, never assuming the previous wave's HEAD** — wave 24 got this base
> wrong and it mattered) and intersect it with the **reachable set**: everything under
> `Joko.NINA.Plugins.HocusFocus/`, plus `TestApp/{OptimizationDiagnosticRunner,HarnessSettingsStore,
> OptimizationRunDiscovery,ParamsDump,StubBehaviorSelectors,DiagnosticUtil,Program}.cs`.
> **Empty intersection ⇒ the gate is discharged**, replaced by hash identity + BuildId novelty (`Q27-V0`, wave
> 26's `Q26-V0` shape) **and** `S27-4`'s byte-identical `OVERALL:` line, which measures the wave's *actual* risk
> on the *actual* instrument and is a strictly sharper control for this change than an `optimize` gate is.
> **Non-empty intersection ⇒ the full 42 m gate runs.**
>
> **`Q27-V1`'s FAIL end is demonstrated**, not asserted: the same check is run against `<B14 tree>..HEAD`, a diff
> known to include plugin files, and must return a **non-empty** intersection and refuse.

**If `Q27-V1` discharges and the controller nevertheless has budget, run the gate anyway** — but the wave must
not report a gate it did not run, and must not call the discharge a pass. Both prices are in §10.

### 6.5 Binary discipline for (A)

Build into `/mnt/d/hf_w27/exe`. Record **dll** sha256s (`TestApp.dll`, the plugin dll) — **never the apphost;
`TestApp.exe` is byte-identical across six distinct published binaries** ([F66](followups.md)) — plus the
`BuildId`, asserted **novel against the sixteen** recorded ids. Run
`find Joko.NINA.Plugins -name '*.cs' -newermt '<build mtime>'` and record that it is **empty**. Never rebuild
mid-wave. B15 stays at `/mnt/d/hf_w25/exe` (`TestApp.dll` sha256
`2fb0c8fdb26f11f9c912dce6d3bc3b599e8b9abab7ee4fdd0a61ace0aac42e67`) and is **not** rebuilt.

---

## 7. COULD-NOT-LOOK STATES, with the guard BEFORE any field read

Every one of these is emitted by the scorer **before** it reads the field in question, and each is a named state
rather than a zero:

| state | guard, and it runs first | consequence |
|---|---|---|
| `CNL-MANIFEST` | the manifest is absent, or its header lacks `ROOT=/mnt/d/hf_w25/table18`, or its row count ≠ 180 | the scorer **refuses**, rc = 3, no output file written |
| `CNL-LOG` | a dataset's `.log` has no parsable `OVERALL:` line | that dataset is named and `T27-V0` fails ⇒ `T-UNEVALUATED` |
| `CNL-SIDECAR` | a `*.truth.json` or `*.golden.json` is missing, unparsable, or its `focuserPosition` disagrees with the manifest | `T27-V3` fails ⇒ `T-UNEVALUATED` |
| `CNL-NODET` | a frame's `detected_f*.csv` has a header but zero data rows | the frame is counted in `frames_no_detections`, contributes 0 to every numerator **and** denominator, and is named |
| `CNL-EMPTYPOOL` | `Σ\|R(d)\| = 0` for a dataset | `chanceProtected = n/a`; the dataset leaves S3's denominator, which prints as `k of 19` with `k` **computed** |
| `CNL-NONFINITE` | any `BinnedCenterX/Y`, box coordinate or `PeakSnr` is NaN/Inf — **and Newtonsoft writes these as the STRINGS `"NaN"`/`"Infinity"`** | the entry is excluded, counted, and named. [F79](followups.md)'s mirror |
| `CNL-DIMS` | a frame's FITS header yields no `NAXIS1`/`NAXIS2` | `A_all` is `n/a` for that frame; if any frame of a dataset is affected, the dataset's S1 is `n/a` and it leaves S1's denominator |

---

## 8. HALF (C) — the register corrections, owed unconditionally

Three documents state as open something that shipped on 2026-08-03. Each is corrected **in place, where the owner
can see it**, with the correction visible rather than a silent rewrite.

| # | file | what changes |
|---|---|---|
| **C1** | `docs/synthetic-af-bank-results-table.md` | the `## READ THE PRECISION COLUMN AS A LOWER BOUND` section (lines 7–24) is **replaced**. New banner: the column is already truth-corrected (`TruthProtection`, shipped 2026-08-03); `D08`'s `0.966` is honest, with `TP=308 FP=11 FN=7` quoted from its own log; the 91.3 % figure measured the **pre-repair** metric and is withdrawn as a caveat on this table; and whatever `RULE T27` returns is stated with its named exceptions. The *"13 genuine junk detections"* paragraph and the *"recall is unaffected"* paragraph are **kept** — both are correct |
| **C2** | `docs/followups.md`, [F84](followups.md) item (2) | corrected in place: the truth re-score is **not owed**, it shipped; the 91.3 % measurement is re-labelled as a measurement of the pre-repair metric; F31 is cited as `Done` with its own next-step-(1) quote |
| **C3** | `docs/waves22+-handoff-prompt.md` `:171,172,188` | backlog item **1** is struck with the reason (the pattern the file already uses for the struck old item 1), and replaced by wave 27's own items. The line *"F23 and F83 are BOTH undecidable"* is corrected: a truth-based pass exists and F31 already re-measured F23 (*"F23 void as written"*) |
| **C4** | `docs/waves27+-autonomous-prompt.md` `:154-160` | **the LIVE charter propagates the false claim verbatim** and is corrected in place, or a successor run will re-open item 1 for a third time |

**The authority for (C) is `docs/wave27-register-correction-survey.md`** — a line-anchored inventory of
**5 FALSE, 3 STALE, 3 MISLEADING, 2 historical-annotate-in-place, 1 source contradiction, 2 gaps** and
**8 correct-and-load-bearing** statements. Read it before making a single edit; it names every line. The three
most load-bearing are the results-table banner at `:7-21` (four distinct false statements),
`waves22+-handoff-prompt.md:171,172,188`, and `waves27+-autonomous-prompt.md:154-160`.

**No published NUMBER is wrong.** All 20 source reports verify. **Only the interpretive banner is false** — so
(C) is a correction of readings, not of measurements, and no table cell moves except the two the junk-count
correction touches (§0.1).

**The controller makes the edits; this design and the survey specify which lines change so the edit is mechanical
rather than interpretive.**

---

## 9. WHAT GOES IN THE REGISTER

A new entry is owed regardless of T27's verdict, because §0 is a finding on its own:

> **F85 — A closed, shipped correction was re-measured as an open defect, because two fields share a name.**
> `golden eval`'s `FP` and a Python re-derivation from the golden's `stars` list alone are both "false
> positives": **11** and **150** on the same detections. Wave 26 read the second and compared it against the
> first, concluding the owner's precision column was a lower bound when it was already corrected. The proximate
> cause is that **`GoldenEvalRunner` applies `TruthProtection` and never reports that it did**, while
> `BankVerifyRunner` reports it in three places. [F68](followups.md) part 1 and [F79](followups.md)'s
> more-than-one-route shape, in one incident. **Remedy (A).**

> **F86 — the golden sidecars on disk carry the PRE-REPAIR policy in their own words.**
> `GoldenFromTruth.cs:744-748` writes *"a detection here should count as a false positive"* into the `Reason`
> field of every `omitted` component, and that string is sitting in all **180** `*.golden.json` in the bank. The
> behaviour it describes was repaired on 2026-08-03 and the string was not, because it is a diagnostic field
> nothing reads programmatically. **It is the single most likely thing that actually misled wave 26.** Wave 27
> fixes the two contradicting XML doc comments (`:34-36` vs `:76-84`) and **deliberately does NOT regenerate the
> sidecars** — that would collide with fingerprint class 2 preservation and invalidate every published
> precision/recall number in the series (§6.2). **Anyone reading a `Reason` field must read this entry first.**

`RULE T27`'s verdict is appended to F31 (whose repair it audits) and to [F83](followups.md) (whose decision it
gates). If the verdict is `T-SATURATED` or `T-CHANCE-DOMINATED`, **F31 is REOPENED** with the density guard as
its costed next step. `RULE D27`'s verdict is appended to [F62](followups.md) (the no-`BestJ`-across-factors
landmine) and to the results table's binning column.

---

## 10. BUDGET, and what is not run

Hard stop **2026-08-14T00:45:17Z**. Estimates are recorded so the controller can record actuals against them
(*price the payoff in both directions and record estimate AND actual*).

**A rate the charter's §5 budget table does not have, added here as MEASURED so the next wave does not price a
golden-eval arm off the `optimize` rate** — [F21](followups.md)'s recorded error, which under-reserved wave 23 by
3.5× in the other direction:

| instrument | rate | how measured |
|---|---|---|
| **`golden eval`, the FULL 20-dataset arm, `table18` settings, B15** | **349 s (~6 m)** | **MEASURED**, controller, 2026-08-13 — the whole arm |
| **`golden eval`, one 9-frame cell** | **6 s (`D06`) – 46 s (`D01`)**, mean ~17 s | **MEASURED**. The spread tracks star count, not frame count: `D01` carries ~12 265 golden stars/frame |
| for comparison, `optimize --per-run --max-evals 250` | ~5.2 m/run | charter §5 — **~18× the whole arm**. Never price `golden eval` from it |

**Price a cell from the RANGE, not the mean** — `D01` is 7.7× `D06`, so a 7-cell arm's price depends entirely on
which 7. [F21](followups.md)'s lesson, in the cheap direction this time.

| step | estimate | notes |
|---|---|---|
| pre-flight `verify_derivation_w27.py` (blocking) + `layout_w27_selftest.sh` | **10 m** | must run **before** anything else |
| manifest driver + `T27_MANIFEST_READY` | **5 m** | zero TestApp minutes |
| `score_t27_w27.py --self-test` (both directions) | **10 m** | |
| **`T27-V0` reproduction + `--protection off` FAIL end** | **15 m** | pure Python, 180 frames |
| **`T27-V1` coordinate gate (both arms)** | **10 m** | |
| **`RULE R27`** — B15 regenerates `table18` | **DONE, 349 s** | `R-IDENTICAL`, 20 cells / 420 files / 0 diffs. **Ran before pre-registration — declared as a deviation, §5.6a** |
| **(B) S1/S2/S3 across 180 frames × 4 null offsets** | **40 m** | numpy rasterisation; the dominant cost is `D01`/`D18`'s ~12 k objects per frame |
| **(B) total** | **~95 m** | **zero TestApp minutes** for the Python route |
| **(D)** `RULE D27` — 7 cells at `--detection-binning 2` on B15 + `D27-V1` FAIL-end run | **~25 m** (of which **~2 m is compute**) | measured rate above; the cost is the gate and the write-up, not the runs |
| (A) code + tests + three mutants | **50 m** | four disclosures, ported |
| (A) build + provenance + `Q27-V1` + **B16 re-run of all 20 cells** (`S27-4`) | **35 m** | the 20-cell re-run is **~4 m** at the measured rate |
| **(A) total** | **~85 m** | |
| (C) document corrections, per `docs/wave27-register-correction-survey.md` | **30 m** | 5 FALSE + 3 STALE + 3 MISLEADING + 2 annotate-in-place |
| full suite by COUNT (baseline **3973**, verified on this branch at HEAD, `/mnt/d/hf_w27/SUITE_BEFORE_3973.txt`) | **5 m** | (A) adds tests; **name the delta before anything is called green** |
| results + register + commit + push + CI by COUNT | **45 m** | |
| **TOTAL** | **~5 h** | against ~11.5 h remaining to the **00:45:17Z** stop |
| *optional* full 42 m gate, if `Q27-V1` does not discharge | **+42 m** | affordable; run it if `Q27-V1` does not discharge |

**Do NOT cut scope for time — the budget is not the binding constraint.** ~5 h of work against ~11.5 h remaining.
**Cut only what the evidence does not support.** If the wave nonetheless overruns, the order is fixed here so it
is not decided under pressure: (D) first (self-contained, census already taken, wave 28 inherits it whole), then
(A)'s *build* half with (A)'s code+tests committed as a costed recommendation. **(B) and (C) are never cut** —
(B) carries the finding, (C) is owed unconditionally.

**(D) is IN.** One line: it costs ~2 minutes of compute on a binary already on disk, it is the only item that
touches a *published* column of the owner's table, and deferring a two-minute item is how wave 19 lost `RULE C19`
its verdict — *an item under two minutes is never dropped.*

**What is NOT run, and its price:**

| not run | price | why |
|---|---|---|
| **item 8** — recall on WIDE fields, re-denominated | **unpriced; needs a design** | untouched by wave 27 by construction (§2.5). Choosing new tier boundaries with the data in hand is behind the F14 fence, so it needs its own pre-registration |
| regenerating the 180 `*.golden.json` to fix the `Reason` string | ~15 m of compute, **unbounded cost in credibility** | collides with fingerprint class 2 preservation and would invalidate every published precision/recall number in the series. §6.2 |
| changing the shipped null to multiple offsets | ~20 m | would change every `bank-verify` number in the series. The variance bound is computed in the **scorer** instead, §4.3 |
| the coordinate-alignment null (F31's 100 % / 2.3 % / 0 %) beyond `D09` | ~15 m | `T27-V1` answers the same question more directly, on all 20, with a run FAIL end |
| the integrated-SNR discriminator | ~30 m | **rejected on the merits**, §2.2 — degenerate within-frame, already measured and superseded by `docs/golden-tier-plausibility-design.md` §2 |
| a truth-based **recall** denominator | ~30 m marginal | `TruthProtection` leaves recall untouched *by design*; wave 27 has nothing to say about it and saying it anyway would be scope drift |

---

## 11. INSTRUMENT DISCIPLINE

Every instrument lives in `/mnt/d/hf_w27/` with **LF endings**.

* **`verify_derivation_w27.py` is a BLOCKING pre-flight** and runs before anything else. Widened from
  `/mnt/d/hf_w26/verify_derivation_w26.py`. Its FAIL fixture is **pinned in source to the named root
  `/mnt/d/hf_w26`** — *a regression fixture that is "whatever came before" has a shelf life of one wave*, and
  wave 26 had to pin its own for exactly this reason. Its token pattern matches identifier tokens with **both an
  optional `_prefix` and an optional `_suffix`**, because **`\b` cannot see `_`** and every marker, function and
  path in this series is underscore-joined ([F80](followups.md), three demonstrated gaps).
* **`score_t27_w27.py` REFUSES without `--out`** (rc = 2, nothing written). A stdout redirect puts the evidence
  in a job-scoped temp directory that is deleted without ceremony; wave 25 lost its scoring artifact twice.
* **Self-tests are never dispatched from a SOURCED file.** `layout_w27.sh` contains *no* `--self-test`
  dispatcher; `layout_w27_selftest.sh` sources it and runs the checks. Wave 26's fired inside its caller's
  `source` line, terminated it, and printed a clean PASS. **Read the OUTPUT, not the exit code** — every
  self-test in this wave prints an explicit `SELFTEST … <n> of <n>` line and the plan checks that line.
* **The driver writes the manifest; the scorer reads paths ONLY from it** and never rebuilds one from a template
  ([F74](followups.md)). *A fallback rooted at the same wrong parent is not a second chance.*
* **The interlock's writer is the code that computes the verdict.** `T27_MANIFEST_READY` is written by the
  manifest driver, last, only after the 180/20/9 assertion passes. `T27_PASSED` is written by
  `score_t27_w27.py` itself on `rc == 0` and only then — so the FAIL end is *unable* to leave one behind
  ([F75](followups.md): an interlock whose writer is a human is a note).
* **Assert the POPULATION SIZE everywhere**, and use `-print0 | sort -z | xargs -0` — `xargs` splits on the space
  in `/mnt/d/Autofocus Bank`.
* **Every ordinal and count in a derived instrument's output is COMPUTED, never typed** — `19`, `18`, `2`, `7`,
  `20`, `180` all derive from the manifest or the metas at run time and are asserted.
* Keep logs **ASCII**. Newtonsoft writes NaN/Infinity as the **strings** `"NaN"`/`"Infinity"`.

---

## 12. SATISFIABILITY SUMMARY — both branches, every clause

| clause | PASS reachable? | FAIL reachable? | how it was checked |
|---|---|---|---|
| `T27-V0` reproduction | yes — the semantics are fully read from `GoldenEvalRunner.cs`/`GoldenMatch`/`TruthProtection` | **yes, and it is RUN** — `--protection off` on the same artifacts | executed, not argued |
| `T27-V1-a` (bin 1 identity) | yes — the transform is provably the identity at `bin = 1` | yes — any reader error breaks it | 18 datasets |
| `T27-V1-b` (bin 2 separation) | yes | **yes, and it is RUN** — the native arm on `D11`/`D12` is the FAIL end, printed | 2 datasets |
| `T27-V3` pairing | yes | yes — self-test fixture mutates a row | |
| `T27-V4` fingerprint | yes | yes — any byte change | BEFORE files already taken |
| `T27-V5` path trap | yes | yes — `/mnt/d/hf_w25/table` exists and gives different numbers | the near-miss is real and named |
| **S1 `A_all ≥ 0.25`** | **yes** — `D10` cannot exceed ~0.001 by arithmetic | **yes** — `D18`'s discs alone reach 0.47 | §4.2 density table |
| **S2 `precisionNull ≥ 0.25`** | **yes** — F31 measured 0.000–0.012 on a sound metric | **yes** — on a saturated reference a decorrelated catalogue still protects | F31's published control |
| **S3 `chanceProtected ≥ 0.25`** | **yes** — sparse datasets must be ~0 | **yes** — `D18`'s conditional coverage exceeds its 0.47 marginal | §4.2 |
| ~~`excess = (p_obs − p_null)/(1 − p_null)`~~ | — | **NO — identically 1 on 19 of 20 datasets** | **REJECTED at design time as unsatisfiable, §4.1.** An unsatisfiable clause is a FINDING; this one is recorded here rather than shipped and discovered |
| `RULE R27` reproducibility | **yes — MEASURED, `R-IDENTICAL`**, 20 cells / 420 files / 0 diffs | **yes** — the comparator is demonstrated failing in both directions in `repro_all_w27.sh`'s own self-test | **ran before pre-registration; declared as a deviation, §5.6a** |
| `S27-1` route agreement (4 fields × 20 datasets) | yes | yes — a disagreement blocks the ship and is a finding about one route | |
| `S27-2` mutants | yes | yes by construction | three named mutants |
| `S27-4` byte-identity, 360 artifacts + 60 counts | yes — B15 and HEAD are C#-identical, so B16 = B15 + (A) exactly | yes — any moved byte or count blocks the ship | the strongest form available |
| `S27-5` `truthViolations = 0` | yes — F31's repair guarantees it | yes — a non-zero value is a product finding | |
| **`D27` `MOVED` at 0.01** | yes — the flag may be inert | yes — 0.01 is the last digit the table prints | 6-dataset verdict population |
| `D27-V1` `PixelScale` carries the factor | yes | **yes, and it is RUN** — `D09` re-run *without* the flag must show the NOTE return and the `pixelScale:` line differ | executed |
| `D27-V2` coordinate collapse | yes — published factor-1 recalls are 0.64–0.98, far above 0.25 | yes — a coordinate break drives recall to ~0 | |
| `Q27-V1` reach-scoped discharge | yes — the diff is expected to be `GoldenEvalRunner.cs` + tests | **yes, and it is RUN** — the same check against `<B14 tree>..HEAD` must refuse | executed |

**One clause was found unsatisfiable at pre-registration and is recorded as a finding rather than repaired into
the wave** (§4.1). That is the discipline working before the data rather than after.
