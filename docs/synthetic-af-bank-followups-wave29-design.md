# Wave 29 — backlog item 1 ([F82](followups.md)): the step-size floor, and what source says about its fix

**Pre-registration. Written before any wave-29 statistic is computed.** §2 records where the controller's
framing is wrong; there are six such places and **three of them change what the wave does**. One of them
removes the wave's ship.

---

## 0. THE VESSEL — decided in writing, before any measurement

**Wave 29 opens a fresh branch off `ghilios/synthetic-af-bank-followups-wave27`, named
`ghilios/synthetic-af-bank-followups-wave29`, and opens its own PR. PR #196 is left to be reviewed and merged
as what it is.**

- PR #195 (`…wave23`, waves 23–26) is **MERGED**.
- PR #196 (`…wave27`) is **OPEN / MERGEABLE** at `6ffa498`, and already carries **two** waves (27 and 28) plus a
  user-visible product change, under a title naming only wave 27's truth-protection audit.

**Reason.** Wave 28's design §0 pre-registered exactly this and the wave did the opposite; wave 28 §13 recorded
it as a deviation and named the cost — *"#196 now carries two waves and a user-visible product change under a
title about wave 27's truth-protection audit."* Repeating it would make #196 carry three. The charter's §1a
warning against silent accretion has now been ignored once with the cost measured; **a third section is the
accretion, not a step toward it.**

**Wave 29 changes no C# and builds no binary** (§9), so nothing about this choice is load-bearing for a
verdict. It is load-bearing for whether the previous wave's declared deviation gets repeated.

---

> ## CORRECTION, added after the measurement — §1(b) IS FALSE AT HEAD, and `RULE W29-S` says so
>
> **This design's headline is half wrong, the wave's own rule refuted it, and the pre-registration text below
> is left EXACTLY as written so the error is visible rather than tidied away.**
>
> **`RULE W29-S` = `S-CARRIER-EXISTS`** (`/mnt/d/hf_w29/w29s_score.txt`): `S-a` **HOLDS**, `S-c` **HOLDS**,
> **`S-b` is FALSE**. A product carrier *does* exist.
>
> §1(b)'s caller table names **two** of `BuildSummaryAsync`'s **five** callers. The full set, discovered by the
> instrument rather than typed: `StartAsync`, `ReOptimizeWithLabelsAsync`, `ContinueOptimizationAsync`,
> `OptimizeAgainAtRecommendedBinningAsync`, and — the one that matters —
> **`CaptureNewSweepAsync`** (`StarDetectionOptimizerWizardVM.cs:5012`), which takes a **fresh sweep**
> (`RunLiveAttemptAsync` per run), calls `BuildSummaryAsync` at `:5098`, and **carries the previous round's
> recommended geometry into the next sweep** through the instance field `recaptureStepSize`.
>
> So the sentence *"No new sweep is taken between wizard rounds, so `SearchSpan` cannot change between them and
> the defect cannot occur there at all"* is **false**, and with it the claims that fix (1) *"reaches no user"*
> and that F82 *"scores goal 2 zero"*. **F82 is a real product defect on a real product path.**
>
> **The controller repeated the false claim in the wave-29 pre-registration commit message** (`544f2b2`), which
> cannot be edited; it is corrected here, in the PR, and in the results document.
>
> **What survives, and it is the larger half:** §1(a) — the truth-model coupling — is `S-a`, and it **HOLDS**.
> The harness still scores the recommender against a fixed point of the recommender. And the wave's refusal to
> flip wave 26's choice still stands, now for a better reason than it was written for: `RULE W29-R` =
> **`R-STANDS`**, so the reversal condition is not met on evidence, not merely unexamined.
>
> **The instrument was written to the pre-registration and NOT tuned to this result.** `S-b` was implemented
> exactly as §4 fixes it; it returned FALSE on its own terms. That is a pre-registration working.

## 1. THE HEADLINE: the pre-registered fix has no carrier in the product, and the "truth" it is scored against is produced by the code under test

Two facts, both decidable from source at zero compute, both verified in this document against shipping files:

**(a) `terminal.stepBehavioral` — the number [F82](followups.md) calls "a truth of 9" — is
`StepSizeRecommender.Recommend` iterated to a fixed point.** `SynthValidateRunner.ComputeStepBehavioral`
(`:1213-1270`) builds an analytic curve, fits it, and calls `StepSizeRecommender.Recommend` at **`:1262`**,
looping until `rec.StepSize == step`. Its own XML doc says so in the file: *"the fixed point A3 compares
against is produced by the same rule as the landing it scores."* **The harness scores the recommender against
a fixed point of the recommender.** That is why F82 observed `stepBehavioral` moving 8.0 → 9.0 across wave
25's two arms while it was identical on the other 17 cells: wave 25 changed `Recommend`, so it changed the
bar as well as the measurement. F82 recorded the symptom and called it *"an assumption this series can no
longer make for free"*; **the cause is one line and nobody has looked at it.**

**(b) No caller in the product has anywhere to put a previous round's floored half-width.** The pre-registered
fix (wave 26 design §14) is *(1) the monotone floor — carry the previous round's floored half-width forward as
a lower bound on the next round's `maxHalfWidth`*, chosen partly because it is *"confined to the caller's round
state, which is where a cross-round property belongs."* The callers, enumerated:

| caller | file:line | has cross-round sweep state? |
|---|---|---|
| `StarDetectionOptimizerWizardVM.BuildSummaryAsync` | `:4117` | **No.** `Continue` (`:4850`) and `Re-optimize` (`:4740`) both call `LoadRunStampedAsync(folder, …)` and re-optimize **the frames already captured**. No new sweep is taken between wizard rounds, so `SearchSpan` cannot change between them and the defect cannot occur there at all |
| `OptimizationDiagnosticRunner` (`optimize`) ×4 | `:1102`, `:1134`, `:1262`, `:1702` | **No.** Single-pass, all four strictly after `OptimizeAsync` returns at `:916` |
| `TiltCalibrationRunner` | `:479` | **No.** Single-pass |
| `SynthValidateRunner.RunRoundAsync` | `:792` | **YES** — `ScenarioRunState` (`:360-371`), and it is a **test harness** |
| `SynthValidateRunner.ComputeStepBehavioral` | `:1262` | the truth model, per (a) |

