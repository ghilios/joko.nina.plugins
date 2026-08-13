# F82 — the fix choice, decided

**DECISION: candidate (3), the requested-span lower bound, in the explicit-parameter form `(3′)` specified in §4.**
**And wave 26's standing pre-registered choice — fix (1), the monotone floor — is REFUTED, not merely out-ranked:
at `D01`'s own published numbers it produces `halfWidth 12.0 → step 3`, the same step that stalled, so it changes
zero recommended steps anywhere in the bank.**

One-sentence reason: `12.0 / 3.5 = 3.43 → 3` and `18.0 / 3.5 = 5.14 → 5`, so the only candidate that moves `D01`
off its stall is the one that bounds `maxHalfWidth` by the span the sweep **requested** — and of the two that do
that, (3) is the one that touches a single round out of thirty-seven while (2) redefines a quantity with four
consumers.

This document commits a decision. **It ships no code and therefore owes no gate.** The gate it *will* owe when
the code is written is priced in §5.

---

## 1. What is settled, and is not re-opened here

- **F82 is a real product defect on a real product path.** `RULE W29-S` = `S-CARRIER-EXISTS`. Verified again here
  at `HEAD`: `CaptureNewSweepAsync` (`StarDetectionOptimizerWizardVM.cs:5012`) takes a **fresh** sweep per run
  (`RunLiveAttemptAsync`, `:5071`), calls `BuildSummaryAsync` at `:5098`, and carries the previous round's
  recommendation forward through the instance field `recaptureStepSize` (`:2608`, assigned `:5037`, consumed
  `:3236` via `ApplyRecaptureGeometry`, `:3294`).
- **It is rare.** `RULE W29-R` = `R-STANDS`: `k = 18`, `c = 1`, `c_blind = 0`; the one qualifying cell is `D01`,
  which is burned (F97).
- **The harness's bar is a fixed point of the code under test.** F96: `SynthValidateRunner.ComputeStepBehavioral`
  (`:1213-1270`) calls `StepSizeRecommender.Recommend` at `:1262` and loops to `rec.StepSize == step`.
- **`RULE D20`'s fence forbids choosing a fix in the wave that proposes it.** Candidate (3) was proposed by
  wave 29 §4.4 and correctly not chosen there. That fence does not bind this document, which is not that wave.

---

## 2. Four facts I verified myself, which change the picture

Every line number below was read at `HEAD` in this session, not inherited.

### 2.1 `SearchSpan` has one docstring and four consumers, and the docstring names only one of them

`StepSizeRecommender.SearchSpan` (`:554-571`) is `max(x) − min(x)` over `bestFit.Inputs`. Its own summary
(`:553`) says it is *"used to bound the outward half-width search."* It is in fact read four times:

| read at | what it sets |
|---|---|
| `:313` → `:314-315` | the outward search budget, `MaxSearchSpanMultiple (4.0) × searchSpan` (`:578-581`) |
| `:320` | `maxHalfWidth = 1.5 × 0.5 × searchSpan` — the cap (`:347-350`) **and** F81's floor (`:332`, `:363-366`) |
| `:373` | `minHalfWidth = 0.5 × 0.5 × searchSpan` — the floor under F18's detectability bound |
| `:395` → `:441-450` | `CappedGrowthRatio`, which is **quoted to the user** in `StepSizeText` (`:390-391`) |

And the *public* documentation of the constant the cap is built from (`:210-212`) reads *"How far past the
**sampled sweep** the recommended half-width may reach, as a multiple of the sampled HALF-span"*, while the
inline comment at `:345` reads *"the data it was **fitted to**."* **F82 is the gap between those two sentences.**
`bestFit.Inputs` is post-dropout and post-outlier-rejection: `RunEvaluationData` keeps a position out of the fit
when its pooled HFR is non-finite (`:750-753`), keeps recovery positions out entirely on the un-weighted path
(`:793-794`), and `SelectBestModel` (`:825-829`) removes consensus outliers — while `FrameFocuserPositions`
(`:815`) retains **every** frame. Candidate (3) splits the overloaded quantity along the line the docstrings
already draw. Candidate (2) redefines it for all four consumers at once.

