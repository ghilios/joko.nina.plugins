# Wave 24 — design (pre-registration)

**Written 2026-08-12, before any wave-24 measurement exists.** Every rule, clause, threshold, population,
statistic, aggregation, empty-set answer and validity gate below is fixed here and is not re-decided after the
data. An unsatisfiable clause is a **finding**, not something to repair and re-score.

Predecessor: [`docs/synthetic-af-bank-followups-wave23-results.md`](synthetic-af-bank-followups-wave23-results.md)
(the evidence base) and [`…-wave23-design.md`](synthetic-af-bank-followups-wave23-design.md) (the shape).
Charter: [`docs/waves22+-handoff-prompt.md`](waves22+-handoff-prompt.md),
[`docs/waves22-27-autonomous-prompt.md`](waves22-27-autonomous-prompt.md).
Register: [`docs/followups.md`](followups.md).
Plan: [`plans/synthetic-af-bank-followups-wave24-plan.md`](../plans/synthetic-af-bank-followups-wave24-plan.md).

---

## 0. The vessel decision, first and in writing

**Wave 24 CONTINUES on `ghilios/synthetic-af-bank-followups-wave23` and appends a section to PR #195. No fresh
branch.**

The charter requires this be decided before any measurement and not defaulted into. The reasoning, and it is the
mirror image of wave 23's:

| | |
|---|---|
| wave 23's situation | PR #191 had **merged**. There was no standing vessel, so continuing was impossible and a fresh branch was the only honest answer |
| wave 24's situation | PR #195 is **open and unmerged** at `e7a5ee6`. There *is* a standing vessel |
| the decisive fact | **wave 24's binary must carry `e7a5ee6`.** That commit is [F79](followups.md)'s ASCII fix — 20 TestApp files — and wave 23 shipped it explicitly *"at the top of wave 24, before that wave's build"*. A fresh branch cut from `develop` would not have it; a fresh branch cut from `ghilios/…-wave23` is a stacked PR against an unmerged parent, which gives the owner two reviews of one contiguous body of work and a merge order to remember |
| the cost of continuing | PR #195 grows a second section. That is the cost the charter's §0.3 anticipates and it is small |
| what would change the answer | if the owner merges PR #195 before the wave-24 pre-registration is committed, wave 24 cuts a fresh branch from the resulting `develop`. **The controller checks `gh pr view 195 --json state` immediately before committing and takes that branch instead.** Recorded here so the contingency is pre-registered rather than improvised |

---

## 1. Where the framing I was given is wrong. This is the most important section in the document.

The controller's prompt says wave 24 is *"the first wave in a long series that would **change what the product
does on the next autofocus run**, and that is why it is the right one."* **That sentence is false for the fix it
is attached to, and I found out before writing a rule.**

### 1.1 [F26](followups.md)'s livelock is in the TEST HARNESS, not in the shipping plugin

F26's title says *"A stuck binning recommendation starves the step update indefinitely"* and its body says
*"**The wizard** applies binning first and defers the step by a round."* Wave 23 located the mechanism at
`SynthValidateRunner.cs:829` / `:836-848`. That file is
`Joko.NINA.Plugins/TestApp/SynthValidateRunner.cs` — **the `synth-validate` validation harness**. It ships in
`TestApp.exe`. It is not in `NINA.Joko.Plugins.HocusFocus.dll` and NINA never loads it.

I checked the shipping side rather than assuming it, and the wizard does not have this defect:

```
grep -rn "deferr|Binning first|binningDiffers" --include=*.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/
   ->  four hits, ALL unrelated (window re-fit, an out-of-bounds rejection, a viewport latch)
```

`StarDetectionOptimizerWizardVM.cs:4060-4085` builds ONE `OptimizationSummary` that carries
`RecommendedStepSize` **and** `RecommendedDetectionBinning` **together**, from the same fit, in the same pass.
There is no round loop, no deferral, and no `binningDiffers` branch anywhere on the path that produces a
recommendation. The wizard's `DetectionBinningDiffers` / `DetectionBinningPendingApply`
(`:163-170`) are *display and Accept-path* predicates, not a scheduler. **The user is shown both
recommendations at once and drives the next round by hand.**

**So `D08_c11_2800mm`/S1's four-round livelock is a property of the instrument, not of the product.** Fixing it
does not change what NINA does on the next autofocus run. It changes what the series' own measuring device
reports about the recommender.

**That does not make it not worth doing — it makes it worth doing FIRST, and for a different reason than the one
I was given.** Wave 23's headline number, `V23-G` S1 **13 of 17**, was measured on an instrument that spends
four of four rounds refusing to apply a correctly-computed step on at least one cell. Every future
`synth-validate` arm about goal 2 inherits that. **Repairing the instrument before wave 25 measures goal 2 again
is the right order; presenting it as a product bridge is not.** This document ships it as an instrument repair
and says so in the results obligations (§9).

### 1.2 The product defect that IS there is a different one, and it needs no threshold

`StepSizeRecommender` **is** shipping plugin code
(`Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/StepSizeRecommender.cs`) and the wizard calls it at
`StarDetectionOptimizerWizardVM.cs:4062`. Its three degenerate exits (`:234`, `:240`, `:253` — the guards are
`:233`, `:239`, `:252`) all return

```csharp
private static StepSizeRecommendation Degenerate(int currentStepSize, int? focuserMaxStep) {
    return new StepSizeRecommendation {
        StepSize = ClampStep(currentStepSize, focuserMaxStep),
        OffsetSteps = DefaultOffsetSteps,
        HalfWidth = double.NaN
    };
}
```

and every other property takes its initializer — `WasCapped = false`, `WasDetectBounded = false`,
**`SampledHfrRange = double.NaN`**, `CappedGrowthRatio = NaN`.

The wizard then writes `RecommendedStepSize = recommendation.StepSize` into the summary and shows it. **On a
fit the recommender could not use, the user is told "recommended step size: 21" — a number that means "I held
what you already had", presented in the same field, with the same authority, as a measured recommendation.**

> **This is [F79](followups.md)'s failure shape, in the product.** F79 is *"it fails closed to zero: it reports
> 'the feature is absent' rather than 'I could not look'"*. `Degenerate` fails closed to *the current value*:
> it reports "this is my recommendation" rather than "I could not measure this". The whole series is built on
> keeping COULD-NOT-LOOK a separate state from a measurement, and the product does not.

**And the field wave 23 proposed to gate a fix on is NaN exactly where the gate would need it.** Wave 23's
Bridge A says *"the gate must be on `sampledHfrRange`, not on R²"*. `SampledHfrRange` is assigned **only** on the
non-degenerate path (`:295`). On the `:252` exit `bestFit.Outputs` is present and measurable, and it is not
measured. **Wave 23's Bridge A is not implementable as written** — its decision variable does not exist on the
branch it is meant to decide.

### 1.3 So Bridge A does not ship in wave 24, and this is a decision about the fence, not about nerve

Three independent reasons, any one sufficient:

1. **Its threshold cannot be chosen blind from anything now on disk.** Every degenerate example in evidence is
   at the **narrow** end (`D01`/`D02`/`D03` S1, all `StepFactor = 0.25`). [F25](followups.md)'s own wide-end
   example, `D05_tec140_1000mm`/S2, is **published** — trajectory `140 → 240 → 36`, R² `−0.223` then `1.000` —
   so it cannot be blind either. A directional threshold picked from narrow examples only, and scored on those
   same narrow examples, is the [F14](followups.md)/[S16](waves17-21-handoff-prompt.md)/[D20](followups.md)
   fence exactly.
2. **Its decision variable is not measurable yet** (§1.2). You cannot pre-register a bar on a field that is NaN
   on the branch the bar is about.
3. **Its risk population costs a second paired arm the window cannot buy** (§7.3 prices it).

**What wave 24 ships instead is the instrument that makes wave 25's threshold choosable blind**: measure
`SampledHfrRange` on the degenerate path, and give the recommendation a first-class reason for the degeneracy.
That is [F76](followups.md)'s rule applied one wave earlier than it was last time — *"instrument first when the
mechanism is not observable"*, *"a fix that cannot report whether it engaged is not finished"* — and it costs
about twenty lines.

### 1.4 [F26](followups.md)'s own "next step" does not fix the form wave 23 reproduced

F26's next step, verbatim: *"if the binning recommendation **has not changed the applied value** for N
consecutive rounds, stop deferring and let the step update proceed."*

On wave 23's reproduction the applied value **changes every single round** — `2 → 1 → 2 → 1`. A counter keyed on
*"the applied value has not changed"* **never fires** on the trace F26 is now cited for. The wording matches
F26's *original* 2026-08-03 evidence, where the recommendation was stuck at `1` for four rounds; it does not
match the oscillating form. **This is recorded as a correction to F26 in §9 and it is why the rule this wave
ships is not F26's rule as worded.**

### 1.5 Four smaller corrections to the prompt and to wave 23

