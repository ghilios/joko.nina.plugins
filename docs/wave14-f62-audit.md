# Wave 14 — the F62 audit: where the register scored a point-set-changing knob on a within-fit statistic

**Status:** Done (wave 14, item "F62 audit") · reading and reasoning only, no `TestApp` run, no C# changed ·
**this file is not a register entry and proposes text rather than editing it**

---

## THE HEADLINE — read this first

**F62's prediction is confirmed: other conclusions did rest on this, and two of them are cited in the wave-14
rationale for changing a SHIPPED DEFAULT.**

Twelve passages meet all three inclusion criteria. **Two are INVALIDATED, three are WEAKENED**, and the two most
consequential are the ones wave 14 is about to ship on:

- **[F61] — "`MOR`=0 is better on 10 of 10 moved `sChi`"** is **WEAKENED**, and the mechanism is verified in
  code, not conjectured. The sensor paraboloid is weighted by **1/σ² where σ is the per-star hyperbola's own
  `MinimumStdError`** (`SensorModel.cs:838-841` → `SensorParaboloidModel.RegularizeStdDev` →
  `NonLinearLeastSquaresSolverBase`'s `OutputStdDevs`). At `MaxOutlierRejections` = 1 every star whose rejection
  fires reports a *smaller* σ, which *raises* its weight, which *raises* the paraboloid's reduced χ². **`sChi`
  moves in the observed direction for arithmetic reasons that have nothing to do with the sensor fitting worse.**
  The `sRMS` half (9 of 10) uses an **unweighted** residual metric (`NonLinearLeastSquaresSolver.RMSError`) and
  is the part that carries the claim. F62 named `sChi` as one of the four contaminated statistics; nobody
  noticed it had a **second** path into the sensor model.
- **[F45] — "`caboose` degrades to σ_focus 1.4325 → 18.185 (12.7×)" at budget 3** is **WEAKENED on magnitude
  only**. The *direction* survives F62 outright (F62's bias predicts improvement; this is a degradation, and a
  bias cannot manufacture its own opposite), and reduced χ² 0.00427 → 3.342 with R² 0.99987 → 0.9235 carries the
  conclusion. But σ_focus also rises **mechanically** as the surviving point count approaches the parameter
  count — `s² = weighted RSS/(n−p)` and `(JᵀWJ)⁻¹` both grow — so **12.7× is not an accuracy factor** and should
  not be quoted as one. No out-of-sample vertex check was run at budget 3.

Both are quoted in `docs/waves14-21-autonomous-prompt.md:41-45` as the supporting evidence for flipping
`MaxOutlierRejections`'s default. **The decision is not overturned** — `MOR`=1 winning on 0 of 4 against the
generator's truth is untouched by this audit and remains the strongest line — but two of the four bullets need
the wording in §6 before they are published again.

The third INVALIDATED/WEAKENED cluster is older and has no live consumer: **[F58]**'s clause *"allowing one
Grubbs rejection improves a fit, which is the observed direction"* is **INVALIDATED** as a quality claim
(F62's own D1 measured that same improvement buying accuracy on **zero** of four datasets), and **[F48]**'s
*"a real, structured signal"* about narrow executed sweeps is **INVALIDATED** — it is a σ_focus-only reading
across arms that sample **different focuser positions**, on synthetic datasets whose true focus was available
and never consulted.

**What did NOT break, and it is most of the register.** Every conclusion that is a claim of *non-identity*
(F55, F57), of *existence* (F35/F20), of *attribution* (F58's partition + F57's one-key intervention), or that
was explicitly refused a vote (F63, F18) **SURVIVES**. The register has been unusually disciplined about this
since wave 6; the failures are concentrated in exactly three places where a **direction** was read off a
within-fit statistic without saying so.

---

## 1. Criteria, fixed before looking

*Restated verbatim from the item's commission. These are this audit's pre-registration; they were not widened
or narrowed after reading.*

> A passage is a **HIT** only if **all three** hold:
>
> 1. **A comparison was made** between two or more arms / settings / values (not merely a quantity reported once).
> 2. **The independent variable can change WHICH POINTS ENTER THE FIT.** This includes, at minimum:
>    `MaxOutlierRejections`, `OutlierRejectionConfidence`, the Grubbs `RejectionTest` itself,
>    `FitRejectionCriterion`, `ReducedChiSquaredRejectionThreshold`, any per-star or per-point
>    pruning/filter/gate, any change to which frames or positions are admitted, and the sensor model's per-star
>    rejection budget. **A pure DETECTOR change is NOT a hit** (same point set both arms) — F62 says so
>    explicitly; classify those as OUT and do not report them.
> 3. **The comparison was scored on a within-fit statistic** — σ_focus / `MinimumStdError`, `J` / `BestJ` /
>    `BaselineJ`, R² / `afR2` / `sR2`, reduced χ² / `afChi` / `sChi`, `sRMS`, or any other quantity the fit
>    under test computes.
>
> Record for each hit: **where** (file + section/line), **what was compared**, **what it was scored on**, **what
> conclusion was drawn**, and — the part that matters — **whether that conclusion SURVIVES F62**. A hit does not
> automatically invalidate a conclusion: it may have had an independent out-of-sample check, it may have been
> reported as a magnitude rather than a verdict, it may have been explicitly refused a vote, or the direction may
> be argued from mechanism rather than from the statistic. **Say which.** Classify every hit as one of exactly:
>
> - **INVALIDATED** — the conclusion depends on the within-fit statistic and has no other support;
> - **WEAKENED** — the conclusion stands on other grounds but its stated evidence is partly compromised, or its
>   magnitude is overstated;
> - **SURVIVES** — an out-of-sample arbiter, a free control, or an explicit refusal-to-vote already covers it;
> - **UNEVALUATED** — you could not determine it from the record (say exactly what you would need to read or
>   measure).
>
> **"Could not look" needs its own state — never silently score an unread passage as SURVIVES.**

### The one ruling the criteria forced, stated so it can be checked

Criterion 2 lists "any per-star or per-point pruning/filter/gate" as IN and "a pure DETECTOR change" as OUT, and
several knobs are both (`MinHFR`, `Sensitivity`, `MinDetectionKeepFraction`). **The tie is broken by the
carve-out's own parenthetical — "same point set both arms".** For the AF hyperbola the points are the sampled
**focuser positions**; for the sensor paraboloid they are the **stars**. A knob is therefore IN when the record
itself shows a position (or a star) entering or leaving the fit, and OUT when only the measurement at each
common point changed. Every borderline ruling in §5 names which side of that line it fell on and why. §7(i)
records that this line is **not** where F62 currently draws it, which is a defect in F62.

---

## 2. Coverage

### Read in full

| file | how |
|---|---|
| `docs/followups.md` (5081 lines, all 64 `### F<n>` entries) | grep-located then read in passage; every entry that mentions σ_focus, `J`/`BaselineJ`/`BestJ`, R², reduced χ², `sChi`, `sR2`, `sRMS`, `MaxOutlierRejections`, `OutlierRejectionConfidence`, Grubbs/`RejectionTest`, step size / half-width / `offsetSteps`, or `MinHFR` was read with ≥40 lines of context. **Primary target; complete.** |
| `docs/synthetic-af-bank-followups-wave{2,3,4,5,6}-results.md` | read end to end |
| `docs/synthetic-af-bank-followups-wave{7,8,9,10,11,12,13}-design.md` and `-results.md` | read end to end |
| `docs/waves14-21-autonomous-prompt.md` | read; §4 standing discipline and §2's shipping rationale verified against source |
| `/mnt/d/hf_w{10,11,12,13}/*.py` (15 files) and `*.sh` (20 drivers), plus `hf_w11/{bisect,det,pop}/*.py` | every scorer read; IV and scored quantities extracted per file |

### Read in source, to settle a mechanism rather than take one on trust

`AlglibHyperbolicFitting.ComputeMinimumStdError` / `ComputeChiSquared` (`:703-782`),
`AlglibHyperbolicFitting.Create` (`stepSize` is a seed, **not** an abscissa normalisation — so `MinimumStdError`
is in raw focuser units), `RunEvaluationData.cs:824`, `SensorModel.cs:759-841`,
`SensorParaboloidModel.cs:22-70,209-230`, `NonLinearLeastSquaresSolver.RMSError/ReducedChiSquared/GoodnessOfFit`,
`StarDetector.cs:1925-1928` (`MinHFR` is `RejectionGate.TooLowHFR`, a detector-side per-star gate).

### NOT read, and why

- `docs/synthetic-af-bank-design.md`, `-baseline-results.md`, `-expectations.json`,
  `docs/f23-objective-precision-term-results.md`, `docs/f11-*`, `docs/donut-hfr-normalization-*`,
  `docs/star-detection-*` — **outside the commissioned scope** (the item names the register, waves 2–13, the
  wave-14 prompt, and the `hf_w1{0,1,2,3}` scorers). Their arms are detector/golden-scored and would be OUT on
  criterion 2 or 3, but that is an expectation, not a check. **Declared unread, not declared clean.**
- `/mnt/d/hf_w{1..9}/` scorers — the commission scoped the scorer sweep to `hf_w1{0,1,2,3}`. Waves 2–9's
  *conclusions* are covered through their results docs; their *scripts* are not. Wave 5's `scorecard.py` and
  wave 9's φ scorer are the two most likely to hold an unquoted cross-arm within-fit delta.
- The **generating script behind `/mnt/d/hf_w12/b12d_score.txt` was never saved to disk** (the driver
  `bankverify_disc_w12.sh` remains). Its saved output is MOVED/same only, so nothing is at risk, but the
  provenance gap is recorded.

**Nothing in the commissioned scope is being reported as clean without having been read.** The two passages I
could not resolve are in the UNEVALUATED rows of §3, with the exact artifact needed to close each.

---

## 3. The hit table

Ordered INVALIDATED → WEAKENED → SURVIVES → UNEVALUATED.

---

### INVALIDATED

#### H1 — "allowing one Grubbs rejection improves a fit, which is the observed direction"

| field | |
|---|---|
| **where** | `docs/followups.md:3964-3968` (**F58**, "And it explains F57 without a second cause"). Same sentence at `wave11-design.md:90-93` and `wave11-results.md:142-145`; the sibling form at `wave11-results.md:343-346` (*"allowing the rejection improves the fit — which is the direction observed on every dataset where the two groups were compared"*). |
| **what was compared** | `MaxOutlierRejections` = 0 against = 1, first across five pre-existing NINA profiles, then across wave 9's and wave 10's gates (`toml999` 0.997840 → 0.983477). |
| **scored on** | `BaselineJ`. |
| **conclusion drawn** | That the *direction* of the movement corroborates the mechanism — i.e. that permitting the rejection makes the fit better, which is why the higher `J` belongs to the `MOR`=1 group. |
| **verdict** | **INVALIDATED**, as a quality claim only. F62's D1 measured exactly this improvement against the generator's truth and found it bought accuracy on **0 of 4** datasets, opposing it on three. A prediction that cannot fail is not corroboration: `J` is computed by the fit after the fit was allowed to discard its worst point, so "it improves" was guaranteed before the data were seen. **The surrounding conclusions are untouched** — the 5-of-5 partition is an *attribution* argument, its discriminating half is that `OutlierRejectionConfidence` varies *within* the firing group and moves nothing, and F57 later closed the same question by a one-key intervention with an explicit negative control. F58's answer is right; one sentence of its evidence is circular. |

#### H2 — F48's "real, structured signal" about narrow executed sweeps

| field | |
|---|---|
| **where** | `docs/followups.md:3230-3233` (**F48**, "Why it matters"), from `wave8-results.md:483-485`; the underlying table at `followups.md:3216-3218`. |
| **what was compared** | F18's arm **S** (`--step-size-for-executed-sweep`, divisor 3.5 → 4.375) against control **C**, over 26–28 (dataset, scenario) cells. **The step size decides which focuser positions the sweep visits**, so the two arms fit *different point sets* — criterion 2's named case. |
| **scored on** | σ_focus, per cell (`D05` S1 0.2296 → 0.0055, `D15` S1 0.1622 → 0.0471, `D16` S6 0.0651 → 0.1433, …). |
| **conclusion drawn** | *"The wins are concentrated in the ×0.25-step scenarios (S1), i.e. runs recovering from a too-narrow sweep, and the losses in S6 (step AND exposure both wrong). That is a real, structured signal — a narrower executed sweep helps when the fit is being rebuilt and hurts when the run is also photon-starved — and the median threw it away."* |
| **verdict** | **INVALIDATED.** σ_focus is `√(mgᵀ·s²·(JᵀWJ)⁻¹·mg)` in raw focuser units over the points actually fitted, so its value is a joint property of the curve **and of the sampling geometry**. Comparing it across arms that sampled different positions cannot separate "the vertex is better determined" from "the abscissa was rearranged" — and a 40× ratio on `D05` S1 is far outside anything the register attributes to a real focus improvement elsewhere. There is **no other support**: no recall, no assertion count, and — the sharp part — **these are synthetic datasets, whose `renderRequest.OptimalFocuserPosition` was available the whole time and was never consulted.** F62's own remedy was sitting in the same bank. The *shape* of the observation (S1 wins, S6 losses) may well survive re-scoring; nothing in the record establishes it. |

---

### WEAKENED

#### H3 — F61's "`MOR`=0 is better on 10 of 10 moved `sChi` and 9 of 10 `sRMS`"  ⭐ most consequential

| field | |
|---|---|
| **where** | `docs/followups.md:3754-3757` (**F61**, the wave-13 block quote), from `wave13-results.md:211-251` (§2.3 D3). Cited as shipping evidence at `docs/waves14-21-autonomous-prompt.md:42`. Scorer: `/mnt/d/hf_w13/compare_bv_w13.py:32-36,210-213`. |
| **what was compared** | `MaxOutlierRejections` = 0 against = 1 — **one key in one settings file** — over 19 real-bank runs, restricted to the **14 runs whose star population is unchanged**, "the only subset where the goodness-of-fit statistics compare like with like". The knob is `SensorModel.cs:714-715`'s **per-star** rejection budget: criterion 2's explicitly named case. |
| **scored on** | `sChi` (paraboloid reduced χ²), `sRMS`, `sR2`, with `sStars`/`sTheta` reported rather than scored. |
| **conclusion drawn** | *"the sensor paraboloid fits WORSE when each star's own curve is allowed a rejection"*, and through it *"the AF fit is silent, the sensor fit prefers 0, and the out-of-sample arbiter never prefers 1 … There is nothing to average."* |
| **verdict** | **WEAKENED**, with a verified mechanism and a specific repair. The like-for-like control fixes the **outer** point set (which stars enter the paraboloid) and is exactly the right control for that confound. It does **not** touch the F62 confound, because the per-star σ that the paraboloid **weights by** is itself post-rejection: `SensorModel.cs:838-841` sets `bestFocusStdDevMicrons = fitting.MinimumStdError × focuserSizeMicrons`, `:869` passes it through `SensorParaboloidDataPoint.RegularizeStdDev` (2 µm quadrature floor), and `NonLinearLeastSquaresSolverBase` turns it into the 1/σ² weights behind `ReducedChiSquared` and `GoodnessOfFit`. **Where the rejection fires, σ falls, the weight rises, and reduced χ² rises — which is the observed direction, for reasons that are not about fit quality.** `sChi` and `sR2` are therefore contaminated; the 2 µm floor damps the effect but does not remove it. **`sRMS` is what saves the conclusion:** `NonLinearLeastSquaresSolver.RMSError` is an **unweighted** RMS of the µm residuals, so 9 of 10 favouring `MOR`=0 is a real measurement of noisier per-star focus estimates. It is insulated, not immune — the surface it measures residuals against was still solved with the contaminated weights. **The direction stands on `sRMS`; the "10 of 10 `sChi`" should not be quoted as independent corroboration, because it is the same σ shrinkage F62 is about, arriving one level up.** |

#### H4 — F45's `caboose` cascade at `MaxOutlierRejections` = 3

| field | |
|---|---|
| **where** | `docs/followups.md:3052-3057` (**F45**, wave-13 block quote), from `wave13-results.md:178-209`. Cited as shipping evidence at `docs/waves14-21-autonomous-prompt.md:44-45`. |
| **what was compared** | budget 0/1 against budget 3 on `caboose`, a real-bank run whose fit at budget 0 has R² = 0.99987. |
| **scored on** | σ_focus (1.4325 → 18.185, **12.7×**), R² (→ 0.9235), reduced χ² (0.00427 → 3.342). |
| **conclusion drawn** | *"The shipped default of 1 is the BOUND, not merely a safe value — the smallness of the budget is the only thing containing it."* |
| **verdict** | **WEAKENED — direction survives in full, magnitude does not.** The rescue is real and should be stated where the number is: **F62's bias predicts that dropping points makes σ_focus fall, and here it rose**, so the effect cannot be the self-serving arithmetic. Reduced χ² rising ~780× while the degrees of freedom fall by at most a factor of a few is the part that cannot be explained by a shrinking point set at all — it says the refit genuinely fails on the points it *kept*, which is the claim. **But "12.7× worse σ_focus" is not an accuracy factor.** `s² = weighted RSS/(n−p)` and `(JᵀWJ)⁻¹` both grow as `n` falls toward the parameter count, so an unknown share of 12.7× is the point set shrinking rather than the focus being less well known — and **no out-of-sample vertex check was run at budget 3**, on a real bank where none exists. Wave 13 states the corresponding caveat for the *improving* direction (*"On the real bank the rejection makes σ_focus look up to 70 % better and there is no truth to check it against"*) and never applies it to this row. |

#### H5 — F48 part (a): "arm S beats the control by 10.2 %, so the rule would have FIRED"

| field | |
|---|---|
| **where** | `docs/followups.md:3241-3256` (**F48**, "PART (a) DONE 2026-08-06 (wave 8)"), from `wave8-results.md:451-486`. |
| **what was compared** | arm S against control C, and arm D against C, restricted to the **12 movable cells** (more than one round rendered). Step size ⇒ different sampled positions ⇒ different fitted point sets. |
| **scored on** | median σ_focus ratio S/C — 1.0000 over 28 cells, **0.8977** over 12. |
| **conclusion drawn** | *"Over the cells that can move, arm S beats the control by 10.2 % … on the movable denominator the rule would have FIRED and arm S would have shipped. The data did not change; the denominator did."* |
| **verdict** | **WEAKENED.** The **load-bearing** conclusion is methodological — *"the choice of denominator was worth more than the entire effect being measured"* — and it is about the arithmetic of medians over forced ties, which F62 cannot touch: the verdict really would have flipped. What does *not* survive is the reading underneath it, that arm S produces **better** fits by 10.2 %; that is H2's problem restated as a summary statistic. The passage's own guardrail (*"The wave-7 verdict still stands and this does not re-open it"*) is what keeps this at WEAKENED rather than INVALIDATED — **no behaviour was changed on the strength of the number.** Arm D's half is untouched: "arm D is 1.0000 on all twelve" is a *non-identity* reading (it never acted), which F62 cannot threaten. |

---

### SURVIVES

#### H6 — F18's step-size arms C/D/S, with σ_focus as the pre-registered acceptance metric

| field | |
|---|---|
| **where** | `docs/followups.md:196-233` (**F18**), from `wave7-design.md:181,260-289` and `wave7-results.md:287-334`. |
| **what was compared** | arms C / D / S of `StepSizeRecommender` over 28 cells. Same point-set-changing IV as H2/H5. |
| **scored on** | median σ_focus, by a rule fixed in advance (D ships if no worse than C by >2 % with no dataset worse by >20 %; S ships only if it beats D by >5 %). |
| **conclusion drawn** | Rule 1 **FIRES** for arm D; rule 2 does not fire for S; **the flag ships default OFF**. |
| **verdict** | **SURVIVES — by explicit refusal to read its own pass.** *"Arm D is byte-identical to the control on 26 of 28 cells … its only active cell produced no readable acceptance metric. **Zero** cells improved. The rule fires because the arm is INERT, not because it is good."* and *"Turning it on for every user on a vacuous pass would be reading a null as a green light."* Three further supports are non-fit quantities: a round-0 control at **0 violations**, `BaselineJ` identity across C/D/S as F41's free check, and the A1/A3 `synth-validate` assertions (**Pass 82 / Fail 2, identical across all three arms**) which score against `step_behavioral`, not against the fit. Nothing was adopted, and the null was refused rather than banked. **The standing rule the entry writes down is a different matter — see §6(f).** |

#### H7 — F63's landing comparison, with `BestJ` refused

| field | |
|---|---|
| **where** | `docs/followups.md:3643-3645` (**F63**), from `wave13-results.md:332-361`, pre-registered at `wave13-design.md:150-151`, enforced in `/mnt/d/hf_w13/score_pop_w13.py:12-15,145-156`. |
| **what was compared** | the eight-run gate at `MaxOutlierRejections` = 0 against = 1, `--max-evals 250`. |
| **scored on** | the **landed detector knobs**, `RecommendedStepSize`, `BrightnessSensitivity` — deliberately *not* `BestJ`. |
| **conclusion drawn** | the landing moves on 6 of 8; *"What is established is dependence, not direction."* |
| **verdict** | **SURVIVES, and it is the pattern the rest of the register should copy.** The refusal is structural, not rhetorical: `BestJ` is absent from the scorer's compared-key list, and the module docstring says *"a number that must not be compared is best not computed"*. `land_score.txt`'s only occurrence of "BestJ" is the refusal line. The scored quantities are detector outputs in their own units, common to both arms. |

#### H8 — F57's `BaselineJ` gap, and its closing intervention

| field | |
|---|---|
| **where** | `docs/followups.md:4030-4046` (**F57**, the CLOSED block) and `:4082-4083`; the bisect at `/mnt/d/hf_w11/bisect/score_bisect.py`. |
| **what was compared** | `MaxOutlierRejections` = 0 against = 1 — two settings files differing in exactly one line, profile pinned on both arms, `--max-evals 1`. |
| **scored on** | `BaselineJ` (0.98347680 vs 0.99784046, Δ +0.01436366). |
| **conclusion drawn** | that a `--settings`-pinned arm was never pinned, that the profile was the carrier and one integer the cause, and that every cross-wave `BaselineJ`/landing comparison is exposed. |
| **verdict** | **SURVIVES.** Every load-bearing claim here is one of **non-identity** or **attribution**, and F62 threatens neither — it says the number moves *for a reason that is not quality*, which is precisely F57's point. The bisect's negative control (`OutlierRejectionConfidence` alone must move nothing while the budget is 0) is the clause that makes it an experiment, and comparisons are exact rather than tolerance-based *"because the claim being tested is 'accounts for ALL of it' and a tolerance would manufacture that conclusion"*. **One rhetorical exposure worth noting, not enough to reclassify:** *"0.0144 … is larger than the entire Δ`J` any wave has ever argued about"* compares a rejection-induced Δ`J` against detector-induced Δ`J`, and F62 says those two are not commensurable. The severity is real; the comparison flatters it. |

#### H9 — F55's concurrency arms

| field | |
|---|---|
| **where** | `docs/followups.md:4150-4232` (**F55**), incl. the 40-second reproducer and the 15 %/44 % table. |
| **what was compared** | sequential against fan-out 4, and arms A/B/C — whose true IV turned out (F58) to be the profile's `MaxOutlierRejections`. |
| **scored on** | `BaselineJ` and `BestJ`. |
| **conclusion drawn** | the seed evaluation is not a function of (frames, settings) alone; the defect is bimodal; the landing rate is 44 %, triple the seed rate; **no φ verdict is published from the arm.** |
| **verdict** | **SURVIVES.** These are claims that two numbers *differ*, and a difference is a difference regardless of what the number means. F62 cannot make two unequal doubles equal. RULE G was applied as written and the arm's own verdict was withheld — the strongest possible form of not resting on the statistic. |

#### H10 — F35/F20's `MinHFR` seed, seed ON against seed OFF

| field | |
|---|---|
| **where** | `docs/followups.md:2713-2751` and `:2821-2875` (**F35**), `:1266-1267` (**F20**); `wave3-results.md:109-141`, `wave6-results.md:51-76`. |
| **what was compared** | `--no-min-hfr-seed` against the seed, per dataset. **IN, not OUT**, despite `MinHFR` being a detector knob: the register documents the point set changing, not merely the measurement — *"put 5 stars back on the vertex frame at `MinHFR` 0.5, **which clears the objective's `NHard` = 3 stars-per-frame requirement** … That is the whole of `FinalJ = 0.00000`."* A focuser position enters the fit that was not in it before. |
| **scored on** | `FinalJ` (D01 0.000000 → 0.991551; D02 0.000000 → 0.996510). |
| **conclusion drawn** | *"D01 and D02 go from `FinalJ` exactly 0 to a real landing — the whole of F20's defect."* |
| **verdict** | **SURVIVES, comfortably, and on four independent grounds.** (1) The claim is **existence, not magnitude** — 0 → non-zero is "the run produced an answer at all", which no amount of denominator arithmetic can manufacture. (2) The success criterion was **explicitly moved off the fit statistic**: *"Score this fix on 'the hard floor passes', not on recall"*, and separately *"Recall does not recover and was never the criterion."* (3) The safety question ("how low is safe") is answered by an **out-of-sample arbiter** — `golden eval` recall against synthetic truth with **FP = 0 in all 32 configurations**. (4) Controls designed not to move: D05 bit-identical seed-on/seed-off (and it is what caught the seed-leak bug), 14 of 17 synthetic and **19 of 19 real** landings bit-identical, `BaselineJ` identical on every dataset. |

#### H11 — F45's original Grubbs finding, rejection ON against OFF

| field | |
|---|---|
| **where** | `docs/followups.md:2987-3016` (**F45**), and the wave-13 `D16` reproduction at `:3041-3050`. |
| **what was compared** | the same 10 points with the Grubbs `RejectionTest` enabled and disabled — criterion 2's canonical case. |
| **scored on** | `TrendlineFitting.Minimum.X` (25015 vs 25000), argued alongside R² = 0.9994 and the fit's own MAD of weighted residuals. |
| **conclusion drawn** | that `Minimum` is computed on the post-rejection set and the discarded point is the in-focus one; that a robust scale applied to a near-perfect fit inverts its own purpose. |
| **verdict** | **SURVIVES.** The anchor is **`argmin(Y + ErrorY)` over the measured set** — raw data, not a fit output — and the decisive residual comparison is against **the point's own measurement error** (0.0345 px against 0.174 px), which the hyperbola does not compute. Wave 13 then reproduced it where the answer is known by construction: on `D16_esprit550_ha3` the rejected point **is** the generator's true focus (position 8000) and the vertex moves *away* from it (0.00067 → 0.00200 step). That is the out-of-sample arbiter, applied to this exact claim. The R² = 0.9994 quoted alongside is post-rejection and corroborative only; nothing rests on it. |

#### H12 — F25's degenerate-fit rounds

| field | |
|---|---|
| **where** | `docs/followups.md:247-268` (**F25**). |
| **what was compared** | round 0 (step 140) against round 1 (step 240) on `D05_tec140_1000mm` S2 — different steps, therefore different sampled positions. |
| **scored on** | fit R² (−0.223 → 1.000). |
| **conclusion drawn** | *"It only recovered because round 1 happened to fit cleanly at the wider spacing"*; the recommender has no fit-quality gate. |
| **verdict** | **SURVIVES.** R² is used **within** each round as a diagnosis of degeneracy (a negative R² means "worse than a horizontal line" — an absolute statement about one fit, not a cross-arm ranking), and the entry's actual finding is a **code reading**: *"`Recommend` still has no fit-quality gate"*. The cross-round R² pair is narrative, and the entry already discounts its own headline (*"the manifestation is seed-dependent … the specific 4×→7× over-reach did not recur"*). |

---

### UNEVALUATED

#### H13 — the wave-12 exposure ladder's σ_focus anchors *(inclusion undetermined)*

- **where** — `/mnt/d/hf_w12/score_excess_w12.py:247-256`, driver `ladder_w12.sh:45-46`; consumed by **F19** at
  `docs/followups.md:1050-1067` (rules W1/W2 and the 13 usable rungs).
- **what was compared** — exposure rungs 0.5/2/8/30/120 s per dataset; each dataset's σ_focus **minimum rung**
  and the set of rungs ≥20 % worse are the anchors against which the candidate wing statistic is calibrated.
- **scored on** — `BestSigmaFocus` across rungs. The driver states it plainly: *"σ_focus is the ground truth —
  what the user actually gets — and the statistic is the predictor."*
- **why UNEVALUATED** — exposure is not on criterion 2's list and does not normally change which positions are
  admitted, so the default ruling is OUT. But at the starved end it *does*: wave 12 found `D17`@0.5 s coming
  back `HardFloorPassed = false, WorstFrameCount = 0` and correctly excluded it as UNEVALUATED. **Whether any
  of the 13 *retained* rungs also lost a frame — which would make the σ_focus minimum a comparison across
  different point sets — I did not verify.**
- **what would close it** — the per-rung `HardFloorPassed` and `WorstFrameCount` columns for all 13 usable rungs
  in `/mnt/d/hf_w12/ladder_score.txt`, cross-checked against the per-frame star counts in `ladder_w12.log`. If
  every retained rung has all nine frames above `NHard`, this is OUT and the F19 calibration is clean.

#### H14 — wave 9's φ verdict, and its pre-registered external clause that was never reported

- **where** — `docs/followups.md:1689-1692` (**F32** status line) and `wave9-results.md:184-226` §1.6; the
  pre-registration at `wave9-design.md:186-188` and `:117`.
- **what was compared** — `MinDetectionKeepFraction` φ = 0.50 (arm B) against the shipped default (arm A) and
  against restarts (arm C), 117 optimizations over both full banks.
- **scored on** — median Δ`J`, **worst σ_focus regression** (`vsn07` 0.00083 → 1.71721, a factor of 2000),
  and Δ`J`-recovery. Conclusion: *"it destroys the focus fit"*; **`MinDetectionKeepFraction` stays default OFF
  permanently.**
- **why UNEVALUATED** — φ is an optimizer *acceptance constraint* on candidate detector settings, not a
  per-point pruning rule, and both arms fit the same nine focuser positions, so the §1 tie-break rules it **OUT**
  as a detector change. It is listed here rather than in §5 because **the record shows its pre-registered
  out-of-sample clause was never reported**: design `:187-188` fixed R1 as *"(b) `bank-verify` `recall@SNR≥12`
  under B is ≥ under A on a majority of the real-bank runs with no run regressing by more than 0.05"*, and
  `wave9-results.md` contains **no occurrence of `bank-verify`, `recall@SNR`, or R1(b)** — the verdict table
  reports R1(a), R1(c), R2 and R3 only. So a **permanent** default decision on a star-keeping constraint rests
  on `J` and σ_focus with its one external clause silently dropped. *(Wave 5's screening arm did carry golden
  recall/precision with FP = 0, so the evidence exists at φ ∈ {0.30, 0.50, 0.75} — it was the wave-9 confirmation
  that omitted it.)*
- **what would close it** — either (a) `vsn07`'s per-frame star counts under arms A and B, to establish whether
  a frame fell below `NHard` (which would make it a hit rather than OUT), or (b) running the pre-registered
  R1(b) `bank-verify --opt-a` comparison, which needs no new optimization if the arm-B landings are still on
  disk.

---

## 4. Counts

| class | n | which |
|---|---|---|
| **INVALIDATED** | **2** | H1 (F58's "improves a fit"), H2 (F48's "structured signal") |
| **WEAKENED** | **3** | H3 (F61's `sChi` 10 of 10), H4 (F45's `caboose` 12.7×), H5 (F48's 10.2 %) |
| **SURVIVES** | **7** | H6 (F18), H7 (F63), H8 (F57), H9 (F55), H10 (F35/F20), H11 (F45 original), H12 (F25) |
| **UNEVALUATED** | **2** | H13 (wave-12 ladder anchors), H14 (wave-9 φ verdict + unreported R1(b)) |
| **total hits** | **12** | (H13/H14 are counted as hits-with-undetermined-status, per the commission's fourth state) |

**Register entries touched:** F18, F25, F32 (via H14), F35, F45, F48, F55, F57, F58, F61, F63, F20.
**Scorers touched:** `/mnt/d/hf_w13/compare_bv_w13.py` (D2/D3), `/mnt/d/hf_w13/score_pop_w13.py` (D4 block,
disclosed and non-voting; D5 the refusal), `/mnt/d/hf_w12/score_excess_w12.py`.
**Scorers that are clean and worth naming as the standard:** `/mnt/d/hf_w13/score_affit_w13.py` (verdict is
`|vertex − OptimalFocuserPosition|`, never σ), `/mnt/d/hf_w13/score_pop_w13.py`'s landing mode (the refusal),
`/mnt/d/hf_w10/score_step.py` (a genuine step-size IV, scored on a **ratio-invariance** test and on the
recommender's own output — the nearest miss in the corpus and it lands the right side of the line).

---

## 5. Considered and ruled OUT — the boundary, so it can be argued with

None of the following are counted above. Each is recorded with the criterion it failed.

| candidate | where | why OUT |
|---|---|---|
| Detection binning 1 vs 2, σ_focus up 17–95 % on 7 of 7 | F39/F46, `wave7-results.md:102-124`, `wave8-results.md:302-319` | **Criterion 2** — pure detector change; all nine positions common to both arms; the 13 binning-1 datasets are bit-identical as a free control. *But see §7(i): `D17`'s `BaselineJ` 0.000000 → 0.994830 is a position entering the fit.* |
| Config A vs B / C0 vs A on the shedders — "the optimizer is behaving rationally", σ_focus 10 of 10, median ratio 0.320 | F4, F5, F32; `wave4-results.md:124-151` | **Criterion 2** — the IV is the landed Sensitivity/StarClip gate, a detector knob; the fitted positions are common. Flagged in §7(i) as the case where that premise is least safe (`mccomiskey` 3606 → 43 detections). Partially retracted already by wave 5 on optimality grounds. |
| `MinDetectionKeepFraction` φ arms, wave 5 and wave 9 | F32; `wave5-results.md:78-182`, `wave9-results.md:184-226` | **Criterion 2** — an acceptance constraint over candidate detector settings, not a point-set filter. Carried to **H14** because of its unreported external clause. |
| Exposure ladders (arm E, arm X, RULE W12) scored on σ_focus | F19; `wave7-results.md:160-201`, `wave8-results.md:323-368`, `wave12-results.md:207-271` | **Criterion 2** — exposure changes photon counts, not admitted positions. Carried to **H13** for the starved end only. Note wave 12 *does* carry an F62-shaped refusal here: a rung with `HardFloorPassed = false` is reported **UNEVALUATED** and anchors nothing. |
| `--structure-layers` / `--min-box` / donut-master sweeps | F46, F24, F49 | **Criterion 3** — scored on golden recall/precision and per-gate FN attribution, never on a fit statistic. |
| F21's half-width instability "even at R² = 1.0000" | `followups.md:1283-1292` | **Criterion 2** — the IV is the **noise seed** at the same step. Reported here anyway because it contains the register's **earliest independent statement of F62's argument**, three waves early, and it is a rescue rather than a hit: *"`R² = 1.0000` is no evidence against that: R² measures fit to the SAMPLED points and says nothing about whether the vertex is identifiable from them, which on a sweep that never leaves the focus zone it is not."* |
| F54/F53's landing movements, F41's `BaselineJ` tell, RULE G/G10/G11/G12/G13/A12/K7/K8, `score_w12.py`, `compare_bv_w12.py`, `trace_diff.py`, `score_probe.py` | passim | **Criterion 1 or 3** — these test **bit-identity** or **cardinality**, not better/worse. An invariance check on a within-fit statistic is not a hit; it is the correct use of one. |
| F60 (fan-out cost) | `followups.md:3768-3800` | **Criterion 3** — wall clock. |

---

## 6. Recommendation — proposed text, not applied

Nothing below has been written into `docs/followups.md`. Each is a self-contained insertion.

**(a) F61** — after *"`MOR`=0 is better on 10 of 10 moved `sChi` and 9 of 10 `sRMS`"* (`followups.md:3756-3757`):

> **The `sChi` half is F62 arriving one level up, and only `sRMS` carries this.** The paraboloid is weighted by
> 1/σ² where σ is **the per-star hyperbola's own `MinimumStdError`** (`SensorModel.cs:838-841` →
> `SensorParaboloidDataPoint.RegularizeStdDev` → `OutputStdDevs`). Where the per-star rejection fires, σ falls,
> the weight rises, and the paraboloid's reduced χ² rises — the observed direction, for reasons that are not
> about the surface fitting worse. `sR2` inherits the same weighting. **`sRMS` is the uncontaminated half**
> (`NonLinearLeastSquaresSolver.RMSError` is an unweighted µm residual RMS), and 9 of 10 is what the conclusion
> should be quoted from. To make `sChi` readable, re-score both arms with the **`MOR`=0 weights held fixed**;
> until then it is not independent corroboration.

**(b) F45** — after *"σ_focus 1.4325 → 18.185 (12.7×)"* (`followups.md:3053`):

> **The direction is what carries this, not the factor.** F62's arithmetic predicts a rejection makes σ_focus
> *fall*; here it rose, so the effect cannot be the fit grading itself. But σ_focus also grows mechanically as
> the surviving point count approaches the parameter count (`s² = weighted RSS/(n−p)` and `(JᵀWJ)⁻¹` both grow),
> so **12.7× is not an accuracy factor** and no out-of-sample vertex check was run at budget 3. The reduced χ²
> (0.00427 → **3.342**) and R² (0.99987 → 0.9235) are the parts that cannot be explained by a shrinking point
> set: the refit fails on the points it *kept*.

**(c) F58** — replacing *"and allowing one Grubbs rejection improves a fit, which is the observed direction"*
(`followups.md:3965-3966`):

> and the group that may drop a Grubbs outlier is the group with the higher `J` — **which is a tautology, not
> corroboration** ([F62](#f62--σ_focus-is-anti-informative-when-an-outlier-rejection-is-what-changed-it-it-improves-by-up-to-88--while-the-distance-to-a-known-truth-improves-on-none):
> `J` is computed after the fit was allowed to discard its worst point, and wave 13 measured that same
> improvement buying accuracy on **0 of 4** datasets). **The attribution does not depend on it** — it rests on
> the 5-of-5 partition, on `OutlierRejectionConfidence` varying *within* the firing group without moving `J`,
> and on F57's one-key intervention.

**(d) F48** — replacing *"That is a real, structured signal … and the median threw it away"*
(`followups.md:3231-3233`), and appended to part (a):

> That looks like a structured signal, **and σ_focus cannot establish it**
> ([F62](#f62--σ_focus-is-anti-informative-when-an-outlier-rejection-is-what-changed-it-it-improves-by-up-to-88--while-the-distance-to-a-known-truth-improves-on-none)):
> arm S changes the **step**, so the two arms fit different focuser positions, and σ_focus is a joint property
> of the curve and the sampling geometry. The 10.2 % on the movable denominator is a real **verdict flip** — the
> lesson about denominators stands — but it is not evidence that arm S focuses better. **These are synthetic
> datasets: re-score both arms on `|fitted vertex − renderRequest.OptimalFocuserPosition|`, which the bank has
> carried since it was built.** Until then the S1-helps / S6-hurts reading is a hypothesis.

**(e) F62 itself** — three amendments, see §7 for the evidence:

> **Two corrections to this entry's own statement.** (1) *"It must improve"* is too strong: this entry's own
> `D12` row is **+13.0 %**. σ_focus is `√(mgᵀ·s²·(JᵀWJ)⁻¹·mg)` with `s² = weighted RSS/(n−p)`, so losing a point
> shrinks the residual sum **and** the degrees of freedom **and** the information matrix, and the last two can
> dominate. Read it as *biased to improve* — with the useful corollary that **a degradation under a rejection is
> evidence while an improvement is not**, which is what makes F45's `caboose` result readable at all.
> (2) The DETECTOR carve-out holds only while **the point set really is common**, and a detector knob can move a
> frame across `JRun`'s `NHard` floor and remove a focuser position outright — `MinHFR` does exactly that
> ([F20](#f20--below-minhfr-the-autofocus-objective-collapses-to-exactly-zero-with-no-diagnostic)/[F35](#f35--minhfr-should-be-seeded-from-the-sweep-wings-and-neither-available-hfr-statistic-can-size-it):
> *"5 stars back on the vertex frame … clears the `NHard` = 3 requirement"*), and wave 8 records `D17`'s
> `BaselineJ` going **0.000000 → 0.994830** under a binning change. **The test is not "was it the detector"; it
> is "did a frame or a star enter or leave the fit".** (3) The warning has a **second consumer nobody named**:
> the sensor paraboloid weights by 1/σ² with σ = the per-star `MinimumStdError`, so `sChi` and `sR2` are
> contaminated the same way — see [F61](#f61--f58ds-real-consumer-was-the-sensor-model-not-the-af-fit-the-per-star-paraboloids-rejection-budget-came-from-the-active-profile).

**(f) F18** — appended to the pre-registered acceptance rules (`followups.md:196-200`):

> **This rule would not be written the same way today.** σ_focus is the acceptance metric for arms that change
> the **step**, i.e. that change which focuser positions are fitted — the class
> [F62](#f62--σ_focus-is-anti-informative-when-an-outlier-rejection-is-what-changed-it-it-improves-by-up-to-88--while-the-distance-to-a-known-truth-improves-on-none)
> says it cannot arbitrate. The wave-7 verdict is unaffected because the arm was inert and the pass was refused
> as vacuous; **any future step-size arm must be scored on `|fitted vertex − truth|` on the synthetic bank**,
> with σ_focus reported as a magnitude that does not vote.

**(g) `docs/waves14-21-autonomous-prompt.md:41-45`** — the shipping rationale. Bullets 1 and 3 are untouched.
Replace bullets 2 and 4:

> - the sensor fit prefers 0 on **9 of 10** like-for-like **`sRMS`** runs (F61/wave 13) — `sRMS` rather than
>   `sChi`, because the paraboloid weights by the per-star post-rejection σ and `sChi` therefore moves partly by
>   arithmetic (F62 audit, wave 14);
> - at budget 3 the cascade takes `caboose` (R² = 0.99987) to a reduced χ² of **3.342 from 0.00427** and R² to
>   0.9235 — the fit fails on the points it *kept* — **so the direction of risk is entirely on the high side.**
>   *(The "12.7× σ_focus" form of this number should not be used: part of it is the shrinking point set.)*

**(h) A new standing line for the wave design template**, beside the existing σ_focus discipline:

> **Say which class of change an arm is, in the design, before it runs.** If the arm can move a point into or
> out of the fit — a rejection budget, a step size, a sweep width, a per-star gate that can starve a frame below
> `NHard` — then σ_focus, `J`, R², reduced χ², `sChi` and `sR2` are **magnitudes, not votes**, and the design
> must name the out-of-sample arbiter it will use instead. Wave 13's `score_pop_w13.py` is the reference
> implementation: the quantity that must not be compared is **not computed**.

---

## 7. Three structural findings about F62 itself

These are not hits; they are defects in, or extensions of, the finding this audit was measured against.

**(i) The DETECTOR carve-out's premise is falsifiable, and the register falsifies it twice.** F62 exempts
detector changes because *"the point set is then common to both arms"*. That is true of the **stars** but not
guaranteed of the **positions**: `JRun` applies a hard `NHard` floor per frame, so a detector knob that starves
one frame removes a focuser position from the fit entirely. F20/F35 document precisely this for `MinHFR`
(*"which clears the `NHard` = 3 stars-per-frame requirement … That is the whole of `FinalJ = 0.00000`"*), and
wave 8 records `D17`'s `BaselineJ` going **0.000000 → 0.994830** under a *binning* change. The correct test is
not "was the detector what changed" but "did a frame or a star enter or leave the fit" — which is the line §1
adopted and which F62's current wording does not draw. The most exposed OUT ruling in §5 is wave 4's
`mccomiskey` shedder (3606 → 43 detections across nine frames): at that density a frame crossing `NHard` is
plausible and unrecorded.

**(ii) "It must improve" is not a theorem, and F62's own table shows it.** `D12_c14_585_afbin2` moves **+13.0 %**
(worse) at budget 1. `ComputeMinimumStdError` forms `s² = weighted RSS/(n−p)` and propagates
`(JᵀWJ)⁻¹` through the delta method, so dropping a point reduces the residual sum but also the degrees of
freedom and the information matrix. The bias is strong, not absolute. **The practical corollary is worth more
than the correction:** because the bias points one way, a within-fit statistic that *degrades* under a rejection
is informative, while one that *improves* is not. That is exactly why H4 survives at all and why H1 does not.

**(iii) F62 named four statistics and missed a fifth path.** It lists σ_focus, `J`, R² and reduced χ² as
"computed by the fit under test". The per-star `MinimumStdError` is *also* the **weight** the sensor paraboloid
fits with, so a per-star rejection budget propagates into `sChi` and `sR2` through a route that is not the
paraboloid's own point set — and the like-for-like control F61 built (hold `sStars` constant) does not close it.
This is the finding with a live consumer, and it is why H3 is the most consequential hit in this audit.

---

## 8. What a clean result would have looked like, and why this is not one

F62 recorded a prediction: *"other conclusions may rest on this"*. It would have been a real result to refute
it — 12 hits with 7 SURVIVES and 0 INVALIDATED would have said the register's discipline had been ahead of its
own finding. **It is not that.** Two conclusions are invalidated, three are weakened, and two of the five are
being cited **this wave** in support of changing a shipped default. The discipline is genuinely good — the
refusals in F63, F18, F35 and wave 13's scorers are the reason the number is five and not fifteen — but it
arrived in wave 13, and waves 4–11 predate it.

The single most consequential hit is **H3 (F61's `sChi`)**, because it is the only one where the contaminating
mechanism was **invisible to F62's own statement of itself**, and because it is quoted in a live product
decision. The single most consequential *non*-hit is **H10 (F35/F20)**: a point-set-changing knob, scored on
`FinalJ`, that survives completely — because its author moved the success criterion off the fit statistic and
onto the hard floor, and checked the safety question against golden truth. That is the template.