And nothing persists a half-width across sessions: `OptimizedStarDetectionSettings` carries
`RecommendedStepSize` (`:69`) and no half-width; `OptimizationSummary` carries `RecommendedStepSize`,
`StepSizeWasCapped`, `StepSizeWasBandFloored`, `StepSizeSampledHfrRange`, `StepSizeCappedGrowthRatio`,
`StepSizeDegenerateReason` — **and no `HalfWidth` at all.**

**So the real product's "next round" is the next SESSION**, and the only carrier for a monotone floor across
sessions would be new persisted state. **Fix (1), implemented as pre-registered, is a change to
`SynthValidateRunner.ScenarioRunState` and reaches no user.**

**What this does and does not do to wave 26's pre-registered choice.** It impeaches reason (iii) —
*"confined to the caller's round state"* — because the caller with round state is the harness. It leaves
reason (i) — blast radius — standing and **quantified for the first time**: §5.1 measures that **37 of 37
rounds across 18 paired cells are capped-or-floored**, so fix (2) moves every round of every cell in the
population, not merely "every capped round". And fact (a) supplies a **fourth reason wave 26 never had**:
fix (2) changes `SearchSpan`, which `ComputeStepBehavioral` also consumes, so **fix (2) moves the truth model
as well as the landing**, while fix (1) — a new parameter defaulting to `NaN` — does not.

**Two of wave 26's three reasons for (1) are impeached; the third is confirmed and a fourth is added. That is
not a licence to flip, and wave 29 does not flip it.** What it is, is a demonstration that the choice was made
without knowing where the state would live — and that is a finding, registered rather than repaired.

### 1.1 The sentence this wave is not allowed to write

**Wave 29 does not ship a fix to F82 and does not decide between (1) and (2) on new grounds.** It executes
what wave 26 already pre-registered and nobody has run — the **reversal condition** (§5) — publishes the
source rule above as a rule (§4), and hands the item forward re-priced with a third candidate registered but
**not chosen** (§4.4). Choosing a fix in the same wave that discovers a new reason to prefer it is the
[F14](followups.md) / `RULE D20` fence in a new costume.

---

## 2. WHERE THE CONTROLLER'S FRAMING IS WRONG

### 2.1 "F82 … scores goal 2 ✓✓✓" — **not as pre-registered, it scores goal 2 ZERO**

Goal 2 is *accuracy of step-size recommendations* — the recommendation a **user** receives. Fix (1) lands in
`SynthValidateRunner`, which no user runs. Worse: shipping it there alone would make the harness model a
product that does not have the fix, so `synth-validate` would stop reproducing the product's own stall.
**A harness that diverges from the product in the direction of the product being better is not an
improvement, it is a broken instrument.** Wave 28 §12 scored this item `2 ✓✓✓`; on the code as it stands the
honest score is **0 until a product carrier is chosen and priced**, and choosing one is a design question, not
an execution question.

### 2.2 "Priced in the handoff at ~45 m code + tests, ~10 m 3-cell re-run" — **the price is for the wrong thing**

45 m is the price of the harness change. The product change is a new persisted value on the optimizer's
recommendation, surviving between wizard sessions, with the `Continue`/`Re-optimize` paths (where it is inert)
explicitly excluded, plus the migration and — per `CLAUDE.md` — the question of whether it is an *option* and
therefore owes a control in `Resources/OptionsDataTemplates.xaml`. **That is not 45 m and nobody has
priced it.** §11 prices it as a band and says it is a band.

### 2.3 "F82's fix touches `StepSizeRecommender`, which IS plugin code, so the intersection is probably non-empty and the gate probably IS owed" — **not "probably". CERTAINLY, and it is already on disk by name**

`Q27-V1`'s FAIL end was not a hypothetical. It was **run**, against the B14 tree, and
`/mnt/d/hf_w27/q27_v1.txt` records the two files that made the intersection non-empty:

```
intersection is NON-EMPTY (2 files), e.g.:
Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/StarDetectionOptimizerWizardVM.cs
Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/StepSizeRecommender.cs
>>> FAIL END DEMONSTRATED: the same predicate REFUSES on a diff that reaches the gate.
```

**Wave 27 used F82's own two files as its canonical example of a diff that reaches the gate.** The reachable
set is the literal ERE `^Joko\.NINA\.Plugins/Joko\.NINA\.Plugins\.HocusFocus/|^Joko\.NINA\.Plugins/TestApp/(OptimizationDiagnosticRunner|HarnessSettingsStore|OptimizationRunDiscovery|ParamsDump|StubBehaviorSelectors|DiagnosticUtil|Program)\.cs$`
— a **directory prefix over the whole plugin assembly**, not a call graph. `StepSizeRecommender.cs` sits under
it. **Any wave that ships F82 in the product owes the full 42 m gate, and there is no argument left to have.**

**And the argument a later wave will be tempted to make must be refused in advance.** `StepSizeRecommender` is
genuinely *not* reachable from `optimize`'s objective — the evaluator handed to `OptimizeAsync` is built by
`RunEvaluationData.CreateEvaluator` (`:929-953`), whose body is `results.Add(await run.EvaluateAsync(p,
token))`, and every one of the seven `Recommend` call sites is strictly downstream of `OptimizeAsync`
returning. Wave 25 *measured* this: it shipped a change to `StepSizeRecommender.cs` and the gate returned
`FinalJ` bit-identical to K8, **8 of 8 at sixteen digits**. **None of that licenses narrowing a pre-registered
reachable set after seeing which files your own change touches.** It licenses one thing only: the gate becomes
**a rule with a predicted answer** — predict `G-PASS`, 8 of 8 bit-identical, on wave 25's measurement — which
the charter calls a virtue (`RULE R24`) because it is the only kind of rule that can be wrong.

### 2.4 "Item 2 … needs no product change and reuses wave 28's arm shape … say whether it can reach a verdict at all, or whether it can only rank" — **it can reach a verdict, if it runs one more cell than the controller specified**

Opening one gate at a time and differencing gives, per gate, the number of stars that gate **exclusively**
blocked — the ones that pass every other gate. It does not decompose the multiply-blocked set, so a
one-at-a-time design **ranks and lower-bounds, and cannot attribute.** Adding a single cell with **all three
gates opened together** measures the total recoverable set, and then

> joint (multiply-blocked) share = total − Σ(exclusive shares)