| claim | correction |
|---|---|
| *"`Degenerate` is literally `StepSize = ClampStep(currentStepSize)`"* (wave 23 §4.1) | it is `ClampStep(currentStepSize, focuserMaxStep)` — two arguments. Immaterial in the harness (`focuserMaxStep` is always `null` there, `SynthValidateRunner.cs:783,1233`), material if quoted |
| *"four conditions (`:233`, `:239`, `:252`, and the `NaN` from `FindHalfWidth:470`)"* | there are **three** `Degenerate` call sites. `FindHalfWidth` has **two** NaN returns (`:462` non-finite model value, `:470` budget exhausted) and both feed the third guard at `:252`. Three exits, four causes |
| *"the existing growth cap, ×1.714"* | 1.714 is **derived and reported**, not a constant. The constant is `MaxHalfWidthSampledHalfSpanMultiple = 1.5` (`:185`); `CappedGrowthRatioOf` returns `1.5 × executedPointsPerSide / pointsPerSide`, which is `1.714…` only for a 4-offset sweep at `PointsPerSide = 3.5`. It is `2.143` at 4 offsets + 1 recovery. **A pre-registration that quotes 1.714 as "the cap" has quoted one sweep geometry's answer as a constant** |
| the prompt's *"~30 m"* for the deferral bound and *"~1 h"* for the gate | both are code-and-test prices and both look right. They are **not** the wave's cost. The wave's cost is dominated by the paired arm the fix must be scored on, which neither number includes. §7 prices the wave, not the diff |

### 1.6 Is a fix wave the right call at all?

**Yes, but a narrower one than proposed, and the honest name for it is "instrument repair plus observability".**
The cheaper-question alternatives, each priced and each losing:

- **The P23 flat-direction perturbation arm** (wave 23 §5.2, ~30 m, no product code). Genuinely interesting and
  the deepest open question in goal 3(a). It loses on the same ground wave 23 gave: nothing the owner would *do*
  differs until it is known. It stays wave 25's strongest non-product candidate.
- **Item L** (`lumos`/`Panos`, ~12 m). Two trap entries become measurements. **It is under thirteen minutes, the
  charter says an item under two minutes is never dropped, and it is carried in this wave as item L24** (§6),
  last, conditional on the clock. Ten waves of folklore is long enough.
- **F67's residual / F73's code axis.** Wave 23 already ruled both out for wave 24 on decision value and price.
  Nothing has changed.
- **"There is nothing left worth a wave."** Not honest here. §1.1 and §1.2 are two findings from source alone,
  before any measurement, and both are register corrections.

---

## 2. THE PRE-REGISTRATION FENCE — what I read, what I did not, and what binds the analysis agent

The evidence base for this wave is **already on disk** (`/mnt/d/hf_w23/`), so the same hazard wave 23 faced with
RULE P23 applies here with more force: this is a *fix* wave, and a fix scored on the cells that motivated it is
not a measurement.

| | |
|---|---|
| **MAY read, and did** | wave 23's published results and register entries; **source**, in full; the wave-23 report **schema** and the values of `terminal.expectedDetectionBinning` / `expectedExposureSeconds` / `expectedStepSize` (these are `SynthBankDerivations.DeriveExpectedOptimal` outputs — deterministic functions of the checked-in spec, identical in every scenario, and **no clause below reads them as an outcome**); the per-cell **wall times** from `run.log` mtimes, for pricing; the `applied.reasons` **deferral census** over wave 23's 56 rounds, for satisfiability |
| **MAY read, and did, and it is declared as a value read** | `D08_c11_2800mm`/S1's four round records in full (`bootstrap.detectionBinning`, `binningRecommendation.*`, `applied.reasons`, `stepRecommendation.sampledHfrRange`). This is the cell that motivated the fix and **reading it is how the fix was designed**. It is therefore a **labelled reproduction control, excluded from every denominator in every rule below** — the treatment wave 23 gave `D11` |
| **MAY NOT read, and did not** | any value from **scenario S4 on any dataset** — the population RULE B24 is scored on. S4 has never been run in any wave and appears in no register entry and in no results document. `grep -c 'S4' docs/followups.md` was **not** run as a value probe; the scenario's *definition* was read from `SynthValidationScenarios.cs:111-118`, which is source |
| **counts only** | `EXPECTED_S4_APPLICABLE = 7` is derived from the **spec-derived** `expectedDetectionBinning == 2` predicate (`SynthValidationScenarios.cs:115`), on the 20 datasets. A count of which datasets a scenario *applies to* is addressability ([F74](followups.md)), not an outcome |

**The cells that are NOT blind, named here, before the data, and excluded from every denominator:**

| cell | why it cannot be blind | how it is handled |
|---|---|---|
| `D08_c11_2800mm`/S1 | motivated P2; its four rounds were read to design the rule | **labelled reproduction control**, run BEFORE and AFTER, reported in full, **in no denominator** |
| `D08_c11_2800mm`/S4 | same **dataset** as the motivating cell, different scenario | **in S4's population, and labelled.** `B24-A` is reported **twice**: over all applicable S4 cells, and over S4-minus-`D08`. Both denominators printed; the branch table reads the **minus-D08** rate |
| `D01`/S1, `D02`/S1, `D03`/S1 | motivated P1; wave 23 published their trajectories | run AFTER only, as **labelled degenerate-fit controls** for RULE D24, which **carries no bar** |
| `D11_rc10_585_afbin2`/S1 | published by wave 21 and again by wave 23 | **the cross-binary reproduction control**, unchanged in role from wave 23 |
| `D05_tec140_1000mm`/S2 | published by [F25](followups.md) (`140 → 240 → 36`) | **not run.** S2 is run only over the seven S4-applicable datasets (§4.4), which do not include `D05` |
| `D12_c14_585_afbin2`/S6 | published by [F26](followups.md) (`35 → 60 → 60 → 74`, re-measured `→ 80`) | **not run.** S6 is not in this wave |

---

## 3. What SHIPS, and the ship rule, fixed and committed BEFORE the data

Three changes, in one commit, landing **before the build**. The charter's §0.1 permits an unconditional ship
where the ship is not what a rule decides; two of these three are in that class and say so.

### 3.1 P1 — the product change: a degenerate recommendation says so

**File:** `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/StepSizeRecommender.cs`
(shipping plugin), mirrored into `Joko.NINA.Plugins/TestApp/SynthValidationReport.cs` and
`SynthValidateRunner.cs`.

| | |
|---|---|
| (a) | Add `public string DegenerateReason { get; set; }` to `StepSizeRecommendation` (default `null`). `Degenerate(...)` takes the reason as a parameter and sets it. The three call sites pass three distinct constants: `"no-fit"` (`:233`), `"non-finite-vertex"` (`:239`), `"half-width-unresolved"` (`:252`) |
| (b) | `Degenerate(...)` measures `SampledHfrRange = MeasureSampledHfrRange(bestFit)` **when `bestFit` is non-null** and leaves it `NaN` otherwise. `MeasureSampledHfrRange` already returns `NaN` for a null/empty/non-finite `Outputs` (`:305-319`), so the guard is only against the `bestFit == null` path at `:233` |
| (c) | Mirror `degenerateReason` into `StepRecommendationSnapshot` (`SynthValidationReport.cs:67-85`) and populate it at `SynthValidateRunner.cs:785-790` |
| (d) | Surface it on the wizard summary: `OptimizationSummary.StepSizeDegenerateReason`, beside the existing `StepSizeSampledHfrRange` / `StepSizeCappedGrowthRatio` (`StarDetectionOptimizerWizardVM.cs:113-185, 4062-4085`), **and show it in the wizard's step block** so a held step is visibly a non-measurement |

**P1 changes no recommended step size, no `OffsetSteps`, no `WasCapped`, and no objective.** It fills two fields
that were previously `null`/`NaN` and adds a line of UI text. **Its inertness is measured, not asserted** —
`G24-1` and `G24-P3b` below.

**Ship rule for P1: UNCONDITIONAL.** No clause of `G24`, `B24`, `R24` or `D24` reads `degenerateReason` as a
threshold. `D24` reports it with no bar. This is wave 20's D1/D2/D3 shape and wave 23's F79 shape, and it is
stated here so the results document cannot present it as a verdict harvested from the data.

### 3.2 P2 — the instrument repair: the binning-revisit deferral bound

**File:** `Joko.NINA.Plugins/TestApp/SynthValidateRunner.cs` (harness only — see §1.1).

**The rule, fixed here:**

> The round loop defers the exposure and step updates for a binning change **only when the recommended factor
> has not already been in effect during this scenario.** On a *revisit* — the recommended factor equals a factor
> the run has already been at — the binning change is applied **and** the exposure/step block runs in the same
> round.

Implementation shape (the code agent may vary spelling, not semantics):

- `ScenarioRunState` gains `HashSet<int> VisitedDetectionBinningFactors`, seeded at `:484-490` with the initial
  `DetectionBinningFactor`.
- At `:829`, when `binningDiffers`: compute `revisit = Visited.Contains(rec)`. Apply the binning change and add
  `rec` to `Visited` in both branches. If `!revisit`, defer as today. If `revisit`, do **not** defer — fall
  through to the exposure/step block.
- Record it: `applied.reasons` gains `"binning X -> Y (already measured at factor Y; not deferring)"`, and the
  two new booleans in §3.3 are set.

**Why the revisit rule and not F26's counter.** §1.4: F26's counter is keyed on *"the applied value has not
changed"* and never fires on the oscillating form. A consecutive-deferral counter (`N = 2`) would fire, but it
also fires on a **legitimate monotone walk** — `1 → 2 → 3` is two deferrals in a row and both are honest,
because no measurement at factor 3 exists yet. The revisit rule changes behaviour **if and only if the binning
recommendation has entered a cycle**, which is precisely the defect, and it is derived from the deferral's own
stated justification (`:830-832`: *"changing binning invalidates the exposure measurement (and the HFR-derived
step geometry) this round just took"* — a justification that is empty for a factor already measured).

