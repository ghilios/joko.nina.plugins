# Wave 23 — design (pre-registration)

**Written 2026-08-12, before any wave-23 measurement exists.** Every rule, clause, threshold, population,
statistic, aggregation, empty-set answer and validity gate below is fixed here and is not re-decided after the
data. An unsatisfiable clause is a **finding**, not something to repair and re-score.

---

## 0. The vessel decision, first and in writing (§0.4 of the autonomous prompt)

**A fresh branch: `ghilios/synthetic-af-bank-followups-wave23`, cut from `develop` @ `3d370ff`.**

The question the prompt requires answering before any measurement — *is PR #191 still the right vessel?* — was
already resolved by owner action during wave 22: **PR #191 merged to `develop` at `12:50:41Z` as `d2f3400`**,
carrying waves 13–21, and wave 22's own work merged as PR #193 (`2623771`). PR #194 merged at `15:48Z` as
`3d370ff`. There is therefore no standing vessel to continue and no fourteenth section to drift into. **PR #192
(F70(b′)) was CLOSED — rejected by the owner — and is not re-proposed anywhere in this wave.**

Wave 22 produced a finding worth a register entry (F77, plus *"the lock is acquired at plugin LOAD, not at NINA
start"*), so the **two-consecutive-dry-waves stop condition is not armed**.

---

## 1. The re-scope, and why the wave that was half-designed is not the wave that runs

### 1.1 The owner's three goals, restated as the scoring rubric

> 1. Verify that optimization + autofocus can work on a wide range of setups, while making improvements to
>    bridge detected gaps
> 2. Ensure accuracy of recommendations for step size
> 3. Recommend appropriate adjustments to exposure time when it may help, and specifically to avoid situations
>    where some parameters are pinned to extreme values (such as sensitivity at 0)

Every item below is scored against those three. An item that serves none of them needs a different
justification, and one item in this wave has one (§3).

### 1.2 F73's code axis is DEMOTED, and I agree with the demotion

I had this wave half-designed around F73's code axis — two binaries at `2623771` and `3d370ff`, two 20-dataset
passes, all 33 landing keys diffed — before the goals were restated. **Scored against the three goals it is
0 for 3.** It verifies nothing about setup coverage, nothing about step-size accuracy, nothing about exposure or
pinned parameters. It is a control on the series' own instrument: *can a code change move a landing while `J`
stays fixed?* That is a real and unanswered question, it is genuinely the honest missing arm **of wave 19**, and
it stays in the register as such — but it is not what this window should buy.

**Two things I am carrying over from that half-design, because they are worth more than the arm was.**

**(a) The published price is wrong, and the correction stands whether the arm ever runs.** Both authority
documents price F73's code axis at **~53 m** — one 20-dataset pass. That price silently assumes wave 19's
`/mnt/d/hf_w19/reA0` can serve as the "old" side. It cannot: `reA0` was built from wave-19-era source
(`e5be699`), so the treatment would be *"everything shipped between wave 19 and now"* —

```
git diff --name-only e5be699..2623771 -- '*.cs'      ->  13 files  (7 outside the test project)
git diff --name-only 2623771..3d370ff -- '*.cs'      ->  14 files  (8 outside the test project)
```

— across three waves plus F76's eight rounds, F77 and the replay-report work. That is a confounded arm, not an
inert-change arm. **The honest form needs two binaries built from this wave's own tree and two 20-dataset
passes: 2 × 52 m 43 s ≈ 1 h 45 m, plus builds and provenance ≈ 1 h 50 m, plus a 42 m gate.** Wave 19's own
measured arm time (`04:32:00Z → 05:24:43Z` = 52 m 43 s) is the rate. **Record the correction in F73.**

**(b) A synthetic inert change would NOT have answered F73, and it is worth saying which shape does.** Two
shapes were open: an authored inert change (an unused method, a `Logger` line off the search path), or a real
shipped delta. **The authored change reproduces wave 19's exact defect one level up.** Wave 19's results already
say why: *"the `R-J-ONLY` branch could only have fired here from run-to-run or build-stamp nondeterminism, never
from a code difference, because there was no code difference."* An unused method is a code difference that
**cannot execute**, so the expensive branch is again reachable only from noise — and R19 already measured that
noise at exactly zero. It would look like it answers F73 and would not. Only a **real shipped delta whose
changed code executes inside the search loop** makes the expensive branch reachable by construction.

**(c) And this wave gets the cheap slice of it for free, at zero extra minutes.** `2623771..3d370ff` (PR #194)
touches two files on the **headless** `optimize` path this wave's gate runs:

| file | what changed | reaches the arm? |
|---|---|---|
| `StarDetection/Optimization/StarDetectionOptimizer.cs` (+142/−24) | a `StarDetector.ComputeEarlyCacheKey(p)` **SHA256 computed for every candidate inside the search loop** to classify cheap/expensive steps; `Emit` now fires per evaluation instead of per ten | **yes** — new work inside the search loop |
| `StarDetection/Optimization/RunEvaluationData.cs` (+43/−2) | `CreateEvaluator` gains an optional `IProgress<RunLoadProgress>`; the null path still calls the unchanged `EvaluateAsync(p, token)` | **yes** — but the arm's caller (`OptimizationDiagnosticRunner.cs:826`) passes no progress |

The source states its own belief at `RunEvaluationData.cs:919-925`: *"Passing null (every headless caller)
leaves the evaluation byte-identical."* **`G23-1` passing is the first measurement of that claim** — eight runs
reproducing K8 bit-identically on a binary that carries the change. That is wave 20/21's shape (ship the
believed-inert change into the gate's binary on purpose so the gate's PASS *is* the inertness measurement) and
it costs nothing. It is **not** F73's arm — 8 runs × 1 field, versus 20 datasets × 33 fields — and the results
doc must not report it as one.

### 1.3 What replaces it

| item | goal 1 (coverage + gaps) | goal 2 (step size) | goal 3 (exposure / pinning) | TestApp cost |
|---|---|---|---|---|
| **RULE G23** — the gate | — | — | — | **~42 m** |
| **RULE V23** — `synth-validate`, 20 datasets × 2 scenarios | **yes** — per-setup completion census + named gaps | **yes** — the headline | **yes (b)** — `exposureRecommendation` / `binningRecommendation` per round | **~30–35 m** |
| **RULE P23** — the extreme-value / pinning audit | partly (which setups pin) | — | **yes (a)** — the headline | **0 m** |
| **item L** — the two named real-bank gaps | **yes** — converts two nine-wave-old trap entries into a measurement | — | — | **~12 m**, conditional |

**Total TestApp ≈ 1 h 29 m against a ~6 h ceiling.** The slack is deliberate and §10 names what it was offered
to and why each was refused.

---

## 2. THE PRE-REGISTRATION FENCE — what I read, what I did not, and what the analysis agent inherits

RULE P23's population is **already on disk**. That means its statistic could be computed before the rule is
fixed, which is exactly what the F14 / S16 / D20 fences forbid (*"a repaired clause scored against the data that
motivated the repair is not a measurement"*). The line is drawn here and it binds the analysis agent too.

| | |
|---|---|
| **MAY read** | the **schema** (which keys a landing carries, their types) and the **bounds** (where each knob's search range is declared in source). A satisfiability analysis that does not know the instrument's range and precision is not one, and *"boundary"* is undefined without the range. §5.2's bounds table is entirely from `OptimizerVariable.cs`, `StarDetector.cs`, `HocusFocusStarDetection.cs` and `StarDetectionOptions.cs` — **source, not artifacts** |
| **MAY NOT read** | the **values** across the population, or any distribution, count or rate over them, before this document is committed. **Not even to check a clause is satisfiable.** Where satisfiability genuinely needs values, the clause is instead written so it is **satisfiable by construction** (§7.3) |
| **declared deviation** | the 35 landing top-level **key names** were enumerated at pre-registration time from **one** file, `/mnt/d/hf_w19/reA0/D18_m24_deep_shed/attempt01/optimized_settings.json`. That file is a **wave-19 arm landing**, and it is in **none** of RULE P23's three populations. No value from it is used by any clause. Reading forty-two would have been a measurement; reading one for key names is declared here rather than done quietly |
| **counts only** | `find … -name optimized_settings.json \| wc -l` was run on the two bank roots (22 + 20 = 42) to confirm the population is addressable ([F74](followups.md)). A count of files is not a read of values |

**Corollary — the `D11` problem, and it is real.** Wave 21 **published** `D11_rc10_585_afbin2` / `S1`'s entire
trajectory: `14 → 24 → 41`, `wasCapped=True` on both rounds at a `1.714…` growth ratio, stopping on
*"converged (step 41 within the 22 tolerance band of step_behavioral 55)"*. Those are precisely the quantities
RULE V23's step clauses read. **`D11` therefore cannot be blind for any V23 clause.**

**The choice, made here: `D11` is RUN, scored as a labelled REPRODUCTION CONTROL, and EXCLUDED from every V23
denominator.** Excluding it costs one dataset of twenty and buys blindness on the other nineteen. Running it
anyway costs the same ~50 s it would have cost inside the population and buys something the wave could not
otherwise have: an **independent determinism check on `synth-validate` across a binary change** — wave 21's
probe ran on the twelfth binary, this one runs on the thirteenth. If `D11`/`S1` reproduces wave 21's published
trajectory the instrument is stable across a rebuild; if it does not, **RULE V23 is `V-UNEVALUATED` and that is
the finding**, because a step recommender that moves under a rebuild cannot be scored for accuracy.
The reproduction control's expected values are quoted from wave 21's published record and are fixed **here**.

---

## 3. RULE G23 — the gate. One binary, the thirteenth.

**Which goal does it serve? None of the three, and that is stated rather than dressed up.** Its justification is
the charter's: it is the stopping control on the binary every arm runs, and *a partial reproduction stops the
wave*. Its second, free job is §1.2(c) — the first measurement of PR #194's written inertness claim.

**The binary:** ONE, built from `develop` @ `3d370ff` into `D:\hf_w23\exe`. Driver `/mnt/d/hf_w23/gate_w23.sh`.
`optimize --per-run --max-evals 250`, sequential, one `TestApp.exe`, no NINA, no fan-out, both pins named in the
driver header:

```
--settings    D:\hf_w11\pinned_settings_w11.json     md5 a67ffc06164c81613aef5c4f8324b9b8
--profile-id  ce3f3e63-8fd3-4b72-a0ca-d90db9441382   (astrodet)
```

**K8, the eight values, unchanged and still valid** (wave 18's Part 2 never shipped, so no coordinate-system
debt exists):

```
toml999 0.9957838768299878 | CWhiteFocus 0.9960675916058808 | uneven 0.9963677194179505
muggsie 0.9971948738498605 | mccomiskey 0.9767460801208465
D18     0.9998815090506263 | D19        0.9994870586135448 | D20    0.9997378027339423
```

### 3.1 The clauses

| clause | P — population (artifact **and** field) | S — statistic | A — aggregation | E — empty set | threshold |
|---|---|---|---|---|---|
| **G23-1** | `FinalJ` (the landing's own copy, at the top level, one per landing) in `<gate>/<run>/**/optimized_settings.json`, 8 runs | equality with K8 | **rate over 8, denominator printed**; bit-identity to 16 digits reported as the stronger observation | a landing that cannot be read is **COULD-NOT-LOOK, named, and G23-1 FAILS** — for a stopping gate "could not look" is not "not yet decided", it is "has not passed" | **8 of 8 to 6 dp.** A partial reproduction is a FAILURE and stops the wave |
| **G23-2** | `aggregate_summary.json` under `<gate>`, at depth ≥ 2 | count | `== 8` | 0 found ⇒ FAIL by count, never "0 of 0 match" | `== 8`, asserted **in the driver** before any scoring |
| **G23-P1** | the `PARAMS-DUMP optimize/seed` **BEGIN..END range only** in the 8 `<gate>/<run>.log` files — **not** `optimize/baseline`, **not** `optimize/detected`; three blocks in each log carry a field of this name | `NoiseReductionRadius` value | `== "3"` on 8 of 8 | a missing block is COULD-NOT-LOOK **by name** and P1 FAILS | **8 of 8.** If it reads 4, `/mnt/d/hf_w18/part2_w18.patch` has leaked into the tree and the wave STOPS |
| **G23-P2a** | the 8 gate logs | count of `^PARAMS-DUMP optimize/detected BEGIN$` per log | `== 1` on 8 of 8 | absent ⇒ 0, which FAILS | **8 of 8** |
| **G23-P2e** | the 8 gate logs | count of `optimize/baseline` and `optimize/seed` BEGIN lines per log | `== 1` each on 8 of 8 | as P2a | **8 of 8** |
| **G23-P3** | `RecommendedStepSize` (the landing's own copy, top level) in the 8 gate landings | present **and** finite | rate over 8 | missing or `NaN`/`"NaN"` ⇒ COULD-NOT-LOOK, named, P3 FAILS | **8 of 8** |
| **G23-3a…3f** | the free controls, as **FIELDS**, read **ACROSS** the arm: `BuildId`, `DetectorVersion`, `ProfileId`, `FitInputs`, `ConcurrencyCheck`, `BaselineJ` | see `prov_w23.py` | per-root cardinality / containment / equality | an unreadable landing appends an explicit `None`, which is a FAIL and never shortens a list into agreement | all six PASS |

**G23-P2 is a regression control on wave 20's shipped D1, on a thirteenth binary and a second wave running.**
Its **FAIL end is observed, not argued**: the same predicate on `/mnt/d/hf_w19/gate` returns `P2a` **0 of 8**,
because wave 19's binary predates D1. `score_v23_w23.py --gate` runs both ends and its exit status is wired to
the verdict.

**G23-P3 does not threshold the step recommender's VALUE.** That is RULE V23's job, on `synth-validate`, against
the harness's own expected step. P3 establishes only that the field goal 2 is about is produced by the shipped
`optimize` path, so a later wave that wants to score it there knows the field exists.

**BuildId novelty — the list is TWELVE, and wave 22's absence is a fact, not an oversight.**

```
5cb7e474 (w11)  103d61c4 (w12)  62334f10 (w13)  084e3485 (w14)  df3a867d (w15)  10bc1b47 (w16)
932a1366 (w17)  e745c958 (w18 B1)  7a3a03ba (w18 B2)  763d1476 (w19)  1a4dd85a (w20)  58b3b082 (w21)
```

Wave 22 built no measurement binary, ran no gate and no arm, and wrote no landing; `/mnt/d/hf_w22` does not
exist. A BuildId can only be recorded by a landing, so wave 22 has none — exactly as wave 14's `exe_floor` has
none. **Wave 23's is the thirteenth.** `prov_w23.py` records this in a comment so a later reader does not
mistake the gap for a lost id.

**Two instruments, two tables, never merged (F66).** `PRIOR_BUILD_IDS` holds BuildIds recorded in landings;
`KNOWN_DLL_SHA256` holds dll **file** hashes. `prov_w23.py` **fails loudly if a BuildId field ever starts with a
known dll hash** — a check for a *confusion*, not for a value. **Waves 20 and 21 never added their own dll
hashes**; they were computed at this pre-registration's time from the on-disk exe roots and added:

```
wave 20  TestApp.dll bec4e0a647bf338b…   HocusFocus.dll cc629ffc570f677e…
wave 21  TestApp.dll a834abbdec15a286…   HocusFocus.dll e321ced823ee6141…
```

**And wave 23 closes the gap wave 18's results already named** (*"no wave-18 driver computes a binary hash at
all; only truncated prefixes were logged"*): the chain writes the full 64-hex dll pair to
`/mnt/d/hf_w23/binary_provenance_w23.txt` as a first-class artifact. **Hash the dlls, never `TestApp.exe`** — it
was byte-identical across wave 18's B1/B2 while both dlls differed.

**Mandatory provenance for the binary** (recorded even though this wave compares only one binary, because the
gate's inertness claim in §1.2(c) is a two-tree statement):

```
git diff --name-only 2623771..3d370ff -- '*.cs'                 # 14 files, recorded verbatim in the results
find Joko.NINA.Plugins -name '*.cs' -newermt '<build mtime>'    # must be EMPTY
```

---

## 4. RULE V23 — `synth-validate` across the bank. Goals 1, 2 and 3(b).

**The instrument is now permitted, and both readings of the permission are satisfied.** The handoff says of
`synth-validate`: *"No clause may be written over it until a rate exists ([F68](followups.md))."* That sentence
is ambiguous and this wave satisfies **both** readings rather than picking the convenient one:

- **timing rate** — §5's budget table said *"NO measured rate"*; wave 21 measured **45–52 s per
  dataset × scenario × 2 rounds**, so 20 datasets × 2 scenarios is affordable and priceable. Satisfied.
- **statistic rate** — F68's own subject: a threshold stated as an absolute count is hostage to its denominator,
  and wave 21's probe was **n = 1**. This wave gives denominators of **19** (20 minus `D11`) per scenario.
  Satisfied — and a wave that had run a second single-dataset probe would satisfy the first reading and fail
  this one.

### 4.1 The arm

`synth-validate`, **one invocation per (dataset, scenario) cell**, 20 datasets × 2 scenarios = **40 cells**,
sequential, one `TestApp.exe`, no NINA. Driver `/mnt/d/hf_w23/v23_arm_w23.sh`; layout from `layout_w23.sh`;
handoff through `/mnt/d/hf_w23/v23_manifest.tsv` (F74).

```
<exe>/TestApp.exe synth-validate \
  --spec 'D:\hf_w23\exe\SynthBank\synthetic-bank-spec.json' \
  --out  'D:\hf_w23\v23\<DATASET>__<SCENARIO>' \
  --datasets <DATASET> --scenarios <SCENARIO> --max-rounds 4 \
  --settings 'D:\hf_w11\pinned_settings_w11.json' \
  --profile-id 'ce3f3e63-8fd3-4b72-a0ca-d90db9441382'   > <cell>/run.log 2>&1 < /dev/null
```

**`--max-rounds 4` — the SHIPPED default (`SynthValidateRunner.cs:135-141`), not wave 21's probe value of 2.**
Goal 2 asks whether the recommender converges; capping the loop at two rounds would **manufacture**
non-convergence and make `stoppedReason = "reached --max-rounds (2)"` an artefact of the harness rather than a
property of the product. F21's own follow-on at `--max-rounds 5` stopped at `roundsUsed = 2` with an identical
trajectory, so the extra headroom costs little and removes an excuse. **The consequence is priced**: the 45–52 s
rate was measured at 2 rounds, so it is ~25 s/round and this arm's rate is **unmeasured**; §10 budgets it at
~35 m with a `timeout 600` per cell and a start deadline, and the timebox is part of the deliverable.

**Why one cell per invocation and not one call for the whole bank.** `synth-validate` writes **one**
`synth_validate_report.json` per invocation, flat at `<out>` (`SynthValidateRunner.cs:285-286`), rewritten after
every dataset. A single call covering twenty datasets would put all twenty in one report. One cell per call
means a failure costs exactly one cell and the scorer sees a **missing manifest row** — a NOT-RUN that is
**named** — rather than a truncated report that silently shrinks a denominator. `--datasets` is comma-separated
and **repeating the flag does not accumulate** (`DiagnosticUtil.GetArg` returns the first match), so a
one-id-per-call driver is also the form least able to silently run the wrong set.

> **THE TRAP THAT WOULD HAVE DESTROYED THE CENSUS, CAUGHT AT PRE-REGISTRATION.**
> `synth-validate` sets **exit 1 when at least one `(dataset, scenario)` ends with `overallVerdict == Fail`**
> (`SynthValidateRunner.cs:288, 294-298, 327-329`). Wave 21's arm driver pattern is *"non-zero exit ⇒ no
> manifest row ⇒ NOT-RUN and named"*. Applied here that would have **discarded exactly the failing cells** —
> the ones goal 1's gap census exists to find — and reported a clean population of survivors.
> **So this driver's contract is explicit: exit 0 and exit 1 are BOTH scoreable and BOTH get a manifest row
> (the exit code is recorded as a manifest column). Only exit 2 (usage/config refusal), a timeout, a crash, or
> a missing report is NOT-RUN.** A scoreability rule inherited from a different command is not a scoreability
> rule.

**Pins, and they are verified from the REPORT rather than from the command line I typed.** `synth-validate`
accepts `--settings` and `--profile-id` through `HarnessSettingsStore.Resolve` (the former is honoured but is
**not in the usage banner**), and the report echoes back, at its top level: `specPath`, **`specSha256`**,
`maxRounds`, `maxEvals`, **`fitInputs`**, **`profileId`** (as `"{name} ({guid})"`). Every one of those is a
free control that reads the pin **as the binary resolved it**, not as the driver intended it. An arm pinned in
one dimension and floating in the other is not pinned.

**The spec ships with the build** at `<exe>\SynthBank\synthetic-bank-spec.json`, sha256
`bf10522e670a119e260a8b3d06c2cc6ec52ce513dde25ca9ca13f426c38672d6` — **verified at pre-registration time
against the repo copy at `Joko.NINA.Plugins/TestApp/SynthBank/synthetic-bank-spec.json`, which matches.** It
declares **20 datasets**, whose ids are **exactly** the 20 bank directory names, and **no dataset carries a
`stepSizeOverride`**, so `stepTheory` is a pure derivation with no pin on top.

> **AND A CORRECTION TO THIS DESIGN'S OWN EARLIER DRAFT, WHICH IS THE KIND OF THING THIS SECTION IS FOR.**
> `synth-validate` is **spec-driven, not bank-driven**. It re-renders every sweep itself and **never opens a
> bank artifact** — `SynthValidateRunner.cs` contains no reference to `synthetic_meta.json`, and the focuser
> centre comes from `$.datasets[i].optimalFocuserPosition` in the **spec** (`:485`), not from
> `renderRequest.OptimalFocuserPosition` in the bank. Two consequences: (a) the *"the shipped spec is a later
> revision than the one that rendered the bank"* caveat **does not apply to V23 at all** — V23 never mixes the
> two, because it uses only the spec; (b) V23 cannot disturb fingerprint classes 1 or 2 even in principle,
> which is stated so the AFTER check is read as the control it is and not as evidence V23 was careful.

**Clock guard.** Datasets run in the pre-registered order below, each cell gets `timeout 600`, and the driver
**refuses to start** a cell after `$V23_DEADLINE_UTC`. A cell that does not run gets **no manifest row** and is
therefore NOT-RUN and **named** by the scorer — it never silently shrinks a denominator.

**Population, pre-registered order** (wave 19's, written to `EXECUTED_CELLS` before the first run):

```
D18_m24_deep_shed  D19_cygnus_deep_shed  D20_m24_bright_control
D01_ultrawide_40mm D02_rich_135mm D03_redcat_250mm D04_esprit_550mm D05_tec140_1000mm
D06_sparse_1000mm  D07_rc10_2000mm D08_c11_2800mm D09_c14_3800mm D10_rc16_3250mm_sparse
D11_rc10_585_afbin2   <-- RUN, but EXCLUDED from every denominator; reproduction control only (§2)
D12_c14_585_afbin2 D13_apo200_1800mm D14_cdk14_2563mm_e47 D15_cdk20_3454mm_e47
D16_esprit550_ha3  D17_cdk14_oiii5
```

`D17_cdk14_oiii5` is in the population **on purpose** and is the known-hazardous member (it finds zero stars at
short exposures). If it produces no scoreable cell that is a **named COULD-NOT-LOOK**, not a quiet exclusion —
and under goal 1 it is one of the census's most informative rows either way.

### 4.2 The two scenarios, and why two

Goal 2 has two directions and a one-scenario arm measures only one of them.

| scenario | what it asks | why it is in the rule |
|---|---|---|
| **S1** | *does the recommender converge from a deliberately wrong step?* (step ×0.25; **guarantees a second round** — S0 converges in one) | the accuracy-under-correction direction |
| **S0** | *does the recommender leave an already-adequate step alone?* | the **do-no-harm** direction. A recommender that always grows the step would score perfectly on S1 alone |

**Both are read from `SynthValidationScenarios.cs` and the driver asserts the scenario ids it was given are
accepted by the binary** (an unknown `--scenarios` token must abort the cell, not silently run something else).
If the binary's S0 is not the "already-adequate step" scenario, the driver's self-test **fails and names the
mismatch**, and the wave runs S1 alone with the second direction recorded as unmeasured and priced. *The
scenario set is read from the binary, not from memory.*

### 4.3 The three step numbers, and which one goal 2 is actually about

**This is the single most important thing in the wave and it was not obvious.** The report carries **three**
distinct step quantities and they are not interchangeable:

| quantity | what it is | is it truth? |
|---|---|---|
| **`terminal.stepTheory`** and **`terminal.expectedStepSize`** | the bank's **render-spec physics**: `√8 · max(HFR_min, 0.70 px) / κ / 3.5`, rounded (`SynthBankDerivations.cs:80-87`), plus any `stepSizeOverride` — **and the shipped spec declares none** | **YES — ground truth.** And they are **the same number**, read from the same object (`SynthValidateRunner.cs:410` and `:579`). They are **not two independent witnesses** and must never be quoted as corroborating each other |
| **`terminal.stepBehavioral`** | the **shipping recommender iterated to a fixed point on the analytic, noiseless truth curve** (`SynthValidateRunner.cs:1184-1241`): truth HFR, no detector, no noise, budget 50 iterations, `NaN` if the fit fails to solve | **NO.** It is what the recommender *would* settle on if measurement were perfect. It differs from `stepTheory` on purpose |
| **`rounds[N].stepRecommendation.stepSize`** → **`terminal.finalStepSize`** | the recommender on the **real, noisy fit** of rendered frames. `finalStepSize` is the **last ACCEPTED recommendation, which was never itself rendered** (`:575`, `:846`); `rounds[last].bootstrap.stepSize` is the last step actually swept | **NO** — it is the product's answer, and it is what goal 2 is asking about |

`terminal.converged` and `stepToleranceBand` are defined **against `stepBehavioral`**, not against truth
(`ConvergenceBand.HalfWidth(stepBehavioral, 0.4)` = `max(0.5, 0.4·|stepBehavioral|)`, `ConvergenceBand.cs:50-55`).

> **So `converged == true` does NOT mean "the recommendation is accurate".** It means the recommender reached
> **its own** fixed point. A recommender whose fixed point sits far from the physics could converge on every
> dataset and be wrong on every dataset. **Goal 2 needs both numbers, and the wave measures both.**

The gap between them is itself a reported field — `terminal.stepBehavioralVsTheoryDeltaFraction` — measured on
a **noiseless** curve, so it is a property of the recommender with the detector and the noise removed. **That is
the purest goal-2 number available anywhere in this project and it costs zero extra minutes.**

### 4.4 The clauses

Every clause reads the **report JSON**, never the console and never the Markdown, and every field is named with
the report's **real** key — wave 21's probe printed four `None`s because it guessed them:

| the obvious guess | what the report actually calls it |
|---|---|
| `dataset`, `scenario` | **`datasetId`**, **`scenarioId`** |
| per-round `HalfWidth`, `step` | **nested**: `rounds[N].stepRecommendation.{halfWidth, stepSize}` |
| assertion `name`, `passed` | **`id`**, **`verdict`**, `detail` |

**Four serialization facts every clause below depends on, read from source at pre-registration time:**

1. **`verdict` and `overallVerdict` are JSON INTEGERS `0`/`1`/`2`** (`Pass`/`Flag`/`Fail`,
   `SynthValidationReport.cs:28`). There is **no** `StringEnumConverter` anywhere in the solution and
   `WriteJson` passes no settings (`:233`). **A clause written against the string `"Fail"` matches nothing** —
   those strings exist only in the Markdown.
2. **`NaN` is the JSON STRING `"NaN"`**, not a number and not null (Newtonsoft's default
   `FloatFormatHandling.String`). Every `= double.NaN` field can arrive as `"NaN"`. **Every clause asserts the
   value is a number before any arithmetic**, and a `"NaN"` is COULD-NOT-LOOK, never zero.
3. **Every numeric field is full precision.** No `G6`, no `0.###`, no format string on the way into the JSON —
   the rounded forms exist only inside `detail`, `stoppedReason` and `reasons[]` **prose**. **No clause parses a
   number out of a prose string.** This is the check `D20-B` did not make, made.
4. **`stoppedReason` case 4 contains a U+2014 EM DASH** (`SynthValidateRunner.cs:543-545`). A Unicode character
   in a redirected log arrives as the single byte `0x1A` on this machine's console code page. **Every scorer
   ASCII-folds `stoppedReason` and `detail` before printing** (non-ASCII → `?`), and reports the fold count.

**Null-ness traps, also from source:** on the kernel-cap-guard bail (`:637-644`) and the `GenerateSweep` refusal
(`:658-663`) a round is emitted with `stepRecommendation`, `exposureRecommendation` and `binningRecommendation`
**all `null`** and an **empty** `assertions[]`. On a scenario- or dataset-level exception (`:308-312`, `:451-455`)
`terminal` is emitted with `rounds: []`, `finalStepSize`/`expectedStepSize` = **0**, `stepTheory`/`stepBehavioral`
= `"NaN"`, and `assertions[0].id == "RUNTIME"`. **A zero is not a measurement; the guard is `rounds` non-empty
and `assertions[0].id != "RUNTIME"`, before any field read.**

**And one invariant hole, named in advance:** `converged` is set true by the no-op branch (`:539-541`) when
`stepToleranceBand` is **not finite**, with no band check. **`V23-B` therefore reports `converged` split into
`converged-with-a-finite-band` and `converged-with-no-band`, and only the first counts toward the bar.**

#### Validity gates — all must pass or RULE V23 is `V-UNEVALUATED` and issues no verdict

| clause | P (artifact **and** field) | S | A | E | threshold |
|---|---|---|---|---|---|
| **V23-V1** | manifest rows in `v23_manifest.tsv`, against `EXECUTED_CELLS` written before the first run | count | scoreable cells per scenario | `EXECUTED_CELLS` missing ⇒ **COULD-NOT-LOOK**, V23 UNEVALUATED — never "0 of 40" and never a drop | **≥ 15 of 19 scoreable per scenario.** Below that the rule is `V-UNEVALUATED` **by name** |
| **V23-V2** | the report's **own top-level** `profileId`, `fitInputs`, `specSha256`, `maxRounds` — the pins **as the binary resolved them**, not as the driver typed them | equality: `profileId` contains `ce3f3e63-…`; `fitInputs` == the gate's `Provenance.FitInputs`; `specSha256` == `bf10522e…`; `maxRounds` == 4 | cardinality 1 across all rows, and equality with the pinned constants | a report whose top level cannot be read is COULD-NOT-LOOK and V2 FAILS | all rows |
| **V23-V3** | the binary: the `<exe>` dll sha256 pair in `binary_provenance_w23.txt` | equality with the gate's | one binary across the whole wave | missing ⇒ FAIL | exactly one |
| **V23-V4** | the gate interlock `/mnt/d/hf_w23/G23_PASSED` | existence **and** the BuildId it carries | — | absent ⇒ the arm does not run at all (driver ABORT) | present, written by the scorer |
| **V23-V5** | **the `D11` reproduction control.** `rounds[*].stepRecommendation.stepSize` and `terminal.{converged, roundsUsed, finalStepSize, stepBehavioral, stepToleranceBand}` for `D11_rc10_585_afbin2`/`S1` | equality with wave 21's **published** values: trajectory `14 → 24 → 41`, `roundsUsed = 2`, `converged` true, `finalStepSize = 41`, `stepBehavioral = 55`, `stepToleranceBand = 22` | all-of | the cell missing ⇒ COULD-NOT-LOOK, **named**, and V5 is **NOT-EVALUATED** — which does **not** by itself unevaluate V23; a missing control is weaker than a failing one and the two are different states | all five reproduce, **or RULE V23 is `V-UNEVALUATED`** |

**Why V5 is a validity gate and not a measurement.** If the step recommender's output moves between the twelfth
and thirteenth binaries on the one cell whose answer is already known, then *nothing* the arm reports about
accuracy on the other nineteen can be attributed to the recommender rather than to the rebuild. That is the same
logic wave 18 used to refuse to run its gate on B2. **One caveat stated in advance:** wave 21's probe ran at
`--max-rounds 2` and this arm runs at 4. `roundsUsed = 2` on a real stop (which F21 established at
`--max-rounds 5`) means the trajectory is comparable; **if `D11` uses more than 2 rounds here, V5 is
`NOT-COMPARABLE` — a third state, named — and RULE V23 is `V-UNEVALUATED`**, because the control could not do
its job. That is fixed here, not after the data.

#### Measurement clauses

| clause | goal | P (artifact **and** field, **and which copy**) | S | A | E | threshold / role |
|---|---|---|---|---|---|---|
| **V23-G** | **2** | `terminal.finalStepSize` (int) and `terminal.expectedStepSize` (int) — **ground truth** — per scoreable cell | `abs(finalStepSize − expectedStepSize) <= max(0.5, 0.4·abs(expectedStepSize))` | **rate per scenario, never pooled, denominators printed** | either field missing/zero-from-exception ⇒ COULD-NOT-LOOK, named, out of the denominator, which is re-printed | **THE HEADLINE. This is goal 2 against truth** |
| **V23-A** | **2** | `terminal.{finalStepSize, stepBehavioral, stepToleranceBand}` | `abs(finalStepSize − stepBehavioral) <= stepToleranceBand` | rate per scenario, never pooled | a non-finite `stepBehavioral` or band ⇒ COULD-NOT-LOOK, named, out of the denominator | self-consistency: did it reach **its own** fixed point |
| **V23-H** | **2** | `terminal.stepBehavioralVsTheoryDeltaFraction` (double) | the value itself | **per dataset**, printed as a table; plus the rate within `±0.4` | `"NaN"` ⇒ COULD-NOT-LOOK, named | **reported, and thresholded only through V23-G/A** — it is the *explanation* variable, measured with noise removed |
| **V23-B** | **2** | `terminal.converged` (bool), `terminal.stepToleranceBand`, `terminal.stoppedReason` (string, ASCII-folded) | `converged == true` **AND** the band is finite | rate per scenario; the `converged-with-no-band` cells are counted **separately and named**; every non-converged cell is NAMED with its folded `stoppedReason` | as V23-A | thresholded via the branch table |
| **V23-C** | **1** | every element of each cell's per-round and terminal `assertions[]`: `id` (string), `verdict` (**int**), `detail` (ASCII-folded) | `verdict == 0` | **(i)** PASS-rate over all assertion instances, denominator printed; **(ii)** **per-`id` rate across cells** — and `A1`, `A2`, `A5` emit **several findings under one `id`**, disambiguated by the `detail` prefix, so the per-`id` denominator is instance-count and is printed | an empty `assertions[]` is its own state, **named**, in neither rate. **`assertions[0].id == "RUNTIME"` is an EXCEPTION cell, named separately and never counted as a failed assertion.** A `verdict` outside `{0,1,2}` is `UNKNOWN-VERDICT`, which FAILS | reported; **this is the coverage census** |
| **V23-D** | **1** | `terminal.overallVerdict` (**int**) per cell | `== 0` | rate per scenario + the **named list** of every cell at 1 or 2 | unknown value ⇒ `UNKNOWN-VERDICT`, FAILS | reported; feeds the census |
| **V23-E** | **3(b)** | `rounds[N].exposureRecommendation.{sensitivityAtFloor, hasRecommendation, increasesExposure, wasCapped, cappedByAbsoluteLimit, exposureIsNotTheLimit, starCountIsTheLimit, starFieldIsExhausted}` — **the round record's own copy** | each flag; and the reconstructed `capLimitsRecommendation = wasCapped && increasesExposure` (the product's own property at `ExposureRecommender.cs:288`, which is **`[JsonIgnore]`** and must be rebuilt) | **TWO denominators, and this is the F68 point**: (i) `sensitivityAtFloor` over **all** rounds; (ii) every other flag over **rounds where `sensitivityAtFloor` is true**, because `computed == sensitivityAtFloor` gates the whole recommendation (`SynthValidateRunner.cs:794, 800`). Both printed | a `null` block (kernel-cap bail) ⇒ COULD-NOT-LOOK, named, out of both | **reported with denominators; no bar** (§4.5) |
| **V23-F** | **3(b)** | `rounds[N].binningRecommendation.{hasMeasurement, vertexHfr, recommendedFactor}` | (i) a factor is recommended; (ii) **pinned at an extreme**: `recommendedFactor == 4 && vertexHfr/3 >= 4.5` (ceiling) or `== 1 && vertexHfr/3 <= 1.5` (floor) — `RecommendFromHfr` is `clamp(round(hfr/3), 1, 4)` (`DetectionBinningResolver.cs:44,47,57-62,142`) and **nothing in the report distinguishes a clamp from a free interior answer**, so the clamp must be reconstructed | rate over round instances with `hasMeasurement` true, denominator printed; plus the named list | `recommendedFactor` `null` (the R² < 0.9 gate) is its own state, **named**, out of the denominator | **reported with denominators; no bar** |

**V23-E's two-denominator structure is where goals 3(a) and 3(b) turn out to be one mechanism.** The exposure
recommender only computes anything when `SensitivityIsAtFloor(sensitivity)` — `sensitivity <= 1.0`,
`ExposureRecommender.cs:462` — which is **the same predicate RULE P23-C(ii) applies to the landings**. The
product raises exposure *because* sensitivity has pinned at the floor. So P23 measures how often the pin
happens on landings, and V23-E measures what the product then does about it, and **they are two views of one
thing.** Neither was designed to meet the other; the source says they do.

**V23-E and V23-F carry no threshold, and that is a decision with a reason, not an omission.** The discipline
says *"a number that is reported but not thresholded is a number the wave has agreed in advance not to act
on"* — and that is exactly right here. **There is no prior for what fraction of setups SHOULD have their
exposure capped**; a bar invented now would be the `S16-A(b)` defect (a threshold whose denominator comes from
somewhere other than the instrument the clause reads). What these two clauses produce is the **first
denominatored population** for exposure and binning recommendations across the bank, which is exactly what lets
a **later** wave write a bar. **Stated here so the results doc does not quietly act on them.**

### 4.5 THE BRANCH TABLE, fixed before the data

```
V23-V1..V5 all pass?                                     no  -> V-UNEVALUATED  (name the failing gate)
V23-G rate == 1.000 on BOTH scenarios
      AND V23-B rate == 1.000 on BOTH scenarios?         yes -> V-ACCURATE
V23-G rate <  0.500 on EITHER scenario?                  yes -> V-BROKEN
otherwise                                                    -> V-GAPPED  (every miss NAMED, with its folded
                                                                           stoppedReason, per scenario)
```

**The bar is on `V23-G` — accuracy against TRUTH — and not on `V23-A`.** `V23-A` is reported beside it, and the
pair is diagnostic: `A` passing while `G` fails means the recommender converges to a fixed point that is not
the physics, and `V23-H` says by how much. That decomposition is fixed here so it is not invented afterwards.

**Why the bar is a rate and never a literal count**, and why 1.000 / 0.500: F68. `V-ACCURATE` requires the
property to hold on **every** scoreable cell of **both** directions — the same shape as `R19-A`'s `== 1.000`,
and the only bar that supports the sentence *"optimization + autofocus works across this range of setups"*
without a caveat. `V-BROKEN` at below half is the point at which the recommender is not a recommender.
`V-GAPPED` is the middle, and it is **not a failure of the wave** — a named gap list is goal 1's actual
deliverable (*"while making improvements to bridge detected gaps"*).

**What each verdict costs, fixed here so it is not decided by the result:**

| verdict | what it means, and what it obliges |
|---|---|
| **`V-ACCURATE`** | goal 2 is answered affirmatively on 19 setups in **both** directions, at the pinned configuration. The step recommender needs no work, and a later wave that wants to move it owes a re-baseline. Goal 1's coverage question is answered affirmatively for the synthetic bank |
| **`V-GAPPED`** | the named list **is** the deliverable. Each named cell gets a costed bridge proposal in the results doc (§9). This is the branch the owner's goal 1 is written for, and it is not a disappointment |
| **`V-BROKEN`** | the step recommender does not generalise across the bank, and everything downstream that quotes `RecommendedStepSize` — the wizard summary, the landing field, F21's convergence claim — is conditional. **This is the outcome that would make the arm worth far more than 35 minutes** |
| **`V-UNEVALUATED`** | an unsatisfiable rule is a finding and is **not** re-decided after the data. If `V5` is what failed, the finding is that `synth-validate` is not stable across a rebuild, which is a bigger result than the rule was built for |

---

## 5. RULE P23 — the extreme-value / pinning audit. Goal 3(a). Zero TestApp minutes.

> *"…and specifically to avoid situations where some parameters are pinned to extreme values (such as
> sensitivity at 0)"*

### 5.1 The clause as originally posed is BROKEN, and the arithmetic proves it before any data

The natural form — *"of the 25 curated knobs in each landing, how many sit on a boundary of their search
range?"* — is **unsatisfiable as a product claim**, and this is F68's exact shape caught at pre-registration:

1. **A stock `optimize` searches 12 of the 25 knobs, not 25.** `OptimizerVariable.CreateCuratedSet` builds a
   **12-axis** base set and appends **9** defocus axes **only when the seed's `DefocusAwareDonutDetection` is
   true** (`OptimizerVariable.cs:118-119`), which for `optimize` requires `--donut`
   (`OptimizationDiagnosticRunner.cs:141, 373-377`). Three landing keys — `DefocusAwareDonutDetection`,
   `LocallyAdaptiveBinarization`, `AdaptiveNoiseBlockSize` — are **not optimizer axes at all**.
2. **Two of the thirteen unsearched keys sit on an axis bound BY CONSTRUCTION, on every landing that can
   exist.** `DonutMaxStreakEccentricity` is seeded `1.0` (`HocusFocusStarDetection.cs:451`) and its axis maximum
   is `1.0`; `DonutSaturationBloomRadius` is seeded `0.0` (`:452`) and its axis minimum is `0.0`. A 25-knob
   audit would therefore report **at least 2 "pinned" instances per landing on 100 % of the population** — a
   number produced by the denominator, not by the optimizer. *A bar stated over a population the clause does not
   read is hostage to that population.*
3. **`Sensitivity`'s lower bound is not a constant.** It is a method argument
   (`OptimizerVariable.cs:145,150`) injected by `--sensitivity-floor`
   (`OptimizationDiagnosticRunner.cs:163 → :479`), and **it is not persisted in the landing** — recoverable only
   from `Provenance.CommandLine` (`OptimizedStarDetectionSettings.cs:370`). *"At its minimum"* is undefined
   without resolving the invocation's floor. This is F68's fifth part applied to a **bound** rather than a
   value: name the field, and say which copy.
4. **The landing key is `BrightnessSensitivity`, not `Sensitivity`.** The optimizer variable is
   `nameof(StarDetectorParams.Sensitivity)` (`OptimizerVariable.cs:168`) and it is **renamed** on the way to the
   landing at `OptimizedStarDetectionSettings.cs:306`. A clause grepping for `Sensitivity` finds
   `EffectiveSensitivityGate` and misses the knob.
5. **A BOOLEAN AXIS HAS NO INTERIOR — and this one was found by the scorer's own `--self-test`, not by
   inspection.** `HotpixelThresholdingEnabled` (base) and `DefocusAwareGates` (donut) are boolean axes, so
   their value is **always** its min or its max. Including them puts a **floor of (#bool axes)/(#axes) on the
   pinned rate for 100 % of any population that can exist** — the same defect as (2), a third time in one rule.
   The first draft of `score_p23_w23.py` counted them and **its own `P-CLEAN` branch could not return**; the
   self-test caught it at 15 of 19. **That is the whole argument for writing the self-test before the arm, and
   it is recorded rather than quietly repaired.** Booleans are excluded from P23-A's numerator **and**
   denominator and reported as `P23-E`, a labelled diagnostic with no bar.

**So the clause is rebuilt from the instrument up, and the denominator is resolved per landing.**

### 5.2 The bounds table — read from SOURCE, at pre-registration time, with no artifact touched

The 12 base axes (`OptimizerVariable.cs:147-223`), which are the **only** axes a stock `optimize` moves.
**Eleven of them carry P23-A's rate; the twelfth, `HotpixelThresholdingEnabled`, is boolean and goes to P23-E
(§5.1(5)).**

| landing key | optimizer variable | type | min | max | initial step | seed literal (`HocusFocusStarDetection.cs`) | early/late |
|---|---|---|---|---|---|---|---|
| `BrightnessSensitivity` | `Sensitivity` | continuous | **0.0** (runtime-injectable) | 50.0 | 1.0 | `10.0` (`:436`) | LATE |
| `StarClippingMultiplier` | same | continuous | 0.25 | 10.0 | 0.5 | `2.0` (`:430`) | LATE |
| `NoiseClippingMultiplier` | same | continuous | 1.0 | 10.0 | 0.5 | `4.0` (`:429`) | **EARLY** |
| `StarPeakResponse` | `PeakResponse` | continuous | 0.1 | 1.0 | 0.05 | `0.75` (`:437`) | LATE |
| `MaxDistortion` | same | continuous | 0.1 | 1.0 | 0.1 | `0.5` (`:438`) | LATE |
| `MinHFR` | same | continuous | 0.1 | 5.0 | 0.25 | `1.2` (`:456`), **but see below** | LATE |
| `StarCenterTolerance` | same | continuous | 0.05 | 1.0 | 0.05 | `0.3` (`:453`) | LATE |
| `StructureLayers` | same | integer | 1 | 8 | 1 | `4` (`:433`) | **EARLY** |
| `NoiseReductionRadius` | same | integer | 0 | 10 | 1 | `3` (`:428`) | **EARLY** |
| `MinStarBoundingBoxSize` | `MinimumStarBoundingBoxSize` | integer | 2 | 20 | 1 | `5` (`:455`) | LATE |
| `HotpixelThresholdingEnabled` | same | boolean | 0 | 1 | 1 | `true` (`:427`) | **EARLY** |
| `HotpixelThreshold` | same | continuous | 0.0001 | 0.05 | 0.001 | `0.001` (`:466`) | **EARLY** |

**Seed caveat, recorded rather than assumed away:** `MinHFR`'s seed is **not always 1.2** —
`MinHfrSeed.SeedFloor = 0.30` (`MinHfrSeed.cs:72`) lowers the seeded gate on rigs whose fit vertex is under it
unless `--no-min-hfr-seed` is passed. The seed-discriminator clause (P23-B) therefore treats `MinHFR` as
**seed-indeterminate** and reports it in its own row rather than folding it into the rate.

**Precision of the instrument, which decides the predicate** (§7.2 and the `D20-B` lesson: *a tolerance finer
than its own instrument's resolution*):

- **integer and boolean axes** (`StructureLayers`, `NoiseReductionRadius`, `MinStarBoundingBoxSize`,
  `HotpixelThresholdingEnabled`) — the landing stores an integer/bool; the predicate is **exact equality** with
  the bound. Precision is exact and no tolerance is needed or permitted.
- **continuous axes** — `Quantize` is the identity for continuous variables (`OptimizerVariable.cs:83-92`) and
  the search halves its step down to `InitialStep × StepFloorFraction`, so a landed value **need not be an exact
  multiple of anything**; but `Write` clamps through the same descriptor (`:99-103`, `:311-323`), so a value
  **driven to the bound lands exactly on it**. The predicate is therefore
  `abs(landed − bound) <= 1e-12 * max(1, abs(bound))` — a float-representation tolerance only, **not** a
  physical one, and it is stated as such. The landing is JSON, so the value is a full-precision double; nothing
  passes through a `G6` or `0.0#` format on the way in. **This is the check `D20-B` did not make.**

### 5.3 The three populations, named, never pooled

| | population | n | provenance | why it is here |
|---|---|---|---|---|
| **P1 — primary** | `/mnt/d/hf_w18/seedA0` (20) + `/mnt/d/hf_w18/seedA1` (20) landings | **40** | wave 18's B1 and B2, `optimize --per-run --max-evals 250`, `--settings`+`--profile-id` pinned, 20 synthetic setups, **one configuration each** | **This is the licensed one.** R19's `R-DETERMINISTIC` says in terms: *"Wave 18's 40 arm landings are a MEASUREMENT … and a later wave may consume them under a rule fixed first."* This is that wave and this is that rule. They are also fingerprint class 3, so the denominator is itself controlled |
| **P2 — secondary, LABELLED** | the 42 class-1 bank landings (22 under `D:\Autofocus Bank`, 20 under `D:\SyntheticAutofocusBank`) | **42** | **heterogeneous and historical.** F15's own entry records that `optimize --per-run` wrote into run source folders for thirteen waves, *"destroyed bobp_m101's historical row mid-investigation and silently re-baselined both banks in wave 3"* | a rate over these is a statement about **history**, not about the shipped recommender. Reported separately, never pooled with P1, and every row's `Provenance.CommandLine` is resolved or the row is COULD-NOT-LOOK |
| **P3 — fresh** | this wave's 8 gate landings | **8** | the thirteenth binary, this wave's pins, 5 real-bank + 3 synthetic setups | the only population produced **by the binary under test**, and the only one covering real-bank optics |

**P2 is where I most disagree with the framing I was given** — see §11(1). It is included, but it is not the
primary and it cannot carry a product claim on its own.

**The preservation controls matter more this wave than any before it:** P1 is fingerprint class 3 and P2 **is**
fingerprint class 1. A wave whose measurement population is also its preservation population must show the
population did not move under its own arms — otherwise it is measuring the arm. `--update-run-folder` is passed
**nowhere** in this wave.

### 5.4 The clauses

| clause | P (artifact **and** field, **and which copy**) | S | A | E | threshold |
|---|---|---|---|---|---|
| **P23-V1** | landing counts per root | count | `== 40 / 42 / 8` | a short root is a **POPULATION failure**, reported separately from a content failure, and P23 is `P-UNEVALUATED` for that population | exact |
| **P23-V2** | `Provenance.CommandLine` — the landing's own single copy — per landing | presence of `--donut`; value of `--sensitivity-floor` | resolves **that landing's** searched-axis set (12 or 21) and **that landing's** `Sensitivity` floor | **unreadable or absent ⇒ COULD-NOT-LOOK, named, and that landing leaves BOTH the numerator and the denominator**, which are re-printed. It is never defaulted to 12 silently | every scored landing resolves |
| **P23-V3** | fingerprint classes 1 and 3, before and after every arm | sha256 | byte-identity | a file that cannot be read is COULD-NOT-LOOK — neither "unchanged" nor "changed" | 42 of 42 and 48 of 48 |
| **P23-A** | for each landing: each **searched** axis's top-level key (the landing carries each knob **exactly once**; this is a landing, not a converted settings file — F71's two-copy defect is a property of converted files) | at-bound per §5.2's predicate | **a rate with a named denominator: pinned axis-instances / (resolved searched axes × landings)**, computed **per population**, plus a **per-axis** rate across landings and a **per-setup** count | zero landings resolvable ⇒ **UNEVALUATED**, never `0.000` and never "clean" | see the branch table |
| **P23-B** | the same instances, plus the seed literals in §5.2 | `landed == seed` | for every pinned instance: **"never moved"** (landed == seed, and the seed is itself at the bound) vs **"driven to the bound"** (landed != seed) | `MinHFR` is **seed-indeterminate** (`MinHfrSeed`) and is reported in its own row, in neither bucket | reported per instance; **this is the clause that makes P23-A actionable** |
| **P23-C** | `BrightnessSensitivity` **and** `EffectiveSensitivityGate` (both top-level, one copy each) in the same landing | (i) `BrightnessSensitivity` at the resolved floor per §5.2; (ii) the product's own predicate `SensitivityIsAtFloor(s) => s <= 1.0` (`ExposureRecommender.cs:364, 462`); (iii) `EffectiveSensitivityGate > BrightnessSensitivity` | three rates over the same denominator, printed | a landing missing either field ⇒ COULD-NOT-LOOK, named, out of all three | **the owner's named case, and it is a PAIR** |
| **P23-D** | the **13 unsearched** keys | at-bound per §5.2 | reported **separately and labelled "seed artifact, not a search outcome"**, with `DonutMaxStreakEccentricity` (seeded at its max) and `DonutSaturationBloomRadius` (seeded at its min) **named in advance** | — | **no bar, ever.** It exists so the number cannot be mistaken for P23-A's |
| **P23-E** | the **boolean** axes (`HotpixelThresholdingEnabled`; `DefocusAwareGates` when searched) | the value's rate of being `true` (== the axis max) | rate per axis, denominator printed | absent ⇒ COULD-NOT-LOOK, named | **no bar, ever** — §5.1(5). A bool is always on a bound, so "pinned at an extreme" is not a property it can fail to have |

**P23-C is the wave's best clause and the reason is F66(c).** *"When a control reduces a property to ONE field,
corroborate with a second field that must move the same way."* `BrightnessSensitivity = 0` is **not by itself**
the pathology the owner named: `EffectiveSensitivityGate = max(Sensitivity, PeakResponse ×
MinEffectiveClipMultiplier)` (`StarDetector.cs:1552-1553`, `:1532-1533`), and the source's own worked example
(`:1540-1550`) is a landing reading `Sensitivity 0.0` with `PeakResponse 0.98 × StarClip 10.0` **enforcing a
gate of 9.81**. So a nominal pin at zero can be **completely inert**. The honest answer to *"avoid situations
where sensitivity is pinned at 0"* is therefore two numbers, not one: how often it is nominally at floor, and
how often that floor actually binds. **Both are already in the landing; neither costs a minute.** And note
`OptimizerVariable.cs:127-131` states that a floor at or below ~1.5 is *"PROVABLY INERT at shipped defaults"* —
so the product has an argument on record, and P23-C is the first measurement of it.

### 5.5 THE BRANCH TABLE, fixed before the data

```
P23-V1..V3 all pass on population P1?             no  -> P-UNEVALUATED  (name the failing gate and the population)
P23-A pinned-instance rate on P1 == 0             yes -> P-CLEAN
P23-C(i) rate on P1 > 0                           yes -> P-SENSITIVITY-PINNED  (the owner's named case; and
                                                          P23-C(iii) says whether it BINDS or is inert)
otherwise (some axis pins, sensitivity does not)      -> P-PINNED   (axes and setups NAMED)
```

`P-SENSITIVITY-PINNED` and `P-PINNED` are not exclusive in substance; the table orders them so the owner's named
case is reported **as itself** rather than folded into an aggregate. P2 and P3 are scored with the same clauses
and reported as **separate, labelled rows**; **neither can change P1's verdict.**

---

## 6. Item L — the two named real-bank gaps. Goal 1. Conditional, ~12 m.

Three coverage gaps have sat in the traps list for nine waves as folklore: `D17_cdk14_oiii5` finds zero stars at
short exposures, **`lumos` exits rc=3**, and **`Panos` has a degenerate σ fit and must be UNEVALUATED by name**.
`D17` is inside RULE V23's population and is measured there. `lumos` and `Panos` are **real-bank** runs that no
arm in this series has touched; the gate's five real runs are `toml999`, `CWhiteFocus`, `uneven`, `muggsie`,
`mccomiskey`.

**Item L runs `optimize --per-run --max-evals 250`, pinned, on `lumos` and `Panos`, into `/mnt/d/hf_w23/gap`,
and records what actually happens on the thirteenth binary.** ~6 m each.

```
L-REPRODUCED   the recorded failure mode reproduces, with the exit code and the error text QUOTED
L-CHANGED      it does not reproduce -- the entry is stale and the register is corrected
L-NOT-RUN      the wave ran out of window; named, priced, and NOT reported as either of the above
```

This is on goal 1 in the owner's own words (*"making improvements to bridge detected gaps"*): it converts two
entries that are currently reasons-to-avoid into either a measured failure with a costed bridge proposal, or a
correction. **It is last in the order and it is the first thing cut.**

**There is deliberately NO SCORER for item L, and that is a decision with a reason.** The recorded failure
modes are **prose in the traps list**, not fields, so a machine comparison would be a comparison against a
string the driver itself wrote — a check that cannot fail (F66). The driver records the exit code, the recorded
mode and the ASCII-folded tail of the log into `gap_manifest.tsv`, and **the outcome is assigned by hand in the
results doc, per run, from that evidence.** Naming that is the honest form; automating it would be theatre.
`Panos` is expected to be **UNEVALUATED BY NAME** — a degenerate σ fit is a known state, not a crash, and
reporting it as a failure would re-commit the register's own trap.

---

## 7. Satisfiability analysis

### 7.1 Every clause's population is ADDRESSABLE (F74's half that wave 20 skipped)

| clause | the artifact it reads | addressable? |
|---|---|---|
| G23-1 / 3a–3f | `<gate>/<run>/**/optimized_settings.json` | the gate driver **asserts count 8 before scoring**, and the landing is **found** by `w23_landing_find` (a `find`, never a template) — a real-bank run nests one level deeper than a synthetic one, and wave 21's own pre-registration hit exactly that with a glob that rewrote 6 of 8 and reported success |
| G23-P1/P2 | `<gate>/<run>.log` | the gate's layout is **unchanged from waves 20 and 21** on purpose: `score_w12.py` and both ends of the P2 demonstration read the same code path |
| V23-A…F | `<v23>/<ds>__<sc>/synth_validate_report.json` | **the driver writes every path into `v23_manifest.tsv` and the scorer reads paths ONLY out of it.** There is no `os.walk` fallback, deliberately — wave 20's was rooted at the same wrong parent as its primary path |
| P23-A…D | 40 + 42 + 8 landings | counts verified at pre-registration time by `find … \| wc -l` (40 via fingerprint class 3's own manifest, 22 + 20 = 42 for class 1). **File counts only; no value was read** |
| L | `<gap>/<run>.log` | the driver asserts the log exists before recording |

### 7.2 Every clause's INSTRUMENT PRECISION is checked, not only its reachable values

| clause | instrument | resolution | is the threshold finer than the instrument? |
|---|---|---|---|
| G23-1 | `FinalJ`, a JSON double | full double | no — the clause is 6 dp and bit-identity is the *reported* stronger observation |
| G23-P3 | `RecommendedStepSize`, a JSON number | full | no — presence and finiteness only |
| V23-G | `finalStepSize`, `expectedStepSize` — both **JSON integers** | exact | no. The band `max(0.5, 0.4·|expectedStepSize|)` re-uses the product's **own** `StepSizeTolerance = 0.4` (`SynthBankSpec.cs:231`) and `ConvergenceBand`'s functional form, re-centred on truth. **The substitution is declared, not slipped in** |
| V23-A | `finalStepSize` (int), `stepBehavioral`, `stepToleranceBand` (doubles) | full | **no, and this is deliberate**: the predicate uses the harness's **own** published band rather than a number this wave invents. `D20-B` failed a correct program because its `5e-7` was finer than the `G6` console it read; every V23 clause reads **JSON**, where nothing is formatted, and **no clause parses a number out of a prose string** |
| V23-C/D | `verdict` / `overallVerdict` — **JSON INTEGERS 0/1/2**, not strings | exact | no — **and a value outside `{0,1,2}` is `UNKNOWN-VERDICT` and FAILS.** An enum read by a scorer that silently buckets the unknown into "pass" is the same defect as a rate over an empty set reported as 1.000. **A clause written against the string `"Fail"` would have matched nothing, silently** |
| V23-E/F | the round blocks' flags and `vertexHfr` | full | no — but `capLimitsRecommendation` and the binning **clamp** are `[JsonIgnore]`/absent and are **reconstructed** from fields that are present, per §4.4. A reconstruction is declared as one |
| P23-A integers/bools | JSON integer | exact | no — exact equality, no tolerance |
| P23-A continuous | JSON double, written by a `Write` that clamps through the bound descriptor | full double | **`1e-12` relative is a float-representation tolerance and is stated as such.** It is not a claim about physical proximity to the bound |
| P23-C(ii) | `BrightnessSensitivity` vs the product's own `<= 1.0` | full | no — the predicate is the **product's**, at `ExposureRecommender.cs:462`, not one this wave invented |

### 7.3 BOTH branches of every clause are reachable — and where a branch needs values to prove, the clause is made satisfiable BY CONSTRUCTION instead

| branch | reachable? | how it is established **without reading the population** |
|---|---|---|
| `G23-1` PASS | yes — observed on twelve binaries | prior waves |
| `G23-1` FAIL | yes | `score_w12.py` is demonstrated to FAIL on a mutated copy of the gate before it is quoted |
| `G23-P2a` PASS / FAIL | **both OBSERVED on real artifacts** | PASS end on wave 21's gate (D1 shipped); **FAIL end on `/mnt/d/hf_w19/gate`, where it is 0 of 8** because that binary predates D1 |
| `V-ACCURATE` | yes by construction — the predicate can hold on every cell | it is a conjunction of per-cell booleans over a non-empty population |
| `V-GAPPED` | yes by construction | one failing cell suffices |
| `V-BROKEN` | yes by construction | it is the same statistic below a fixed fraction |
| `V-UNEVALUATED` | yes — five independent gates, one of which (`V5`) is a cross-binary reproduction with its own third state (`NOT-COMPARABLE`) | |
| `V23-C`'s `RUNTIME` state | yes by construction | `SynthValidateRunner.cs:311,454` emit it on any scenario- or dataset-level throw |
| `P-CLEAN` | **yes by construction**: the statistic is a count over a non-empty population with a predicate that can be 0 | **no value read.** This is the answer to *"how do you know the clause is satisfiable without looking?"* — because the predicate's range is `{0, 1}` per instance and the population is non-empty and addressable |
| `P-PINNED` / `P-SENSITIVITY-PINNED` | **yes by construction**: one instance above zero suffices | as above |
| `P-UNEVALUATED` | yes | three independent gates |
| `P23-D` / `P23-E` verdict branches | **deliberately absent** | both have values foreclosed by arithmetic on 100 % of any population; a clause with a foreclosed value is written as a labelled diagnostic, never as a threshold |
| `L-REPRODUCED` / `L-CHANGED` | both by construction | an exit code either matches the recorded one or does not |

**And one branch is DELIBERATELY foreclosed, stated rather than left for a reader.** `P23-D` (the 13 unsearched
keys) has **no** verdict branch at all: §5.1(2) proves by arithmetic that at least two of its instances are at a
bound on **100 %** of any landing that can exist. A clause with a foreclosed value is not written as a
threshold; it is written as a labelled diagnostic. *That is the check `S16-A(b)` never made.*

### 7.4 Where "could not look" lives, in every scorer

Three states, never two, and **the guard comes before any field read**. A file that cannot be read is
COULD-NOT-LOOK; it is neither "unchanged" nor "changed", neither "pass" nor "fail". A rate over an empty set is
**UNEVALUATED**, never `1.000` and never PASS. Every all-of clause is paired with a cardinality or an `n of N`
companion, because `all()` over an empty list is vacuously true.

---

## 8. Controls

### 8.1 The four fingerprint classes

| class | population | script | role this wave |
|---|---|---|---|
| 1 | 42 bank landings | `bank_fingerprint_w15.py` | **preservation AND measurement** — this is P23's population **P2**. Eight clean waves running |
| 2 | 59 aux (`harness_settings.json` ×39 + `synthetic_meta.json` ×20) | `aux_fingerprint_w17.py` | preservation |
| 3 | 48 prior-wave arm landings (`hf_w18/seedA0` 20, `seedA1` 20, `hf_w18/gate` 8) | `arm_fingerprint_w19.py` | **preservation AND measurement** — 40 of the 48 are P23's population **P1** |
| 4 | wave 21's 4 `f21`/`f21b` synth-validate report files | `f21_fingerprint_w23.py` (new, 60 lines) | **evidence** — the published `D11`/`S1` trajectory that V23-V5 checks against and that justifies excluding `D11` from every denominator was counted on them. *A file class that becomes a denominator is evidence, and a file class that becomes evidence must become a control in the same wave* |

Class 4 is a belt-and-braces: the authoritative copy of the `D11` values is
`docs/synthetic-af-bank-followups-wave21-results.md`, which git controls. Both are named.

### 8.2 The interlock (F75), and it is written by the code that computes the verdict

**Wave 21 held two standards for two interlocks and this wave holds one.**

| marker | written by | when |
|---|---|---|
| `/mnt/d/hf_w23/G23_PASSED` | **`score_v23_w23.py --arm-gate <gate root>`**, and nothing else | it **re-reads the gate** (it does not trust a claim): 8 landings present, all eight `FinalJ` reproducing K8, the `optimize/seed` block reading `NoiseReductionRadius=3` on 8 of 8, and `prov_w23.py` returning 0 **read from `$?` directly, not through a pipe**. It writes the marker on `rc == 0` **and only then**, carrying the BuildId it just read. It **refuses if the marker already exists** |
| `/mnt/d/hf_w23/V23_ARM_READY` | **`v23_arm_w23.sh`**, last, and only if `>= 15` manifest rows exist **per scenario** | carries `manifest=<path>`, `rows=<n>`, `written=<utc>` |

**`gate_w23.sh` writes no marker, and its own `--self-test` asserts that its text does not.** Neither marker is
ever hand-written. If a driver refuses, **that refusal is the result**.

**The two-directional demonstration is free and it runs in this order, FAIL end first:**

```
python3 /mnt/d/hf_w23/score_v23_w23.py --arm-gate /mnt/d/hf_w19/gate    # must exit != 0
test ! -f /mnt/d/hf_w23/G23_PASSED                                     # asserted immediately after
python3 /mnt/d/hf_w23/score_v23_w23.py --arm-gate /mnt/d/hf_w23/gate    # exit 0, writes the marker
```

Wave 19's gate reproduces K8 (its `G19` passed) but its BuildId `763d1476` is on the prior list, so the refusal
is on the **novelty** clause — the same shape as wave 15's `prov_w15.py` refusing to certify itself against wave
14's gate. **A FAIL end that cannot leave a marker behind is a two-directional demonstration of the interlock
itself, and it costs nothing.**

**A self-test that runs AFTER the thing it guards is not a guard** (wave 21 ran `b21_arm_w21.sh --self-test`
while the arm was four datasets deep). Every `--self-test` in this wave runs **before** its arm, and the plan
orders them that way.

### 8.3 Free controls carried unchanged

`prov_w23.py` is `prov_w21.py` carried forward with **only** the two tables edited (twelve BuildIds; four new
dll hashes). *Carrying a self-tested instrument forward unchanged is the point; editing one mid-series is how an
instrument stops being the same instrument.* Its `ConcurrencyCheck` trap is unchanged: `WaitOne(0)` is won by
exactly one of N contenders, so **read it across the whole arm**.

---

## 9. What the wave will be able to say, goal by goal

| goal | at the end of this wave | what it still will NOT say |
|---|---|---|
| **1 — coverage across setups, and bridge the gaps** | a **per-setup census** over 19 synthetic setups × 2 scenarios: which complete, which assertions fail and **per assertion `id`**, every `stoppedReason` quoted, and `D17_cdk14_oiii5`'s behaviour measured rather than remembered. Plus item L: `lumos` and `Panos` measured on the current binary, with a costed bridge proposal for each named gap | nothing about setups outside the bank; nothing about the 22 real-bank runs beyond the gate's 5 and item L's 2 |
| **2 — step-size accuracy** | a **rate with a named denominator**, per scenario, never pooled: how often the final recommended step lands inside the harness's own tolerance band of the behavioural step, in **both** the correct-a-bad-step direction (S1) and the **do-no-harm** direction (S0) — with every miss named. Plus a cross-binary reproduction control on `D11` | it says nothing about `optimize`'s `RecommendedStepSize` **value** (G23-P3 establishes only that the field exists); and nothing across binning factors, where **no `BestJ` may be quoted** (F62) |
| **3 — exposure, and parameters pinned to extremes** | **(a)** the first denominatored pinning audit: pinned axis-instances / (searched axes × landings) on a **licensed, pinned, 40-landing, 20-setup** population, per axis and per setup; the owner's named case reported **as a pair** — nominally at floor, **and whether the floor actually binds** via `EffectiveSensitivityGate`; and the seed-vs-search discriminator saying whether a pin was *driven* or *never moved*. **(b)** the first denominatored population of `exposureRecommendation` and `binningRecommendation` outcomes across the bank | (b) carries **no bar** and the results doc may not act on it — there is no prior for what fraction *should* be capped, and inventing one now is the `S16-A(b)` defect. It is the population a **later** wave writes a bar on |

---

## 10. Budget, order and drop order

| step | instrument | estimate | notes |
|---|---|---|---|
| pre-registration commit | — | — | **before any measurement** |
| build ONE binary into `D:\hf_w23\exe` + dll sha256 + `find -newermt` | `dotnet build -c Release` | **~1 m** | wave 19 measured 32 s; wave 18's two builds 42.6 s total |
| four BEFORE fingerprints | python | ~2 m | all four, or the gate ABORTS |
| **RULE G23** — 8 runs | `optimize --per-run --max-evals 250`, mixed | **~42 m** | measured 41 m 24 s (w19), 42 m 08 s (w18); **~5.2 m/run for a sixth wave** |
| gate scoring + both interlock ends + self-tests | python | ~6 m | FAIL end first |
| **RULE V23** — 40 cells | `synth-validate`, 20 ds × {S0,S1}, `--max-rounds 4` | **~35 m, TIMEBOXED** | wave 21 measured 45–52 s per dataset×scenario×**2 rounds** ⇒ ~25 s/round. S0 is one round; S1 is 2–4. So ~75–125 s/dataset ⇒ **21–35 m**. **The rate at `--max-rounds 4` is UNMEASURED and the timebox is part of the deliverable** (§5's own budget table: `synth-validate` must never be priced from `optimize` or `af-fit`). `timeout 600` per cell + a start deadline |
| **RULE P23** | python over landings on disk | **0 m TestApp**, ~5 m scoring | |
| **item L** — `lumos`, `Panos` | `optimize --per-run` | **~12 m**, conditional | ~6 m/run at the mixed rate |
| AFTER fingerprints + scoring | python | ~5 m | |
| analysis agent + results doc | — | ~40 m | first action is `ls -la --time-style=full-iso` on the artifact root, **timestamp printed at the top** |
| full suite by COUNT | `dotnet.exe test` | ~5 m | pre-wave baseline **3958** (controller-measured; **I did not verify it** and say so) |

**TestApp wall ≈ 1 h 29 m** (1 h 17 m without item L) **against a ~6 h ceiling.**

**Drop order, fixed here so it is not chosen by curiosity:**

| # | drop | saves | cost of dropping |
|---|---|---|---|
| D1 | item L | 12 m | two trap entries stay folklore for a tenth wave; priced |
| D2 | scenario S0 | ~10 m | goal 2 is measured in one direction only, and a recommender that always grows the step would score perfectly. **Recorded as a named hole, not omitted** |
| D3 | V23 datasets from the **tail** of the pre-registered order (`D17` backwards) | ~1.5–2 m each | moves a **denominator**, never a threshold. Below 15 of 19 per scenario, RULE V23 is `V-UNEVALUATED` by name. **`D11` is never dropped** — it is the reproduction control, and dropping it costs the rule its verdict |
| D4 | P23 populations P2 and P3 | ~2 m | P1 is the licensed one and carries the verdict alone |

**Nothing in the gate is droppable.** A partial reproduction stops the wave.

### 10.1 What is NOT run, and priced

| item | price | why not this wave |
|---|---|---|
| **F73's code axis** | **~1 h 50 m of arms + a 42 m gate** — *not* the ~53 m both authority docs quote (§1.2(a)) | 0 for 3 on the owner's goals. **Demoted, with the costing correction recorded in F73**, and the gate gives its 8-run × 1-field slice free (§1.2(c)) |
| **F67's residual thread** | ~30 m of `af-fit` at **both** binning factors + a rule | a second instrument on a second population with an F62 landmine (**no `BestJ` across factors**). It is the best candidate for **wave 24** and is named as such |
| **the out-of-sample pass over wave 18's 40 arm landings** | ~8 m of `af-fit` | **partly superseded**: RULE P23 consumes exactly that population under a rule fixed first, which is what R19 licensed. The `af-fit` half remains unspent and has **no pre-registered question**; inventing one to spend 8 m is manufacturing work |
| **F72 / A5, A7** | ~20 m of NINA UI | genuinely unblocked, and it would smoke-check PR #194's `StarDetectionOptimizerWizardVM.cs` (+201). **Cut on risk, not on price**: F72 blocked nine waves and F76 took eight rounds, and NINA cannot run while any arm here does. It would have to be last, and D1/D2 are cheaper things to spend the tail on |
| **F72 / A8** | — | **declared NOT-SELECTABLE and here is the reason**: `optimize`'s console prints the **exposure** recommender's `capped=` (`OptimizationDiagnosticRunner.cs:1437,1447`) but **not** the step recommender's `WasCapped`, and `StepSizeWasCapped` is a wizard-side quantity. Selecting a saved run whose recommended step is capped therefore needs either a code probe or one wizard session per candidate. **Priced as a prospective item, not dropped silently** |
| **F78 (`SystemParameters.WorkArea`)** | ~25 m | one display on this machine; provably inert and **not exercisable in either direction**. Wave 22 declined to ship it blind and nothing has changed. Not re-opened |
| **F70(a)** | — | owner's call, not decidable by measurement |
| **F70(b′)** | — | **REJECTED by the owner. Not re-proposed.** |
| **F45(b)** | ~3–4 h | behind the RULE S16 fence; rejected on time twice |

---

## 11. Where the framing I was given was wrong

**(1) The 42 bank landings are the wrong PRIMARY population for the pinning audit.** The count is right (22 + 20
= 42, verified) and it is exactly F15's class 1 — but their provenance is **heterogeneous and historical**. F15's
own entry records that `optimize --per-run` wrote into run source folders for thirteen waves and *"destroyed
bobp_m101's historical row mid-investigation and silently re-baselined both banks in wave 3."* A pinning rate
over them is a statement about the series' history, not about the shipped recommender. **The licensed primary is
wave 18's 40 arm landings** — one pinned configuration, 20 setups, and R19's `R-DETERMINISTIC` says in terms
that a later wave may consume them under a rule fixed first. The 42 stay as a **labelled** secondary that cannot
carry the verdict.

**(2) *"Each carries 25 curated detector knobs — how many land on a boundary of their search range?"* is broken
before the data, and the arithmetic proves it.** A stock `optimize` searches **12** of the 25; the other 13 are
written at seed values, and **two of those seeds sit exactly on an axis bound by construction**
(`DonutMaxStreakEccentricity` seeded `1.0` = axis max; `DonutSaturationBloomRadius` seeded `0.0` = axis min). A
25-knob audit reports ≥ 2 pinned instances on **100 %** of every landing that can exist — F68's exact shape,
caught by arithmetic at pre-registration rather than after the data. The denominator must be the **searched**
axes, **resolved per landing** from `Provenance.CommandLine`.

**(3) *"sensitivity at 0"* names a key that does not exist and a bound that is not constant.** The landing key is
**`BrightnessSensitivity`** (the optimizer variable is `Sensitivity`, renamed at
`OptimizedStarDetectionSettings.cs:306`), and its floor is **runtime-injectable** via `--sensitivity-floor`
(`OptimizerVariable.cs:145,150`; `OptimizationDiagnosticRunner.cs:163,479`) and **is not persisted in the
landing**. Worse for the clause as posed: `BrightnessSensitivity = 0` may be **entirely inert**, because
`EffectiveSensitivityGate = max(Sensitivity, PeakResponse × MinEffectiveClipMultiplier)` and the source's own
worked example enforces a gate of **9.81** from a nominal zero. The clause must be a **pair**, and it now is.

**(3b) And a THIRD instance of the same defect, inside the base set, found by the scorer's own self-test.**
`HotpixelThresholdingEnabled` is a **boolean** axis. A boolean is always on a bound, so counting it puts a
permanent floor under the pinned rate on 100 % of any population. The first draft of `score_p23_w23.py` counted
it and **could not return `P-CLEAN` on a deliberately clean fixture** — caught at 15 of 19 self-test branches,
before any arm ran. It is recorded here rather than quietly repaired, because the self-test catching it *is*
the argument for writing one before the arm rather than after.

**(4) The F68 permission sentence has two readings and I satisfy both, rather than the convenient one.**
*"No clause may be written over it until a rate exists"* can mean a measured **timing** rate (45–52 s — yes) or,
following the F68 citation, that the clause's **statistic** must be a rate with a named denominator rather than
an absolute count over an n = 1 probe. **A wave that ran a second single-dataset probe would satisfy the first
and fail the second.** Twenty datasets × two scenarios satisfies both.

**(5) I agree on demoting F73's code axis, and the agreement comes with a correction the register should keep.**
Both authority documents price it at **~53 m**. That price is wrong: it assumes wave 19's `reA0` can be the old
side, which makes the treatment *everything shipped since wave 19*. The honest arm is **two binaries and two
20-dataset passes ≈ 1 h 50 m** plus a gate. And an **authored** inert change would not have answered the
question at all — it recreates wave 19's own defect, where the expensive branch is reachable only from noise.

**(6) The gate serves none of the three goals, and this design says so** rather than claiming coverage it does
not have. Its justification is the charter's — it is the stopping control — plus one free measurement (§1.2(c)).

**(7) Two prices in the handoff are stale and one is now superseded.** The `synth-validate` row of §5's budget
table still reads *"NO measured rate"*; wave 21 measured one and this design uses it. The *"out-of-sample pass
over wave 18's 40 arm landings, ~8 m"* is **partly superseded** by RULE P23, which consumes that exact
population under a rule fixed first — the `af-fit` half remains, unspent and question-less.

**(8) I did not verify the suite baseline of 3958.** It is taken from the controller's measurement and quoted as
such. Wave 22's results doc records **3933** and `develop` post-merge **3922**; the difference is PR #194's new
tests. **The wave's final gate is a COUNT read out of the log, never the tick (F37)** — and if the count is not
3958, the delta is named before anything is called green.

---

## 12. The traps this wave is walking past, baked into the drivers rather than remembered

- **Pass scorers WSL paths** (`/mnt/d/...`). A Windows path yields UNEVALUATED and looks exactly like a failed arm.
- **`< /dev/null` on every `TestApp.exe` invocation inside a loop.** It eats a `while read` loop's stdin; wave
  13's I2 "completed" in 25 s having scored 1 of 19 runs.
- **Assert the POPULATION SIZE**, always, as its own line.
- **Keep logs ASCII.** A Unicode character in a redirected log arrives as the single byte `0x1A` on this
  machine's console code page. Newtonsoft writes NaN/Infinity as the **strings** `"NaN"`/`"Infinity"`.
- **One bank path contains a SPACE** (`D:\Autofocus Bank`).
- **`synth-validate` exits 1 when any cell FAILS, and that is a RESULT, not a broken arm.** Inheriting wave
  21's *"non-zero exit ⇒ NOT-RUN"* rule would have thrown away exactly the cells goal 1 exists to find. Exit 0
  and 1 are scoreable and the code is a manifest column; only exit 2, a timeout, a crash or a missing report is
  NOT-RUN. **A scoreability rule inherited from a different command is not a scoreability rule.**
- **`stoppedReason` case 4 carries a U+2014 EM DASH**, and a Unicode byte in a redirected log arrives as `0x1A`
  on this machine's console code page. Every scorer ASCII-folds `stoppedReason` and `detail` before printing,
  and reports how many characters it folded.
- **Only one `TestApp.exe` at a time. Never NINA during a pinned arm. No fan-out.**
- **Never rebuild an arm's directory mid-wave** (F53(c)). Every binary is planned up front; this wave has one.
- **`strings` is two instruments** — type names in `#Strings` (UTF-8), literals in `#US` (UTF-16, `strings -el`).
  `grep AtrousWaveletFast` returns 1 on every binary this project has produced. **Read the `DetectorVersion`
  field.**
- **A landing is not a harness settings file** and carries each knob exactly once; a **converted** file carries
  each knob twice and in Simple mode the `Options` copy is dead (F71). No clause here reads a converted file.
- **The deployed-plugin check before any UI claim** (F77) — and wave 22's refinement: **the lock is acquired at
  plugin LOAD, not at NINA start**, so the mtime/hash check can pass while a long-lived session still runs older
  code in memory. This wave makes no UI claim, so the check appears only as the reason item F72 was cut.
- **The write-up's FIRST action is `ls -la --time-style=full-iso` on `/mnt/d/hf_w23`, with the timestamp printed
  at the top of the results doc.** Wave 19's doc was falsified by an artifact written 33 seconds before its own
  commit. Any row still open at that instant is marked open **in place**.