### 2.2 Fix (1) does not fix `D01` — arithmetic, from the published report

`/mnt/d/hf_w25/after/D01_ultrawide_40mm__S1/synth_validate_report.json`, read this session:

| round | requested span | `halfWidth` | branch | `step = round(halfWidth / 3.5)` |
|---|---|---|---|---|
| r0 | `2 × 4 × 2 = 16` | **12.0** | `wasBandFloored` | `12/3.5 = 3.43 → 3` |
| r1 | `2 × 4 × 3 = 24` | **9.0** | `wasCapped` | `9/3.5 = 2.57 → 3` — **stall** |

Fix (1) carries r0's floored `12.0` forward as a lower bound on r1's `maxHalfWidth`. Both the cap (`:347-350`)
and the floor (`:363-366`) clamp `halfWidth` **to** `maxHalfWidth`, so under fix (1) r1's `halfWidth` is `12.0`
whichever branch is taken — and `round(12.0 / 3.5, AwayFromZero) = 3`. **Step 3 again.** The alternate reading
("a lower bound on `halfWidth`" rather than on `maxHalfWidth`) gives `max(9.0, 12.0) = 12.0` and the same 3.

Candidate (3) instead sets r1's `maxHalfWidth` to `1.5 × 0.5 × 24 = 18.0`. The raw fitted half-width is
unknown, and **it does not need to be known**: if it exceeds 18 the cap clamps to 18; if it is below 18 the
`BandDemonstrablyUnsampled` floor (`sampledHfrRange = 1.4587 < 3.0`) raises it to 18. Either way
`halfWidth = 18.0` and `round(18/3.5) = 5`. **`D01` r1 moves 3 → 5.**

F82's own entry describes fix (1) as *"simple, fixes `D01` directly."* On the numbers in F82's own table, it
does not.

### 2.3 On the measured population, (2) and (3) are indistinguishable, and both touch ONE round in thirty-seven

Computed this session over the 18 addressable paired S1 cells under `/mnt/d/hf_w25/after`, using
`impliedSearchSpan = halfWidth / 0.75` (the `W29-R` inversion, valid because all 37 rounds are capped-or-floored
and none detect-bounded) against `requested = 2 × offsetSteps × stepSize`:

```
rounds: 37   cells: 18
rounds where the band was demonstrably unsampled (sampledHfrRange < 3): 37
rounds where implied SearchSpan < requested span:                        1
candidate-3 firing rounds:                                               1   (D01_ultrawide_40mm r1)
```

**On 36 of 37 rounds the fitted span EQUALS the requested span exactly.** So:

- Wave 29 design §1's inference — *"37 of 37 rounds are capped-or-floored, so fix (2) moves every round of every
  cell"* — **does not follow and is false on the artifacts.** Capped-or-floored means `maxHalfWidth` is
  load-bearing on all 37; it does not mean redefining its input *changes* it. Fix (2) is bit-inert on 36 of 37.
- Which means **the blast-radius axis does not separate (2) from (3) on this bank at all.** They separate only on
  rounds this bank never produced: rounds where the band *was* reached and the fit lost points (there (3) is
  inert by its guard and (2) fires), and rounds where `FindHalfWidth` returns `NaN` under the fitted span but
  resolves under a larger one (there (2) flips a *branch*, not a value — `:578-581`).
- The separation that remains is a **contract** separation, not a measured one, and it is §2.1's: (2) changes the
  detectability floor at `:373` and the growth ratio quoted to the user at `:390-391`; (3) does not.

**Blindness burn, recorded rather than glossed:** producing the table above read `sampledHfrRange` on the 15
formerly-blind cells, extending F100's burn (which covered `halfWidth`, `bootstrap` and the implied span). The
consequence is written into §6: the do-no-harm arm is a **predicted-answer** rule, not a blind one.