**Ship rule for P2 — this one IS decided by a rule.** P2 ships **only if RULE R24 returns `R-PRESERVED`**
(§5). If `R24` is anything else, **P2 is reverted from the branch before the wave is pushed**, and the wave
reports it as a costed recommendation. `R24` is the do-no-harm rule and it is written to be able to unship this
change; see §5.3 for exactly what "unship" means mechanically.

### 3.3 P3 — the observability the register has been owed since 2026-08-03

**File:** `Joko.NINA.Plugins/TestApp/SynthValidationReport.cs` + `SynthValidateRunner.cs` (harness only).

| field | where | why |
|---|---|---|
| `binningRecommendation.currentFactor` (int) | `BinningRecommendationSnapshot`, `:109-113` | F26's owed *"there is no `currentFactor`"*. The factor in effect when the recommendation was computed |
| `binningRecommendation.appliedFactor` (int) | same | F26's owed *"there is no `appliedFactor`"*. The factor in effect at the **end** of the round |
| `applied.stepDeferredByBinning` (bool) | `AppliedSnapshot`, `:117-126` | the deferral, as a first-class fact instead of a substring of `reasons[]` |
| `applied.binningDeferralBoundReached` (bool) | same | **P2's engagement marker.** [F76](followups.md): a fix that cannot report whether it engaged is not finished |

**Ship rule for P3: UNCONDITIONAL**, on the same ground as P1 — no clause thresholds any of them; `B24` reads
`binningDeferralBoundReached` as a *mechanism* fact and `D24` reads the rest as a census.

> **Why `appliedFactor` is not merely a convenience, stated precisely.** The current factor *is* already
> recoverable for round `N` from `rounds[N+1].bootstrap.detectionBinning`. It is **not** recoverable for the
> **last** round, which has no successor — and the last round is exactly where a terminal state is decided.
> Wave 23's own §4.2 inferred the `D08` starvation from *"the bootstrap step never moving"* because of this.
> **`B24-V4` below turns that into a free control**: on every round where both are available, the new
> `appliedFactor` must equal `rounds[N+1].bootstrap.detectionBinning`. A new field that disagrees with the
> independent reconstruction is a defect in the field, not a finding about the product.

### 3.4 Tests — every one shown to FAIL against the pre-change source

| test | pins | must fail pre-change because |
|---|---|---|
| `StepSizeRecommenderTests.Degenerate_ReportsItsReason_AndMeasuresSampledHfrRange` | P1(a)(b) on all three exits | `DegenerateReason` does not compile / is `null`, and `SampledHfrRange` is `NaN` on the `:252` exit where `Outputs` exists |
| `StepSizeRecommenderTests.NonDegenerateRecommendation_HasNoDegenerateReason` | P1's inertness | would pass pre-change — **it is a companion, not the failing test**, and is labelled as such |
| `SynthValidateRunnerBinningDeferralTests.RevisitedBinningFactor_DoesNotDeferTheStep` | P2 | the loop defers on every `binningDiffers`, so the step is unchanged |
| `SynthValidateRunnerBinningDeferralTests.FirstVisitToANewFactor_StillDefersTheStep` | P2's do-no-harm half | would pass pre-change — companion, labelled |
| `SynthValidateRunnerBinningDeferralTests.OscillatingRecommendation_AppliesTheStepWithinThreeRounds` | P2 end-to-end on a synthetic oscillation | four rounds of no-op pre-change |
| `SynthValidationReportSchemaTests.RoundRecord_CarriesTheBinningAndDeferralFields` | P3 | the fields do not exist |

**New API breaks a plain revert**, so the failing-test demonstration uses the charter's second form where
needed: **byte backups and a named mutant**, never `git checkout --`. The tree will carry the uncommitted
pre-registration and the results document; a VCS revert takes them (wave 20's trap, and wave 23 §12.1's).

**Suite baseline: 3960 by COUNT** (wave 23 §12, controller-verified). Wave 24 adds the tests above; the final
gate is a COUNT read out of the log, never the tick ([F37](followups.md)), and the delta is named before
anything is called green. **3922, 3933 and 3958 in older documents are stale.**

---

## 4. RULE G24 — the gate. One new binary, the fourteenth.

`optimize --per-run --max-evals 250`, mixed real + synthetic, sequential, one `TestApp.exe`, no NINA, no fan-out.
`--settings D:\hf_w11\pinned_settings_w11.json` (md5 `a67ffc06164c81613aef5c4f8324b9b8`) **and** `--profile-id
ce3f3e63-8fd3-4b72-a0ca-d90db9441382`, both named in the driver header. **~42 m.**

**Which goal does it serve? None of the three, and that is stated rather than dressed up.** It is the stopping
control on the binary every arm runs. **A partial reproduction stops the wave. Nothing in the gate is
droppable.**

### 4.1 Do I expect the gate to move, and what does it mean if it does?

**I expect it not to move, and I have said so before the data.** The prompt asks this directly and it deserves a
mechanism, not a hope:

- `FinalJ` is the optimizer's objective. `StepSizeRecommender.Recommend` is called **once, after the search
  completes**, at `StarDetectionOptimizerWizardVM.cs:4062` and its headless analogue. It cannot feed back into
  `J`.
- P2 and P3 are in `SynthValidateRunner.cs` / `SynthValidationReport.cs`, which the `optimize` path does not
  reference at all.
- P1 fills two previously-`null`/`NaN` fields on the recommendation object and adds no arithmetic to any
  evaluated candidate.
- The binary also carries `e7a5ee6` ([F79](followups.md), 20 TestApp files, console string literals only).

