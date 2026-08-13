# Wave 26 -- design (pre-registration)

**Written before any wave-26 measurement exists.** No `TestApp.exe`, no `dotnet build`, no `dotnet test` was run
by the agent that wrote this document. The drivers under `/mnt/d/hf_w26/` were written and **self-tested** by it
(python and bash only, no product binary), and sec 4 records exactly what those self-tests found. Everything below
is fixed before the data, and the analysis agent is bound by sec 11 and sec 13.

Predecessor: [`docs/synthetic-af-bank-followups-wave25-results.md`](synthetic-af-bank-followups-wave25-results.md).
Controller's live deviation log for wave 25: `/mnt/d/hf_w25/CONTROLLER_DEVIATIONS.md` (D1-D10).
The question this wave finishes: **RULE P23**, [wave 23 design sec 5](synthetic-af-bank-followups-wave23-design.md)
and [wave 23 results sec 5](synthetic-af-bank-followups-wave23-results.md).
Charter: [`docs/waves22+-handoff-prompt.md`](waves22+-handoff-prompt.md),
[`docs/waves22-27-autonomous-prompt.md`](waves22-27-autonomous-prompt.md).
Register: [`docs/followups.md`](followups.md).
Direct input: [`docs/synthetic-af-bank-results-table.md`](synthetic-af-bank-results-table.md), delivered to the
owner at `7e98915`.
Plan: [`plans/synthetic-af-bank-followups-wave26-plan.md`](../plans/synthetic-af-bank-followups-wave26-plan.md).

---

## 0. The vessel decision, first and in writing

**CONTINUE on `ghilios/synthetic-af-bank-followups-wave23`, PR #195 (checked OPEN at `06:29Z`, HEAD `7e98915`).**

The deciding fact is smaller than wave 25's and it points the same way: **wave 26's instrument is B15, the binary
already on disk at `/mnt/d/hf_w25/exe`, and wave 26 ships no code.** There is nothing to build, so there is no
"which commit must the binary carry" question at all. A fresh branch would buy a second review target and a merge
ordering, five hours before the owner's stop, in exchange for nothing. The wave's only commits are two documents,
a driver set and a results document.

**Contingency, pre-registered:** if the owner merges #195 before the wave commits, cut fresh from the resulting
`develop` and re-verify **by content, not by hash**, that
`grep -q "MaxHalfWidthSampledHalfSpanMultiple" Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/StepSizeRecommender.cs`
still passes. Nothing else about this wave depends on the tree, because **nothing this wave measures is built from
it.**

---

## 1. Where the framing I was given is wrong

Six corrections. The first changes the instrument, the second changes the question, the third retires a backlog
item that has already been discharged, the fourth is about the gate, the fifth is about the price and the sixth is
about the wave's motivation.

### 1.1 A one-axis perturbation of the landed vector is NOT the instrument the owner's question needs

The prompt frames the arm as *"perturbing `BrightnessSensitivity` away from the floor"* and asks whether `J`
changes. That is a legitimate measurement and it is **not the decision-relevant one**, for a reason that is in the
owner's own sentence:

> *"...and specifically to **avoid** situations where some parameters are pinned to extreme values (such as
> sensitivity at 0)"*

**"Avoid" is a constraint, and the cost of a constraint is measured by re-optimising under it, not by stepping off
the optimum and reporting that things got worse.** A landed vector is, by construction, the best point the search
found; nudging any coordinate of it is *expected* to be neutral-or-worse and says almost nothing. The question the
owner is actually asking is: **if the optimizer is forbidden to go there, what does it find instead, and what does
that cost?**

That instrument exists and it is already in the product's own CLI surface: **`--sensitivity-floor`**
(`OptimizationDiagnosticRunner.cs:163`, `:479-480`), which is injected as the `Sensitivity` axis's lower bound
(`OptimizerVariable.cs:133-136`, `:145`, `:150`, `:168-169`) and **is recorded in every landing's
`Provenance.CommandLine`** -- which is how `RULE P23` resolved the floor per landing in the first place. It is the
only per-axis bound `optimize` exposes, and it is the exact shape of "avoid this region".

So the arm is a **paired re-search** -- the same invocation with and without a raised floor -- and the one-axis
perturbation is demoted to a labelled secondary that is cheap and comes with the fixed-vector clause anyway.

### 1.2 [F6](followups.md) does not merely complicate the single axis -- it **forecloses** it, and the re-search is the answer

F6 is explicit and it was measured, not theorised:

> At clip 2 sensitivity barely matters; at clip 10 it is decisive. Raising clip *hurts* at default sensitivity and
> *helps* strongly at high sensitivity. **Neither knob does anything useful alone -- the objective surface is a
> diagonal valley, not two independent axes**, which is a concrete mechanism for the plateau ... and a hint that a
> coordinate-wise search is the wrong shape for this space.

| | `clip 2` | `clip 10` |
|---|---|---|
| **`sens 10`** | 1.648 | 2.960 |
| **`sens 33.3`** | 1.503 | **0.441** |

Two consequences, both binding on this design:

1. **A single-axis move on a diagonal valley walks up the wall.** A large `dJ` from moving sensitivity alone would
   be evidence about the *valley's cross-section*, not about whether the pin matters. So the fixed-vector clause is
   the **2x2 ablation F6 itself used** -- sensitivity alone, clip alone, both -- and a single cell of it is
   pre-declared uninterpretable on its own.
2. **The re-search is the only move that travels ALONG the valley**, because it lets every other axis -- clip first
   -- re-optimise in response to the raised floor. `RULE P23` already noted that `BrightnessSensitivity` and
   `StarClippingMultiplier` pin **9 times each, more than every other axis combined**, and that *"a coordinate-wise
   search on a diagonal valley walks to the walls"*. If that reading is right, the constrained search should be
   able to trade clip for sensitivity and land at nearly the same `J`. **That is a falsifiable prediction and it is
   what Q26-B measures.**

### 1.3 Priority 1 of the handoff backlog -- `N25-E` -- is ALREADY DISCHARGED, and the charter has not caught up

Both `docs/waves22+-handoff-prompt.md` sec 1c and `docs/waves22-27-autonomous-prompt.md` sec 7 list `N25-E` as the
cheapest open question in the series and schedule it first for wave 26. **It ran.** The controller executed it
after wave 25's write-up timestamp; the artifact is `/mnt/d/hf_w25/n25e_score.txt` and the result is published in
wave 25 results **sec 16.1**:

```
DIFFERS D08_c11_2800mm on ['converged','roundsUsed','finalStepSize','deltaStepVsExpected','overallVerdict']
paired cells: 18   identical on all 17 terminal fields: 17 of 18 = 0.9444
>>> P2 moved 1 of 18 paired S1 cells. Wave 23's 13 of 17 is formally RETIRED as uncomparable.
```