**exactly.** Four intervention cells plus a baseline turn a ranking into a decomposition. §6 pre-registers it
that way, with the prediction that the singles will sum to well under the joint total — because a 0.7 px blob
on a 19.4″/px rig is simultaneously too small, too distorted and low-signal, and the gates are not
independent.

### 2.5 "[F22](followups.md) bounds the payoff … weigh whether recall is even the right objective there" — **agreed, and it is why item 2 is re-framed rather than dropped**

The controller is right and the consequence is larger than the controller drew. `D01` reaches `BestJ`
0.994825 from a `BaselineJ` of 0.000000; below ~1.1 px the measured HFR over-reads by up to **+234 %**; its
0.231 px optical vertex measures 0.77 px. **Recovering `D01`'s recall cannot be justified as a focus
improvement and wave 29 does not justify it that way.** What survives F22 untouched is the **goal-3** question:
`MinimumStarBoundingBoxSize` landed at **6** on the rigs with the smallest stars, against a shipped default of
**5** (`IStarDetector.cs:435`) and a `WideField` preset that *lowers* it. That is a parameter pinned against
the physics, and *"avoiding parameters pinned to extreme values"* is goal 3 verbatim. **Item 2 runs as an
attribution of which gate binds — a goal-3 question — and its results section is forbidden from quoting a
recall gain as a benefit.**

### 2.6 "Two waves running, the verdict tree shipped with uncovered regions ([F94](followups.md))" — **agreed, and a sentence is again not the remedy**

Wave 28's design §12 wrote the standing rule and wave 28 shipped a tree with 4 of 135 regions uncovered.
Writing the sentence a third time will not work either. §10 of this document **enumerates every region of
every tree in a table, in this document**, and every scorer re-derives that enumeration in code and prints
`regions enumerated: N  uncovered: 0`. **Where a region is logically unreachable, it is named, asserted empty,
and given a refusal verdict** — the failure mode F94 describes is a region silently absent, and a region that
is present-and-impossible is a different, checkable thing.

---

## 3. THE ITEM, AND WHY THE OTHERS LOSE

**Chosen: item 1 ([F82](followups.md)), re-scoped from "ship the fix" to "execute the pre-registration nobody
ran, and publish what source says about the choice" — plus item 2 as a co-equal second half.**

| candidate | verdict | why |
|---|---|---|
| **1 — F82** | **TAKEN, re-scoped** | Its pre-registered **reversal condition has never been evaluated** and can be evaluated at zero compute on 15 blind cells. Writing the code before checking the escape clause is executing a pre-registration with its own reversal ignored. And §1 says the fix as written does not reach a user, which the wave must publish before anyone spends 45 m on it |
| **2 — localise `D01`'s residual loss** | **TAKEN**, re-framed to goal 3 | Cheap (no build, one binary already on disk, ~25 m), finishes wave 28's named biggest open question, and with a fifth cell it **attributes** rather than ranks. Re-framed per §2.5: the deliverable is which gate binds, not a recall gain |
| **3 — re-register the band's lower edge** | **REJECTED** | It produces a *rule*, not a *finding*. `B-SPLIT` already delivered the substantive answer — the lower edge is not where the design drew it, `Δacc > 0` on all 17. Re-scoring buys a boundary number on a synthetic-only population with **no real under-sampled dataset** ([F35](followups.md), [F43](followups.md)). It is worth ~10 m as a note on `docs/synthetic-af-bank-results-table.md`, and that note is owed anyway (wave 28 §9.5). It is not worth a rule |
| **4 — a `NoiseClippingMultiplier` default change** | **REJECTED for wave 29** | [F93](followups.md) says plainly it is not licensed by `N-RECOVERS`; it is explicitly behind item 2; it owes a fresh **~1.7 h** `optimize` baseline because the optimizer *chose* the landed values; and its measured payoff on `D01` is **+0.012**, which is exactly the number item 2 exists to explain. Running it before item 2 is proposing a default change on the strength of a dataset where the change does almost nothing |

**Is there nothing here worth a wave?** That answer was seriously considered and is rejected, but narrowly.
The wave produces: a source rule that changes what F82 is (§4), the execution of a two-wave-old
pre-registration on a blind population of 15 (§5), and a decomposition that closes wave 28's largest open
question (§6). **Three register entries, no product risk, ~25 minutes of compute.** What it does *not*
produce is a ship, and the honest reading of §1 is that **a wave that shipped fix (1) this afternoon would
have shipped it into a test harness and scored it against a bar computed by the code it changed.**

---

## 4. `RULE W29-S` — the carrier and the truth model, decided from source. Zero compute

**Population:** four source files at `HEAD`, each recorded by **sha256** in the scorer's output:
`TestApp/SynthValidateRunner.cs`, `HocusFocus/StarDetection/Optimization/StepSizeRecommender.cs`,
`HocusFocus/StarDetection/Optimization/StarDetectionOptimizerWizardVM.cs`,
`HocusFocus/StarDetection/Optimization/OptimizedStarDetectionSettings.cs`.
**Wave 28 §7.5's lesson applied in advance:** design §4 of that wave named one artifact where its clause
needed two, and the scorer had to say so. Here the population is named as four and the scorer **refuses** if
any is absent.

| clause | statement | refusal condition |
|---|---|---|
| **`S-a`** *(validity, and it is IN the tree)* | `ComputeStepBehavioral`'s body contains **exactly one** `StepSizeRecommender.Recommend(` call site, **and** `ComputeStepBehavioral` is the **only** writer of the value serialized as `terminal.stepBehavioral` | any count ≠ 1; more than one writer; the method cannot be located by **declaration** (not first textual occurrence — wave 28 §7.9) |
| **`S-b`** | Over every `Recommend(` call site under `Joko.NINA.Plugins.HocusFocus/`: **none** sits in a method whose enclosing round loop captures a new sweep, **and** neither `OptimizedStarDetectionSettings` nor `OptimizationSummary` declares a member matching `(?i)halfwidth` | a product caller with a re-capturing round loop; any `halfwidth` member on either type |
| **`S-c`** | `SweepDetectability` declares a member carrying the **requested** focuser positions, and it is reachable inside `Recommend` | no such member |

**Verdicts.** `S-a` false ⇒ `S-UNEVALUATED` (the coupling could not be established; nothing downstream in this
rule is quoted). `S-a` ∧ ¬`S-b` ⇒ `S-CARRIER-EXISTS` — a product carrier was found, wave 26 reason (iii)
stands, and §1's headline is **withdrawn in the results document, in place.** `S-a` ∧ `S-b` ∧ `S-c` ⇒
`S-NO-CARRIER`. `S-a` ∧ `S-b` ∧ ¬`S-c` ⇒ `S-NO-CARRIER-NO-ALT`.