### 2.4 F82's entire evidence was produced with F18's detectability bound DISABLED, and the product always enables it

`SynthValidateRunner` builds `stepDetectability` only when `--step-detect-bound` or
`--step-size-for-executed-sweep` was passed (`:783-793`); wave 25's S1 arm passed neither, which is why `D01`'s
rounds carry `maxUsefulHalfSpan: NaN`, `detectHalfWidth: NaN`, `wasDetectBounded: false`.

**The product supplies it unconditionally.** `BuildSummaryAsync` constructs `representativeDetectability` at
`:4073-4079` from `bestEval.Metrics` on every round and passes it at `:4117-4118`. `Recommend` applies the F18
bound **after** the floor (`:368-379`) and it *only ever tightens*.

So the ordering `floor → detect-bound` is the shipped order, and on a star-poor sweep like `D01`'s the detect
bound may already be the binding constraint — in which case candidate (3)'s widening is discarded at `:375-378`
before it reaches the step. **This is a precondition, not an objection**: `MeasureMaxUsefulHalfSpan` returns
`NaN` (no bound) unless at least one non-recovery frame is starved **and** ≥ 3 qualify **and** the furthest is
positive (`:496`). It is the single largest implementation risk and §6 makes it clause `V-0`, run first.

It also means something the register should carry: **F82's published arithmetic is from a configuration the
product does not run**, and `S-CARRIER-EXISTS` established that a carrier exists without checking that the
carrier's own configuration reproduces the numbers.

---

## 3. The three candidates, scored on the same axes

