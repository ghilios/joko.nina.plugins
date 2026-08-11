# Synthetic AF bank — followups wave 19 (design / pre-registration)

Charter: [`docs/waves17-21-handoff-prompt.md`](waves17-21-handoff-prompt.md).
Wave 18: [`docs/synthetic-af-bank-followups-wave18-results.md`](synthetic-af-bank-followups-wave18-results.md) — **this
wave exists because of it.** Register: [`docs/followups.md`](followups.md).
Plan: [`plans/synthetic-af-bank-followups-wave19-plan.md`](../plans/synthetic-af-bank-followups-wave19-plan.md).

> ## PROVENANCE — what is fixed by this document, before any measurement
>
> | input | value |
> |---|---|
> | tree | branch `ghilios/synthetic-af-bank-followups-wave13`, PR #191. HEAD is **`28f02ce`** (wave 18's results commit) and **the working tree is CLEAN** — verified at pre-registration time. **This document and the plan are committed before the binary is built.** |
> | code shipped by this wave | **YES — item D, unconditionally.** All of it is inert on the search by construction, which is why it goes into the gate's binary and why RULE G19 passing is the *measurement* of that inertness |
> | binaries — **ONE**, and §11.3 is why that is right here | `D:\hf_w19\exe`. Record `TestApp.dll` and `NINA.Joko.Plugins.HocusFocus.dll` sha256 **and** the `BuildId` field. **Never rebuilt mid-wave** ([F53](followups.md)(c)) |
> | settings **S0** | `D:\hf_w11\pinned_settings_w11.json` md5 **`a67ffc06164c81613aef5c4f8324b9b8`**. **Not re-pinned** ([F63](followups.md)(b) closed DO-NOT-RE-PIN in wave 17; [F59](followups.md) rejected again in §9) |
> | profile | `astrodet` `ce3f3e63-8fd3-4b72-a0ca-d90db9441382`, pinned on **every** arm |
> | banks | `D:\SyntheticAutofocusBank` (20 datasets), `D:\Autofocus Bank` (19 runs) |
> | **prior-wave artifacts this wave READS** | `/mnt/d/hf_w18/seedA0` (20 landings, `BuildId` `e745c958…`), `/mnt/d/hf_w18/gate` (8), `/mnt/d/hf_w18/probe` (2 `af-fit` runs), `/mnt/d/hf_w18/exe_b1` (dll sha256 `3c5b2a5c…` / `08aa0809…`, re-verified at pre-registration time). **All read-only. Nothing this wave runs writes under `/mnt/d/hf_w18`** |
> | **prior-wave artifacts this wave DELIBERATELY DOES NOT READ** | `/mnt/d/hf_w18/seedA1`. It is fingerprinted as a control and appears in **no clause**. §3 |
> | F15 controls — **THREE classes now** | 42 bank landings (`bank_fingerprint_w15.py`), 59 bank aux files (`aux_fingerprint_w17.py`), and **40 wave-18 arm landings + 8 wave-18 gate landings** (`arm_fingerprint_w19.py`, new). Every driver aborts without all three BEFORE files |
> | K8 | **STILL VALID.** Wave 18's Part 2 did not ship, so no coordinate-system debt was created and this wave owes no fresh baseline |
> | suite | baseline **3831** (wave 18). Item D adds tests; the final count is named in the results doc and verified by COUNT ([F37](followups.md)) |
>
> **No fan-out.** Every arm is `--profile-id`-pinned and a pinned arm cannot fan out at all (wave 11's K3);
> the authorisation is worth 1.33×, not 4× ([F60](followups.md)). Wave 5's φ table is quoted nowhere.
>
> **[RULE F14 is closed and is not re-opened. RULE S16 is not re-scored.](waves17-21-handoff-prompt.md)** Nothing
> here reads wave 14's `sem_audit.tsv`, wave 16's rungs, or any repaired form of `S16-A(b)`.
> **And RULE N18 is not repaired.** No clause in this wave is a restatement of an N18 clause, and §3 is the
> argument for why the obvious repair is refused rather than merely deferred.

---

## §0 — Items

| item | what | rule | ships? | `TestApp` cost |
|---|---|---|---|---|
| the gate | eight values on a tenth binary, and the measurement that item D is inert | **RULE G19** | — | ~42 m |
| **A** | **The gate checks ONE field. Is the coordinate system one number or thirty-three?** Re-run wave 18's arm A0 on this wave's binary and diff the FULL landing vector | **RULE R19** | — | ~55 m |
| **B** | **[F71](followups.md)(a)(b)(c)** — the corrected conversion control: read the SNAPSHOT, corroborate on ≥ 3 fields, make the two-copy structure visible | **RULE C19** | harness only | ~1 m |
| **D** | code: [F69](followups.md)(b), F69(c), [F70](followups.md)(b′), and the manual line F70's own Part 1 made wrong | — (ships unconditionally; the gate measures the inertness) | **YES** | 0 |
| **E** | **F70(a): the finding that it is not decidable by measurement on this bank**, and the owner escalation | — | — | 0 |
| **F** | [F21](followups.md)'s price probe — rule-free, ≤ 20 m hard timebox, **first dropped** | — | — | ≤ 20 m |
| **C** | the A1–A9 UI check | — | **BLOCKED, blocker named** (§7) | 0 |

**Estimated wave `TestApp` wall: ~1 h 38 m, or ~1 h 58 m with item F**, against a **6 h** ceiling. Code + tests
~1 h 30 m to 2 h.

> **This wave deliberately uses about two of its six hours, and the eight minutes it declines are not a budget
> decision.** §3 and §9.

---

## §1 — RULE G19: the gate, and this wave's stopping gate

Eight runs, `optimize --per-run --max-evals 250`, both pins named in the driver header, sequential, one
`TestApp.exe`, no NINA. Driver `/mnt/d/hf_w19/gate_w19.sh`. It runs on the **one** binary this wave builds, and
§11.3 is why that is correct here when it was wrong for wave 18.

### 1.1 Every clause: (P)opulation, (F)ield **and which copy**, (S)tatistic, (A)ggregation, (E)mpty answer

**The fifth column is new, and wave 18 is why.** Its `N18-V7` had P, S, A and E all filled in correctly and was
broken anyway, because the artifact carried two fields with the same name and the clause read the dead one
([F71](followups.md)). *A denominator error is a population that is not what you think it is; that was a
**quantity** that is not what you think it is.*

| clause | threshold, fixed here | P — artifact | F — field, and **which copy** | S | A | E — on an empty set |
|---|---|---|---|---|---|---|
| **G19-1** | all eight `BestJ` reproduce the K8 table to **6 dp**. A partial reproduction is a FAILURE and stops the wave | the 8 `optimized_settings.json` under `/mnt/d/hf_w19/gate` | `FinalJ`, top level. **A landing carries each name exactly once** — the two-copy hazard is a property of *converted settings files*, not landings, and that is stated rather than assumed | equality to 6 dp | all-of, printed `n of 8` | an unreadable landing is COULD-NOT-LOOK, **named**, and **G19-1 FAILS**. For a *stopping* gate, "could not look" is not "undecided" — it is "has not passed" |
| **G19-2** | `aggregate_summary.json produced: 8`, asserted **in the driver before any scorer runs** | `find` over the gate root | file existence | count | `== 8` | 0 found ⇒ FAIL |
| **G19-3a** | exactly **one** `BuildId`, **novel** against **nine** recorded ids: `5cb7e474`, `103d61c4`, `62334f10`, `084e3485`, `df3a867d`, `10bc1b47`, `932a1366`, `e745c958`, `7a3a03ba` | `Provenance.BuildId` ×8 | one copy per landing | set | cardinality 1 **and** disjointness | empty ⇒ FAIL. **Two tables, never merged**: `KNOWN_DLL_SHA256` is separate and the scorer fails loudly if a `BuildId` starts with a known dll hash ([F66](followups.md)) |
| **G19-3b** | `DetectorVersion` **2**, read as the **FIELD** | `Provenance.DetectorVersion` ×8 | one copy per landing | set | `== {"2"}` | empty ⇒ FAIL. `strings -el` appears nowhere as a detector check |
| **G19-3c** | `ProfileId` contains `ce3f3e63-…` **and** cardinality exactly 1 | `Provenance.ProfileId` ×8 | one copy | containment + cardinality | **both, evaluated separately** | `all()` over an empty list is vacuously true, so the **cardinality** clause carries the empty read; empty ⇒ FAIL |
| **G19-3d** | one distinct `FitInputs`, exactly `MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid` | `Provenance.FitInputs` ×8 | one copy | set | `== {expected}` | empty ⇒ FAIL |
| **G19-3e** | `ConcurrencyCheck == exclusive` on **all eight**, read **across the arm** | `Provenance.ConcurrencyCheck` | one copy, as a list carrying an explicit `None` per unreadable landing | equality | all-of over a length-8 list | a `None` is a FAIL, not a skip — the list is built so an empty read cannot shorten it into agreement |
| **G19-3f** | `BaselineJ` reproduces wave 11 ([F41](followups.md)) to 6 dp | `BaselineJ` ×8 | one copy | equality | `n of 8` | missing ⇒ FAIL |
| **G19-4** | `prov_w19.py --self-test` PASSES on the real arm and FAILS on a mutated copy, **with the mutation asserted by read-back**, before any G19 number is quoted | the arm and a `copytree` of it | `Provenance.ProfileId` on one named landing | — | both directions | a mutation that cannot be made prints **SELF-TEST COULD NOT RUN** — not a pass, not a fail |
| **G19-P1** | the gate's own `optimize/seed` block reads `NoiseReductionRadius=3` on **8 of 8** logs, asserted **in the driver** | the 8 gate logs | **`NoiseReductionRadius` in the `PARAMS-DUMP optimize/seed` block** — *not* `optimize/baseline`, *not* `optimize/detected`, *not* the landing's copy. Read as an `awk` BEGIN…END range so it cannot straddle blocks | field value | `== 8` | a missing block ⇒ the driver refuses to hand the arm on |

The eight values, exactly as they must reproduce:

```
toml999    0.9957838768299878   CWhiteFocus 0.9960675916058808   uneven 0.9963677194179505
muggsie    0.9971948738498605   mccomiskey  0.9767460801208465
D18        0.9998815090506263   D19         0.9994870586135448   D20    0.9997378027339423
```

### 1.2 `G19-P1` is not decoration this wave, and the hazard has an address

Wave 18's Part 2 — the one-literal change that moves the optimizer's seed from 3 to 4 — **is preserved as a
patch at `/mnt/d/hf_w18/part2_w18.patch`**, and the wave-19 brief's own `git status` block listed its three
files (`HocusFocusStarDetection.cs`, `StarDetectionOptionsTests.cs`, `preprocessing.md`) as **modified**. They
are not: HEAD is `28f02ce`, `git diff` is empty, verified at pre-registration time. But a stale snapshot that
says "Part 2 is applied" plus a patch file on disk is precisely the setup in which a binary silently becomes
wave 18's B2 and the gate stops being a gate. `G19-P1` reads the seed dump and refuses; `R19-V3` reads it again
on 20 more logs.

### 1.3 What a PASS proves, stated narrowly

Two things and no more. **(a)** A tenth binary reproduces the coordinate system. **(b) Item D is inert on every
harness path — and that is a MEASUREMENT, not an assumption.** That is the whole reason item D sits in the
gate's binary rather than beside it, and it is wave 18's best structural idea reused (its `G18-1` PASS is what
established Part 1's inertness). **If G19-1 fails, F69(b) is the first suspect and the wave stops.** It is not
reported as a machine problem.

### 1.4 What it does not prove

Nothing about the landing beyond `FinalJ` and `BaselineJ` — which is exactly what item A exists to measure, and
§2 argues that the gap between those two statements is the most interesting unmeasured thing in this series. It
runs at `MaxOutlierRejections = 0`, so it says nothing about the rejection path.

---

## §2 — Item A, RULE R19: the gate checks ONE field. Is the coordinate system one number or thirty-three?

### 2.1 The question, and why nobody has asked it

For nine waves the gate has established that a new binary reproduces the coordinate system by checking **one
field** — `BestJ`, which is the landing's `FinalJ` — on eight runs. A landing carries **35 top-level keys**
(verified by reading one: 25 curated detector knobs, `RecommendedStepSize`, `RecommendedOffsetSteps`, and eight
bookkeeping fields). **Nobody has ever checked the other 33.**

The gap is not hypothetical. `J` is saturated near 1.0 on this bank ([F32](followups.md)) and the search moves
through a flat valley ([F6](followups.md), [F8](followups.md)), so *two different knob vectors with the same `J`
to sixteen digits is exactly what a flat valley produces.* If a binary can move a landing's knobs while leaving
its `J` bit-identical, then:

- every cross-wave landing comparison this series has made carries an unmeasured error term;
- wave 18's 40 arm landings are a **sample**, not a **measurement**, and no rule may consume them;
- the charter's *"search noise measured at zero"* ([F63](followups.md), from wave 15's G-e) is a statement
  about `J`, not about the landing.

### 2.2 What is already observed, at zero compute, and where the observation is weak

Measured at pre-registration time from wave 18's own artifacts, with no `TestApp` run:

> **Wave 18's GATE landings and its arm-A0 landings for `D18`, `D19`, `D20` — same binary (B1), same pins,
> different invocation, different output directory — are IDENTICAL on all 33 comparable keys. 3 of 3.** The only
> two differing keys are `CreatedAtUtc` and `Provenance.CommandLine` (the `--out` path).

That is the 1.000 end **observed**, and the 0 end is reachable by construction (any key differing). It is also
exactly the observation wave 15's Lesson 4 warns about: *"A control demonstrated on one dataset is a control
demonstrated on one dataset… It varied the right axis on the wrong sample."* `D18/D19/D20` are **the gate's own
three synthetic runs**, selected for the gate long ago; they sit in the agreeing set. And the axis it varies is
**invocation**, not **binary**. Extending it across the population, on a genuinely new binary, is the prescribed
repair and it is what this arm costs 55 minutes to do.

### 2.3 The arm

`optimize --per-run --max-evals 250 --settings S0 --profile-id astrodet`, **20 synthetic datasets**, sequential,
on **this wave's binary**, into `/mnt/d/hf_w19/reA0`. Dataset order is wave 18's, fixed: `D18, D19, D20` then
`D01…D17`. Driver `/mnt/d/hf_w19/redo_w19.sh`.

**The treatment is "a different binary," and the free control that it took is `BuildId`.** Two roots sharing a
`BuildId` is an explicit FAIL: it means the re-run used wave 18's binary — which is still on disk at
`/mnt/d/hf_w18/exe_b1` — and the arm measures nothing at all. That is the single easiest mistake available in
this wave, which is why it is a clause and not a hope.

**This arm cannot harvest.** It reads arm A0 against arm A0. The string `seedA1` appears in no clause, in no
driver, and in no scorer path. It can produce no statement whatever about the seed literal. §3.

#### The clauses. Every one names P / F-and-which-copy / S / A / E.

**Validity gates** — all of V1…V5 must pass or R19 is `R-UNEVALUATED` and issues no verdict.

| clause | threshold | P | F — and which copy | S | A | E |
|---|---|---|---|---|---|---|
| **R19-V1** *population* | the landings present on **both** sides equal the datasets the driver **scheduled** | `optimized_settings.json` under each root, against `reA0/EXECUTED_DATASETS` | file existence | count | `== scheduled` on both sides | `EXECUTED_DATASETS` missing ⇒ COULD-NOT-LOOK, R19 UNEVALUATED, never "0 of 20" and never a drop |
| **R19-V2** *a DIFFERENT binary ran* | one `BuildId` per root; the two **differ**; the new one is novel against the nine; the old one is exactly `e745c958502244f498950e99ac3d6f00` | `Provenance.BuildId` in the 2 × N landings | one copy per landing | set per root | cardinality 1 each, disjointness across | an unreadable landing is its own state, named, clause FAILS. **Two roots sharing a `BuildId` is an explicit FAIL** |
| **R19-V3** *the seed did not move* | `NoiseReductionRadius == 3` on N of N | the `PARAMS-DUMP optimize/seed` block in the new root's N `run.log` | **the `optimize/seed` block only.** Three blocks in each log carry this name once F69(b) ships; the read is an `awk` BEGIN…END range | field value | `== N` | a missing block ⇒ COULD-NOT-LOOK by name; if any is, R19 is UNEVALUATED |
| **R19-V4** *the pins* | `ProfileId` by containment **and** cardinality; one `FitInputs`; `DetectorVersion` 2; `ConcurrencyCheck` exclusive across the arm — **on both roots** | `Provenance.*` in the 2 × N landings | one copy each | equality / cardinality | per root, and across the arm for concurrency | a `None` in the concurrency list is a FAIL, not a skip; every all-of clause is paired with a cardinality or `n of N` companion |
| **R19-V5** *both roots read the same settings file* | `SettingsFingerprint` identical across roots on N of N | `Provenance.SettingsFingerprint` | one copy | cross-root equality | rate, denominator printed | missing ⇒ COULD-NOT-LOOK, named, clause FAILS |

**Measurement clauses.**

| clause | what it measures | P | F — and which copy | S | A | E |
|---|---|---|---|---|---|---|
| **R19-A** *the landing, field by field* — **the measurement** | whether a binary can move a landing | the paired landings' comparable top-level keys: the union of both sides' keys **minus `CreatedAtUtc`** (a timestamp — it *necessarily* differs; this is wave 15's `G-d` defect **named and excluded in advance** rather than discovered afterwards) **and minus `Provenance`** (whose `BuildId` MUST differ by V2 and whose `CommandLine` MUST differ because `--out` differs; its comparable members are scored by V2/V4/V5). **33 keys on the real artifacts.** | each key at the landing's top level; one copy each | `repr()` equality. `repr()` deliberately: it makes `float('nan')` equal to itself so a NaN column is not a permanent false difference, while keeping the **string** `"NaN"` — which is what Newtonsoft writes — distinct from the float. **A key present on one side only IS a difference and is named** | **two rates, both denominators printed**: (i) datasets with zero differing keys over the scheduled count; (ii) key-level agreement over `sum(comparable keys)` | a landing missing on either side ⇒ COULD-NOT-LOOK, **named**, out of **both** denominators, which are re-printed. A rate over an empty set is UNEVALUATED, never 1.000 |
| **R19-B** *the objective alone* | what nine waves of gates have actually been checking | the same paired landings | `FinalJ`, `BaselineJ` | `repr()` equality | two rates over the same denominator | as R19-A |
| **R19-C** *free confound-splitter, 3 datasets, zero extra `TestApp`* | invocation alone | this wave's **gate** landings for `D18/D19/D20` against the new root's for the same three — same binary, different invocation, different output dir | as R19-A | as R19-A | rate over 3 | COULD-NOT-LOOK, named, out of the denominator |

> **`R19-A` minus `R19-B` is the number nobody has ever had.**

#### The branch table, fixed before the data

```
R19-V1..V5 all pass?                        no  -> R-UNEVALUATED     (name the failing gate)
R19-A dataset rate == 1.000?                yes -> R-DETERMINISTIC
R19-B rate == 1.000 AND R19-A < 1.000?      yes -> R-J-ONLY
otherwise                                       -> R-NOT-DETERMINISTIC
```

**What each verdict costs the register, fixed here so it is not decided by the result:**

- **`R-DETERMINISTIC`** — the landing is a deterministic function of (binary-class, settings, profile, frames)
  at n = 20 on 33 fields. **[F8](followups.md) is BOUNDED**: its *"optimizer landings are not reproducible
  across invocations"* does not survive `--settings` + `--profile-id` pinning with one `TestApp.exe`, and every
  wave since has had to caveat around an entry whose evidence (`bobp_m101` across three *different pipelines*)
  predates that discipline. Wave 18's 40 arm landings are a **measurement**, and a later wave may consume them
  under a rule fixed first. **And it settles, with data rather than argument, that a fresh pair of arms at the
  two seeds would be a re-run** — §3.
- **`R-J-ONLY`** — the eight-value gate is a control on **one field** and the landing moves underneath it.
  Every cross-wave landing comparison in this series inherits an unmeasured error term; wave 18's 40 landings
  are a sample; everything downstream of F63 becomes conditional. **This is the outcome that would make the
  arm worth far more than 55 minutes, and it is the reason the arm exists.**
- **`R-NOT-DETERMINISTIC`** — as above and `J` moves too. Note this branch is **partly foreclosed by RULE G19**,
  which checks the same `J` against K8 on `D18/D19/D20` — but **not on `D01…D17`**, where 17 of the 20 datasets
  have never had their `J` checked across binaries at all. Stated here rather than left for a reader.
- **`R-UNEVALUATED`** — an unsatisfiable rule is a finding and is not re-decided after the data.

#### If R19-A comes back short, the disambiguation is pre-registered

This arm varies **binary** and **invocation** together. `R19-C` splits them for free on 3 of the 20. Beyond
that: re-run each failing dataset on **wave 18's own binary** (`/mnt/d/hf_w18/exe_b1`, dll sha256 re-verified
against `3c5b2a5c…` and `08aa0809…`) into `/mnt/d/hf_w19/reA0_b1`, ~2.7 m each, **capped at five datasets**
(~14 m). More than five: record it and stop. *The cap is fixed here so it is not chosen by curiosity.*

---

## §3 — The decision the brief asks for: **(c)**, and the argument

The brief poses it exactly right and then offers three answers. **Neither (a) nor (b) is the best available
move, and the reasons are different for each.**

### 3.1 The situation

Wave 18 returned `N-UNEVALUATED` because its conversion control compared the wrong copy of the field. Its 40 arm
landings are on disk. Re-scoring them under a corrected control costs ~8 minutes of `af-fit`. **A rule written
now over that data has a known outcome**, because wave 18 published the diagnostics: `N18-J` reads A1-better
**13**, A0-better **7**, 0 ties; `N18-A` 0.900 vs 0.750. That is the harvest §3 and §3a forbid.

### 3.2 **(a) fresh arms is refuted, and it is refuted by measurement, not by preference**

The brief calls it *"the honest route to a verdict, ~2 h for the pair"* and asks what would make the arms
**fresh** rather than a re-run. **The answer is: nothing, and this is measurable rather than arguable.**

| evidence | what it says | where it comes from |
|---|---|---|
| the gate's own record | eight `BestJ`, **bit-identical to sixteen digits, on nine binaries** — 72 reproductions of the landing's objective | K8, waves 11–18 |
| wave 15's `G-e` | *"search noise measured at zero"* | charter §1, F63 |
| wave 18's `N18-V6` | gate vs arm A0 on `D18/D19/D20`, **3 of 3 bit-identical** on `FinalJ` **and** `BaselineJ` | wave 18 §4.2 |
| **new, this document, at zero compute** | the same three landings are identical on **all 33 comparable keys**, not just the two `J` columns | §2.2 |

So a fresh `A0'/A1'` pair, at the same two seeds, on the same 20 datasets, with the same pins, would
**reproduce** wave 18's landings — and therefore re-derive 13-vs-7 and present it as new data.

> **(a) is not more honest than (b). It is (b) plus two hours and a false claim of freshness.**

And it is a fork with no good tine:

- **if the arms reproduce** — the overwhelmingly likely case — the "fresh" verdict is the harvested verdict with
  a two-hour alibi, and the alibi makes it *worse*, because it is harder for a later reader to see;
- **if they do not reproduce**, then wave 18's `N18-J` at 13/7 was noise, the in-sample instrument is worthless
  on 20 datasets, and a second sample of 20 cannot decide either.

Either way (a) buys no verdict. What it *would* buy is the reproduction measurement itself — and that costs
55 minutes as a clean control (§2), not two hours as a contaminated treatment.

### 3.3 **(b) is cheap, correct in form, and irreversible — which is the objection**

Option (b) — re-score wave 18's landings as a **corrected diagnostic that names no verdict**, wave 15's `V4′`
shape — is legitimate in form and costs 8 minutes. **The reason to refuse it is what wave 15's `V4′` actually
did to RULE F14.**

Wave 15 built the corrected instrument, ran it, and published *"585 of 585 triples, 0 violations, visibly
pointing at family A, `f = 0.50`"* under an explicitly-no-verdict label. The charter's §3 then had to declare
RULE F14 **permanently NO VERDICT**, and its first reason is the one that matters here:

> *"The rule's own guard — 'never resolved by preferring whichever rung agrees with something else' — cannot
> bind a reader who has already seen the published diagnostics."*

**Publishing a diagnostic SPENDS the population it reads.** Once the out-of-sample `e` values for both arms are
in a results document, no later rule over them can bind anybody, and F70(a) joins F14 in the permanently-
undecidable pile. That is an irreversible act, taken to buy a number, for a change whose measured reach is
**0 of 9 profiles on this machine** (wave 18's `N18-R`, 0 by construction — Part 2 edits
`BuildDefaultStarDetectorParams` only and no profile's loaded detector reads it).

*The cheapest way to keep a question decidable is to not look.* That is a new discipline point and it is worth
a register line: **a diagnostic published under an UNEVALUATED verdict is not free.**

### 3.4 **(c): repair the instrument, demonstrate it, and deliberately do not spend the population**

So wave 19:

1. **Repairs the control and demonstrates it in both directions** — F71(a) (read the snapshot), F71(b)
   (corroborate on ≥ 3 fields), F71(c) (make the two-copy structure visible). §4. **~1 minute of `af-fit`,
   plus a free retro-demonstration on the exact artifacts the broken control read.**
2. **Does not run the 40-landing pass.** Not for budget — the wave uses ~2 of 6 hours. **And it ships no driver
   that could**: there is no `affit_w19.sh` in `/mnt/d/hf_w19`. *The surest way not to look is to not build the
   thing that looks.*
3. **Measures whether the population is a measurement at all** (RULE R19, §2), which is the precondition for
   any future wave spending it, and which is genuinely unmeasured on 33 of the landing's 35 fields.
4. **Records F70(a) as not decidable by measurement on this bank**, with the argument below, and escalates it.

### 3.5 The finding this replaces the verdict with: **F70(a) is not decidable by measurement on this bank**

This is item E, it costs zero minutes, and it is a stronger statement than a verdict would have been. Every
class of evidence that could decide the seed literal, and its state:

| evidence class | state | why it cannot decide |
|---|---|---|
| **source / counting argument** | **COMPLETE and free.** Wave 18 §2.4: `ResetDefaultsImpl` and `DerivePresetSettings` share 20 properties and agree literal-for-literal on **19**; the twentieth is the only one the derivation post-adjusts; `BuildDefaultStarDetectorParams` sets both hotpixel flags true — *exactly the configuration the `+1` exists for* — and does not carry the compensation; every construction yields 4 and persists it; `ResetDefaults()`'s 3 does not survive a restart | it *does* decide — it decided in wave 18, and the register already records **"(a) IS DECIDED AND UNSHIPPED. The answer is 4."** It is not a measurement, so it cannot satisfy a measurement rule |
| **in-sample objective (`FinalJ`)** | **SPENT.** Published at 13/7 | a rule over it now has a known outcome. §3 of the charter |
| **anchoring rate (`N18-A`)** | **SPENT.** Published at 0.900 / 0.750 | same |
| **out-of-sample `e`** | **UNMEASURED, and preserved by this wave** | and even unspent it is structurally weak: wave 18 §5.1 computed that the largest landing-driven out-of-sample movement ever measured on this bank is **0.02584 step** (wave 15's `L15-P`, same instrument, same 20 datasets) and the largest of any kind is **0.00993**, against the **0.10-step** materiality floor four waves have used. **`e` can be a VETO here; a veto is not a decider**, and a rule whose only measurement clause is a foreclosed veto is a rule that cannot fail to ship — [F66](followups.md)(a)'s defect |
| **reach on real profiles** | **0 of 9, by construction.** Free, published | it measures that the change is nearly inert, which argues neither way |
| **the other 11 `BuildDefaultStarDetectorParams` call sites** | unmeasured, ~30 m each | wave 18: *"buys no decision"* |

> **The only clause with a live range was in-sample `FinalJ`, and it is spent. F70(a) is a decision to be taken
> on the source argument, by the owner — and this series has the precedent: wave 14's
> `MaxOutlierRejections` default was set to 0 by the owner, overriding wave 13's pre-registration, and the
> register recorded it as such.** The charter's standing authorisation §1 already provides for this: a change
> that no rule licenses is *"recorded as a costed recommendation"*.

### 3.6 What wave 20 inherits, and the rule it must fix before it looks

Priced here so the option is scheduled rather than merely preserved:

| | |
|---|---|
| **precondition** | RULE R19 returns `R-DETERMINISTIC`. If it returns `R-J-ONLY` or `R-NOT-DETERMINISTIC`, wave 18's landings are a sample and this item is **dead** — record that and close F70(a) on the source argument alone |
| **cost** | **~8 m** of `af-fit` (wave 15's `affit_mor0` covered these exact 20 datasets in 3 m 45 s), plus the rule. The arms — 1 h 55 m — are already paid for and must **not** be re-run ([F53](followups.md)(c)) |
| **the instrument** | `score_c19_w19.py`'s corrected control, which by then has a demonstrated PASS and a demonstrated FAIL in both a retro and a live direction, and `C19_PROBE_PASSED` on disk saying exactly what it does and does not license |
| **the shape the rule must have** | it must **not** read `FinalJ` or `N18-A` in any clause (both spent); it must state its bar as a **rate or a comparison of counts over a shared denominator**, never as an absolute count borrowed from another instrument (F68); and it must route every empty answer **away** from shipping |
| **the bar this design refuses to fix, and why the refusal is the honest move** | F68 requires a bar's reachable range be computed **on the real artifacts** before the data. For `e` that cannot be done without computing `e`, and computing `e` is the thing being deferred. **So this design states the shape and declines to fix the number**, rather than inventing one whose reachability it cannot demonstrate. It leaves two concrete pieces of arithmetic instead: a **movement threshold of 0.10 step is foreclosed** (it needs 4× the largest movement ever recorded), and a **threshold at 0.01 step is in the range of movements this bank has actually produced** |
| **the elegant option, if wave 20 wants a bar it can defend** | **split the 8 minutes.** Compute `e(A0)` alone first — the status-quo arm's out-of-sample error, which reveals the scale without revealing the comparison — fix the bar on that scale, commit it, and only then compute `e(A1)`. That is "fix the rule before the data" made literal on a population that already exists |

---

## §4 — Item B, RULE C19: the corrected conversion control

Driver `/mnt/d/hf_w19/probe_w19.sh`, scorer `/mnt/d/hf_w19/score_c19_w19.py`, converter
`/mnt/d/hf_w19/convert_landing_w19.py` (wave 15's, **copied forward** — wave 15's file is not edited, because
three waves of results quote its behaviour).

### 4.1 What was wrong, and the two repairs

A converted settings file carries every detector knob **twice**: `Options[<knob>]`, round-tripped verbatim from
the base, and the same knob inside `Options["OptimizedSettingsJson"]`. **In Simple mode the `Options` copy is
dead** — `ConfigureSimpleSettings` takes the `UseOptimizedSettings && HasOptimizedSettings` branch and
`ApplyOptimizedSnapshotToLiveProperties` overwrites it. Wave 18's control read the dead copy, and the dead copy
agreed with the derivation's live output by arithmetic accident (F70's `+1`). Both verdicts inverted.

**(a) Read the snapshot**, named in the clause text and not only in the code. `snapshot()` deliberately
**refuses to fall back** to `Options[<knob>]` when the snapshot is absent, because falling back *is* the defect.

**(b) Corroborate — and do not nominate a signature knob at all.** The control computes **D**, the set of
`af-fit/detector` dump fields that actually **differ** between the good run and the mutant run, then requires
the good run to agree with the snapshot on every one and the mutant on none.

> **Why the bar is `|D| ≥ 3` and not "one field matched".** A two-directional demonstration proves the branches
> are DISTINGUISHABLE, not that they are CORRECTLY ASSIGNED — both directions are scored by the same definition,
> so a definition error moving both verdicts the same way reads as a clean PASS. Wave 18's was caught only
> because the labels came out *swapped*. Requiring three independent fields means a same-direction definition
> error would have to be wrong about three quantities at once.

**(c) Make the structure visible.** `convert_landing_w19.py` now prints, per curated knob, the
`(Options value, snapshot value)` pair it is creating, in three named states — `BOTH-DIFFER`, `SNAP-ONLY`
(the base has no key at all, so it falls back to a **code default** that appears in no file), `BOTH-AGREE`.
Run against wave 18's own inputs at pre-registration time it prints **5 `BOTH-DIFFER` and 6 `SNAP-ONLY` of 25**
— and if `BOTH-DIFFER` is ever 0 it says so loudly, because a control then has nothing to discriminate on.

### 4.2 The clauses

| clause | threshold | P | F — and which copy | S | A | E |
|---|---|---|---|---|---|---|
| **C19-D1** *retro, zero compute* | good agrees with the snapshot on **all** of `D ∩ snapshot`, mutant on **none**, with `\|D\| ≥ 3` | wave 18's saved `/mnt/d/hf_w18/probe/{good,mutant}/run.log` `af-fit/detector` blocks against `{good,mutant}.json` | **`Options["OptimizedSettingsJson"]` parsed as JSON.** One alias, carried explicitly and printed: the dump prints `Sensitivity` where the snapshot key is `BrightnessSensitivity` | `D` = fields differing between the two runs; then per-field equality against the snapshot | `\|D\|` printed; two counts over `\|D ∩ snapshot\|` | a missing dump / settings / snapshot ⇒ COULD-NOT-LOOK, named, **UNEVALUATED — never "TOOK"**. **`\|D\| = 0` is the empty-domain case and is UNEVALUATED, not PASS**: two runs producing identical detectors cannot demonstrate a conversion binds. A field in `D` with no snapshot key and no alias is **named as NOT-CORROBORATED**, never silently dropped |
| **C19-D2** *live, ~1 m* | as D1 | `/mnt/d/hf_w19/probe/*`, built by `probe_w19.sh` from **this wave's own gate D18 landing**, mutant differing in one key, **asserted by read-back**, with the snapshot asserted byte-identical across the pair | as D1 | as D1 | as D1 | as D1 |
| **C19-V1** | `convert_landing_w19.py --self-test` passes, including F71(c)'s four new assertions | the self-test | — | pass/fail | all clauses | a self-test that cannot run is COULD-NOT-LOOK, not a pass |

```
C19-V1 fails, or D1 or D2 is UNEVALUATED?     yes -> C-UNEVALUATED       (nothing is written)
D1 and D2 both correct in BOTH directions?    yes -> C-DEMONSTRATED      (write C19_PROBE_PASSED)
otherwise                                         -> C-NOT-DEMONSTRATED  (name which side failed)
```

**`C-DEMONSTRATED` licenses nothing in this wave.** The marker's own file body says so. It exists for the wave
that pre-registers an out-of-sample rule **first**. §3.4.

### 4.3 `C19-D1` has already been run, and its result is stated here as OBSERVED

Because it costs nothing and F68 requires denominators be derived on the real artifacts at pre-registration
time. Run against `/mnt/d/hf_w18/probe`:

```
dump fields: good=55  mutant=55   |D| = 8
|D and snapshot| = 8   (alias applied: Sensitivity -> BrightnessSensitivity)
good agrees with the SNAPSHOT on   8 of 8
mutant agrees with the SNAPSHOT on 0 of 8
```

and the eight reconcile **exactly** with the converter's own two-copy table: 5 `BOTH-DIFFER`
(`BrightnessSensitivity`, `MinHFR`, `NoiseReductionRadius`, `StarClippingMultiplier`, `StructureLayers`) plus
the 3 `SNAP-ONLY` knobs whose code default differs from the snapshot (`HotpixelThreshold`, `MaxDistortion`,
`StarCenterTolerance`) — the other three `SNAP-ONLY` knobs happen to equal their code defaults and therefore
cannot discriminate. **Two instruments, derived independently, agreeing on 8.**

> **One correction the wave owes the register.** Wave 18's results §5.3 and F71 both say the `af-fit/detector`
> dump carries **56** fields. Counted directly on both saved logs, it carries **55**. Nothing downstream
> depends on it; it is corrected here rather than repeated.

---

## §5 — Item D: the code, all of it inert on the search

Ships unconditionally. Gated only by (1) the suite green by **COUNT**, (2) every new test demonstrated to fail
against the pre-change source or a **named mutant**, verified by the controller rather than asserted by the code
agent, and (3) **RULE G19 passing on the binary that carries it — which is the measurement of the inertness.**

### 5.1 F69(b) — emit the dump that describes the detector that actually ran

`OptimizationDiagnosticRunner` prints `optimize/baseline` and `optimize/seed` **before**
`ApplyRunDetectionBinningIfRequested` mutates `ctx.Baseline`'s `DetectionBinning` **and** `PixelScale`. Wave 16's
RULE P16 compared `optimize`'s params *as constructed* against `af-fit`'s *as detected*, excluded
`DetectionBinning` as a candidate on the strength of a comment that was backwards, and reached a wrong verdict;
wave 17's RULE C17 found the cause to be exactly that field. Wave 18 fixed the comment (F69(a)). **F69(b) is the
durable fix**: emit a second dump *after* the mutation.

**Constraints fixed here, because this wave's theme is "name the field" and this is the change that adds a third
copy of every field name to every `optimize` log:**

- the tag is **`optimize/detected`**. It must contain **no existing tag as a substring** (`optimize/baseline`,
  `optimize/seed`, `af-fit/detector`), so that a prior wave's `grep -l "PARAMS-DUMP optimize/baseline"` cannot
  double-count it. A name like `optimize/baseline-resolved` would break wave 17's and wave 18's saved drivers.
- `ParamsDump.Write`'s contract holds: the source carries no whitespace, the parser reads `\S+` and pairs
  BEGIN with END by backreference.
- every clause in this wave that reads a knob out of a log **names its block** and reads it as an `awk` or
  regex BEGIN…END range.
- a test asserting the new block exists **and** that its `DetectionBinning`/`PixelScale` differ from the
  pre-mutation block on a run whose resolved factor is not 1. *A dump that is identical to the one above it is
  a dump that has not been demonstrated.*

### 5.2 F69(c) — make the no-op loud

`--apply-run-detection-binning` is still accepted and does nothing; the behaviour has been on by default since
wave 8. Print a one-line notice when it is passed, saying it is an accepted no-op and the behaviour is on by
default. ~10 m, with a test. *It converts a silent misnomer into a loud one, which is what the register asked
for as the third option.*

### 5.3 F70(b′) — the dead literals

Wave 18's Part 1 made `ResetDefaultsImpl` end in an **unconditional** `ConfigureSimpleSettings()`. The ~20
preset-owned literals above it are now **unobservable through any public path**, so editing one is a silent
no-op — a hazard wave 18 flagged rather than left. Delete them.

**The contract, and the demonstration required:** the shared set must be **derived from source** (the properties
`DerivePresetSettings` assigns) and printed in the commit message, not guessed. One parametrized test: for each
name in the shared set, perturb the property, call `ResetDefaults()`, assert the derived value. It must be
demonstrated red against the named mutant **M-R1** — *remove the unconditional `ConfigureSimpleSettings()` at
the end of `ResetDefaultsImpl`*. **Any literal the test cannot cover stays.** Droppable (§8, `D2`).

### 5.4 The manual line — and wave 18 bundled it with the wrong question

`documentation/docs/settings/preprocessing.md:23` gives `NoiseReductionRadius`'s default as **3**, and **line 41**
repeats it in prose (`**Default:** \`3\``). **It is 4**, and it is wrong under *both* answers to F70(a):

- the page's own preamble says these are **Advanced** settings, *"derived for you in Simple mode from the Noise
  Level, Pixel Scale and Focus Range presets"* — so the column documents what the derivation produces, and the
  neighbouring rows confirm it (`StarClippingMultiplier` 2.0, `NoiseClippingMultiplier` 4.0,
  `AdaptiveNoiseBlockSize` 128 — all the derivation's Typical values);
- at Typical the derivation gives 3 and then adds **+1** whenever `HotpixelThresholdingEnabled && HotpixelFiltering`,
  **both on by default**, and the setter persists **4**. On the 9 real profiles on this machine the four without
  an optimizer snapshot all read **4**;
- **and since `93e366a` — wave 18's own Part 1 — `ResetDefaults()` derives too.** Before Part 1 the "restore
  defaults" button could at least produce 3. **After Part 1 nothing in the product produces 3 for this option.**

> **Wave 18 made this fix conditional on RULE N18, N18 returned `N-UNEVALUATED`, and the revert therefore left a
> documentation line that the same wave's Part 1 had just made unambiguously wrong.** The manual documents the
> **option**; F70(a) is about `BuildDefaultStarDetectorParams`'s **seed** — two different quantities that share
> a name. *That is F71's own defect, in the manual instead of in a settings file.* It ships this wave,
> unconditionally, and it is not evidence for or against the seed literal.

**And the patch that was going to fix it would have fixed half of it.** `/mnt/d/hf_w18/part2_w18.patch`'s
documentation hunk is `@@ -20,7 +20,7 @@` — **the table row at :23 only**. The prose `**Default:** \`3\`` at
**:41**, eighteen lines further down the same page, is untouched. So even on the `N-ALIGN` branch the manual
would have kept saying 3, in the section a reader actually reads. **Do not treat the saved patch as a template
for the documentation half of this item**; it is a template for the product literal, which this wave is not
touching.

---

## §6 — Item F: the F21 price probe, rule-free and timeboxed

One `synth-validate --scenarios S0 --datasets D17_cdk14_oiii5 --max-rounds 2` run, **hard-timeboxed at 20 m by
`timeout`**, driver `/mnt/d/hf_w19/f21_probe_w19.sh`. **Optional tail item, first thing dropped.**

**No clause is pre-registered over it, and that is deliberate.** F21's own hypothesis — that `FindHalfWidth`'s
coarse walk returns a wildly different bracket — was **refuted in wave 10**: `FindHalfWidth` is exact for a
hyperbola, the movement is in the fitted **vertex**, the entry's own case **did not reproduce** (round 0
reproduced exactly at 143.583/41; round 1's 12.1/3 never occurred), and with a fixed seed the recommender is
bit-reproducible across binaries. **Writing a clause over a population wave 10 showed may not exist is exactly
the defect [F68](followups.md) catalogues.** The probe asks only: *does the population exist, and what does one
round cost?*

Deliverables, and nothing else: the wall time; the number of rounds it converged in; `HalfWidth` and recommended
step per round; and whether a second round occurred at all. **If it times out, "> 20 m, UNMEASURED" is the
deliverable** — a more useful number for wave 20 than a wave that ran long. Wave 17 asked for this price, wave
18 dropped it; if wave 19 drops it too, the register should say the instrument has now been unpriced for three
waves.

---

## §7 — Item C: BLOCKED, and the blocker is a permission

**Zero minutes. No NINA launched. No display touched.** `gate_w19.sh`, `redo_w19.sh`, `probe_w19.sh` and
`f21_probe_w19.sh` all abort if any NINA process is running.

The state is carried from wave 18 and **nothing in it is re-derived here**, because the pre-registered
disposition is zero minutes: NINA **3.3.0.1048** crashes at startup in `PluggableBehaviorSelector<,>..ctor`
inserting into an `AsyncObservableCollection`; the plugin targets `NINA.Plugin` **3.2.0.2001-beta` and is
deployed into that 3.3 install's `Plugins\3.0.0\`.

> **The absence of "Hocus" in the stack does NOT exonerate the plugin.** `PluggableBehaviorSelector<,>`'s
> constructor is precisely where MEF-imported plugin behaviours are inserted, and HocusFocus exports two —
> `IStarDetection` and `IStarAnnotator` (`AutoFocusEngineFactory.cs:31-32`, `InspectorVM.cs:101-102`). A
> behaviour built against a different NINA contract can NRE inside the selector's own frames without the
> plugin's name appearing anywhere. **The blocked isolation test is therefore DECISIVE, not optional.**

| | |
|---|---|
| **the blocker** | one launch of NINA 3.3.0.1048 with `…\Plugins\3.0.0\` moved aside was **refused by the permission classifier**. It is a permission the agent does not hold and **must not route around**. **This wave designs no arm that needs it** |
| **what would settle it** | **Crashes anyway** ⇒ the fault is NINA's install and A1–A9 are blocked on that. **Starts clean** ⇒ the plugin is implicated, leading hypothesis a 3.2-built plugin in a 3.3 host |
| **what it costs** | ~3 m of wall time. The cost is **not compute** — it is a permission grant, which is the user's to give |
| **if the answer is the retarget** | **every UI item in this backlog is blocked behind a NINA 3.3 retarget** — a package bump, whatever API drift it brings, and a full regression pass. A project-level decision, not a wave item. A1–A9 stay NOT ATTEMPTABLE until it is taken |
| **what changed this wave** | nothing, by design. Recorded so the disposition does not silently decay into "we forgot" |

---

## §8 — Satisfiability, in two parts

### 8.1 Part 1 — maxima and reachable range, computed **on the real artifacts**

Not on constructed input: *a constructed population cannot detect a denominator error, because the constructor
chooses the denominator* ([F68](followups.md)).

| clause | bar | max attainable **on the artifact the clause reads** | min | both reachable? | how the denominator was derived |
|---|---|---|---|---|---|
| G19-1 | 8 of 8 at 6 dp | 8 — the driver asserts 8 landings before scoring | 0 | yes; the FAIL direction is demonstrated by `prov_w19.py --self-test` on a mutated copy | fixed list of 8 run names |
| G19-2 | `== 8` | 8 | 0 | yes | `find` over the arm |
| G19-3a–f | set equalities and cardinalities | — | — | yes, both directions demonstrated before the check is quoted | the 8 landings |
| **G19-P1** | 8 of 8 seed blocks read `3` | 8 | 0 | **yes, and the 0 end has an address**: `/mnt/d/hf_w18/part2_w18.patch` applied to the tree produces exactly it, and the brief's own stale `git status` said it already was | `awk` over the 8 gate logs |
| **R19-V1** | landings == scheduled, both sides | the scheduled count | 0 | yes | `find` over each root; **20 verified on `/mnt/d/hf_w18/seedA0` at pre-registration time** |
| **R19-V2** | cardinality 1 per root; roots differ; old == `e745c958…` | — | — | **yes; the "two roots share a BuildId" branch is exactly what re-running on `/mnt/d/hf_w18/exe_b1` produces**, and that binary is on disk | the 2 × 20 landings |
| **R19-V3** | 20 of 20 seed blocks read `3` | 20 | 0 | yes, as G19-P1 | `awk` over the new root's 20 logs |
| **R19-V4** | equality / cardinality | — | — | yes; demonstrated in both directions by the scorer's self-test | the 2 × 20 landings |
| **R19-V5** | rate 1.000 of 20 | 20 | 0 | **yes, and 1.000 is OBSERVED on wave 18's gate-vs-A0 pair** (their `SettingsFingerprint` values are identical), while 0 is reachable if either root's `--settings` pin fails | the 2 × 20 landings |
| **R19-A** | dataset rate == 1.000; key rate reported | 20 datasets / 660 key comparisons (20 × 33) | 0 | **yes. The 1.000 end is OBSERVED at 3 of 3 on all 33 keys** (§2.2) — on the *invocation* axis, on the *agreeing* three datasets. The 0 end is reachable by any key differing, and the whole point is that nobody has looked at the other 17 datasets or the *binary* axis | 33 comparable keys counted by reading a real landing (35 top-level keys, minus `CreatedAtUtc` and `Provenance`); 20 landings counted on disk |
| **R19-B** | rate reported | 20 | 0 | **yes, and the 1.000 end is OBSERVED 72 times over** — eight `BestJ` bit-identical on nine binaries — though never on `D01…D17`, which is 17 of this clause's 20 | the same 20 |
| **R19-C** | rate over 3 | 3 | 0 | yes; identical construction to §2.2's observation | the 3 gate synthetic runs |
| **C19-D1/D2** | good == snapshot on all of `D ∩ snapshot`, mutant on none, `\|D\| ≥ 3` | `\|D\|` ≤ 55 (the dump's field count, counted directly) | 0 | **yes, and BOTH ends are demonstrated**: `\|D\| = 8` with good 8/8 and mutant 0/8 measured on wave 18's saved pair at pre-registration time; the scorer's self-test demonstrates the inverted pair, the `\|D\| = 0` empty domain, the `\|D\| = 1` under-corroborated case, and the missing-snapshot case, all routing to UNEVALUATED or rejection | the two saved `run.log` files |
| **item F** | none — no clause | — | — | n/a: it is rule-free by design (§6) | n/a |

**Every denominator above was counted on the artifact the clause reads, at pre-registration time.** The one
number imported from another wave — the 20 landings under `/mnt/d/hf_w18/seedA0` — was **re-counted on disk**,
not inherited from wave 18's text.

**Absolute counts in this design, and their reachable ranges** — enumerated by re-reading every threshold in
§1.1, §2.3 and §4.2 and classifying each one. **Wave 17 claimed it used no absolute counts while `C17-B`'s bar
was literally "27 of 27"; this design does not make that claim.** There are **five** true absolute counts, and
two bars that look like counts and are not:

| bar stated as an ABSOLUTE COUNT | reachable range | why a count and not a rate |
|---|---|---|
| G19-1 "8 of 8" | 0–8 | the gate is one instrument of eight fixed runs; a rate would invite a partial reproduction to read as 0.875 rather than as a failure |
| G19-2 "== 8" | 0–8 | a population assertion, derived by `find` |
| G19-P1 "8 of 8" | 0–8 | a population assertion over the same 8 logs |
| R19-V3 "N of N seed blocks read 3" | 0–N | a population assertion; **N comes from `EXECUTED_DATASETS`, so a pre-registered drop moves N and never the bar** |
| C19 "`\|D\| ≥ 3`" | 0–55 | a **floor on the corroboration available**, not a bar on a result; below it the control is UNEVALUATED. Observed at 8 |

| bar that READS like a count and is a RATE against 1.000 | reachable range | why the distinction matters |
|---|---|---|
| R19-V5 "`SettingsFingerprint` identical on N of N" | rate 0–1 over a printed denominator | implemented as `rate(same, common) != 1.0`. Behaviourally the same as "N of N" **and immune to a denominator change** — which is exactly the property `N18-V1` lacked when it fixed 20 and could not tell a planned drop from a failure |
| R19-A "dataset rate == 1.000" | rate 0–1 over a printed denominator | same. The `20` never appears as a literal in the bar |

Everything else is a rate with a printed denominator, a set equality, a cardinality, or a comparison of counts
over a shared denominator.

**And one absolute count this design deliberately does NOT contain.** `R19-A`'s bar is `rate == 1.000`, not
"20 of 20": the denominator comes from the file the driver writes before the first run, so cutting the tail
(§8.3 `D3`) changes the denominator and the bar is unmoved. Wave 18's `N18-V1` fixed 20 and could not
distinguish a planned drop from a failure; this one can, and the scorer's self-test demonstrates both.

### 8.2 Part 2 — measurement → consuming-branch reachability, **both branches checked**

*"Check that BOTH branches of a clause are reachable, not just that the PASS value is attainable"* (wave 15's
`G-d`), plus *"what does it return when its set is empty"* (F68) — **and, wave 18's addition, neither of those
can detect a quantity error, so every row below also names the copy.**

| branch | is there an input that reaches it? | possible on this wave's population? |
|---|---|---|
| **G19 FAIL** | any `BestJ` moving, a repeated `BuildId`, a non-`exclusive` `ConcurrencyCheck`, or `G19-P1` reading 4 | yes; all demonstrated by `prov_w19.py --self-test`, and the `G19-P1` branch has a patch file on disk that produces it |
| **R-UNEVALUATED** via V1 | `EXECUTED_DATASETS` missing, or a scheduled dataset with no landing | yes; both demonstrated in the scorer's self-test, and they are **different states** |
| **R-UNEVALUATED** via V2 | the re-run using wave 18's binary | yes — copy-pasting wave 18's driver does it, and `exe_b1` is on disk |
| **R-UNEVALUATED** via V3 | wave 18's Part 2 patch applied to the tree | yes, as above |
| **R-DETERMINISTIC** | 20 of 20 on all 33 keys | yes — **observed at 3 of 3** on the invocation axis (§2.2) |
| **R-J-ONLY** | `FinalJ`/`BaselineJ` identical while some knob moves | **yes by construction** — a flat valley produces exactly this, and `J` is saturated near 1.0 on this bank ([F32](followups.md)). **Never measured. It is the reason the arm exists** |
| **R-NOT-DETERMINISTIC** | `FinalJ` moving | yes on `D01…D17` (17 of 20). **Partly foreclosed on `D18/D19/D20`**, which RULE G19 checks against K8 first — stated rather than left for a reader |
| **C-DEMONSTRATED** | the corrected control reading the snapshot | yes — 8 corroborating fields **measured** on wave 18's saved pair |
| **C-NOT-DEMONSTRATED** | an inverted pair, or a good side that does not bind | yes; the self-test constructs the inverted pair explicitly |
| **C-UNEVALUATED** | `\|D\| < 3`, a missing dump, a missing snapshot, or a pair whose snapshots differ | yes; four separate self-test demonstrations |
| **item D ships / does not ship** | it ships unconditionally; the gate measures its inertness | the not-inert branch is `G19-1` failing, which stops the wave |

**Three clauses are deliberately NOT decisive and are labelled so everywhere they appear**: `R19-C` (a
confound-splitter — it is *reported* and cannot move the verdict; the scorer emits a NOTE if it disagrees with
`R19-A` rather than overriding it), the item-F probe (rule-free), and the key-level rate inside `R19-A`
(reported alongside the dataset rate, which is the bar).

### 8.3 The F68 self-audit of this design — **checked against the clauses above, not asserted**

Wave 17 wrote a self-audit it had not checked and was wrong; wave 18 wrote one that was right on all four
columns and was broken on a fifth. So, item by item, with the check named:

1. **A denominator carried across instruments.** Checked by re-reading §8.1's "how the denominator was derived"
   column: every entry names a `find`, an `awk`, or a direct read of a real file. The only cross-wave number is
   `/mnt/d/hf_w18/seedA0`'s 20 landings, and it was **re-counted on disk** rather than quoted from wave 18.
2. **An aggregation that cannot see the effect.** Checked by grepping this design for "median": **there is no
   median anywhere in it.** Every aggregation is a count or a rate over a printed denominator. That is
   `S16-E(i)`'s defect avoided by not creating the opportunity.
3. **A quantifier over a possibly-empty domain.** Checked by reading every **E** cell above: every one routes to
   UNEVALUATED or FAIL, never a vacuous PASS. `rate(num, 0)` returns `None` and prints `UNEVALUATED (empty
   set)`. Every `all()`-shaped clause is paired with a cardinality or `n of N` companion, because `all()` over
   an empty list is vacuously true. Four self-tested empty-domain demonstrations in `score_c19_w19.py`, three
   in `score_r19_w19.py`.
4. **The FIFTH question — name the FIELD, and say which copy.** Every clause table above carries an **F**
   column. And the design states, once, how many copies each artifact class holds: a **landing** — one; a
   **converted settings file** — two, and in Simple mode the live one is the snapshot; an **`optimize` run.log**
   — **three** once F69(b) ships (`optimize/baseline`, `optimize/seed`, `optimize/detected`). **This wave is
   the one that creates the third**, so §5.1 fixes the tag name against substring collision with the other two
   and every log read in this wave is a BEGIN…END range.
5. **Improvised instrumentation.** Wave 17's two escapes were both outside its pre-registered clauses. So: the
   controller's progress counter has a **could-not-look** state and the plan specifies the exact `find` and
   filename per instrument; `query session` is **not used at all**, because item C is blocked on a permission
   rather than a session state; and the three fingerprint controls each have a `--self-test` with a
   read-back-asserted mutation (`arm_fingerprint_w19.py`'s demonstrates four directions, including that a
   missing **root** is COULD-NOT-LOOK rather than "unchanged").
6. **A claim this self-audit makes that could be wrong, stated rather than hidden.** §2.2's "33 comparable keys"
   is derived from **one** landing read at pre-registration time. If any dataset's landing carries a different
   key set, `R19-A`'s per-dataset key count differs — which is why the scorer computes the comparable set
   **per dataset from the union of both sides** and prints it per row, and why a key present on one side only
   is a **named difference** rather than a silent skip.

---

## §9 — Budget, priced from the same instrument, with the drop order

| step | population | instrument | estimate | derivation |
|---|---|---|---|---|
| build | — | `dotnet build -c Release` | **~25 s** | **wave 18 measured 20.62 s and 21.96 s** for two incremental Release builds — 42.6 s for the pair. Wave 18's own estimate was ~6 m, **8× high**; the actual is now recorded and used |
| the suite | — | `dotnet.exe test` | ~3 m 45 s | wave 18 measured 3 m 38 s and 3 m 42 s |
| **RULE G19** | 8 runs | `optimize --per-run --max-evals 250`, mixed real+synthetic | **~42 m** | 40 m 55 s / 41 m 36 s / 41 m 29 s (waves 16–17) and **42 m 08 s** (wave 18) on the identical arm |
| **RULE C19-D2** | 2 `af-fit` runs | `af-fit` | **~1 m** | wave 18's equivalent probe was **~28 s** |
| **RULE R19 arm** | 20 all-synthetic | `optimize --per-run --max-evals 250`, all-synthetic | **~55 m** | **wave 18's arm A0 — these exact 20 datasets, this exact invocation shape — took 54 m 34 s (3274 s, 2.73 m/run).** The charter's 2.6 m/run gives 52 m independently |
| RULE C19-D1 | wave 18's saved probe | Python | **0** | already run at pre-registration time (§4.3) |
| **item F** (tail) | 1 dataset | `synth-validate` | **≤ 20 m, HARD timebox** | **UNMEASURED — this series has never run `synth-validate` as an arm.** The timebox is the deliverable, not the estimate |
| scoring | — | Python | < 10 m | |
| **wave total `TestApp` wall** | | | **~1 h 38 m, or ~1 h 58 m with item F** | against a **6 h** ceiling; plus ~1 h 30 m–2 h of code and tests |

**Three instruments, three rates, never crossed**: `optimize` mixed ≈ 5.2 m/run, `optimize` all-synthetic
≈ 2.73 m/run (measured, wave 18), `af-fit` ≈ 11 s/dataset. **An `af-fit` 39-run rung is ~11 m and must never be
priced from the `optimize` rate** — doing so over-prices it by roughly an order of magnitude.

### The drop order, fixed in advance

| # | cut | saves | what it costs | what it does to a bar |
|---|---|---|---|---|
| **D1** | item F, the F21 price probe | ≤ 20 m | wave 20 inherits an unpriced instrument for the **third** consecutive wave, and the register should say so | nothing — it has no rule |
| **D2** | item D's F70(b′) (the dead literals) | ~20 m of code + tests | the residual hazard stays flagged, exactly as wave 18 left it | nothing — it ships no rule |
| **D3** | drop datasets from the **tail** of R19's order (`D17` backwards) | ~2.73 m each | population | **no threshold moves.** `R19-A`/`B`/`V3`/`V5` take their denominator from `EXECUTED_DATASETS`, written before the first run, and every rate re-prints it. A short population is a **different state** from a planned drop and the scorer distinguishes them |
| **D4** | drop the R19 arm entirely | 55 m | the wave's only measurement; `R-UNEVALUATED` disclosed by name, and F8/F63's basis stays at n = 3 | nothing — it is disclosed, not restated |
| **never** | the gate; `R19-V2` (without it the arm measures nothing); `C19-D1` (it is free) | | | |

---

## §10 — What this wave will NOT run, and what it costs

- **The out-of-sample pass over wave 18's 40 arm landings.** ~8 m. **Not run, and no driver exists that could.**
  This is the wave's central decision and §3 is the argument: publishing a diagnostic spends the population it
  reads, wave 15's `V4′` is the precedent that cost RULE F14 permanently, and the population is preserved for a
  wave that fixes a rule first. **Wave 20 inherits it priced, with the instrument demonstrated and the shape of
  the rule written down** (§3.6).
- **Fresh arms at the two seeds.** ~2 h. **Refuted by measurement, not declined for budget** (§3.2): the
  landing is bit-identical across invocations on all 33 keys at n = 3, `BestJ` has reproduced on nine binaries,
  and RULE R19 is the honest version of the same 55 minutes.
- **Re-running arm A1, or reading `/mnt/d/hf_w18/seedA1` in any clause.** Zero minutes, deliberately. Re-running
  A1 would reconstitute the harvested comparison *inside a control*. `seedA1` is fingerprinted and read by
  nothing.
- **Any repaired form of `N18-V7`, `S16-A(b)`, or any re-scoring of wave 16's rungs or wave 14's `V4`/`V4′`.**
  Zero minutes, per [F68](followups.md)(c) and the charter's §3 and §3a.
- **[F45](followups.md)(b)'s production plumbing.** ~3–4 h + tests. **Rejected on substance, not only budget.**
  (i) RULE S16 returned NO RECOMMENDATION and §3a forbids repairing `S16-A(b)`, so the plumbing would deliver a
  mechanism **no rule has recommended** — the brief's own "nothing to ride with", and it is the decisive
  reason. (ii) After wave 14 flipped `MaxOutlierRejections`'s default to 0, `RejectionTest` does not run at all
  on a default profile, so a SEM floor reaches **3 of the 9** profiles on this machine. (iii) `N*` is not
  reachable in `AutoFocusEngine` — `:901` drops the count into `MeasureAndError`, a NuGet struct of two doubles
  — so it needs a parallel count map, a pooling rule that does not double-divide against the existing
  `/√frames`, the same again in the optimizer's path, and a choice between `DetectedStars` and `hfrStars.Count`.
  **The correct home for it is a wave that first writes the new rule §3a demands.**
- **[F59](followups.md)'s five knobs.** ~1 h. **The wave-18 objection does lapse and the item is still
  rejected**, for three reasons the brief does not have. (i) F59's item is a **re-pin of the settings file**,
  and re-pinning was closed **DO-NOT-RE-PIN** in wave 17 under [F63](followups.md)(b). *Refuting an argument is
  not refuting its conclusion* (wave 14's lesson): before overturning a decision because its reason has lapsed,
  ask what would make the decision right — and what would is a question that depends on the answer. (ii) There
  is none: the five knobs are absent from the pinned file, so there is no printed value to diff, and nothing in
  the register turns on it. (iii) A re-pin moves the detector on every arm forever after, i.e. it is a
  coordinate-system move with no question behind it. **"It is cheap right now" is not a reason to move a
  coordinate system** — that is exactly the fatigue-driven decision the drop order exists to prevent.
- **The other 11 `BuildDefaultStarDetectorParams` call sites** (`bank-verify`, `golden eval`, `synth-validate`,
  `bank-donut-meta`, `inspect-align`, `tilt-calibration`). Moot while the seed literal does not ship. **None is
  measured**; measuring one is ~30 m and buys no decision.
- **A fresh eight-value baseline.** **Not owed.** Wave 18's Part 2 did not ship, K8 stays valid, and nothing
  this wave ships can move the search — which RULE G19 measures rather than assumes.
- **The wizard-side confirmation of anything.** Blocked with item C (§7). ~30 m *if the permission is granted*,
  which is not this agent's to grant.

---

## §11 — Where the controller's brief is wrong

Recorded because every pre-registration in this series has been asked to say so, and because four of these
change what the wave does.

1. **"(a) fresh arms — the honest route to a verdict" is the brief's central premise and it is false.** A fresh
   pair would reproduce wave 18's landings: the landing is bit-identical across invocations on **all 33
   comparable keys** at n = 3 (measured for this document at zero compute), `BestJ` has reproduced
   bit-identically at sixteen digits on **nine binaries**, and wave 15's `G-e` measured search noise at zero.
   **(a) is (b) plus two hours and a false claim of freshness**, and it is a fork with no good tine: either the
   arms reproduce (the harvested verdict, with an alibi) or they do not (the in-sample instrument is noise and
   20 datasets cannot decide). §3.2.
2. **The brief treats the 8 minutes as cheap. They are not — and the mechanism is already in the register.**
   Publishing a diagnostic **spends** the population it reads. Wave 15 published `V4′` under an explicitly-
   no-verdict label and RULE F14 is now permanently NO VERDICT *because a rule cannot bind a reader who has
   seen it*. Option (b) does that to F70(a), irreversibly, to buy a number for a change whose measured reach is
   **0 of 9 profiles**. **The cheapest way to keep a question decidable is to not look.** §3.3.
3. **"K8 is still valid — wave 18 created no debt" is right, and it has a consequence the brief does not
   draw: this wave needs exactly ONE binary, and the controller's standing "code → ONE binary → gate → arms"
   order is CORRECT here.** Wave 18's two-binary departure was correct *for a change the gate can see* and
   **does not generalise**. Everything wave 19 ships is inert on the search by construction — a log-only dump,
   a no-op flag notice, a dead-literal deletion behind a UI-only entry point, and a manual line. Putting all of
   it in the gate's binary is what turns the inertness claim into a **measurement**. §1.3.
4. **The brief's five-part statement is right and its "which copy" question has THREE answers for an `optimize`
   log, not two — and this wave is the one that creates the third.** F69(b) adds `optimize/detected` to every
   log. A wave whose theme is *name the field* must not silently add a third copy of every field name, so the
   tag is fixed here against substring collision with `optimize/baseline` and `optimize/seed`, and every log
   read in the wave is a BEGIN…END range. §5.1, §8.3(4).
5. **The manual line is not part of F70(a), and wave 18 bundled it with the wrong question.** The settings page
   documents the **option** — 4 on every path since `93e366a`, because Part 1 made `ResetDefaults()` derive too
   — while F70(a) is about the **optimizer's seed**. Wave 18 made the manual fix conditional on RULE N18, N18
   returned UNEVALUATED, and the revert left a documentation line that the same wave's Part 1 had just made
   unambiguously wrong. It ships unconditionally. *F71's own defect, in the manual.* §5.4.
6. **The brief's `git status` is stale, and the stale version describes a live hazard.** It lists
   `HocusFocusStarDetection.cs`, `StarDetectionOptionsTests.cs` and `preprocessing.md` as modified — exactly
   wave 18's Part 2 — and names `93e366a` as the tip. **HEAD is `28f02ce` and the tree is clean**, verified.
   But `part2_w18.patch` is on disk, and a wave that trusted the brief would have built a binary carrying a
   moved optimizer seed and called it a gate. `G19-P1` and `R19-V3` are the clauses that catch it. §1.2.
7. **"F59's objection lapses if this wave does not move the coordinate system" — the objection lapses and the
   item is still rejected**, because it is a re-pin and re-pinning was closed DO-NOT-RE-PIN in wave 17.
   *Refuting an argument is not refuting its conclusion.* §10.
8. **On item C the brief is right in every particular**, including that the absence of "Hocus" does not
   exonerate the plugin and that the permission must not be worked around. Nothing is added except the
   discipline of recording an unchanged disposition rather than letting it decay. §7.

---

## §12 — Step order, fixed and not negotiable

1. Commit this pre-registration and the plan. **Before any build and any measurement.**
2. **Item D** lands in the tree (F69(b), F69(c), F70(b′), the manual line). Run the suite. Every new test must
   be shown to **fail against the pre-change source or a named mutant** — **M-R1** for F70(b′) — and the
   demonstration is **verified by the controller**, not asserted by the code agent.
3. Build **one** binary into `D:\hf_w19\exe`. Record both dll sha256 values; the `BuildId` is **read from the
   first landing**, not guessed. **Hash the DLLs, not the exe** — wave 18's `TestApp.exe` was byte-identical
   across two different binaries.
4. Write **all three** BEFORE fingerprints (42 bank landings; 59 aux files; 48 prior-wave landings).
5. Run **RULE G19**. Score it: `score_w12.py --rule G19`, `prov_w19.py --self-test` in **both** directions with
   the mutation asserted, and all three fingerprints. **A partial reproduction stops the wave, and F69(b) is
   the first suspect.** Then arm the interlock with `score_r19_w19.py --arm-g19-passed`, which **re-reads the
   gate** rather than trusting a claim — including that all 8 seed dumps read 3.
6. Run `probe_w19.sh` (~1 m) and score **RULE C19** with `--self-test`, then `--retro --probe`. Both
   demonstrations are required before `C19_PROBE_PASSED` is written, and **the marker is not consumed by
   anything in this wave.**
7. Run the **RULE R19** arm (~55 m). All three fingerprints. **Read the log, not the exit code** —
   `exit=0` means the wrapper exited.
8. Score **RULE R19**: `--self-test` first, then the full scorer. **Apply the branch table as written. Do not
   re-decide a clause — an unsatisfiable rule is a finding.**
9. If `R19-A < 1.000`: run the pre-registered disambiguation on **at most five** failing datasets against
   `/mnt/d/hf_w18/exe_b1` (~2.7 m each), into `reA0_b1`. More than five: record and stop.
10. **Optional, only if the wave is under 4 h:** item F, the 20-minute-timeboxed F21 price probe.
11. Run the suite on the **final** tree. Verify by **COUNT** (F37). Baseline **3831**. Name every added test.
    *Do not pipe to `tail` — it masks the exit code.* `SendAsync_WritesOnABackgroundThread` is a known flake
    and is not chased on a full-suite run.
12. Commit, push, append the wave's section to PR #191, verify CI by **COUNT read out of the log**.

**If a second binary becomes necessary at any point, it is a FINDING and it is disclosed in the results doc —
it is not quietly done, and the gate is re-run.**