**Both branches of every clause are reachable and the FAIL ends are demonstrated on real files**, not
fixtures: `S-a`'s zero-site end on `StepSizeRecommender.cs` itself (which contains no such call), its
multi-site end on `SynthValidateRunner.cs` scoped to the whole file (**two** sites, `:792` and `:1262`);
`S-b`'s carrier end on `StepSizeRecommendation`, which **does** declare `HalfWidth`; `S-c`'s absent end on
`StepSizeRecommendation`, which declares no focuser positions.

### 4.4 The third candidate — REGISTERED, PRICED, AND NOT CHOSEN

If `S-c` holds, a fix exists that wave 26 never considered, because it needs no cross-round state at all:
**bound `maxHalfWidth` below by the span the sweep actually REQUESTED, and only on a round where
`BandDemonstrablyUnsampled(sampledHfrRange)` is already true.** The requested positions are in
`SweepDetectability.FrameFocuserPositions`; the guard is the same predicate F81 already ships; and it is inert
on every round that reached the band, which is where the cap is *the* convergence mechanism.

**It is proposed from source and it is NOT chosen in this wave.** Wave 26 pre-registered a binary choice
before anyone looked; introducing a third option after reading the diagnosis and then selecting it in the same
breath is exactly the shape the `RULE D20` fence forbids. It is registered with its price (§11) and the
**next** wave pre-registers the choice among three.

---

## 5. `RULE W29-R` — wave 26's reversal condition, executed. Blind on 15 cells, zero compute

Wave 26 design §14, quoted, and this is the whole of what the rule tests:

> **And the condition that would reverse it**, stated now: if a later wave measures that `SearchSpan` over
> `bestFit.Inputs` **shrinks on more than a single cell** while the requested sweep widens, then the defect is
> in the quantity and not in its persistence, and (2) becomes correct.

**Nobody has run it.** Three waves have quoted the pre-registered choice and none has evaluated its escape
clause. It costs nothing.

### 5.1 The instrument — exact, on data already on disk, needing no field that is not serialized

`maxHalfWidth = MaxHalfWidthSampledHalfSpanMultiple × 0.5 × SearchSpan` (`StepSizeRecommender.cs:320`), and
`maxHalfWidth` is **not** serialized. It does not need to be. On any round where the cap bound
(`:347-349`, `halfWidth = maxHalfWidth`) **or** the floor engaged (`:332`, `:364-365`, `halfWidth =
maxHalfWidth`), the serialized `stepRecommendation.halfWidth` **is** `maxHalfWidth` exactly, so

```
implied SearchSpan(round)  =  halfWidth / (MaxHalfWidthSampledHalfSpanMultiple × 0.5)   =  halfWidth / 0.75
requested span(round)      =  2 × bootstrap.offsetSteps × bootstrap.stepSize
```

Validated against the one burned cell before the instrument was written: `D01` r0 floored `halfWidth` 12.0 ⇒
implied 16, requested 2×4×2 = 16; r1 capped `halfWidth` 9.0 ⇒ implied 12, requested 2×4×3 = 24. **F82's
published table reads *"requested 16 → 24, fitted 16 → 12"*. The identity reproduces on the nose.**

**The trap that must not be silent, and it is checked before any statistic:** there is a **third** bound.
`WasDetectBounded` / `MaxUsefulHalfSpan` (`:368-379`) can also set `halfWidth`, in which case
`halfWidth ≠ maxHalfWidth` and the inversion is wrong. **A round with `wasDetectBounded == true` is a
could-not-look**, named and counted, never silently included. A round that is neither capped nor floored is
likewise a could-not-look. Measured at pre-registration over the 18 addressable cells: **37 rounds, 37
capped-or-floored, 0 detect-bounded.** The instrument is applicable everywhere.

**And `MaxHalfWidthSampledHalfSpanMultiple = 1.5` is asserted from source at BOTH trees** — wave 25's B15 tree
(`29665a62…`, read out of `/mnt/d/hf_w25/binary_provenance_w25.txt`, never assumed) and `HEAD` — because the
inversion is only valid if the constant that produced the artifacts is the constant the scorer divides by.
A mismatch is `R-UNSATISFIABLE`, not a silently rescaled number.

### 5.2 Population, and what is already known about three of its members