| axis | **(1) monotone floor** | **(2) redefine `SearchSpan`** | **(3) requested-span lower bound** |
|---|---|---|---|
| **fixes the defect it was written for** | **NO** (§2.2): `D01` r1 → step 3, unchanged | yes: → step 5 | yes: → step 5 |
| **reaches a user** | yes, via `CaptureNewSweepAsync` — but delivers nothing | yes | yes |
| **rounds changed, measured** | **1 of 37** (a reported `halfWidth`), **0 recommended steps** | **1 of 37** (§2.3) | **1 of 37** (§2.3) |
| **consumers of `SearchSpan` disturbed** | none (bounds `maxHalfWidth` only) | **all four** (§2.1): search budget, cap/floor, F18 floor, user-quoted growth ratio | one (`maxHalfWidth`), and only under `BandDemonstrablyUnsampled` |
| **can flip a branch** | no | **yes** — a larger budget at `:578-581` can resolve a half-width that was `NaN`, moving a round out of the floor branch | no: `Math.Max` against an existing ceiling, one-sided by construction (F81's shape) |
| **moves the truth model (F96)** | no — inert in `ComputeStepBehavioral` (see below) | no — inert, same reason | no — inert, same reason, and provably |
| **new state, and where it lives** | **the only one that needs any.** Within a session: an instance field mirroring `recaptureStepSize` (`:2608`) plus a `HalfWidth` on `OptimizationSummary` (`:98`), which has none. Across sessions: `OptimizedStarDetectionSettings` (`:69` carries `RecommendedStepSize` and no half-width) + migration | none | none |
| **testability** | needs a two-round fixture and a carrier | unit-testable, but every existing `SearchSpan`-dependent expectation must be re-derived | unit-testable in one file: `StepSizeRecommenderTests` already builds fits from truth curves (`:82`) and position arrays (`:366-378`); a sweep whose outer points are withheld from the fit is a ~10-line fixture |
| **migration / upgrade** | cross-session variant needs settings migration; within-session variant needs none | none | none |
| **owes an option → owes XAML** | within-session: **no** (`recaptureStepSize` is an un-optioned instance field). Cross-session: **yes**, and then a control in `Resources/OptionsDataTemplates.xaml` per `CLAUDE.md` | no | **no** |
| **verification cost** | 3-cell re-run; but with nothing to detect | **a full paired 18-cell arm (~2 h 40 m)** — it is the only one that can move a round the guard would have excluded | 3-cell re-run + the do-no-harm prediction of §6 |

**On F96, stated precisely rather than inherited.** The coupling is real: `ComputeStepBehavioral` consumes the
same `Recommend`. But none of the three moves it, and the reason is structural: `ComputeStepBehavioral` fits
**all** `2·offsetSteps+1` synthesized points (`:1224-1240`, no `SelectBestModel`, no rejection, all finite by the
pixelization floor at `:1237`) and populates `FrameFocuserPositions` from the *same* expression (`:1250` vs
`:1226`). So requested span ≡ fitted span at every iterate, and both (2) and (3) reduce to identity there. (1) is
inert too, because its carry only exists after a floored round and the truth-curve walk widens monotonically, so
the previous iterate's bound is always the smaller one. **Wave 29's fourth reason for (1) — "(2) moves the truth
model" — is true as a coupling and false as an effect.** §6 turns this from an argument into an assertion.

**On wave 26's "fix the assertion, not the product".** Wave 26 rejected (2) partly because it *"would make the
harness and the product agree by moving the PRODUCT to the harness's model."* That objection has force against
(2) and not against (3), and the distinction is the guard: (3) uses the requested span **only as a lower bound**,
**only** when the sweep's own HFRs prove the band was never sampled. On every round that reached the band the cap
still refuses to trust the model past the points that survived fitting — the property the cap exists for is
untouched. That assertion `A4` (whose boundary is already computed from the requested sweep) would flip from FAIL
to PASS on `D01` r1 is **corroboration and is labelled as such**; it is not the reason.

---

## 4. `(3′)` — the form that is chosen, and how it differs from the registration

Wave 29 §4.4 registered candidate (3) sourcing the requested positions from
`SweepDetectability.FrameFocuserPositions` (`StepSizeRecommender.cs:160`). **Take the mechanism; change the input
plumbing.** Three reasons, all from source:

1. **`SweepDetectability` is optional and null on the harness's default arm** (`SynthValidateRunner.cs:783-793`).
   Candidate (3) as registered would be **inert in the very re-run that must score it** — a 3-cell arm would
   report "nothing changed" and the fix would look dead when it is not.
2. **`FrameFocuserPositions` includes recovery frames** (`FrameIsRecovery`, `:167`), which are deliberately far
   from focus. Reading the span off it unfiltered inflates the bound on exactly the runs F81's own scope note
   was careful about.
3. `MeasureMaxUsefulHalfSpan` (`:471-500`) already reads that array under a *different* contract (outermost
   starved-bounded position; `NaN` when nothing starved). Two rules reading one array with two meanings is the
   overload §2.1 is trying to undo.

**`(3′)`:** `Recommend` takes an optional `double requestedSpan = double.NaN`. When it is finite **and**
`BandDemonstrablyUnsampled(sampledHfrRange)` is true, `maxHalfWidth = Math.Max(maxHalfWidth,
MaxHalfWidthSampledHalfSpanMultiple × 0.5 × requestedSpan)` — inserted at `:320`, before both the cap and the
floor, so both clamp to the same raised value. `NaN` ⇒ byte-identical to today, which is what keeps the four
non-wizard call sites (`OptimizationDiagnosticRunner` ×4, `TiltCalibrationRunner`) inert without touching them.

Callers supply it as **`2 × offsetSteps × stepSize` of the sweep actually taken**, excluding the recovery wing:

- product — `StarDetectionOptimizerWizardVM.cs:4115-4118`, from `runs[0].AfOptions`, which
  `RunEvaluationLoader` reads back off the saved attempt (`:170`, `:251`);
- harness — `SynthValidateRunner.cs:792`, from the round's `bootstrap`.

**Companion change, or the fix is dishonest:** `CappedGrowthRatioOf` (`:441-450`) computes the factor quoted to
the user from `searchSpan`. If the cap fires at a bound derived from a *different* span, the quoted ratio is no
longer the exact factor. It must be handed the same span the bound used.

**New observable, per F76's rule that a fix which cannot report whether it engaged is not finished:** a field on
`StepSizeRecommendation` naming which span set the bound, mirrored into `OptimizationSummary` and the report
snapshot. **Do not overload `WasCapped` or `WasBandFloored`** — F81 kept those two separable on purpose
(`:56-59`) and `A4` reads `WasCapped` alone.

**Two traps, both named before anyone writes code:**

- `ApplyFocusRecovery` (`:3301-3313`) *adds* the recovery steps to `AutoFocusInitialOffsetSteps` on the captured
  options, so a requested span computed from `AfOptions` on a recovery-enabled run **includes the recovery
  wing**. Subtract it (`capturedRecoveryStepsPerSide` is already on the VM, `:2582`).
- On a `CaptureNewSweepAsync` round the sweep is taken at `recaptureStepSize`, not the profile's step. Assert
  that `runs[0].AfOptions.AutoFocusStepSize` reads back the **executed** step, not the profile's — if it does
  not, the fix reads the wrong span on the one path that makes F82 a product defect.

---

## 5. Price, as a band

| line item | est |
|---|---|
| `(3′)` in `StepSizeRecommender.cs` — the optional parameter, the guarded `Math.Max`, the `CappedGrowthRatioOf` companion, the new observable | 25–40 m |
| wizard wiring (`:4115-4118`) + recovery-wing subtraction + the `AfOptions` assertion | 15–25 m |
| harness wiring (`SynthValidateRunner.cs:792`) + report snapshot field | 10–15 m |
| unit tests — inert-when-equal, fires-when-larger-and-band-unsampled, inert-when-band-reached, recovery excluded, the `D01` r0→r1 replication fixture, growth-ratio consistency | 30–45 m |
| **code + tests subtotal** | **1 h 20 m – 2 h 05 m** |
| unit suite, **by COUNT** (F37) against the **3997** baseline — not "green", the count | ~4 m |
| **the `optimize` gate — OWED, certainly** | **42 m** |
| targeted 3-cell `synth-validate` re-run, **run twice**: without and with `--step-detect-bound` (§2.4) | ~20 m |
| **total** | **2 h 25 m – 3 h 10 m** |

**Why the gate is certain and there is no argument to have.** `Q27-V1`'s reachable set is the literal directory
prefix `^Joko\.NINA\.Plugins/Joko\.NINA\.Plugins\.HocusFocus/…`, and its **FAIL end was run** against a diff
whose two files were `StarDetectionOptimizerWizardVM.cs` and `StepSizeRecommender.cs` —
`/mnt/d/hf_w27/q27_v1.txt`, quoted in wave 27 results §5.2. **That diff is `(3′)`'s own file set.** Measured cost
42 m 43 s (wave 25), ~5.2 m/run over eight consecutive waves. **Predicted verdict: `G-PASS`, 8 of 8 `FinalJ`
bit-identical to K8**, on wave 25's precedent that a `StepSizeRecommender` change contributes nothing to the
objective — every `Recommend` call site is strictly downstream of `OptimizeAsync` returning. A gate with a
predicted answer is the only kind that can be wrong; if it is not 8 of 8, the shipped code is not the designed
code.

**Wave 29's published price for fix (1) in the product — "a BAND, and it is one: ~2–4 h … new persisted
half-width surviving between wizard sessions … migration … whether it is an *option*" — is priced against the
wrong carrier and should be corrected in the register.** It was written under the pre-`CORRECTION` belief that
the only cross-round path was session-to-session. `S-CARRIER-EXISTS` found a **within-session** carrier, and
within a session the state is an ordinary instance field like `recaptureStepSize` — no persistence, no
migration, no option, no XAML. Fix (1) scoped to the wizard's re-capture loop is ~1 h, not 2–4 h. **It is still
not chosen, because §2.2 says it does nothing.**

---

## 6. How the fix will be VERIFIED — pre-registered, before anyone writes it

**The trap, named explicitly.** The obvious population is the three published stall cells `D01`/`D02`/`D03`.
**It is not available.** Their rounds are printed in F82's own table, `D01` is the cell the candidate was
designed against, and F14/`RULE S16`/`RULE D20` all say the same thing in three costumes: *a repaired clause
scored against the data that motivated the repair is not a measurement.* Those three cells may be used as a
**reproduction control with a predicted answer** — a number computed here, in advance, from the shipped
arithmetic — and never as efficacy.

**The legitimate populations are three, and they are different kinds of evidence.**

| | population | statistic | bar, fixed now | refutes if |
|---|---|---|---|---|
| **`V-0`** *(validity, first)* | `D01`/S1, 3-cell arm run **with `--step-detect-bound`** — the product's configuration | `wasDetectBounded`, `maxUsefulHalfSpan`, final step | report both values; **no bar** | if the detect bound (`:368-379`) already clamps `D01` below the requested-span floor, `(3′)` cannot fix `D01` **in the product** and the finding is an F18-vs-F81 conflict, not F82. **Then this decision is void and the entry is re-scoped.** |
| **`V-1`** *(reproduction, predicted answer)* | `D01`/S1 r0→r1, unit fixture and 3-cell re-run, detect bound OFF | r1 `halfWidth`, r1 step | **`halfWidth == 18.0` exactly; step `3 → 5`**, computed in §2.2 before any code exists | any other value ⇒ the shipped code is not the designed code |
| **`V-2`** *(do-no-harm, the real bar)* | the **15 non-burned** S1 cells, all **36** non-`D01` rounds | per-round `halfWidth`, `stepSize`, `wasCapped`, `wasBandFloored`, `degenerateReason` | **36 of 36 bit-identical**, and `stepBehavioral` **18 of 18** bit-identical across arms per cell | **one** changed round refutes the guard, because §2.3 measured `implied == requested` on all 36 — a change there means the requested span was read wrong (recovery wing, or the profile's step instead of the executed one) |
| **`V-3`** *(efficacy, and it is honest about being unmeasurable)* | none on this bank | — | **`c_blind = 0` (F97): there is no blind cell that exhibits the defect, so no efficacy rate is measurable here.** Say so; do not manufacture one from `D01` | — |

`V-2` is the bar and it is a **predicted-answer** rule rather than a blind one, because §2.3 burned
`sampledHfrRange` on those cells to produce the prediction. That is the correct trade and it is declared: a rule
whose answer is derivable from the code *before* the arm runs is `RULE R24`'s virtue — it can be unambiguously
wrong — and it is the only form still available after the burn.

**The `stepBehavioral` clause in `V-2` is what discharges F96 for this change**, and no prior wave has run it: it
asserts that the bar did not move while the measurement did. §3 predicts `18 of 18` identical, from the structure
of `ComputeStepBehavioral`; if any cell's `stepBehavioral` moves, `(3′)` has moved the truth model and the
comparison is a self-consistency check, exactly as F96 warns.

**One paragraph, for the implementer.** Run `V-0` first and stop if it fires. Then write the unit fixture for
`V-1` — `StepSizeRecommenderTests` already has everything: `FitTruthSweep` (`:82`) to build a curve, and the
`Detectability`/`F18Positions` helpers (`:366-378`) to supply a position array; construct the shrink-while-
widening condition directly by fitting a *subset* of the swept positions while passing the full requested span,
which is deterministic, costs no bank time, and is not any published cell's data. Then ship, run the suite by
count against 3997, run the 42 m gate expecting 8 of 8, and run the 3-cell arm scoring `V-1` and `V-2` together
— `V-2` on the 15 cells is the clause that can fail, and it must be scored before `V-1` is reported.

---

## 7. What would change this decision

Stated now, in wave 26's style, so it cannot be written after the data:

1. **`V-0` fires.** If the product's always-on detectability bound (`:368-379`, supplied at `:4073-4079`) already
   clamps `D01` below `1.5 × 0.5 × requested`, then no requested-span bound can fix it in the product, `(3′)` is
   the wrong entry, and the right one is the ordering conflict between F18's measured bound and F81's floor.
2. **`V-2` shows any harm.** One changed round among the 36 that §2.3 predicts unchanged means the requested span
   is being read wrong — the recovery wing, or the profile's step instead of the executed one — and the decision
   reverts to **won't fix** until that is corrected, not to fix (1).
3. **`stepBehavioral` moves on any cell.** Then `(3′)` moves the bar as well as the measurement, F96 binds, and
   the change must not be scored against `A3` at all until a spec-derived truth model exists (backlog item 4).
4. **A round is found where the band was REACHED and the fitted span still lost points.** `(3′)`'s guard makes it
   inert there by design; if that shape turns out to be the common one in the field, the guard is wrong and (2) —
   redefining `SearchSpan` outright, with its full four-consumer blast radius and its 18-cell arm — becomes
   correct. Nothing in the 37 measured rounds is of that shape.
5. **New data.** The 1.4–19.4 ″/px render (wave 30 §12.1) is the only route to a blind population that could
   raise `c` above 1 of 18. If it does not, the honest long-run position is that this is a defect worth fixing on
   its *severity* — an unbounded stall costing 30–120 minutes of sky per round, the F49/F51 failure mode — and
   never on its *rate*.

**And the case for doing nothing, weighed rather than dismissed.** `c = 1` of 18, `c_blind = 0`, and the one cell
is burned; goal-2 efficacy is unmeasurable on this bank; the price is ~2.5–3 h including a 42 m gate. That is a
live option and it was taken seriously. **It loses on one point:** the register currently records fix (1) as
F82's chosen remedy, and §2.2 proves fix (1) changes zero recommended steps. Leaving that in place is strictly
worse than either alternative, because it records a remedy that does nothing as the answer to a real defect on a
real product path. **So the minimum obligation of this document is discharged either way: fix (1) is retired.**
If the 42 m gate is not to be spent, the correct entry is *"won't fix, and here is the arithmetic showing why the
pre-registered fix would not have worked"* — not fix (1). The recommendation is to spend it: `(3′)` is one
`Math.Max` behind an existing predicate, provably inert on 36 of 37 measured rounds, and it is the only candidate
that moves the number the entry is about.

---

## 8. Register deltas this document owes

- **F82** — status → *fix chosen: candidate (3), in the `(3′)` form*. Append §2.2 (fix (1) is refuted by
  arithmetic on F82's own table), §2.3 (the 1-of-37 blast radius, and that wave 29 §1's "moves every round of
  every cell" does not follow), §2.4 (the evidence was taken with `--step-detect-bound` off while the product
  always supplies detectability), and the corrected price for fix (1) in the product (~1 h within-session, not
  2–4 h; no option, no XAML).
- **F96** — append: the coupling binds none of the three candidates, with the structural reason
  (`ComputeStepBehavioral` fits every synthesized point and derives positions from the same expression), and
  `V-2`'s cross-arm `stepBehavioral` clause is the assertion that discharges it for this change.
- **F97** — append: the transition census is 1 of 18 *cells*; the within-round census is **1 of 37 rounds**, and
  it is the same round.
- **F81** — append: `SearchSpan`'s docstring (`:553`) declares one consumer and the code has four, and the
  constant's public doc (`:210-212`) says *"sampled sweep"* where the implementation reads *"fitted inputs"*.
- **New entry** — F82's published evidence was produced in a harness configuration the product does not run
  (`--step-detect-bound` off vs. `representativeDetectability` always supplied), and `S-CARRIER-EXISTS`
  established a carrier without checking that the carrier's configuration reproduces the numbers.