**Wave 26 owes it nothing**, and the handoff's sec 1c table must be corrected rather than obeyed. Recorded here
because a wave that spends four minutes re-running a discharged item is a wave that has not read its own state --
and because sec 13 owes the correction to the next reader.

### 1.4 The gate: SKIPPED, and the argument for skipping is not the one I was given

The prompt suggests that because B15 is already gated by `G25`, a re-gate *"certifies an already-certified
binary"*. That is true and it is the weaker half of the argument. The stronger half is this:

**A re-gate would not have established the thing this wave actually needs.** `G25` certifies that B15's `optimize`
reproduces eight `FinalJ` values bit-identically -- a statement about B15 alone. Wave 26 compares fresh B15 numbers
against **wave 18's landings, produced on B1 and B2, fifteen binaries ago**
(`BuildId e745c958...` and `7a3a03ba...`, read from the landings' own provenance). What licenses that comparison is
not B15's internal determinism; it is whether **B15's objective evaluates the same vector to the same `J` as
B1/B2 did**. No gate clause in this series has ever tested that, and a sixteenth gate would not test it either.

So the gate is replaced by three cheaper, sharper controls, each of which is a control on **this wave's own
instrument and population**:

| replaces | clause | what it establishes | FAIL end |
|---|---|---|---|
| the binary's identity | **`Q26-V0`** | the bytes on disk are `G25`'s certified binary: `sha256` of `TestApp.exe` and `TestApp.dll`, the `G25_PASSED` marker with its BuildId, and the recorded provenance file | **measured on a real different published binary** -- B14 at `/mnt/d/hf_w24/exe` must hash differently, or the comparator cannot distinguish two binaries and the accepting result is vacuous |
| the search's determinism | **`Q26-V5`** | one Q26-A cell run **twice**, `BaselineJ` bit-identical by `repr()` | the **same comparator** applied to `(base, both)` must report NOT-IDENTICAL, or it discriminates nothing |
| **the thing the gate never checked** | **`Q26-V3`** | B15's `BaselineJ` at the unperturbed landed vector vs wave 18's own recorded `FinalJ` for that landing, exact `repr()` identity | a miss is NAMED and quantified and **does not void the rule**, because Q26-A's reference is its own base cell on B15 -- it voids only the right to quote wave 18's numbers as a comparator |

**Total ~3 minutes against ~42.** If this wave shipped any product code this reasoning would be void and a full
sixteenth gate would be mandatory. **It ships none** (sec 3).

### 1.5 The price is ~1 h 40 m of critical path, not ~30 m -- and the slack is worth spending, deliberately

Wave 23 results sec 5.2 and the handoff both price *"the P23 flat-direction arm"* at **~30 m**. That price is for the
fixed-vector pass **only**. It omits the paired re-search, it omits the unconstrained side (a paired arm is two
arms -- wave 25 sec 1.1), and it omits the precision instrument. sec 12 prices every step from the same instrument and the
same scenario where a rate exists, and gives a **band** where the scenario has never been run
([F21](followups.md)'s rule).

The honest total is **~1 h 40 m** against **~4 h 15 m** available, which leaves roughly two and a half hours of
slack. **A wave that under-spends its budget is as much a planning failure as one that overruns**, so the slack is
spent on **`Q26-D`** -- the same paired re-search extended to every dataset that contributes no at-floor landing
(sec 7.1, population Q4). It is a **labelled extension in no denominator of the primary verdict**, it is clock-gated, and its
dataset order is fixed in advance as **ascending wave-18 optimize seconds** -- a published quantity independent of
anything this wave measures -- so a clock stop truncates the list at a point the outcome did not choose.

### 1.6 "Goal 3 has received nothing for two waves" is the wrong motivation, and it nearly bought the wrong arm

Two things are wrong with it.

**First, it is not true as of this morning.** The owner's results table, committed at `7e98915` minutes before this
design, carries a `BrightnessSensitivity (effective gate)` column for all 20 datasets and names the four at-floor
cases. Goal 3 received something today.

**Second, and more important: goal 3's deficit was never a deficit of MEASUREMENT.** `RULE P23` is the sharpest
goal-3 evidence in the series and it is already two numbers deep -- 8 of 40 nominally at floor, 9 of 40 where the
nominal pin is **inert**, 0 never-moved and 22 driven. What goal 3 lacks is a **decision**, and a decision needs a
**price**. Nobody has ever measured what forbidding the extreme costs.

Framing the wave as *"goal 3 is starving, feed it"* is how a wave manufactures an arm. Framing it as *"the owner's
verb is 'avoid', avoiding is a constraint, and nobody has priced the constraint"* is how it gets the right one. The
arm is the same size either way; only one of the two framings can tell you afterwards whether it worked.

### 1.7 Is the register dry, and should the run stop instead?

**No, and yes, in that order.** The register is not dry -- this wave has a real question with two informative
answers and a mechanism prediction (sec 7.4) that can be refuted. But **wave 26 should be the LAST wave**, and the
plan says so in sec 12: what remains after it is [F82](followups.md) (~45 m code + a 3-cell re-run + a **mandatory**
gate, so ~1 h 40 m minimum), the `A4` truth-model gap (~20 m but it rebuilds TestApp, which forfeits sec 1.4's
argument), [F73](followups.md)'s code axis (~53 m and it needs a second binary) and F67's residual. **Every one of
them needs a binary this wave deliberately does not build.** They do not fit between wave 26's landing and the
owner's stop, and starting one at 11:00Z would be exactly the overrun the budget forbids.

So: wave 26 runs, and the run stops after it. sec 14 discharges the one obligation that costs zero minutes and would
otherwise be lost -- **pre-registering which of F82's two candidate fixes is right, before anyone looks.**

---

## 2. THE PRE-REGISTRATION FENCE -- what I read, what I did not, and what binds me

**What this agent read, exhaustively, because a fence that is not itemised is not a fence:**

| read | why, and what it can contaminate |
|---|---|
| `docs/synthetic-af-bank-results-table.md`, in full | it is direct input by instruction. It names the four seedA0 at-floor datasets and their effective gates (0.56 / 2.39 / 2.36 / 2.13), the precision column, and the wave-18 optimize seconds. **All four are labelled controls under sec 10** |
| `/mnt/d/hf_w18/seedA0/D08_c11_2800mm/` -- `aggregate_summary.json`, `aggregate_summary.txt`, and the two landing files' KEY NAMES and the `Provenance.CommandLine` | `D08` is a **published control**. Its `BaselineJ`, `BestJ`, `EffectiveSensitivityGate`, `GateIsProvablyInert`, `InertGateBound`, `GateRejectedCount` and per-frame `LowSensitivityRejections` were read and are quoted in sec 7.4 |
| `/mnt/d/hf_w18/seedA1/D08_c11_2800mm/optimized_settings.json` -- `BrightnessSensitivity`, `BaselineJ`, `FinalJ`, `Provenance` | read to establish that seedA0 and seedA1 are **two binaries with an identical command line**, not two seeds. `D08`/seedA1 lands at **7.75**, i.e. NOT at floor -- which is why sec 10 can say the seedA1 half is genuinely blind |
| the count of landings under each root (20 and 20) | a population size, not an outcome |

**What this agent did NOT read, and what therefore stays blind:**

- **No landing under `/mnt/d/hf_w18/seedA1/` other than `D08`'s**, so **which** seedA1 landings sit at the floor is
  unknown to this document. `RULE P23` published the pooled count (8 of 40) and this design asserts it as a
  validity gate; the driver resolves the membership.
- **No `J` value anywhere except `D08`'s two, both published.**
- **Nothing at all about the outcome**, which does not exist: no `J` under a raised floor, no constrained landing,
  no precision at a raised sensitivity has ever been computed by anybody.

**What binds me.** Every threshold in this document is either (a) a **source literal** -- `10.0` and `2.0` are the
shipped defaults at `StarDetectionOptions.cs:356` and `HocusFocusStarDetection.cs:430`, which are also the
optimizer's own seeds for those two axes; (b) a **product predicate** -- `SensitivityIsAtFloor(s) => s <= 1.0`
(`ExposureRecommender.cs:364`, `:462`), `HardFloorPassed`, `at-bound` at `1e-12 * max(1, |bound|)`; or (c) an
**instrument property** -- `repr()` bit-identity, exact zero. **Not one number in this design was chosen by looking
at a measurement**, and a threshold that cannot be moved by the data cannot be contaminated by it. The
`RULE F14` / `RULE S16` / `RULE D20` fences are therefore not engaged, and no clause here reads any of them.

---

## 3. What SHIPS: **nothing**. There is no ship rule, and that is a decision, not an omission

**No plugin code, no TestApp code, no test, no binary.** The consequences are stated so nothing is claimed that
was not earned:

- **There is no ship rule and no unship rule**, because there is nothing to unship. `RULE Q26` decides what the
  wave may *say*, not what it may *ship*.
- **The suite is still run and verified BY COUNT** (sec 12 step 8). It is a control on the tree, not on this wave's
  code, and at ~4 minutes it is the cheapest control available. The expected count is **3973**
  ([F37](followups.md): by COUNT out of the log, never by the tick).
- **The gate is skipped** under sec 1.4, and sec 1.4's argument is void the moment any line of C# changes. If the wave
  finds a defect worth fixing, the fix belongs to a later wave with its own binary and its own gate.
- **Q26's most likely product consequence is a one-line change this wave deliberately does not make**: raising
  `DefaultSensitivityLower` from `0.0` (`OptimizerVariable.cs:145`, `:150`). sec 13 records the conditions under which
  the wave may *recommend* it, and it may never do more than recommend, because a shipped floor change would
  invalidate every landing in the bank and owes a fresh 42-minute baseline.

---

## 4. RULE V26 -- the derivation pre-flight. BLOCKING, carried forward, widened, and it already fired

Carried forward from `/mnt/d/hf_w25/verify_derivation_w25.py` as the charter requires. **It found a third gap of
the same family, and the gap was load-bearing.**

### 4.1 The third shape: the rule LETTER, and it is the same underscore for the third time

Wave 25 closed two gaps on opposite affixes -- `G24_START` (suffix) and `w24_gate_log_wsl` (prefix) -- both because
`_` is a word character so `\b` cannot see it. The pattern that closed them still read:

```python
r"(?:\bw%d(?:_[a-z][a-z0-9_]*)?\b|\bW%d\b|\b[GBRD]%d(?:_[A-Z][A-Z0-9_]*)?\b|prov_w%d|hf_w%d|score_\w*_w%d|_w%d\b)"
```

The uppercase alternation **enumerates the rule letters `[GBRD]`** -- the letters waves 19-24 happened to use -- and
the bare `\bW%d\b` arm has **no suffix arm at all**. Waves 21-25 used `W`, `N`, `M`, `V`, `P` and `S`. Measured
against wave 25's own pattern, not asserted:

| token | a real artifact of wave 25? | wave 25's pattern |
|---|---|---|
| `G25_START` | yes | matches |
| `w25_layout_self_test` | yes | matches |
| **`W25_BEFORE_READY`** | **yes -- wave 25's own arm interlock marker** | **MISS** |
| `g25_score.txt`, `v25_passend.txt`, `n25e_score.txt`, `p3c_score.txt` | yes, all four on disk | **MISS** |

`W25_BEFORE_READY` is **exactly D2's load-bearing class on a different letter**: a surviving `*_READY` marker is
what hangs the controller's waiter. Note `n25e_score` in particular -- a letter follows the digits with **no
separator**, so an optional `_SUFFIX` is not enough and the tail must be `[a-z0-9_]*`. The pattern is now matched
by **shape**:

```python
r"(?:\b[a-z]%d[a-z0-9_]*\b|\b[A-Z]%d(?:_[A-Z0-9][A-Z0-9_]*)?\b|prov_w%d|hf_w%d|score_\w*_w%d|_w%d\b)"
```

**Demonstrated in both directions** in the checker's own self-test section `[4]`: all five shapes are reported, and
this wave's own affixed names (`Q26_START`, `w26_layout_self_test`) are **not**, so the check discriminates rather
than merely firing.

### 4.2 The second fix: the FAIL end had to be PINNED, because wave 25 repaired its own root

Wave 25's self-test used *"the previous wave's root"* as its known-bad input. **That works exactly once.** Wave 25
then ran the checker over its own root and repaired all 21 findings, so `/mnt/d/hf_w25` is now `V-CLEAN`, and a
wave-26 checker pointed there would have found nothing and reported itself broken. **A checker whose known-bad
input is "the previous wave" stops having one the moment the previous wave adopts the checker.** The known-bad
root is now named in the source: `/mnt/d/hf_w24`, declared in `ALLOW` with its reason, and the self-test still
requires `V26-C` to name `prov_w24.py` and `gate_w24.sh` by line.

### 4.3 The clauses, and what the pre-flight found on this wave's own instruments

Unchanged in substance from `V25`: `V26-A` sibling targets resolve on disk; `V26-B` no unlicensed previous-wave
token; `V26-C` no ordinal or count typed into printed output. **`V26-A < 100 %` or `V26-B > 0` or `V26-C > 0`
=> `V-DRIFT`, BLOCKING.**

**It was run against `/mnt/d/hf_w26` before this document was finished and returned `V-DRIFT`, 8 findings**, on
instruments written from scratch an hour earlier -- three `V26-A` false-position header-prose references and five
`V26-B` hits on `G25_PASSED` and `/mnt/d/hf_w25/table18`. The last two are **legitimate** cross-wave references
(this wave reads wave 25's gate certificate instead of re-gating, and its scorer's self-test parses a published
`golden_eval.txt`) and are now **declared in `ALLOW` with reasons** rather than suppressed. Re-run: **`V-CLEAN`,
2 of 2 sibling targets resolve, 0 tokens, 0 typed ordinals.**

### 4.4 What the drivers' own self-tests found, before any measurement -- one of them was a control that printed PASS

Recorded here rather than in a deviation log, because it happened at pre-registration time and the next reader
should see it beside the rule it belongs to.

**`layout_w26.sh`'s trailing `--self-test` dispatcher fired when the file was SOURCED.** A sourced bash file sees
the parent's `$1`, so `q26_arm_w26.sh --self-test` sourced the layout, the layout's own dispatcher matched,
`w26_layout_self_test` ran, and **`exit "$?"` terminated the arm driver inside its own `source` line** -- printing a
clean passing layout self-test and running not one line of the arm's own checks. It looked exactly like a pass.
Fixed with `[ "${BASH_SOURCE[0]}" = "$0" ]`.

**This is wave 24's `[ now > "23:59" ]` in a new costume: a control that cannot fail, printing PASS.** It was found
only by *reading the output* of the self-test rather than its exit code -- the charter's own
"READ LOGS, NOT EXIT CODES", applied to an instrument instead of an arm.

Second finding, smaller: the arm's F15 guard (*"the bank-writing flag must appear on no invocation line"*) greps
its own file and therefore **reported the defect it was made of**, on its first run. Now excluded by an inline
marker on its own three lines.

---

## 5. The gate -- SKIPPED. The reasons, in full, and what would reverse the decision

sec 1.4 gives the argument. Stated as a decision with its conditions:

**SKIPPED because all four hold:**

1. **The binary does not change.** No C# is compiled by this wave. `Q26-V0` proves the bytes on disk are `G25`'s
   certified binary, and demonstrates that the comparator can refuse by hashing a real different published binary.
2. **`G25` already answered the question a re-gate asks.** Eight landings, `FinalJ` bit-identical at sixteen
   digits, `BuildId d79dae73...` novel against fourteen. Re-running it produces the same eight numbers or reveals a
   non-determinism `G25` would already have caught.
3. **A re-gate does not reach this wave's actual risk**, which is cross-*binary* comparability against wave 18's
   B1/B2 landings. `Q26-V3` reaches it, at zero extra cost, because the unperturbed base cell of Q26-A has to run
   anyway.
4. **Nothing in this wave's verdict tree reads a gate landing.** A control that no clause consumes is a ritual.

**REVERSED, and a full sixteenth gate becomes mandatory, if any of these occur:** a single line of C# changes for
any reason; `Q26-V0` finds a hash or BuildId mismatch; `Q26-V5` finds the arm's own instrument non-deterministic.
Any of the three and the wave stops, gates, and re-runs -- it does not proceed on a smaller control.

---

## 6. The arms

Sequential. **Only one `TestApp.exe` at a time.** Every phase writes a manifest and its marker **last**, only on
its pre-registered minimum row count; every scorer reads artifacts **out of manifest rows**
([F74](followups.md)); no marker is written by a human ([F75](followups.md)).

| phase | instrument | what it produces |
|---|---|---|
| `v` | none (hashes) | `q26_v0.txt` -- `Q26-V0`, with its FAIL end on B14 |
| prep | `prep_w26.py` + `convert_landing_w15.py` | the population, and 4 settings vectors per at-floor landing |
| `a` | `optimize --per-run --max-evals 1 --start-from-current --no-min-hfr-seed` | `BaselineJ` at each fixed vector -- the 2x2, `Q26-V3`, `Q26-V4`, `Q26-V5` |
| `b` | `optimize --per-run --max-evals 250` +/- `--sensitivity-floor 10.0` | the paired re-search on the at-floor datasets |
| `c` | `golden eval --params optimized --opt-results <w18 landing>` +/- `--sensitivity 10.0` | precision / recall at the landed vector and at the raised floor |
| `d` | as `b` | the labelled extension to the rest of the bank, clock-gated |
| `l` | `af-fit --af-run 'D:\Autofocus Bank\lumos'` | `RULE L26` |

**The fixed-vector instrument, named and justified** (the prompt's point 1). `optimize` **always** evaluates a `J`
for the `--settings` vector before the search starts -- `OptimizationDiagnosticRunner.cs:834-837` evaluates
`ctx.Baseline` against every loaded run and `:872-874` folds it into `baselineJ` -- and the optimizer is not invoked
until `:916`. There is **no `--evaluate-only` flag and `--max-evals 0` is rejected** by the parser (`:118-124`), so
`--max-evals 1` is the minimum and it is this project's established idiom for a seed-only evaluation
(`followups.md:6842`, `:6996`, `:4537`, `:7222`, `:7414`). `golden eval` cannot be the instrument: it computes
precision and recall and **no `J` at all** -- `grep JRun|JTotal|OptimizationObjective GoldenEvalRunner.cs` returns
nothing. `af-fit` computes neither. **So the honest instrument is `optimize` itself, run so that it does not
search**, and the cost is one image load plus two evaluations rather than 250.

Two flags earn their place and both are named in the driver: `--start-from-current` makes the seed the
`--settings` vector too, so the emitted landing **echoes back the value the product actually used** (that is
`Q26-V4`, and sec 8 says why it is not optional); `--no-min-hfr-seed` stops `MinHfrSeed.Resolve` silently lowering
the seed's `MinHFR` before theta0 is read. **F39(b) run detection binning is left ON, exactly as wave 18 left it** --
three of the four at-floor datasets are factor-2 and suppressing it would change the instrument, not control it.

---

## 7. RULE Q26 -- does the pin MATTER? Goal 3

### 7.1 The populations, named, never pooled

| | population | n | resolved how | why it is here |
|---|---|---|---|---|
| **Q1 -- primary, fixed-vector** | the landings in wave 18's P1 (`/mnt/d/hf_w18/seedA0` + `seedA1`, 40) whose `BrightnessSensitivity` is at the floor resolved from **that landing's own `Provenance.CommandLine`** | **8 expected** | by predicate, by `prep_w26.py`, at run time | `RULE P23` published this count. This design does not know its membership beyond the four seedA0 datasets the owner's table names |
| **Q2 -- the re-search** | the **datasets** contributing at least one Q1 landing on the **seedA0** root | **4 expected** | from the prep manifest | the datasets the owner's table names, and the cheapest four in the bank by wave-18 optimize seconds -- 120 s for all four |
| **Q3 -- precision** | the same datasets as Q2 | **4** | as Q2 | the instrument that can see what `J` cannot (sec 7.4) |
| **Q4 -- the labelled extension** | the datasets contributing **no** at-floor landing | **<= 16** | the driver lists **all 20** in a fixed order and removes the at-floor ones from the RESOLVED prep manifest at run time, naming every skip. Listing only sixteen would leave a dataset unreachable if the resolved at-floor set differed from the published one -- F68(e)'s exact defect | in **no** denominator of the primary verdict |

**Preservation.** `--update-run-folder` is passed **nowhere** -- the arm's self-test asserts it appears on no
invocation line, and that guard is itself two-directional. Every `--out` is under `/mnt/d/hf_w26/`; the arm's
self-test asserts no cell directory resolves inside a bank. Wave 18's landings are **read-only inputs**, and they
are wave 25's fingerprint class 3, so the plan re-fingerprints them before and after (sec 12 steps 1 and 9).

### 7.2 The clauses

| clause | **P** population (artifact **and** field, **and which copy**) | **S** statistic | **A** aggregation | **E** empty set | precision |
|---|---|---|---|---|---|
| **`Q26-V0`** | `TestApp.exe` + `TestApp.dll` under `/mnt/d/hf_w25/exe`; `/mnt/d/hf_w25/G25_PASSED`; `/mnt/d/hf_w24/exe/TestApp.dll` | `sha256`; marker presence | identity, plus a **refusal demonstrated on a real different binary** | a missing file is **COULD-NOT-LOOK**, never "unchanged" | exact hex |
| **`Q26-V1`** | landing counts per root | count | `== 20` per root, `== 40` total | a short root is a **POPULATION** failure, reported as itself, `Q-UNEVALUATED` | exact |
| **`Q26-V2`** | each landing's `Provenance.CommandLine` -- **the flat `optimized_settings.json`'s single copy** | `--sensitivity-floor` value; `--donut` presence | resolves **that landing's** floor and searched-axis set | absent or unreadable => **COULD-NOT-LOOK, named, out of BOTH numerator and denominator.** Never defaulted to 0.0 silently | exact string |
| **`Q26-V2b`** | **the THREE copies of the knob** (sec 8) | `repr()` equality across copies | any disagreement => that landing is **COULD-NOT-LOOK** | as above | `repr()` |
| **`Q26-V3`** | Q1 base cells: fresh `aggregate_summary.json[0].BaselineJ` vs wave 18's `optimized_settings.json.FinalJ` | exact `repr()` identity | count / base cells, denominator printed | `Q-UNEVALUATED` | full double, `repr()`, never a `G6` console print |
| **`Q26-V4`** | every **perturbed** Q26-A cell: the **emitted** landing's `BrightnessSensitivity` | `repr()` equality with the intended value | rate over perturbed cells | any miss => **that cell COULD-NOT-LOOK and the RULE is `Q-UNEVALUATED`** | `repr()` |
| **`Q26-V5`** | one base cell run twice | `BaselineJ` `repr()` identity | pass/fail, **plus the same comparator on `(base, both)` which must report NOT-IDENTICAL** | `Q-UNEVALUATED` | `repr()` |
| **`Q26-A`** | Q1 x {base, sens, clip, both}: `aggregate_summary.json[0].BaselineJ` | `dJ(v) = J(base) - J(v)` | **per landing**, all three printed, beside the landing's own wave-18 search gain (`FinalJ - BaselineJ`) as a **named reference scale** | a landing missing a cell leaves Q26-A, named | full double |
| **`Q26-B`** | Q2 x {unconstrained, constrained}: `BestJ`, `BaselineJ`, `HardFloorPassed`, `BrightnessSensitivity`, `ChangedParams` from `aggregate_summary.json[0]` | `dJ = BestJ(unc) - BestJ(con)`; hard-floor transition | **three counts, all printed**: hard-floor lost / equal-or-better / costed. Unpaired datasets NAMED and in no rate | `Q-UNEVALUATED` | full double |
| **`Q26-C`** | Q3 x {base, sens}: `golden_eval.txt`'s `OVERALL:` line and its `recall@high` / `recall@all` and `REJECTED:LowSensitivity` | precision, recall@all, FN-attribution | per dataset, **reported, NO BAR**, plus sec 7.4's single **pre-registered directional prediction** | a missing report => that dataset COULD-NOT-LOOK | 3 decimals as printed |
| **`Q26-C0`** | the `base` variant's `key detector knobs: Sensitivity=` line | equality with the landed value; and precision/recall equal to the owner's published table | reproduction control | mismatch => `Q26-C` UNEVALUATED, named | as printed |
| **`Q26-D`** | Q4 x two sides | as `Q26-B` | **separate table, labelled, in no denominator of the verdict**; datasets not reached are NAMED | 0 pairs is a valid answer | full double |

### 7.3 What the arm adds beyond the already-computable inert/pinned distinction

The prompt's point 3 is the right question and the answer decides whether the arm exists.

`EffectiveSensitivityGate = max(Sensitivity, PeakResponse x MinEffectiveClipMultiplier)`
(`StarDetector.cs:1505-1510`, `:1532-1533`, `:1552-1553`) **is** computable from a landing, and `RULE P23` computed
it: on 9 of 40 landings the nominal pin is inert. **Three further things are also computable and this design
computes them for free, before spending a minute** -- `SensitivityIsAtFloor`, `GateIsProvablyInert`,
`InertGateBound`, `GateRejectedCount` and per-frame `LowSensitivityRejections` are all already in
`aggregate_summary.json`. On `D08`/seedA0 they read `true`, `true`, `0.5625`, `0`, and `0` on every frame
inspected. **So the "is the gate doing anything at the landing" half of goal 3 is answerable at zero cost and is
larger than P23 realised.**

**What is not computable from any landing field, ever, is whether the optimizer can find an equally good vector
under a constraint.** That requires a search. It is the only part of the owner's question that needs an arm, and
it is what `Q26-B` is. If `Q26-B` were dropped, everything left would be arithmetic on files already on disk -- and
this design would say so and recommend the wave not run.

### 7.4 `D08` -- lead or coincidence? **A lead, with a mechanism, and it is registered as a hypothesis with n = 1**

The owner's table shows four datasets at `BrightnessSensitivity = 0.000` with effective gates **0.56, 2.39, 2.36,
2.13**, and `D08` is the table's **only** precision miss (0.966 against 1.000 everywhere else).

**It is a lead**, for three reasons that are structural rather than numerical:

1. **`D08` is the singleton on the side of a boundary the SOURCE draws.** `OptimizerVariable.cs:126-131` records
   that a sensitivity floor at or below **~1.5** is *"PROVABLY INERT at shipped defaults"*, because the clip stage
   guarantees the gate statistic exceeds `PeakResponse x StarClippingMultiplier` (0.75 x 2.0 = 1.5). Of the four
   at-floor gates, **`D08`'s 0.5625 is the only one below 1.5**; the other three (2.39, 2.36, 2.13) are above it.
   `D08` is the one landing of the four where the brightness gate is genuinely doing nothing.
2. **The objective cannot see precision on this bank, and this is not a guess.** `JRun`'s `LabelScore`
   (`OptimizationObjective.cs:426-428`, weight `Wl = 0.25`) is only active when the run carries labels, and wave
   18's own aggregate summary prints `Labels: (none -- unlabeled)`. **So `J` contains no precision term here**, and
   a knob that admits false positives costs `J` exactly nothing. `SStars` counts stars, not correct stars.
3. That is [F32](followups.md)'s recorded shape -- *"the optimizer trades enormous recall for numerically trivial
   gains"* -- pointed at precision instead of recall, and [F23](followups.md)'s precision term is the register's own
   name for the missing half.

**It is n = 1 and it is registered as a hypothesis, not a finding**, with a single **pre-registered directional
prediction** that `Q26-C` measures and that can be refuted:

> **PREDICTION P-D08, fixed before the data.** Re-scoring the landed vector with `--sensitivity 10.0` (the shipped
> default) will **raise `D08`'s precision above 0.966**, leave the other three at 1.000 (where it cannot rise), and
> **lower `recall@all` on all four**, with `REJECTED:LowSensitivity` rising in the false-negative attribution.

Both outcomes are informative. If it holds, the pin is free in `J` and costly in precision, and the objective -- not
the optimizer -- is what needs the fix. If `D08`'s precision does not move, the co-occurrence is a coincidence, that
is said plainly, and the wave has spent three minutes finding out.

### 7.5 THE BRANCH TABLE, fixed before the data

```
Q26-V0 / V1 / V2 / V2b fail on Q1            ->  Q-UNEVALUATED   (name the gate and the population)
Q26-V4 echo-back < 100 % on perturbed cells  ->  Q-UNEVALUATED   (F71: the perturbation reached the DEAD copy)
Q26-V5 not identical, or its comparator
  cannot distinguish (base, both)            ->  Q-UNEVALUATED
no paired (unconstrained, constrained)
  dataset in Q2                              ->  Q-UNEVALUATED
any constrained run loses the HARD FLOOR
  while its unconstrained partner keeps it   ->  Q-PIN-LOAD-BEARING  (datasets NAMED. The extreme is required:
                                                 without it the run fails outright)
else BestJ(constrained) >= BestJ(unconstrained)
  on EVERY paired Q2 dataset                 ->  Q-PIN-UNNECESSARY   (an equal-or-better optimum exists at or
                                                 above the SHIPPED DEFAULT, so the pin can be avoided at no
                                                 objective cost -- the owner's goal 3, answered affirmatively)
otherwise                                    ->  Q-PIN-COSTED        (per-dataset dJ printed against the named
                                                 reference scale. NO BAR, and the owner decides)
```

**All ends reachable, and each was demonstrated on its own fixture by the scorer's self-test** (sec 4.3's sibling:
`score_q26_w26.py --self-test` builds a structurally real root for `Q-PIN-UNNECESSARY`, `Q-PIN-COSTED`,
`Q-PIN-LOAD-BEARING`, and for the `Q26-V4` void, and checks that the refusing runs leave **no marker**).

`Q26-A`'s own sub-verdict is reported beside the rule and **cannot change it**:

```
every Q1 landing has dJ(sens) == 0.0 AND dJ(both) == 0.0 exactly   ->  A-FLAT
any dJ != 0                                                        ->  A-RESPONSIVE  (magnitudes and the ratio to
                                                                       the landing's own search gain printed,
                                                                       NO BAR)
dJ(both) has the OPPOSITE SIGN to dJ(sens) on any landing           ->  reported as the F6 DIAGONAL signature,
                                                                       named per landing, no bar
```

**`Q-PIN-UNNECESSARY` is the branch that would most directly serve the owner**, and it is the branch a naive
one-axis arm could never have reached: it requires the search to be allowed to move `StarClippingMultiplier` in
compensation, which is precisely what F6 says it must.

---

## 8. The [F68](followups.md) fifth part -- THREE copies of the knob on the input side, TWO on the output side

F68's fifth column asks which copy of a field is read and **what happens if the artifact carries more than one**.
This wave carries more than one on both sides, and both are live hazards with a recorded precedent.

**Input side -- three copies, and they are checked against each other:**

| # | file | key | case |
|---|---|---|---|
| 1 | `<landing>/optimized_settings.json` | `BrightnessSensitivity` | PascalCase, flat -- **THIS IS THE COPY READ** |
| 2 | `<landing>/hocusfocus_star_detection.json` | `starDetection.brightnessSensitivity` | camelCase |
| 3 | the same file | `starDetection.optimizedSettings.brightnessSensitivity` | camelCase, nested |

`prep_w26.py` reads (1) -- the copy the DTO readers use and the copy `convert_landing_w15.py` takes as its
`--landing` input -- and **asserts (2) and (3) agree with it to `repr()` bit-identity**. A landing whose copies
disagree, or that has no envelope to cross-check against, is **COULD-NOT-LOOK by name and leaves both numerator
and denominator**. Demonstrated in both directions by `prep_w26.py --self-test` section `[3]`.

**Output side -- two copies, and this is [F71](followups.md), which already cost one wave both of its verdicts.**
`convert_landing_w15.py` round-trips the base file's `Options` block verbatim **and** writes the landing into
`Options["OptimizedSettingsJson"]` with `UseOptimizedSettings = True`, so every curated knob appears twice. Under
`pinned_settings_w11.json`'s `UseAdvanced: False`, `ConfigureSimpleSettings` takes the
`UseOptimizedSettings && HasOptimizedSettings` branch and `ApplyOptimizedSnapshotToLiveProperties` **overwrites the
`Options` copy** -- so the **snapshot is live and the `Options` copy is dead**. Wave 18's `N18-V7` compared against
the dead copy and got **both** of its verdicts exactly inverted.

Three defences, in order, because one is not enough for a defect that has already inverted a verdict:

1. **The perturbation is applied to the LANDING BEFORE CONVERSION**, so it lands in the live snapshot.
2. **The `Options` copy is then aligned to the same value**, so the two agree whichever the loader honours.
3. **`Q26-V4` reads the value back off the EMITTED landing.** With `--start-from-current` at `--max-evals 1` the
   emitted `BestParams` is the seed, so the emitted `BrightnessSensitivity` **is the value the product used**. Any
   mismatch is COULD-NOT-LOOK and **voids the rule** -- it does not silently produce a wrong number.

`Q26-C` has its own echo-back on a different instrument: `golden eval` prints
`key detector knobs: Sensitivity=...` in its report, and the scorer requires it to equal the override.

---

## 9. RULE L26 -- `lumos`'s zero-star frame. Goal 1. ~10 m

Declared dropped **in advance** by wave 25 sec 1.5 and explicitly owed to wave 26. Wave 24 reproduced `rc=3` and
explained it: a **hard-floor FAIL**, `bestJ = currentJ = 0`, *"at least one frame has < 3 stars under optimized
params (min observed = 0)"*. **The question left open is narrow and it has exactly two answers:**

> Is the zero-star frame a property of the **FRAME** -- no usable signal at any settings -- or of the **PARAMETER
> VECTOR** -- the optimizer's landing gates it out?

`af-fit` answers it, because `af-fit` applies **no run detection binning** (that is F67's whole mechanism:
`ApplyRunDetectionBinningIfRequested` exists only on the `optimize` path) and reads the run's own detection result
rather than an optimized snapshot.

| clause | statement |
|---|---|
| **`L26-V1`** | `af_fit_points.csv` exists and parses, with its header `FocuserPosition,Stars,MedianHFR,Sigma_1483MAD,RegularizedSigma`. Absent => **`L-UNEVALUATED`**, and the `rc` is printed |
| **`L26-A`** | the count of fitted rows whose `Stars` column is `0`, over the printed total |
| **branch** | `L26-A == 0` => **`L-PARAMETER-VECTOR`** (the frame yields stars at the shipped defaults, so the landing's gates emptied it -- and that is a goal-1 defect with an owner) * `L26-A > 0` => **`L-FRAME`** (the run is unusable and the gate question is closed) |

**No bar beyond the zero, which is a count the instrument can resolve exactly.** Both branches are reachable and
both are publishable. This is the whole of the wave's goal-1 content and it is honest about being ten minutes.

---

## 10. Blindness -- the population is PUBLISHED, and why that does not contaminate a fixed-direction perturbation

`D08`, `D10`, `D11` and `D12` are named, with their effective gates, in a document the owner has read.

**They are the POPULATION, not labelled controls.** Excluding them would leave 4 blind landings out of 8 and would
exclude the exact cases the owner asked about -- a wave that answers a question about the cases nobody mentioned is
not answering the question. The published four are Q2's entire population.

**Publication does not contaminate, and the reason is structural rather than a promise:**

1. **The perturbation's DIRECTION and MAGNITUDE are fixed from source constants**, not from the table:
   `DELTA_SENS = 10.0` is `StarDetectionOptions.cs:356` / `HocusFocusStarDetection.cs:436`, `DELTA_CLIP = 2.0` is
   `HocusFocusStarDetection.cs:430`, and the constrained floor is the same `10.0`. Nothing in the table could have
   moved any of them.
2. **The OUTCOME exists in no published artifact.** `dJ` under a raised floor, the constrained landing, and the
   precision at a raised sensitivity have never been computed by anyone. There is nothing to have peeked at.
3. **The branch table is committed before the arm** and its ends are demonstrated on fixtures, so the wave cannot
   choose its verdict after the fact.

**And there is a genuinely blind half, which is worth more than a promise.** The **seedA1** at-floor landings are
unknown to this document -- the only seedA1 landing this agent read is `D08`'s, which lands at **7.75** and is
therefore **not** at floor. So seedA0 and seedA1 do **not** pin on the same datasets, the seedA1 members of Q1 are
resolved by the driver, and Q26-A's seedA1 half is a **blind replication** of its seedA0 half. It is reported
separately and it is never pooled.

**`Q26-D`'s sixteen datasets are blind for this outcome** in the ordinary sense: their landings are published but
nothing about their behaviour under a raised floor is.

---

## 11. What the results document may and may not say

**MAY say**

- `RULE Q26`'s verdict in `RULE Q26`'s words, including `Q-PIN-COSTED` as a real result that carries no bar.
- `Q26-A`'s 2x2 per landing, with the F6 diagonal signature named where it occurs.
- `Q26-C`'s precision and recall deltas per dataset, and whether **PREDICTION P-D08** held -- **either way**.
- `RULE L26`'s branch, in its words.
- `Q26-V3`'s cross-binary reproduction finding, either way, as a **labelled diagnostic** -- it is the first time
  this series has compared an objective evaluation across fifteen binaries.
- `RULE V26`'s third-shape finding, with the measured MISS table in sec 4.1.
- `Q26-D` as a **separate, labelled table**, with the datasets not reached NAMED.

**MUST NOT say**

- **No claim that the pin is "cosmetic" on the strength of `Q26-A` alone.** A flat one-axis or 2x2 response at a
  fixed vector is a statement about the neighbourhood of one point; only `Q26-B` speaks to what the search can do.
- **No pooling of `Q26-D` into `Q26-B`'s counts**, and no rate whose denominator mixes Q2 and Q4.
- **No pooling of seedA0 and seedA1**, and no rate over "setups" built from a count over landings -- `RULE P23`'s
  sec 6.1 caveat applies unchanged.
- **No bar invented for a "reported, no bar" clause**: `Q26-A`'s magnitudes and ratios, `Q26-B`'s `dJ`, `Q26-C`
  entirely, `Q26-D` entirely.
- **No goal-2 accuracy rate** from any instrument in this wave. Wave 25 sec 16.1 retired `13 of 17`; nothing here
  replaces it.
- **No claim about the wide end of [F25](followups.md)**, which remains unmeasured, and nothing about
  [F82](followups.md) beyond sec 14's pre-registered choice, which is a **decision, not a measurement**.
- **`RULE F14` / `RULE S16` / `RULE D20` are three permanent fences.** No clause here reads any of them.
- **Nothing about setups outside the bank**, except `lumos` under `RULE L26`, which is named as a real-bank run.
- **No recommendation to change `DefaultSensitivityLower` stated as anything but a costed recommendation** (sec 3).

---

## 12. Timing, priced step by step, with a drop ladder on the clock

Prices marked **measured** come from the same instrument on the same scenario ([F21](followups.md)); prices marked
*derived* carry a **band** and say so.

| # | step | price | TestApp? |
|---|---|---|---|
| 0 | `RULE V26` pre-flight, layout, drivers, every `--self-test` | **done at pre-registration** (~0 m for the controller beyond one re-run) | no |
| 1 | fingerprint wave 18's 40 landings + `Q26-V0` | ~4 m | no |
| 2 | `prep_w26.py` -- resolve Q1, write 32 settings vectors | ~2 m | no |
| 3 | **phase `a`** -- 33 invocations of `optimize --max-evals 1` | ***derived*, band 5-20 m; reserve 20 m** | **yes** |
| 4 | **phase `b`** -- 4 datasets x 2 sides at `--max-evals 250` | ***derived* from wave 18's own 120 s for these four, band 4-12 m; reserve 12 m** | **yes** |
| 5 | **phase `c`** -- 8 `golden eval` cells | **~2 m** (measured: 25 s for these four in wave 25's table arm) | **yes** |
| 6 | **phase `l`** -- `lumos` | ~10 m | **yes** |
| 7 | **phase `d`** -- the labelled extension, 16 datasets x 2 sides | **~96 m** (measured: 2 x 2890 s of wave-18 optimize seconds) | **yes** |
| 8 | scoring, the full suite by COUNT, register, hand-off | ~25 m | no |
| 9 | re-fingerprint, commit, push, PR body, CI by COUNT | ~20 m | no |

**Critical path WITHOUT step 7: ~1 h 35 m. WITH step 7: ~3 h 11 m**, against ~4 h 15 m. Both fit; the second uses
the slack sec 1.5 identified rather than banking it.

**Why step 3's price is a band and not a number.** `optimize --max-evals 1` on a synthetic dataset **has never been
run in this series**. Wave 24's rule is explicit: *a scenario that has never been run is priced as a DERIVATION,
give a band* -- and wave 24's own derived prices came in at ~0.4x. The band's ceiling is the full `--max-evals 250`
time (35.4 / 41.6 / 31.5 / 11.6 s for these four datasets), because 1 evaluation cannot cost more than 250; its
floor assumes load dominates. **A conservative derived price is the cheaper error.**

**Drop ladder, in order, with clock triggers:**

| | item | trigger | cost of dropping |
|---|---|---|---|
| **D1** | **phase `d`** entirely | phase `c` not complete by **09:00Z** | `Q26-D` is a labelled extension in no denominator; the primary verdict is untouched. Its own driver ALSO self-truncates at `10:10Z` per dataset and NAMES what it did not reach |
| **D2** | phase `l` (`lumos`, ~10 m) | phase `c` not complete by **09:40Z** | goal 1 gets nothing this wave and `L26` returns unrun. It is a probe with no dependents |
| **D3** | phase `c` (~2 m) | scoring not started by **10:00Z** | PREDICTION P-D08 goes unmeasured and must be stated as **untested**, not as unsupported |
| **HARD STOP** | phases `a` and `b` | not finished by **10:00Z** | kill; score over whatever exists. If `Q26-V1`/`V4`/`V5` or the pairing fails, the verdict is **`Q-UNEVALUATED` and that is the result** |

**The drop ladder never shrinks a blind denominator.** D1 removes a labelled extension, D2 a separate rule, D3 a
no-bar clause. Nothing above it touches Q1 or Q2.

---

## 13. Register entries this wave owes

| entry | action, conditional on the verdict |
|---|---|
| **`RULE P23`'s owed arm** | **DISCHARGED**, whatever the verdict. Wave 23 sec 5.2 registered *"a one-axis-at-a-time perturbation of the landed vector, re-evaluating `J` at each bound +/-1 step"* as the arm the finding owed. This wave runs a **strictly stronger** instrument and says so: the 2x2 F6 requires, plus the constrained re-search the one-axis form cannot reach |
| **[F6](followups.md)** | **EXTENDED** with the 2x2 on a second population if `Q26-A` names any DIAGONAL landing; **CITED, not extended** otherwise. F6's own table is on `mccomiskey`, a real-bank run; this would be the first synthetic replication |
| **[F32](followups.md)** | **EXTENDED** if PREDICTION P-D08 holds: `J`'s saturation is not only a recall problem -- on an **unlabelled** bank `J` contains no precision term at all, so a knob that admits false positives is free in `J` by construction |
| **[F23](followups.md)** | **CITED** as the register's existing name for the missing precision term, with `Q26-C` as the first measurement of what its absence costs on the synthetic bank |
| **NEW -- the sensitivity floor's price** | **NEW ENTRY**, whichever branch fires. `Q-PIN-UNNECESSARY` => raising `DefaultSensitivityLower` is free in `J` and is a **costed recommendation** (sec 3). `Q-PIN-COSTED` => the price is published per dataset with no bar. `Q-PIN-LOAD-BEARING` => the owner's concern is answered in the other direction and the extreme is the right answer |
| **[F80](followups.md)** | **EXTENDED** with sec 4.1's third shape and sec 4.2's pinned FAIL end, both **demonstrated**. Plus sec 4.4's sourced-dispatcher defect, which is wave 24's tautological-guard class in a new costume |
| **[F71](followups.md)** | **CITED and DEFENDED THREE WAYS** (sec 8). Not closed: the two-copy conversion is unchanged |
| **[F68](followups.md)** | **EXTENDED**: the fifth part now has a case with **three** input copies and two output copies in one clause |
| **[F82](followups.md)** | **CHOICE PRE-REGISTERED, sec 14. Not measured, not fixed.** |
| **the `A4` truth-model gap** | **NOT TOUCHED**, deliberately: fixing it rebuilds `TestApp`, which forfeits sec 1.4's whole argument for skipping the gate. It stays a wave-27 item with its price |
| **[F81](followups.md)** | **NOT DISTURBED.** No clause here reads a `stepRecommendation` field |
| **the charter's sec 1c** | **CORRECTED**: `N25-E` is discharged (sec 1.3), and priority 1 of the backlog must be struck |

**Is there a finding worth an entry?** The design already contains three before any measurement: **the third
derivation-drift shape, demonstrated on a real interlock marker of wave 25's own arm**; **a control that exited its
caller and printed PASS**; and **the structural fact that `J` on this bank carries no precision term at all**,
which makes a sensitivity pin free in the objective by construction rather than by accident. Whether the pin
*matters* is what the arms decide.

---

## 14. [F82](followups.md)'s fix -- the choice, PRE-REGISTERED before anyone looks. Zero minutes

F82 requires the choice to be made before looking, and it will otherwise be made by whoever executes it under time
pressure. **This costs nothing and is recorded here so it cannot be made after the data.**

F82's two candidates:

1. **Make the floor monotone across rounds** -- carry the previous round's floored half-width forward as a lower
   bound on the next round's `maxHalfWidth`.
2. **Make the bound robust to a fit that loses its outer points** -- derive `SearchSpan` from the **requested** span
   rather than from `bestFit.Inputs`.

**PRE-REGISTERED CHOICE: (1), the monotone floor.** Three reasons, all available now:

- **Blast radius.** (2) changes `maxHalfWidth` on **every capped round**, and the cap is *the* convergence
  mechanism for the two published cells that already work (`D08` and `D11` converge because `W` became
  `1.5 x half-span` **exactly**, four for four). Wave 25 shipped a change to that same function under a full
  do-no-harm rule; changing its input quantity two waves later, without a full paired arm, is the shape of change
  `RULE N25` exists to catch and this wave cannot run.
- **(2) would make the harness and the product agree by moving the PRODUCT to the harness's model**, and the
  charter's own instruction on the `A4` gap is the opposite: *"Fix the assertion, not the product."* The requested
  span is the harness's truth model; the fitted span is what the recommender actually has evidence for.
- **(1) is confined to the caller's round state**, which is where a cross-round property belongs. F82's own entry
  calls it *"simple, fixes `D01` directly, and confined to the recommender's caller state."*

**And the condition that would reverse it**, stated now: if a later wave measures that `SearchSpan` over
`bestFit.Inputs` **shrinks on more than a single cell** while the requested sweep widens, then the defect is in the
quantity and not in its persistence, and (2) becomes correct. `D01` is the only cell where this has been observed;
`n = 1` is not enough to move a bound that four published rounds depend on.