**Population: the 18 addressable paired S1 cells under `/mnt/d/hf_w25/after/`.** 20 datasets less
`D05_tec140_1000mm` and `D19_cygnus_deep_shed`, which time out at `timeout 600` on **every** S1 arm — four in
a row, structural, not flaky. **The count is asserted and the gap is asserted to be exactly those two names**
([F74](followups.md); wave 24's `40` vs `38`). Every cell has ≥ 2 rounds; `D08` has 3.

`D01`, `D02` and `D03` are **burned** — their rounds are published in F82's own table — and they are **kept in
the denominator and computed anyway**, because dropping a member because you know its answer is how a
denominator becomes a choice. Their contribution is therefore **predicted here, before the scorer runs**:

| burned cell | published `halfWidth` r0 → r1 | implied `SearchSpan` | qualifies? |
|---|---|---|---|
| `D01_ultrawide_40mm` | 12.0 → 9.0 | 16 → 12, **shrank** | **YES** (it is the cell the condition was written from) |
| `D02_rich_135mm` | 12.0 → 18.0 | 16 → 24, grew | no |
| `D03_redcat_250mm` | 24.0 → 42.0 | 32 → 56, grew | no |

**So `c ≥ 2` holds if and only if at least one of the 15 BLIND cells qualifies**, and the rule is a clean
binary question on a population nobody has computed this statistic over. The scorer **prints both** `c` (over
18, as the condition is written) and `c_blind` (over 15), so no reader has to do that arithmetic — and the
verdict is taken on `c ≥ 2` **as pre-registered by wave 26**, not on a rate this wave invented.

### 5.3 The clauses

| clause | statement | bar |
|---|---|---|
| **`R-0`** *(validity, IN the tree)* | addressable cells `k`, each with ≥ 2 rounds and every read round capped-or-floored and not detect-bounded; **and** `MaxHalfWidthSampledHalfSpanMultiple == 1.5` at both trees | `k ≥ 2` |
| **`R-1`** | `c` = distinct cells with ≥ 1 round transition `r → r+1` where `requested(r+1) > requested(r)` **and** `impliedSearchSpan(r+1) < impliedSearchSpan(r)` | `c ≥ 2` |

**`R-0` false ⇒ `R-UNSATISFIABLE`, and that is a FINDING, not something to repair and re-score.** The
pre-registered default then stands: fix (1) remains the choice, because wave 26 named `n = 1` as insufficient
and an unevaluable condition does not become an evaluated one.

**Aggregation, stated because [F68](followups.md) has five parts.** Population = the report file
`synth_validate_report.json` per cell; field = `datasets[0].scenarios[0].rounds[i].stepRecommendation.halfWidth`
and `.bootstrap.{offsetSteps,stepSize}` — **the per-round copies, never the `terminal` block**, and the scorer
asserts it consumed exactly one `stepRecommendation` per round index. Statistic = per transition. Aggregation
= **any transition qualifies the cell; cells are counted, not transitions**, because wave 26 wrote "cells".
Empty-set answer = `R-UNSATISFIABLE` per `R-0`.

### 5.4 What each verdict costs, fixed before the data

- **`R-STANDS`** — fix (1) remains correct **on wave 26's grounds**, and §1's finding stands beside it
  unchanged: it still has no product carrier. F82 is re-priced and handed forward with three candidates.
- **`R-REVERSE`** — fix (2) becomes correct by wave 26's own pre-registration, **and wave 29 does not ship it.**
  It moves `maxHalfWidth` on **37 of 37 rounds**, and — per §1(a) — it also moves `ComputeStepBehavioral`, so
  it moves assertion `A3`'s bar as well as `A3`'s measurement. Its price is a full paired 18-cell arm
  (~2 h 40 m), the **42 m gate** (§8), and an `A3` re-derivation. **That is not a wave-29 ship and saying so is
  the rule working**, in the same way wave 28's ship (2) did not ship.

---

## 6. `RULE W29-L` — which of `D01`'s three downstream gates actually binds. One arm, no build

Wave 28 converted 34 060 `NO CANDIDATE` into 33 298 gate rejections at `NC = 1.0` and **said, correctly, that
it had not localised them**: the gates fire in sequence — `TooSmall` (`StarDetector.cs:1757`) → `OnBorder`
(`:1766`) → `TooDistorted` (`:1807`) → `Degenerate` (`:1823`) → `LowSensitivity` (`:1854`) — so the published
counts are **first-rejection-wins** and are an ordering, not shares.

**Design: five cells on `D01` alone, `NoiseClippingMultiplier` held at 1.0 throughout, one knob relaxed per
cell plus one cell with all three relaxed.** No build. Binary: `/mnt/d/hf_w25/exe`, `TestApp.dll` sha256
`2fb0c8fd…` — the same B15 that produced `table18` and wave 28's arm. Settings tree copied per cell from the
same source wave 28's arm used, one field edited, `cp` **without** `-p` and `touch` after every copy
([F89](followups.md)).

| cell | edit, against the landed tree with `NoiseClippingMultiplier = 1.0` |
|---|---|
| `L0` **baseline** | `NC = 1.0` only — **must reproduce wave 28's published `D01` high-tier block exactly** |
| `L1` | + `MinimumStarBoundingBoxSize` 6 → **3** |
| `L2` | + `MaxDistortion` 0.5 → **0.9** |
| `L3` | + `Sensitivity` relaxed one notch — **diagnostic only, see below** |
| `L4` | + all three of `L1`/`L2`/`L3` together |

**`L3` is diagnostic and proposes nothing on the Sensitivity axis.** [F84](followups.md) is re-affirmed, not
touched: *"a floor on the Sensitivity axis alone is dead"*, `RULE S16` is a permanent fence, and wave 28
already recorded that `D01`'s `LowSensitivity` rise is a **downstream consequence** of more candidates
existing. You cannot attribute three sequential gates by opening two, so the third cell is necessary and its
role is bounded here, in writing: **no verdict branch reads `L3` alone, and no register entry produced by this
wave may cite it as evidence for or against a sensitivity bound.**

### 6.1 The statistic and the decomposition

Read **only** the `FALSE-NEGATIVE ATTRIBUTION, HIGH TIER ONLY` block, anchored on that header
([F68](followups.md) part 5 — the same line appears twice per report and wave 28's self-test [3] proved a
first-match reader takes the wrong one). For each cell, `FN_total` = the block's total high-tier false
negatives.

```
exclusive(g)  =  FN_total(L0) − FN_total(L_g)                for g in {MinBox, MaxDistortion, Sensitivity}
joint         =  FN_total(L0) − FN_total(L4)
```

`exclusive(g)` is the count of stars that gate `g` blocked **and no other opened gate did**; `joint` is the
total recoverable across all three. `joint − Σ exclusive(g)` is the multiply-blocked residual, **exactly**.

| clause | statement | bar |
|---|---|---|
| **`L-0`** *(validity, IN the tree)* | all 5 cells `exit=0`; each echoes its intended knob values on the `key detector knobs:` line; **and `L0` reproduces wave 28's published `D01` `NC = 1.0` high-tier attribution block field-for-field** | all |
| **`L-1`** | `Σ exclusive(g) < 0.60 × joint` | — |
| **`L-2`** | `Σ exclusive(g) ≥ 0.90 × joint` | — |

`L-1` and `L-2` are mutually exclusive by construction and the scorer **asserts** it.

**Verdicts.** ¬`L-0` ⇒ `L-UNEVALUATED`. `L-0` ∧ `L-1` ⇒ **`L-JOINTLY-BOUND`** — no single gate binds; `D01`'s
residual loss is multiply-gated and **no single-knob change can recover it**. `L-0` ∧ `L-2` ⇒ `L-SEPARABLE` —
the gates are near-independent and the largest `exclusive(g)` names the binding one. Otherwise ⇒ `L-PARTIAL`,
with the shares published and no gate named.

**The prediction, stated before the data so the rule can be wrong: `L-JOINTLY-BOUND`.** A 0.7 px kernel on a
19.4″/px rig produces a candidate that is simultaneously below the bounding-box floor, noisy enough in its
second moments to read as distorted, and marginal in signal. If instead `L-SEPARABLE` comes back naming
`MinimumStarBoundingBoxSize`, that is a **goal-3 product finding** — a knob landed at 6 against a shipped
default of 5 on the rig with the smallest stars, with a `WideField` preset that lowers it — and it is worth
its own wave.

**`L0` is a control that can fail, and its failure end is real:** it re-runs a cell whose numbers are
published to the digit. Wave 28's §4.3 found the equivalent control *"a stronger control for this arm than
the `--profile-id` inertness probe"*, and unlike that probe it is pre-registered here rather than
opportunistic.

---

## 7. BLINDNESS LEDGER — stated once, exactly, before any wave-29 statistic

**READ, and by whom:**

| what | by whom | scope of the burn |
|---|---|---|
| `D01`/`D02`/`D03` per-round `halfWidth`, `stepSize`, `wasCapped`, `wasBandFloored` | [F82](followups.md)'s published table, and §5.2 above | those 3 cells, `W29-R`'s statistic |
| `roundIndex`, round count, `wasCapped`, `wasBandFloored`, `wasDetectBounded` — **all 18 cells** | **this document**, §5.1's applicability count | **none of `W29-R`'s statistic** — see below |
| `D01`'s full `golden eval` block at `NC` landed and `NC = 1.0`, incl. the high-tier attribution | wave 28 §1.1, and §6 above | `W29-L`'s **baseline half** |
| the `synth_validate_report.json` **schema** (key names only, `D01`) | this document | none |

**Why reading the three flags on all 18 costs no blindness.** `wasCapped` / `wasBandFloored` were already
published as aggregates by wave 25 (`RULE N25` `A4` **18 of 18**; `RULE W25` `W-UNEXERCISED`, no blind cell
floored), so they were burned two waves ago. They are `W29-R`'s **applicability filter**, not its statistic:
they decide *whether a round can be read*, and `halfWidth` / `bootstrap` decide *what it says*. **Reading which
rounds are addressable is how a population is asserted; reading the values is how a rule is pre-empted.** This
table exists so a later reader can check that the wave did not blur them. **`halfWidth`,
`bootstrap.offsetSteps` and `bootstrap.stepSize` have been read on `D01` only** (published), and the
requested-vs-implied comparison has **never** been computed on any other cell.

**BLIND, and carrying `W29-R`'s verdict: the 15 paired S1 cells other than `D01`/`D02`/`D03`.**

**`W29-L` has no blind population and this is said plainly.** Its baseline is published to the digit and its
verdict is a **difference** against it; the four intervention cells do not exist yet. The honesty mechanism is
therefore not blindness but **prediction**: §6 fixes `L-JOINTLY-BOUND` as the expected answer, with its
mechanism, before the arm runs. **Wave 28 had to relocate its verdict to the frame level because the bank
could not supply a blind wide-field dataset; wave 29 cannot relocate `W29-L` anywhere, so it says so instead
of claiming a blindness it does not have.**

---

## 8. THE GATE — answered in both directions, and it is NOT owed this wave

**Wave 29 changes no C# and builds no binary.** Wave 26's four conditions for an honest skip therefore all
hold: nothing is compiled; `G25` already answered what a re-gate asks; a re-gate does not reach this wave's
risk (which is a source reading and two artifact re-reads); and **no clause in any verdict tree in this
document reads a gate landing.** *A control that no clause consumes is a ritual.*

**The three conditions that reverse it are checked at Step 8 and any one of them stops the wave:** a single
line of C# changing for any reason; a hash or `BuildId` mismatch on the binary the `W29-L` arm uses; or that
arm's own instrument proving non-deterministic. The replacement control is `Q26-V0`'s shape — dll sha256
identity of `/mnt/d/hf_w25/exe` against `/mnt/d/hf_w25/binary_provenance_w25.txt`, with `L0`'s exact
reproduction of a published population as the arm-level control that *can* fail.

**And the answer the controller asked for, for the record and for the next wave: if wave 29 had shipped
F82's fix, the gate would be owed, certainly and not probably.** §2.3 has the artifact. The intersection is
non-empty by a directory prefix over the whole plugin assembly, `Q27-V1`'s own FAIL end names
`StepSizeRecommender.cs` by path, and **`Q27-V1` does not discharge it.** Price when it comes due: **42 m**
measured (wave 25: 42 m 43 s, ~5.2 m/run, seven consecutive waves), predicted verdict `G-PASS` 8 of 8
bit-identical to K8.

---

## 9. WHAT SHIPS — nothing, fixed here, before the data

**No product code changes in wave 29.** This is not a gated ship that failed its gate; it is a scope decision
made in the pre-registration on the strength of §1, and it is recorded so that no later reader mistakes it for
a verdict.

| candidate ship | status |
|---|---|
| F82 fix (1), harness | **DOES NOT SHIP.** §1: it reaches no user, and shipping it would make `synth-validate` stop reproducing the product's own stall |
| F82 fix (1), product | **DOES NOT SHIP.** Needs new persisted state; unpriced by anyone; §11 gives a band |
| F82 fix (2) | **DOES NOT SHIP**, on either verdict of `RULE W29-R`. §5.4 |
| F82 fix (3), the scoped requested-span bound | **DOES NOT SHIP.** Proposed from source in §4.4, deliberately not chosen in the wave that proposed it |
| anything on the `NoiseClippingMultiplier` or `Sensitivity` axes | **DOES NOT SHIP.** §3, §6 |

**Consequence:** no C# changes, so **the unit suite is not run**, and that is stated rather than skipped
silently. `CLAUDE.md`'s *"run the suite after every code change"* is satisfied vacuously and the condition that
reverses it is written into the plan: **if a single line of C# changes for any reason, the suite runs by COUNT
([F37](followups.md)) and the gate becomes owed (§8).** The baseline to compare against is **3997** — wave 28's
measured close — **not the charter's stale 3973** (wave 28 §7.8: the design quoted a figure two waves old and
would have hidden a 19-test regression).