**If `G24-1` moves, the wave stops and that is the finding.** It would mean one of: P1 is not inert (a real and
important result about `StepSizeRecommender`'s reach); F79's ASCII sweep touched something that is not a
console literal; or the thirteen-binary reproduction has finally broken for an unrelated reason. **All three are
worth more than the rest of this wave and none of them is repaired mid-wave.**

### 4.2 The clauses

| clause | P (artifact **and** field, **and which copy**) | S | A | E | threshold |
|---|---|---|---|---|---|
| **`G24-1`** | `<gate>/<run>/**/optimized_settings.json` → `FinalJ`, the landing **found** by `w24_landing_find` (a `find`, never a template) | equality to 6 dp with the K8 constants below; `repr()` bit-identity **reported** | 8 of 8 | a missing or unreadable landing is COULD-NOT-LOOK, named, and G24 **FAILS** — never "0 of 8" | **8 of 8, or the wave stops** |
| **`G24-2`** | the same landings | the aggregate summary block is present | 8 of 8 | as above | 8 of 8, asserted in the driver before any scoring |
| **`G24-P1`** | `<gate>/<run>.log`, the `PARAMS-DUMP optimize/seed` block extracted with `awk` **first** | `NoiseReductionRadius == 3` | 8 of 8 | absent block ⇒ COULD-NOT-LOOK | 8 of 8. The anti-leak clause: `/mnt/d/hf_w18/part2_w18.patch` must not have leaked into the tree |
| **`G24-P2a/b/c`** | the same logs | exactly one `optimize/detected`, `optimize/baseline`, `optimize/seed` block each | 8 of 8 | as above | 8 of 8, read **in Python on bytes**, never by `grep` ([F79](followups.md)) |
| **`G24-P3a`** | the landings → `RecommendedStepSize` | present and finite | 8 of 8 | non-finite ⇒ FAIL | 8 of 8 |
| **`G24-P3b`** — **NEW, and it is P1's inertness measurement on the shipping path** | the same field | **equality with wave 23's eight measured values**, fixed here: `toml999 16`, `CWhiteFocus 101`, `uneven 488`, `muggsie 550`, `mccomiskey 31`, `D18_m24_deep_shed 18`, `D19_cygnus_deep_shed 35`, `D20_m24_bright_control 19` | 8 of 8 | a missing value ⇒ COULD-NOT-LOOK, named, and P3b FAILS | **8 of 8.** P1 touches the class that produces this number. If it moves, P1 is not inert and the ship rule in §3.1 is void |
| **`G24-P4`** — **NEW, and it measures the PREDECESSOR's deliverable** | the raw **bytes** of all 8 `<gate>/<run>.log` | count of bytes `>= 0x80` | per file, and the total | a log missing ⇒ COULD-NOT-LOOK, named | **0 non-ASCII bytes in 8 of 8.** Wave 23 shipped [F79](followups.md); this is the first gate built after it and the cheapest possible verification |
| **`G24-3a…3f`** | read **across** the arm, as FIELDS, never `strings` | `BuildId` cardinality 1 **and novel** against the thirteen recorded ids; `ProfileId` cardinality 1; `FitInputs` cardinality 1 and equal to `MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid`; `DetectorVersion == 2` on all 8; `ConcurrencyCheck == exclusive` on all 8 | as stated | any unreadable ⇒ COULD-NOT-LOOK | all |

**K8, the eight constants, quoted at full precision from wave 23 §1.1:**

```
toml999              0.9957838768299878     mccomiskey            0.9767460801208465
CWhiteFocus          0.9960675916058808     D18_m24_deep_shed     0.9998815090506263
uneven               0.9963677194179505     D19_cygnus_deep_shed  0.9994870586135448
muggsie              0.9971948738498605     D20_m24_bright_control 0.9997378027339423
```

**`G24-P4`'s FAIL end is measured on real artifacts, and it is already known.** Wave 23 §2 established that
**8 of 8** gate logs in **twelve** wave roots carry the byte `0xE5`. The demonstration runs
`ascii_census_w24.py --gate /mnt/d/hf_w23/gate`, which must report **8 files with a non-zero count and exit
non-zero**, before the same instrument is pointed at wave 24's own gate. That satisfies the charter's *"a clause
about the wave's own deliverable must have its FAIL end measured on real artifacts"* — and it is the rare case
where the FAIL end is a **published prior result**, so the instrument is checked against a known answer.

### 4.3 The branch table, fixed before the data

```
G24-1 == 8 of 8 AND G24-P3b == 8 of 8?      no  -> G-STOP. THE WAVE STOPS. No arm runs. The finding is
                                                    named (which clause, which run, which value) and the
                                                    binary's provenance is published as-is.
G24-2 / P1 / P2a-c / P3a / 3a-3f all 8of8?  no  -> G-PARTIAL. The wave stops. A partial reproduction is
                                                    not a pass.
G24-P4 == 0 bytes on 8 of 8?                no  -> G-ASCII-INCOMPLETE. This ALONE does NOT stop the wave:
                                                    it is a measurement OF WAVE 23's ship, not of this
                                                    wave's binary's fitness. It is reported, the offending
                                                    files and byte offsets are named, and F79 is re-opened.
otherwise                                       -> G-PASS. The arms may run.
```

**`G24-P4` is deliberately non-blocking and that is a decision with a reason.** Blocking the wave on a
predecessor's cosmetic deliverable would let a log-text defect cost this wave its arms. Both branches are
reachable and both are informative, and the asymmetry is fixed here so it is not decided by the result.

### 4.4 The interlock ([F75](followups.md))

`/mnt/d/hf_w24/G24_PASSED` is written by **`score_g24_w24.py --arm-gate <gate root>`**, and by nothing else. It
re-reads the gate (it does not trust a claim): 8 landings present, all eight `FinalJ` reproducing K8, the
`optimize/seed` block reading `NoiseReductionRadius=3` on 8 of 8, `G24-P3b` 8 of 8, and `prov_w24.py` returning
0 read from `$?` **directly, not through a pipe**. It writes the marker on `rc == 0` **and only then**, carrying
the `BuildId` it just read, and it **refuses if the marker already exists**. The arm drivers ABORT without it.

**The two-directional demonstration runs FAIL end first, and its output is KEPT this time** — wave 23 §8's
recorded caveat was that three demonstrations left no artifact:

```
python3 /mnt/d/hf_w24/score_g24_w24.py --arm-gate /mnt/d/hf_w19/gate 2>&1 | tee /mnt/d/hf_w24/g24_failend.txt
test ! -f /mnt/d/hf_w24/G24_PASSED          # asserted immediately after, and TEE'd
python3 /mnt/d/hf_w24/score_g24_w24.py --arm-gate /mnt/d/hf_w24/gate 2>&1 | tee /mnt/d/hf_w24/g24_passend.txt
```

Wave 19's gate reproduces K8 but its `BuildId 763d1476` is on the prior list, so the refusal is on the
**novelty** clause. **Every `--self-test` in this wave runs BEFORE the thing it guards**, and the plan orders
them that way.

---

## 5. The arms and their rules

### 5.1 The two binaries, planned up front ([F53](followups.md)(c))

| | binary | provenance | never rebuilt |
|---|---|---|---|
| **B13** — the BEFORE side | `D:\hf_w23\exe` | **already on disk and already published.** `TestApp.dll` sha256 `82491ac79ce3d9216b4fa0d32c3daeb41aee460e845377c77f4ec9572cfc9ccf`, `NINA.Joko.Plugins.HocusFocus.dll` sha256 `fe9db95c52f25e7e7fc6d9746a96986cfb545f0eb5011c4f03a8719292ddb998`, `BuildId 7927513b6493438aa73da5b91715fe0d`. It passed `G23` | **it is not rebuilt, moved, or written into.** The BEFORE arm's `--out` is `D:\hf_w24\before\…` |
| **B14** — the AFTER side | `D:\hf_w24\exe` | built once, from the wave-24 pre-registration commit, **after** it is committed | one build, one directory, never rebuilt mid-wave |

**Mandatory provenance for a wave whose rule compares two binaries:**

```
git diff --name-only 839c38b..<W24 pre-registration commit> -- '*.cs'
```

recorded **verbatim** in `binary_provenance_w24.txt`, with the count split into "test project" and "not". **A
differing `BuildId` proves a rebuild happened; it has never proved the code differs.** **The dlls are hashed,
never `TestApp.exe`** — that apphost has now been byte-identical across six binaries that are none of each
other ([F66](followups.md)).

**The delta is expected to be large and mostly cosmetic**: `e7a5ee6` alone is 20 TestApp files of ASCII
substitutions plus a 342-line test file. That is *why* `G24-1` and `G24-P3b` are worth their minutes.

### 5.2 RULE B24 — did P2 do what it was built to do, on a population that did not motivate it?

**The instrument:** `synth-validate`, one invocation per (dataset, scenario) cell, `--max-rounds 4` (the shipped
default), sequential, one `TestApp.exe`, `timeout 600` per cell, `< /dev/null` on every invocation.

**The population, and why it is blind.** **Scenario S4** — *"detection binning 1 where 2 expected — asserts
binning-first ordering"* (`SynthValidationScenarios.cs:111-118`), `IsApplicable = e.DetectionBinning == 2`.

- **S4 has never been run in this series.** It appears in no results document and in no register entry.
- It is the scenario whose *stated purpose* is the mechanism P2 changes.
- It is requested on **all 20 datasets** and the **binary resolves applicability**, not this document. The
  design's expectation is **7 applicable** (`D08, D09, D10, D12, D14, D15, D17`), derived from the spec, and a
  different number is a **FINDING** (the derivation moved) that is named and does not shrink a denominator
  silently.
- Both arms run the **same** cell list, produced by the **same** driver, from the **same** dataset order.

**Validity gates — all must pass or RULE B24 is `B-UNEVALUATED` and issues no verdict:**

| clause | P (artifact **and** field) | S | A | E | threshold |
|---|---|---|---|---|---|
| **`B24-V1`** | manifest rows in `b24_before_manifest.tsv` and `b24_after_manifest.tsv`, each against its own `EXECUTED_CELLS_*` written before the first run | count of **paired** cells (present in both, `applicable == true`, `terminal` non-null, `assertions[0].id != "RUNTIME"`) | count | either `EXECUTED_CELLS_*` missing ⇒ COULD-NOT-LOOK, B24 `B-UNEVALUATED` — never "0 of 7" | **>= 5 paired S4 cells excluding `D08`.** Below that, `B-UNEVALUATED` **by name** |
| **`B24-V2`** | each report's **own top-level** `profileId`, `fitInputs`, `specSha256`, `maxRounds` — as the binary resolved them | `profileId` contains `ce3f3e63-…`; `specSha256 == bf10522e670a119e260a8b3d06c2cc6ec52ce513dde25ca9ca13f426c38672d6`; `maxRounds == 4`; `fitInputs` cardinality 1 **within** each arm | cardinality 1 per arm, equality across arms for `profileId`/`specSha256`/`maxRounds` | unreadable top level ⇒ COULD-NOT-LOOK and V2 FAILS | all rows. **`fitInputs` equality ACROSS arms is asserted; a difference is a finding, not a repair** |
| **`B24-V3`** | the dll sha256 pairs in `binary_provenance_w24.txt` | BEFORE arm's pair == B13's published pair; AFTER arm's == B14's; **and they differ** | exactly two, distinct | missing ⇒ FAIL | exactly two distinct binaries, each used for exactly one arm |
| **`B24-V4`** | `rounds[N].binningRecommendation.appliedFactor` vs `rounds[N+1].bootstrap.detectionBinning`, AFTER arm only | equality | over every round that has a successor | absent on the BEFORE arm **by construction** (P3 is new) ⇒ NOT-APPLICABLE, named, not a failure | **equality on 100 % of rounds with a successor.** A disagreement means the new field is wrong |
| **`B24-V5`** | `/mnt/d/hf_w24/G24_PASSED` | existence and the BuildId it carries | — | absent ⇒ the AFTER arm does not run at all (driver ABORT) | present, written by `score_g24_w24.py` |
| **`B24-V6`** | **the `D11_rc10_585_afbin2`/S1 cross-binary reproduction control**, run on **both** binaries | trajectory `14 → 24 → 41`, `roundsUsed == 2`, `converged == true`, `finalStepSize == 41`, `stepBehavioral == 55`, `stepToleranceBand == 22` | all-of, on the AFTER arm | the cell missing ⇒ COULD-NOT-LOOK, **named**, V6 `NOT-EVALUATED` — which does **not** by itself unevaluate B24 | **all six reproduce on B14, or RULE B24 is `B-UNEVALUATED`.** `D11` has no binning deferral (`expectedDetectionBinning == 1`), so P2 must not touch it |

**Measurement clauses:**

| clause | P (artifact **and** field, **and which copy**) | S | A | E | threshold / role |
|---|---|---|---|---|---|
| **`B24-A`** — **the headline** | per paired S4 cell: BEFORE `terminal.{finalStepSize, roundsUsed, converged, overallVerdict}` and AFTER the same | the cell is **CHANGED** iff any of the four differs; **IMPROVED** iff `abs(after.finalStepSize − expectedStepSize) < abs(before.finalStepSize − expectedStepSize)`; **REGRESSED** iff `>` | **rate over paired S4 cells, denominator printed, reported TWICE: all-applicable and minus-`D08`** | a cell unpaired ⇒ named, out of the denominator, which is re-printed | **the branch table reads the minus-`D08` rate** |
| **`B24-B`** — the mechanism, and it is P2's engagement marker | AFTER: `rounds[*].applied.binningDeferralBoundReached` | count of rounds where true; count of cells with >= 1 | count over the AFTER arm's S4 rounds, denominator printed | field absent ⇒ **P3 did not ship** ⇒ B24 `B-UNEVALUATED` (the fix cannot report whether it engaged, [F76](followups.md)) | **reported.** Thresholded only through the branch table's `B-UNEXERCISED` branch |
| **`B24-C`** — the counterfactual, and it is what makes `B-UNEXERCISED` honest | BEFORE: for each cell, the maximum run of consecutive rounds carrying `stepDeferredByBinning`-equivalent evidence, reconstructed as **`applied.binningApplied == true && applied.stepApplied == false && applied.exposureApplied == false`** (P3's boolean does not exist on B13) | count of BEFORE cells with a **revisited factor**: some round's `binningRecommendation.recommendedFactor` equals a value already in effect earlier in the same cell, read from `bootstrap.detectionBinning` | count over BEFORE S4 cells, denominator printed | a null `binningRecommendation` block ⇒ COULD-NOT-LOOK, named, out of the denominator | **reported.** This is the number that decides whether the population exercised the mechanism at all |
| **`B24-D`** — do-no-harm inside S4 | paired cells where `B24-C` says the BEFORE run had **no** revisit | AFTER `terminal.{finalStepSize, roundsUsed, converged, finalDetectionBinning, overallVerdict}` equals BEFORE's, **exactly** | all-of, denominator printed | as `B24-A` | **must be 100 %.** P2 is defined to change nothing without a revisit; a difference is a defect in P2 |
| **`B24-E`** — the assertion census on the new population | every element of each AFTER cell's per-round and terminal `assertions[]`: `id` (string), `verdict` (**int** 0/1/2), `detail` (ASCII-folded) | `verdict == 0` | PASS-rate over instances **and** per-`id`, both denominators printed | empty `assertions[]` is its own named state, in neither rate; `assertions[0].id == "RUNTIME"` is an EXCEPTION cell, named, never a failed assertion; a `verdict` outside `{0,1,2}` is `UNKNOWN-VERDICT` and FAILS | reported. **S4's first census** |

**THE BRANCH TABLE, fixed before the data:**

```
B24-V1..V6 all pass?                                  no  -> B-UNEVALUATED   (name the failing gate)
B24-D < 100 %?                                        yes -> B-HARMFUL       (P2 moved a cell it is defined
                                                                              not to move. P2 is REVERTED.)
B24-C == 0 (no BEFORE cell revisits a factor)?        yes -> B-UNEXERCISED   (the population did not contain
                                                                              the mechanism. NOT a pass and
                                                                              NOT a fail. P2 still ships iff
                                                                              R24 is R-PRESERVED, on the
                                                                              strength of D08/S1 + the unit
                                                                              tests, and the results doc says
                                                                              so in those words.)
B24-A REGRESSED > 0 on the minus-D08 population?      yes -> B-REGRESSED     (P2 is REVERTED.)
B24-A IMPROVED > 0 and REGRESSED == 0?                yes -> B-BRIDGED
otherwise (changed but no distance moved either way)      -> B-INERT         (P2 changed a trajectory without
                                                                              changing how close the answer
                                                                              lands. Reported as itself.)
```

**What each verdict costs, fixed here so it is not decided by the result:**

| verdict | what it means and what it obliges |
|---|---|
| **`B-BRIDGED`** | the instrument no longer starves the recommender on a population that did not motivate the fix. Wave 25 may re-measure goal 2 on S0/S1 **and must**, because `V23-G` S1 `13 of 17` was measured on the starved instrument and is not comparable |
| **`B-INERT`** | P2 engaged and the answer did not get closer. Honest, and it says the deferral was not the binding constraint on S4. F26 stays open with a smaller claim |
| **`B-UNEXERCISED`** | the livelock is **rarer than `D08` suggests** — a real result about F26's population, and the strongest argument yet that F26 is conditional rather than endemic. Wave 23 said the same thing from the other side |
| **`B-REGRESSED`** / **`B-HARMFUL`** | **P2 does not ship.** It is reverted from the branch before push and recorded as a costed recommendation with the failing cells named |
| **`B-UNEVALUATED`** | an unsatisfiable rule is a finding and is not re-decided after the data |

### 5.3 RULE R24 — the regression rule, and it is the one that can unship the change

**This is the clause the prompt demands: what counts as a regression that unships the change.**

**The population, and why it is the best in the wave:** wave 23's **scenario S0 over all 20 datasets**. Its
BEFORE side is **already on disk, already published, and cost this wave zero TestApp minutes** —
`/mnt/d/hf_w23/v23/<DS>__S0/synth_validate_report.json`, 20 of 20 present. **Only the AFTER side is run.**
19 scoreable + `D11` as the control, at wave 23's measured S0 rate (§7).

**Why S0 is the right regression population, argued before the data:** across wave 23's 56 rounds there was
exactly **one** round carrying a binning deferral in S0 (`D12_c14_585_afbin2`, 1 round of 1) and **no** cell
with two. **A revisit is impossible in a single round.** So P2 is *defined* to be inert on all 20 S0 cells, P1
and P3 add fields without changing arithmetic, and **`R24` therefore has a predicted answer: everything
reproduces.** A rule with a predicted answer is a rule that can be wrong, which is the only kind worth running.

| clause | P (artifact **and** field, **and which copy**) | S | A | E | threshold |
|---|---|---|---|---|---|
| **`R24-V1`** | the 20 wave-23 S0 reports (BEFORE) and the wave-24 S0 manifest (AFTER) | count of paired cells | count | a missing BEFORE report ⇒ COULD-NOT-LOOK, named, out of the denominator | **>= 17 paired of 20** |
| **`R24-A`** — **THE UNSHIP CLAUSE** | per paired cell, `terminal.{converged, roundsUsed, stepToleranceBand, stepTheory, stepBehavioral, stepBehavioralVsTheoryDeltaFraction, finalStepSize, finalExposureSeconds, finalDetectionBinning, finalCenterPosition, expectedStepSize, expectedExposureSeconds, expectedDetectionBinning, deltaStepVsExpected, deltaExposureVsExpectedFraction, deltaBinningVsExpected, overallVerdict}` — **17 fields, the report's own copy, integers and booleans compared exactly and doubles compared by `repr()` bit-identity** | all 17 equal | **rate over paired cells, and a per-field breakdown of every difference** | a `"NaN"` string on **both** sides is EQUAL (it is the same value); on one side only it is a DIFFERENCE, named | **17 of 17 fields on 100 % of paired cells** |
| **`R24-B`** | per paired cell, every `assertions[]` element's `(id, verdict)` pair, round-by-round and terminal, in order | sequence equality | rate over paired cells | length mismatch is a DIFFERENCE, named with both lengths | **100 % of paired cells** |
| **`R24-C`** — the trap this clause exists to walk past | per paired cell, `terminal.stoppedReason` and every `assertions[].detail`, **after ASCII-folding both sides** (non-ASCII → `?`) | string equality **of the folded forms** | rate over paired cells; the fold count is printed on both sides | — | **reported, NOT thresholded** |
| **`R24-D`** | AFTER only: `rounds[*].stepRecommendation.degenerateReason` and `sampledHfrRange` | present as keys | schema check | absent ⇒ **P1 did not ship** | reported |

> **Why `R24-C` is deliberately unthresholded, and it is not a loosening.** `e7a5ee6` ([F79](followups.md))
> changed `SynthValidateRunner.cs:543-545` — the `stalled …` `stoppedReason` — from a U+2014 **em dash** to
> `--`. **The prose strings are GUARANTEED to differ between B13 and B14 and it has nothing to do with this
> wave's fix.** A regression clause that compared them raw would fail 100 % of the affected cells and report a
> catastrophe. Folding to ASCII makes `—` and `--` both `?`/`--`… and still not equal. **So the honest form is:
> threshold the numbers and the verdicts, report the strings.** This is fixed here, before the data, and it is
> the reason the wave read `git diff 839c38b..e7a5ee6` before writing a clause. *A wave that compares two
> binaries without reading the diff between them is comparing two hashes.*

**THE BRANCH TABLE, fixed before the data:**

```
R24-V1 passes?                                   no  -> R-UNEVALUATED  (and P2 does NOT ship: an unmeasured
                                                                        do-no-harm is not a passed one)
R24-A == 100 % AND R24-B == 100 %?               yes -> R-PRESERVED    (P2 may ship)
R24-A or R24-B < 100 %,
   and every difference is on a cell whose
   BEFORE run had a binning REVISIT?             yes -> R-EXPLAINED    (P2 moved exactly what it is defined
                                                                        to move. It may ship, and every
                                                                        moved cell is named with its
                                                                        before/after values.)
otherwise                                            -> R-BROKEN       (P2 is REVERTED from the branch before
                                                                        push. P1 and P3 still ship: neither
                                                                        can move any field R24-A reads, and
                                                                        if they did, THAT is the finding and
                                                                        it is named.)
```

**Mechanically, "unship" means:** `git revert` is not used. The code agent's byte backups of
`SynthValidateRunner.cs` (P2's only file) are restored, the P2 tests are deleted, the suite is re-run by COUNT,
and the results document records `R-BROKEN` with the failing cells and the reverted diff. **P1 and P3 survive a
P2 revert by construction** — they are in different files and different classes, which is why they were
specified as three changes and not one.

### 5.4 RULE D24 — the degenerate-fit census. P1's deliverable. **Reported, NO BAR, and none is invented.**

This is the hand-off that makes wave 25's Bridge A threshold choosable **blind**. It carries no threshold for
the same reason wave 23's `V23-E`/`V23-F` carried none: **there is no prior for what `sampledHfrRange` should be
on a degenerate fit, and inventing one now is the `S16-A(b)` defect.** Stated here so the results document
cannot quietly act on it.

**Population:** every round of the AFTER arm — S4 (7 cells), the S2 census (§5.5), the three labelled
degenerate-fit controls `D01`/`D02`/`D03` S1, and `D08`/S1 — whose `stepRecommendation` is non-null.

| clause | P (artifact **and** field) | S | A | E | role |
|---|---|---|---|---|---|
| **`D24-A`** | `rounds[*].stepRecommendation.halfWidth` and `.degenerateReason` | `halfWidth` is `"NaN"` **iff** `degenerateReason` is non-null | rate over rounds, denominator printed | a null `stepRecommendation` block (kernel-cap bail, `:637-644`) is its own named state, out of the denominator | **the schema invariant P1 asserts.** A round with `halfWidth == NaN` and no reason means a fourth degenerate path exists that P1 did not label |
| **`D24-B`** | `.degenerateReason` | the distribution over `{no-fit, non-finite-vertex, half-width-unresolved}` | **count per value**, denominators printed | — | which of the three exits the field actually takes. Wave 23 could not tell them apart |
| **`D24-C`** — **the wave-25 hand-off** | `.sampledHfrRange` on rounds where `degenerateReason` is non-null | the value; **and** `bootstrap.stepSize` beside it | **per round, printed as a table, split by scenario** (`S2` = the wide end, `S1`/`S4` = the narrow end) | `"NaN"` ⇒ COULD-NOT-LOOK, named — and on the `no-fit` exit it is NaN **by construction**, which is not a failure | **NO BAR.** This is the population a later wave writes a directional threshold on |
| **`D24-D`** | the same rounds' `fit.rSquared` | the value | printed beside `sampledHfrRange` | as above | corroborates [F21](followups.md)'s *"R² is no defence"* on a second population, or refutes it |

**`D24`'s empty-set answer, named in advance:** if **zero** rounds in the AFTER arm are degenerate, `D24` is
**`D-NO-DEGENERATE-ROUNDS`** — a named state, neither pass nor fail, and a finding in its own right (the three
labelled controls are included precisely so this branch is unlikely; if it fires **with** them present, the
labelled controls did not reproduce and that is a bigger result).

### 5.5 The S2 census — 7 cells, AFTER only, and what it buys

**Scenario S2** — *"step ×4 — converges within <= 2 rounds"* — over the **same seven datasets** as S4. **The
population is chosen by a spec-derived property (`expectedDetectionBinning == 2`) and by price, and both reasons
are declared**; wave 21 chose `D11` *"for price, not physics"* and said so, and this is the same move.

**Why it is in the wave at all:** `D24-C` is the wave's hand-off, and a directional threshold needs **both**
ends. Every degenerate example on disk is at the narrow end. S2 is the only affordable source of **wide-end**
degenerate rounds, and `D05`/S2 — the one wide-end example that exists — is **published by [F25](followups.md)
and therefore excluded** (§2). **AFTER only**, because `D24` carries no bar and P1 changes no behaviour, so a
BEFORE side would measure nothing.

**It is drop D1** (§7.2). If it is cut, `D24-C` reports the narrow end only and says so.

---

## 6. Item L24 — the two named real-bank gaps. Goal 1. Conditional, ~12 m.

Unchanged from wave 23's item L, which was `L-NOT-RUN` for a tenth wave. `optimize --per-run --max-evals 250`,
pinned, on `lumos` and `Panos`, on **B14**, into `D:\hf_w24\gap`.

| outcome | assigned by hand, from the log, **by name** |
|---|---|
| `lumos` | recorded rc=3. `L-REPRODUCED` if rc == 3; `L-CHANGED` if not, with the new code named |
| `Panos` | recorded degenerate σ fit. **`UNEVALUATED BY NAME`** is the expected outcome and is not a failure |

**No scorer.** The design deliberately specifies a by-hand assignment over two runs; a scorer for n=2 is
ceremony. The driver asserts the log exists before recording. **This is the LAST item and the first to be cut
after the S2 census.**

---

## 7. Satisfiability, addressability, precision, and the budget

### 7.1 Every clause's population is ADDRESSABLE ([F74](followups.md)'s half wave 20 skipped)

| clause | the artifact it reads | addressable? |
|---|---|---|
| `G24-1 / 2 / P3a / P3b / 3a-3f` | `<gate>/<run>/**/optimized_settings.json` | the gate driver **asserts count 8 before scoring**, and the landing is **found** by `w24_landing_find` (a `find`, never a template) — a real-bank run nests one level deeper than a synthetic one |
| `G24-P1 / P2a-c / P4` | `<gate>/<run>.log` | the gate layout is **deliberately unchanged** from waves 20–23; `score_g24_w24.py` is `score_v23_w23.py` with only the path table edited |
| `B24-*`, `D24-*` | `<arm>/<ds>__<sc>/synth_validate_report.json` | **each driver writes every path into its own manifest TSV and the scorer reads paths ONLY out of it.** No `os.walk` fallback, deliberately — wave 20's was rooted at the same wrong parent as its primary path |
| `R24-A/B/C` BEFORE side | `/mnt/d/hf_w23/v23/<DS>__S0/synth_validate_report.json` | **verified addressable at pre-registration time: 20 of 20 exist.** This is a file-existence count, not a value read — and the S0 *outcome* values are wave 23's **published** results, which §2 licenses reading |
| `L24` | `<gap>/<run>.log` | the driver asserts the log exists before recording |

### 7.2 Every clause's INSTRUMENT PRECISION is checked, not only its reachable values

| clause | instrument | resolution | is the threshold finer than the instrument? |
|---|---|---|---|
| `G24-1` | `FinalJ`, a JSON double | full double | no — the clause is 6 dp; bit-identity is the *reported* stronger observation |
| `G24-P3b` | `RecommendedStepSize`, a JSON **integer** | exact | no — exact equality against eight integers, no tolerance. **The integer-ness is what makes an equality clause legitimate here**; the same clause on `FinalJ` would need `repr()` |
| `G24-P4` | raw **bytes** of a log, counted in Python | exact | no. **And it is counted in Python precisely because `grep` cannot count it** — [F79](followups.md) is the instrument's own failure mode, so the clause about F79 must not use the instrument F79 breaks |
| `B24-A` | `finalStepSize`, `roundsUsed` (ints), `converged` (bool), `overallVerdict` (**int** 0/1/2) | exact | no. **A clause written against the string `"Fail"` matches nothing** — those strings exist only in the Markdown ([F68](followups.md)'s fifth part: name the field, and which copy) |
| `B24-D` / `R24-A` | 17 terminal fields, mixed int/bool/double | full | doubles compared by `repr()` bit-identity, ints and bools exactly. **`"NaN"` is a STRING, not a number and not null** (Newtonsoft `FloatFormatHandling.String`); it is compared as a string and never coerced to zero |
| `R24-C` | `stoppedReason`, `detail` — **prose** | — | **not thresholded, for the reason in §5.3.** And **no clause anywhere in this wave parses a number out of a prose string** — the check `D20-B` did not make, made |
| `D24-C` | `sampledHfrRange`, a JSON double | full | **no threshold at all, by design.** §5.4 |

### 7.3 BOTH branches of every clause are reachable, and the wave's own deliverable has its FAIL end on real artifacts

| branch | reachable? | how, without reading the blind population |
|---|---|---|
| `G24-1` PASS | yes — observed on thirteen binaries | prior waves |
| `G24-1` FAIL | yes | the scorer is demonstrated to FAIL on a mutated copy of a landing before it is quoted |
| `G24-P4` PASS | **unknown before the data, and that is the point** — it is wave 23's ship being verified | by construction: a byte census can be zero |
| `G24-P4` FAIL | **OBSERVED ON REAL ARTIFACTS, and it is a published prior result**: `/mnt/d/hf_w19/gate` and `/mnt/d/hf_w23/gate` both carry `0xE5` in 8 of 8 logs (wave 23 §2). The demonstration runs first and its output is kept in `g24_asciifailend.txt` | wave 23's census |
| `B-BRIDGED` | yes by construction — one improved cell with no regressed cell suffices over a non-empty paired population | |
| `B-REGRESSED` / `B-HARMFUL` | yes by construction — one cell suffices | **and this is the branch that unships. It is reachable, which is what makes the ship rule a rule** |
| `B-UNEXERCISED` | yes by construction — `B24-C` counts a property that can be zero. **This branch is LIKELY**: wave 23 found a revisit in 1 cell of 38 | named in advance with its own obligation, so a rare mechanism is a result and not a disappointment |
| `B-UNEVALUATED` | yes — six independent gates | |
| `R-PRESERVED` | **yes by construction, and it is the predicted answer** — a conjunction of per-cell equalities over a non-empty population | |
| `R-BROKEN` | yes by construction — one differing field on one cell suffices | |
| `R-EXPLAINED` | yes by construction | it exists so that a *correct* P2 firing on S0 is not misread as a regression. Wave 23's census says it should not fire; if it does, the branch is there |
| `D-NO-DEGENERATE-ROUNDS` | yes by construction — a count that can be zero | §5.4 names its consequence |
| `L-REPRODUCED` / `L-CHANGED` | both by construction | an exit code either matches the recorded one or does not |

**One thing is deliberately NOT given a verdict branch, stated rather than left for a reader.** `D24-C` and
`D24-D` are labelled diagnostics with **no threshold, ever**, because the population that would justify one does
not exist yet — that is the whole reason they are being measured. *That is the check `S16-A(b)` never made.*

### 7.4 Where "could not look" lives, in every scorer

Three states, never two, and **the guard comes before any field read**. A file that cannot be read is
COULD-NOT-LOOK; it is neither "unchanged" nor "changed", neither "pass" nor "fail". A rate over an empty set is
**UNEVALUATED**, never `1.000` and never PASS. Every all-of clause is paired with a cardinality or an `n of N`
companion, because `all()` over an empty list is vacuously true. **`scenarios[].applicable == false` is its own
fourth state — NOT-APPLICABLE — and it is neither COULD-NOT-LOOK nor a failure.**

### 7.5 The budget, priced from the RIGHT instrument

**The published F21 rate is a probe price and must not be used.** Wave 23 §9.1: 45–52 s was measured on
`D11`, a **factor-2, 38 MB** dataset at `--max-rounds 2`; pricing a factor-1 arm from it under-reserved by
**×3.5**. The arm-level rate is **7 446 s / 40 scheduled cells = 186 s/cell**, and even that is a blend.

**This wave prices per (scenario, dataset) from wave 23's own `run.log` mtimes** — the finest instrument that
exists for this question, derived from consecutive-mtime deltas over the 40-cell arm (sum 7 253 s against the
arm's measured 7 446 s, so the method accounts for 97 % of the wall clock):

| scenario | n | mean | median | min | max | sum |
|---|---|---|---|---|---|---|
| S0 | 19 | **136 s** | 86 s | 12 s | 460 s | **2 581 s** |
| S1 | 20 | **234 s** | 130 s | 30 s | 577 s | **4 672 s** |

**And the seven S4-applicable datasets are the cheap end of the bank** — their wave-23 S0 times sum to **161 s**
and their S1 times to **943 s**. S4 starts at detection binning 1 (four times the pixels of binning 2 on round
0) and uses >= 2 rounds, so it is priced **at 2× those datasets' S1 sum**: **~1 900 s ≈ 32 m per arm**. S2 is
capped at 2 rounds by design and is priced at their S1 sum, **~950 s ≈ 16 m**.

| step | instrument | estimate | basis |
|---|---|---|---|
| pre-registration commit | — | — | **before any measurement** |
| **A-BEFORE arm** on **B13** — S4 × 20 requested (7 expected applicable) + `D08`/S1 + `D11`/S1 | `synth-validate` | **~45 m** | 32 m (S4) + 7 m (`D08`/S1 at 435 s) + 1 m (`D11`/S1 at 48 s) + ~2 m (13 inapplicable cells) + slack |
| build **B14** + dll sha256 + `find -newermt` + `git diff --name-only` | `dotnet build -c Release` | **~2 m** | wave 19 measured 32 s; wave 18's two builds 42.6 s |
| four BEFORE fingerprints | python | ~2 m | all four, or the gate ABORTS |
| **RULE G24** — 8 runs | `optimize --per-run --max-evals 250`, mixed | **~42 m** | 41 m 11 s (w23), 41 m 24 s (w19), 42 m 08 s (w18) — a **seventh** wave at ~5.2 m/run |
| gate scoring + both interlock ends + both ASCII ends + self-tests | python | **~8 m** | FAIL ends first, **outputs `tee`'d** |
| **A-AFTER arm** on **B14** — the same cell list + `D01`/`D02`/`D03` S1 | `synth-validate` | **~50 m** | 45 m + 5 m (the three controls at 92+116+72 s) |
| **R-AFTER arm** on **B14** — S0 × 20 | `synth-validate` | **~45 m** | wave 23's measured S0 sum, 2 581 s, + slack |
| **S2 census** on **B14** — 7 cells | `synth-validate` | **~18 m** | the seven datasets' S1 sum |
| **item L24** | `optimize --per-run` | **~12 m**, conditional | ~6 m/run at the mixed rate |
| AFTER fingerprints + scoring | python | ~6 m | |
| analysis agent + results doc | — | ~40 m | first action is `ls -la --time-style=full-iso` on `/mnt/d/hf_w24`, **timestamp printed at the top** |
| full suite by COUNT | `dotnet.exe test` | ~6 m | baseline **3960** |

**TestApp wall ≈ 3 h 32 m** (3 h 20 m without L24; **2 h 54 m** if the S2 census is also cut). **Against a ~6 h
ceiling and a wave that must land by ~02:30Z.** The code work (§3) runs **in parallel with the A-BEFORE arm**,
which is the only genuine parallelism the charter permits — the code agent launches no `TestApp.exe`.

**Drop order, fixed here so it is not chosen by curiosity:**

| # | drop | saves | cost of dropping |
|---|---|---|---|
| D1 | **the S2 census** | 18 m | `D24-C` reports the narrow end only. Wave 25's threshold stays one-sided and must say so |
| D2 | **item L24** | 12 m | two trap entries stay folklore for an eleventh wave; priced |
| D3 | **the `D01`/`D02`/`D03` S1 controls** on the AFTER arm | 5 m | `D24` risks `D-NO-DEGENERATE-ROUNDS` and the census loses its narrow end too |
| D4 | **R-AFTER's expensive tail** — the S0 cells over 300 s (`D04`, `D05`, `D13`, `D16`, `D19`, and `D18` at 563 s in S1 but 0 s here as the arm's first cell) | up to 20 m | moves a **denominator**, never a threshold. Below **17 of 20**, RULE R24 is `R-UNEVALUATED` **and P2 does not ship** |
| — | **NOT droppable** | — | the gate; the A-BEFORE and A-AFTER pairing; `D08`/S1 and `D11`/S1 on both arms; the P1/P3 code |

**If the wave does not fit, D1 and D2 go first and the results document names them.** Nothing in the gate is
droppable: a partial reproduction stops the wave.

---

## 8. Controls

### 8.1 The four fingerprint classes

| class | population | script | role this wave |
|---|---|---|---|
| 1 | 42 bank landings | `bank_fingerprint_w15.py` | preservation — nine clean waves |
| 2 | 59 aux (`harness_settings.json` ×39 + `synthetic_meta.json` ×20) | `aux_fingerprint_w17.py` | preservation |
| 3 | 48 prior-wave arm landings (`hf_w18/seedA0` 20, `seedA1` 20, `hf_w18/gate` 8) | `arm_fingerprint_w19.py` | preservation — wave 23's RULE P23 population must stay unspent |
| 4 | **wave 23's 40 `synth_validate_report.json` files under `/mnt/d/hf_w23/v23/`** | `v23_fingerprint_w24.py` (new, ~60 lines) | **EVIDENCE, and it is load-bearing this wave.** `R24-A`'s entire BEFORE side is those files, and `B24-V6`'s expected values are read from wave 21's and wave 23's published record. *A file class that becomes a denominator is evidence, and a file class that becomes evidence must become a control in the same wave* |

**Class 4 is the one that matters here and it is new.** Wave 24's BEFORE side is not a hash list — it is 20
files of measured values that this wave's verdict depends on. They are fingerprinted **before** any wave-24
`TestApp.exe` runs and re-checked **after** the last one, and the AFTER check's output is written to a file
(wave 23 §8's recorded caveat was that four AFTER checks left no artifact).

**`synth-validate` cannot disturb classes 1 or 2 even in principle** — it is spec-driven, re-renders every
sweep, and never opens a bank artifact. That makes those AFTER checks a control rather than a plea. **Class 4 it
absolutely can disturb**, if a `--out` is ever pointed at `/mnt/d/hf_w23/v23`, which is why the drivers assert
`--out` is under `D:\hf_w24\` and refuse otherwise.

### 8.2 Free controls carried forward unchanged

`prov_w24.py` is `prov_w23.py` with **only** the two tables edited (thirteen prior BuildIds; the new dll hash
pair). *Carrying a self-tested instrument forward unchanged is the point; editing one mid-series is how an
instrument stops being the same instrument.* Its `ConcurrencyCheck` trap is unchanged: `WaitOne(0)` is won by
exactly one of N contenders, so **read it across the whole arm**.

`score_g24_w24.py` is `score_v23_w23.py` with **only** the wave-root path constants and the K8/marker names
edited, and **its `--self-test` is re-run on the copy before it is quoted** — a self-test that ran on the
original is not a self-test of the copy.

### 8.3 The interlocks, all written by the code that computes the verdict, on `rc == 0` and only then

| marker | written by | when |
|---|---|---|
| `/mnt/d/hf_w24/G24_PASSED` | **`score_g24_w24.py --arm-gate`**, and nothing else | §4.4. Refuses if it already exists |
| `/mnt/d/hf_w24/B24_BEFORE_READY` | **`b24_arm_w24.sh --phase before`**, last, and only if the paired-eligible S4 row count excluding `D08` is `>= 5` | carries `manifest=`, `rows=`, `binary=`, `written=` |
| `/mnt/d/hf_w24/B24_AFTER_READY` | **`b24_arm_w24.sh --phase after`**, same rule | same, plus `before_manifest=` |
| `/mnt/d/hf_w24/R24_READY` | **`b24_arm_w24.sh --phase regress`**, last, and only if `>= 17` S0 rows exist | carries `manifest=`, `rows=`, `before_root=/mnt/d/hf_w23/v23` |

**Neither driver writes a marker it does not compute.** No marker is ever hand-written. If a driver refuses,
**that refusal is the result**. `gate_w24.sh` writes no marker and its own `--self-test` asserts that its text
does not.

---

## 9. What the wave will be able to say, goal by goal — and the results document's obligations

| goal | at the end of this wave | what it still will NOT say |
|---|---|---|
| **1 — coverage and bridging gaps** | the **first census of scenario S4** across the bank, with per-`id` assertion rates; whether the instrument's binning-first deferral starves the step on a population that did not motivate the fix; and — from source, before any measurement — that **F26's livelock is in the harness and not in the product** (§1.1) | nothing about setups outside the bank; nothing about S1 at the new binary (the S1 rate is **not** re-measured, so `V23-G` S1 `13 of 17` stands as wave 23 measured it, on the pre-P2 instrument) |
| **2 — step-size accuracy** | **no new accuracy rate.** This wave repairs the instrument that measures it and instruments the recommender's blind spot; it does **not** re-score goal 2 | **it must not quote a new S1 accuracy number.** If `B24` returns `B-BRIDGED`, wave 25 **owes** a re-measurement of S0/S1, because the published `13 of 17` was taken on the starved instrument |
| **3 — exposure and pinned parameters** | nothing new | wave 23's P23/V23-E/V23-F stand. The flat-direction perturbation arm is still unspent and still priced at ~30 m |

**The results document's standing obligations, fixed here:**

1. **It must not report P1 as a step-size accuracy improvement.** P1 changes no recommended number. If it did,
   `G24-P3b` failed and the wave stopped.
2. **It must not report `B-UNEXERCISED` as a failure**, nor as a pass. §5.2 names what it means.
3. **It must not average any rate across arms or scenarios.** S0, S2 and S4 are three populations; the S4 rate
   is printed twice (with and without `D08`) and both denominators are printed.
4. **It must apply the branch tables as written.** An unsatisfiable clause is a finding.
5. **Its first action is `ls -la --time-style=full-iso /mnt/d/hf_w24` followed by `date -u`, in that order, with
   the timestamp printed at the top**, and anything still open at that instant is marked open **in place**.

---

## 10. What is NOT run, and priced

| item | price | why not this wave |
|---|---|---|
| **Bridge A** (the directional degenerate-fit gate) | ~1 h code + 2 tests, **plus a paired S2 arm across the bank ≈ 2 h** | §1.3: its threshold cannot be chosen blind, its decision variable is NaN on the branch it decides, and its risk population costs a second paired arm. **Wave 24 ships the instrument that makes wave 25's version blind** |
| **the S1 re-measurement** | ~1 h 20 m (S1 × 19 on B14) | it is the *consequence* of `B-BRIDGED`, not a precondition. Running it in the same wave as the fix would score the fix on the population that motivated it |
| **the P23 flat-direction perturbation arm** | ~30 m, no product code | wave 23 §11 priced it and named it wave 25's strongest candidate. Nothing changed |
| **F73's code axis** | ~1 h 50 m of arms + a ~42 m gate (**not** the ~53 m two documents still quote) | 0 for 3 on the owner's goals. Wave 23's correction stands |
| **F67's residual** | ~30 m of `af-fit` at both binning factors + a rule | an [F62](followups.md) landmine (**no `BestJ` across factors**) and it touches no named gap |
| **the `RecommendFromHfr` hysteresis** ([F22](followups.md)) | unpriced; it owes a coordinate-system re-baseline | F26's own next step says it *"is a separate, larger change and should not be bundled"*. **It is not bundled** |
| **F70(b′) / PR #192** | — | **REJECTED by the owner. Not re-proposed anywhere in this wave** |
| **RULE F14 / RULE S16 / RULE D20** | — | **three permanent fences. Not re-scored, not converted, not harvested** |

---

## 11. The traps this wave is walking past, baked into the drivers rather than remembered

- **Pass scorers WSL paths** (`/mnt/d/...`). A Windows path yields UNEVALUATED and looks exactly like a failed arm.
- **`< /dev/null` on every `TestApp.exe` invocation inside a loop.** Wave 13's I2 "completed" in 25 s having
  scored 1 of 19 runs.
- **Assert the POPULATION SIZE**, always, as its own line — and for S4, assert the **binary-resolved** applicable
  count against the expected 7 and treat a difference as a finding.
- **Two binaries: hash the DLLS, never `TestApp.exe`.** That apphost is byte-identical across six binaries that
  are none of each other ([F66](followups.md)). And **`git diff --name-only <old> <new> -- '*.cs'` is mandatory
  provenance**: a differing `BuildId` proves a rebuild, never a code difference.
- **Never rebuild an arm's directory mid-wave** ([F53](followups.md)(c)). B13 is not rebuilt, moved, or written
  into; B14 is built once.
- **`grep` on a redirected `TestApp` log is not evidence of absence** ([F79](followups.md)). Every byte-level
  clause is Python. `G24-P4` exists because of this and must not be implemented with `grep`.
- **Newtonsoft writes NaN/Infinity as the STRINGS `"NaN"`/`"Infinity"`.** Every clause asserts a value is a
  number before arithmetic, and `"NaN"` is COULD-NOT-LOOK, never zero.
- **`verdict` and `overallVerdict` are JSON INTEGERS 0/1/2.** A clause written against the string `"Fail"`
  matches nothing, silently.
- **`stoppedReason` and `detail` are ASCII-folded before printing and before any string comparison**, and the
  fold count is reported — **and `e7a5ee6` changed one of those strings, so `R24-C` is reported, not
  thresholded** (§5.3).
- **`synth-validate` exits 1 when any cell FAILS, and that is a RESULT, not a broken arm.** rc 0 and 1 are both
  scoreable and the code is a manifest column; only rc 2, a timeout, a crash or a missing report is NOT-RUN.
  **A scoreability rule inherited from a different command is not a scoreability rule.**
- **`--out` must never be inside a bank root** (the runner exits 2) **and never inside `/mnt/d/hf_w23/`** (it
  would destroy `R24`'s BEFORE side and fingerprint class 4). Both are asserted.
- **One bank path contains a SPACE** (`D:\Autofocus Bank`).
- **Only one `TestApp.exe` at a time. Never NINA during a pinned arm. No fan-out** (1.33×, F60).
- **A mutation harness that restores from VCS destroys uncommitted work under test.** Byte backups, restored in
  a `finally`, source sentinels asserted after every mutant, abort on `WORKING TREE DAMAGED`.
- **A self-test that runs AFTER the thing it guards is not a guard.** Every `--self-test` runs before its arm.
- **`dotnet test <sln>` does not build TestApp**, so a compile break there never surfaces in the suite. TestApp
  is built separately and the build output is read.
- **The write-up's FIRST action is `ls -la --time-style=full-iso /mnt/d/hf_w24`, with the timestamp printed at
  the top.** Wave 19's doc was falsified by an artifact written 33 seconds before its own commit.

---

## 12. Register entries this wave will owe

Fixed here so the analysis agent does not invent them, and so a wave that finds nothing says so.

| entry | expected action | on what evidence |
|---|---|---|
| **[F26](followups.md)** | **CORRECTED (twice)** | (i) §1.1 — the livelock is in `SynthValidateRunner.cs`, the harness, not in the wizard. The entry's *"The wizard applies binning first"* is wrong and has been quoted forward for nine days. (ii) §1.4 — the entry's own next step, keyed on *"the applied value has not changed"*, **never fires** on the oscillating form wave 23 reproduced. Both are source findings, available before any measurement |
| **[F26](followups.md)** | **STATUS**, from `B24`'s verdict | `B-BRIDGED` / `B-INERT` / `B-UNEXERCISED` / `B-REGRESSED`, each with its named consequence (§5.2) |
| **[F25](followups.md) / [F34](followups.md)** | **EXTENDED** | §1.2 — the owed directional gate's decision variable, `SampledHfrRange`, is **NaN on the branch it must decide**. Wave 23's Bridge A is not implementable as written, and P1 is what makes it implementable |
| **[F79](followups.md)** | **VERIFIED or RE-OPENED**, from `G24-P4` | the first gate built after the ASCII fix. 0 bytes on 8 of 8, or the offending files and offsets named |
| **[F76](followups.md)** | **CITED, not extended** | P2 ships with `binningDeferralBoundReached`. A fix that cannot report whether it engaged is not finished |
| **[F21](followups.md)** | **COSTING EXTENDED** | §7.5 — the first **per-scenario** `synth-validate` rate (S0 136 s/cell, S1 234 s/cell, from mtime deltas over a 40-cell arm), which is finer than the blended 186 s the entry now carries |
| **a NEW entry** | **only if a mechanism appears that none of F18/F21/F25/F26/F34 owns** | wave 23's judgement stands: *"a fifth reading of the same component would make the register harder to act on."* **If the wave finds nothing new, it says so** |