---

## 10. VERDICT TREES, ENUMERATED — [F94](followups.md)'s remedy, executed rather than restated

Every clause appears in its own tree, **including every validity clause**. Every scorer re-derives these
enumerations in code and prints `regions enumerated: N   uncovered: 0`; a gap prints `<RULE>-TREE-GAP`,
enumerates the uncovered regions, prints what this document's tree would have said, and **does not repair and
re-score**.

**`RULE W29-S` — 3 clauses, 8 regions, 0 uncovered**

| `S-a` | `S-b` | `S-c` | verdict |
|---|---|---|---|
| F | * | * | `S-UNEVALUATED` (4 regions) |
| T | F | * | `S-CARRIER-EXISTS` (2 regions) |
| T | T | T | `S-NO-CARRIER` (1) |
| T | T | F | `S-NO-CARRIER-NO-ALT` (1) |

**`RULE W29-R` — 2 clauses, 4 regions, 0 uncovered**

| `R-0` | `R-1` | verdict |
|---|---|---|
| F | F | `R-UNSATISFIABLE` — a FINDING; wave 26's choice stands by default |
| F | T | **logically unreachable** (`c ≥ 2` ⇒ `k ≥ 2`). Present, asserted empty, and entering it is `R-INCOHERENT` — a refusal |
| T | F | `R-STANDS` |
| T | T | `R-REVERSE` |

**`RULE W29-L` — 3 clauses, 8 regions, 0 uncovered**

| `L-0` | `L-1` | `L-2` | verdict |
|---|---|---|---|
| F | * | * | `L-UNEVALUATED` (4 regions) |
| T | T | T | **unreachable by construction** — asserted empty; entering it is `L-INCOHERENT` (1) |
| T | T | F | `L-JOINTLY-BOUND` — **the prediction** (1) |
| T | F | T | `L-SEPARABLE` (1) |
| T | F | F | `L-PARTIAL` (1) |

**`RULE V29` (pre-flight) and `W29-FP` (fingerprint)** inherit wave 28's trees unchanged:
`V-CLEAN` / `V-DRIFT` / `V-UNEVALUATED`, and `PRESERVED` / `VIOLATED` / `UNEVALUATED` on differing membership.

**Why the pattern F94 identified does not recur here.** F94's diagnosis was specific: *"the uncovered region
is always the VALIDITY clause"* — `N-1` was expected to hold, so it fell out of the tree. `S-a`, `R-0` and
`L-0` are all validity clauses and **all three are the first column of their own table.**

---

## 11. PRICE, per half, scored against the owner's three goals, and what is cut first

Goals (charter §8): **(1)** optimization + autofocus across a wide range of setups; **(2)** accuracy of
step-size recommendations; **(3)** appropriate exposure adjustments, no parameters pinned to extremes.

| step | est | compute | goals |
|---|---|---|---|
| 0 vessel + pre-registration commit | 10 m | — | — |
| 1 write **all** instruments (5 files) before any of them runs | 60 m | — | — |
| 2 `RULE V29` pre-flight, **over the populated root** ([F95](followups.md)) | 20 m | — | instrument |
| 3 `W29-FP` fingerprint BEFORE | 8 m | — | instrument |
| 4 `RULE W29-S` | 25 m | — | **2** ✓✓ (it decides what F82 *is*) |
| 5 `RULE W29-R` | 25 m | — | **2** ✓✓ |
| 6 `RULE W29-L` arm + scoring | 55 m | **~25 m** | **3** ✓✓, **1** ✓ |
| 7 [F83](followups.md)'s `P-D08` at `n = 3` | 2 m | ~2 m | **3** ✓ |
| 8 fingerprint AFTER + **closing** `V29` | 12 m | — | instrument |
| 9 results, register, handoff | 75 m | — | — |
| **total** | **~5 h** | **~27 m** | |

Comfortably inside both the ~8 h wall clock and the ~6 h compute ceiling. **Slack is deliberate**, because
this is the fourth consecutive wave in which the analysis halves are over-priced — wave 28 came in **~59 %
under**, wave 27 ~36 % under, and the lesson is now specific: *price compute from the measured rate and price
analysis at a third of instinct.* The one figure here priced from a measured rate is step 6: wave 28 measured
`D01` at `NC = 1.0` at **179 s per 9-frame cell**, the top of the bank's range, and 5 cells is ~15 m plus
copies and fingerprints — reserved at 25 m.

**What is cut first, in order:**

1. **`L3` and `L4`** — the Sensitivity cell and the joint cell. The arm degrades from a decomposition to a
   ranking with lower bounds, saving ~10 m of compute. **This is the cut, and it is the one that costs the
   most per minute saved**, so it is named first to be honest, not because it is cheap.
2. **The whole of `RULE W29-L`.** Wave 29 still delivers `W29-S` and `W29-R`, which are the two halves that
   decide F82's fate. `W29-L` finishes someone else's question.
3. **Step 7** — no. **An item under two minutes is never dropped** (charter §4); wave 19 finished at 1 h 35 m
   of a 6 h ceiling with a one-minute item undone and it cost `RULE C19` its verdict.
4. **Step 9's handoff document**, folded into the results file's §12 rather than written separately.

**What is NOT cut, at any budget:** the closing `V29` run (step 8). It is two minutes and it is
[F95](followups.md)'s entire remedy.

**Prices this wave hands forward rather than paying:**

| item | price |
|---|---|
| F82 fix (1) **in the harness** | ~45 m + tests. **Not recommended** — §1, §9 |
| F82 fix (1) **in the product** | **a BAND, and it is one**: ~2–4 h. New persisted half-width surviving between wizard sessions; the `Continue`/`Re-optimize` paths excluded where it is inert; migration; a `CLAUDE.md` decision on whether it is an *option* and therefore owes a control in the options templates; **+ 42 m gate** (§8) |
| F82 fix (2) | ~45 m code **+ a full paired 18-cell arm ~2 h 40 m + 42 m gate + an `A3` re-derivation**, because it moves `ComputeStepBehavioral` |
| F82 fix (3), the scoped requested-span bound (§4.4) | ~45 m code + a 3-cell re-run + **42 m gate**. Needs no new state and no `A3` re-derivation if the guard holds — **cheapest of the three, and precisely why it must not be chosen by the wave that thought of it** |
| the `A4` truth-model gap (backlog item 4, "~20 m") | **the 20 m is the code. It rebuilds `TestApp`, which trips §8's first reversal condition and adds 42 m of gate.** Naming that is itself a finding: the cheapest item on the backlog is not cheap |

---

## 12. INSTRUMENTS AND CONSTRAINTS

All under `/mnt/d/hf_w29/`, **LF**, derived from `/mnt/d/hf_w28/`.

- `verify_derivation_w29.py` — derived from `verify_derivation_w28.py`, **not** any `.bak`. **Carry forward,
  and demonstrate, all three of wave 28's repairs:** [F87](followups.md)'s `historical_ranges` fix (the
  definition line of `HISTORICAL_BLOCKS` opens nothing — bracket-depth, top-down, `is_definition` excluded);
  self-test clause **`[6]`** in both directions; and wave 28's **per-clause fixture pinning** `[5b]`.
- **Per-clause fixtures: MEASURE which roots still refuse; do not inherit wave 28's choices.** Wave 28
  established that a root pinned for its `-B` findings **goes silent exactly one wave later**, because `PREV`
  moves: `/mnt/d/hf_w26` yielded 0 `-B` at wave 28. `/mnt/d/hf_w27` was wave 28's live `-B` fixture and
  **`PREV` is now 28**, so it is a candidate to have expired in exactly the same way. **The plan measures
  `-A`/`-B`/`-C` finding counts across `/mnt/d/hf_w{24,26,27,28}` and pins from the measurement**, keeps the
  expired root asserted-silent so the expiry is demonstrated rather than described, and treats **a clause with
  no live fixture as a FAIL**. Wave 28 assumed and got lucky; wave 29 measures.
- **`--out` mandatory and REFUSING without it** on every scorer.
- **`--rule` asserted against the file's own declared rule name** ([F90](followups.md)) — a hard refusal.
  Wave 28's scorers made this a refusal and it caught the controller within a minute.
- Populations **asserted**; ordinals and counts **computed, never typed** (`V29-C`); paths handed to scorers
  through a **manifest**, never rebuilt from a template ([F74](followups.md)); one shared layout function.
- `find … -print0 | sort -z | xargs -0` — `/mnt/d/Autofocus Bank` contains a space.
- **BEFORE/AFTER built from ONE expression called twice** ([F91](followups.md)), population size asserted
  **equal** before any hash is compared, and the fingerprint written **outside** every tree it sweeps
  (wave 28 §7.10).
- `touch` after every mutant or settings restore ([F89](followups.md)); `cp` **without** `-p`.
- Provenance: `git diff --name-only <tree> -- '*.cs'` **unioned with** `git ls-files --others
  --exclude-standard -- '*.cs'` ([F88](followups.md)), and the BEFORE tree hash read out of
  `/mnt/d/hf_w25/binary_provenance_w25.txt`, **never assumed from a previous HEAD**.
- **Pass scorers WSL paths.** A Windows path yields `UNEVALUATED` and looks like a failed arm.
- `TestApp.exe` gets `< /dev/null`; `--settings` **and** `--profile-id` pinned on the arm; one `TestApp.exe`
  at a time; **no directory rebuilt mid-wave** — every instrument is written at Step 1.
- **Read-only for the whole wave:** `/mnt/d/hf_w25/{after,before,table18,exe}`, `/mnt/d/hf_w28/nc`,
  `/mnt/d/SyntheticAutofocusBank`.

**Every conclusion in this wave is synthetic-only.** The bank has no real under-sampled dataset
([F35](followups.md), [F43](followups.md)) and `W29-L` is one dataset, `D01`, named as one dataset everywhere
it is quoted.

---

## 13. WHAT WAVE 29 WILL NOT RUN, AND WHAT IT COSTS

| not run | price | why |
|---|---|---|
| any F82 fix | see §11 | §1, §9 — none of the three reaches a user at wave-29 cost |
| the 42 m `optimize` gate | 42 m | **Not owed** — no C# changes (§8). Owed, certainly, by the wave that ships F82 |
| the unit suite | ~4 m | No C# changes (§9). Reverses on one line of C# |
| a full paired 18-cell S1 arm | ~2 h 40 m | `W-UNEXERCISED`: 13 blind cells moved by exactly zero across a real product change. A second full arm buys a second `SAME 13` |
| wave 28's `M1`–`M6` mutant records | ~10 m | **Not applicable** — wave 29 ships nothing and writes no test. Wave 28's debt stays wave 28's, named in its §11 |
| `S27-1` and wave 27's three mutants | ~20 m | **STILL OPEN, two waves running.** Cut for budget; named so it is not lost a third time |
| new datasets between 1.4 and 19.4 ″/px | ≥ 1 h render | The only route to a **blind** wide-field dataset. The bank's coverage hole is unchanged |
| the blur/layer decoupling build ([F92](followups.md)) | ~45 m + 10 m (+42 m to ship) | Better-motivated than at its pre-registration, and it is a detector change owing a baseline |
| the `A4` truth-model gap | 20 m code **+ 42 m gate** | §11's last row |
| anything on real frames | — | The bank has no real under-sampled dataset |

---

## 14. REGISTER ENTRIES OWED, whatever the verdicts

- **F96** — the harness's `stepBehavioral` is `StepSizeRecommender.Recommend` iterated to a fixed point, so a
  change to the recommender moves the bar that scores it; and no product caller carries cross-round sweep
  state, so F82's pre-registered fix has no product carrier. Owed on `S-NO-CARRIER` **or**
  `S-NO-CARRIER-NO-ALT`; **withdrawn in place** on `S-CARRIER-EXISTS`.
- **F97** — `RULE W29-R`'s verdict, whichever it is, with `c`, `c_blind`, `k` and the named qualifying cells.
- **F98** — `RULE W29-L`'s decomposition, with the exclusive and joint shares, framed per §2.5 as goal 3.
- **[F82](followups.md) — AMEND IN PLACE.** Its *"truth of 9"* must be labelled as a **product-derived fixed
  point**, not a spec-derived truth; its *"confined to the recommender's caller state"* must name **which**
  caller; and the third candidate (§4.4) must be added beside the two.
- **[F81](followups.md)** — append that the floor it shipped is reached in the product only at round 0 of a
  session, because the wizard's rounds do not re-sweep.
- **[F94](followups.md)** — append §10's enumeration as the first design to publish its own outcome space,
  and whether the scorers' re-derivations agreed with it.
- **[F95](followups.md)** — append whether the ordering in the plan (instruments **then** pre-flight **then**
  measurement, plus a closing run) produced a non-empty population at the first run. **This is the wave that
  tests the remedy.**
- **[F21](followups.md)** — estimate vs actual for every step, per §11.
- **`docs/synthetic-af-bank-results-table.md`** — wave 28's owed `K` column, and **no 2–4 px band claim below
  the band** (`B-SPLIT`).
