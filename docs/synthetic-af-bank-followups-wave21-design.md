# Synthetic AF bank — followups wave 21 (pre-registration)

Plan: [`plans/synthetic-af-bank-followups-wave21-plan.md`](../plans/synthetic-af-bank-followups-wave21-plan.md).
Charter: [`docs/waves17-21-handoff-prompt.md`](waves17-21-handoff-prompt.md) —
**superseded by [`docs/waves22+-handoff-prompt.md`](waves22+-handoff-prompt.md), a deliverable of this wave.**
Wave 20: [`docs/synthetic-af-bank-followups-wave20-results.md`](synthetic-af-bank-followups-wave20-results.md).
Register: [`docs/followups.md`](followups.md).

> **THIS DOCUMENT IS WRITTEN AND COMMITTED BEFORE THE BINARY IS BUILT.** Every threshold below is fixed here.
> Any number in this file that was measured at pre-registration time is labelled **`[PRE-REG MEASURED]`** and
> names the artifact it came from; those artifacts are frozen under fingerprint class 4 before the gate runs.

> ## THE CLOCK IS THE BINDING CONSTRAINT, AND THE WAVE IS SHAPED AROUND IT
>
> | | |
> |---|---|
> | pre-registration written | `2026-08-11`, starting `~08:35Z` |
> | **hard stop** | **`12:00Z`. No arm starts after that.** |
> | total budget | **~3 h 25 m**, including a ~42 m gate, analysis, the suite, commit, push and CI |
> | design principle | **the cheapest decisive thing first, and a drop order that assumes the cutoff fires** |
> | this is likely the **last wave** of the run | so `docs/waves22+-handoff-prompt.md` is a deliverable, not a courtesy |
>
> Three of this wave's five items cost **zero `TestApp` seconds** and two of those were already **measured
> while this document was being written**. That is deliberate: a wave that can be cut at any point should have
> banked its findings before its first arm starts.

---

## §0 — What this wave does, in one table

| # | item | what it is | `TestApp` | gate reaches it? | the check that does |
|---|---|---|---|---|---|
| **A** | **F69(a)'s second address** | correct the false XML doc at `OptimizationDiagnosticRunner.cs:591-594`, **and** ship a test that makes the *class* mechanical | 0 | **NO** — a comment is not IL and a test does not ship | the **suite**, by COUNT, with the new test demonstrated **RED** against the pre-change source (§3) |
| **G** | **RULE G21** | the eight-value gate on a **twelfth** binary, plus `G21-P1/P2/P3` | **~42 m** | it **is** the gate | itself, two-ended: `G21-P2` FAILs on wave 19's real logs and PASSes on wave 20's and wave 21's (§2) |
| **B** | **RULE B21** | the `optimize/detected` post-mutation property on the **six factor-2 datasets RULE D20 never measured** | ~15 m, clock-guarded | reaches the **code** B21 reads (wave 20's D1) via `G21-P2`; does **not** reach B21's finding | the rule itself (§4) |
| **C** | **F74's fix, by construction** | a manifest the driver writes and the scorer *reads*; one shared layout file; two instrument defects wave 20 recorded | 0 | **NO** — drivers and scorers are in no binary | `score_b21_w21.py --self-test` (20 branches) and `prov_w21.py --self-test` (3 directions) (§5) |
| **P** | **F21's price probe** | rule-free, hard-timeboxed, `synth-validate` finally invoked with a `--spec` | ≤ 20 m | **NO** | none. It has no clause and must not get one (§6) |

**Rejected, with reasons, in §7: F45(b) (time), F59 (merits, on wave 20's measurement), and re-running RULE D20
(the harvest fence, §1).**

---

## §1 — The decision this wave was asked to make: (a), (b) or (c) on RULE D20

Wave 20's **RULE D20 is `D-UNEVALUATED`** on a plumbing failure ([F74](followups.md)), while **the measurement
it was built to make is published** in that wave's results §3.2 as a labelled diagnostic: on `D12`,
`optimize/baseline` reads `DetectionBinning=1, PixelScale=NaN`, `optimize/detected` reads `2` and
`0.6296504611753467`, and the difference set over all 55 fields is exactly `{DetectionBinning, PixelScale}`.

The choice put to this pre-registration was: **(a)** fix the plumbing and apply the rule prospectively;
**(b)** fence D20 permanently like RULE F14 and fix the plumbing for later waves; **(c)** something better.

### 1.1 The answer is **(c)**, and (c) is **(b) plus a disciplined (a)** — which is what the F14 fence already prescribes

**RULE D20 is permanently `D-UNEVALUATED`. It is fenced. And a NEW rule — RULE B21 — carries the corrected
clause forward onto a population D20 never touched.** That is not a compromise between the two options; it is
the remedy the register wrote down when it closed F14: *"the SEM-floor wave writes a NEW rule with a branch
table that CAN consume the SEM result, and applies V4′ **prospectively** to a population measured after the
rule is fixed."* Charter §3, applied to a different rule.

### 1.2 Why not (a) alone — "fix the path, re-run `D12`, score D20"

Two independent reasons, and the second is the one that settles it.

1. **It cannot be blind.** The numbers are in a published results document. The F14 fence's first reason —
   *"the rule's own guard cannot bind a reader who has already seen the published diagnostics"* — applies
   verbatim, and it applies to the person writing the scorer as much as to the person reading it.
2. **A path fix does not repair the rule, because the rule has a second and substantive defect.** `D20-B`
   compares the dump's `PixelScale` against the console line *"to 6 dp"*, implemented as `abs(Δ) < 5e-7`. The
   console value is `v.ToString("G6")` — **six SIGNIFICANT digits** (`OptimizationDiagnosticRunner.cs:1938`,
   through `F()` at `:611`). Below 1.0 the tolerance **equals** the instrument's quantum; at or above 1.0 the
   quantum is `5e-6`, **ten times the tolerance**.

   > **`[PRE-REG MEASURED]` — and this is stronger than wave 20 stated it.** Wave 20 argued the defect
   > analytically (*"`D20-B` would have failed on a correct program"*). It is an **observation**. Applying
   > `D20-B` verbatim to wave 20's own eight gate logs — `/mnt/d/hf_w20/gate/*.log`, frozen under fingerprint
   > class 4 — it **FAILS on 4 of the 8**, and they are exactly the four whose `PixelScale >= 1`:
   >
   > | dataset | console (`G6`) | dump | Δ | `D20-B` (`5e-7`) | corrected |
   > |---|---|---|---|---|---|
   > | `CWhiteFocus` | `1.32574` | `1.3257361905796277` | `3.809e-6` | **FAIL** | pass |
   > | `D18_m24_deep_shed` | `1.4101` | `1.4101012208892405` | `1.221e-6` | **FAIL** | pass |
   > | `D20_m24_bright_control` | `1.4101` | `1.4101012208892405` | `1.221e-6` | **FAIL** | pass |
   > | `muggsie` | `1.03356` | `1.033556334333394` | `3.666e-6` | **FAIL** | pass |
   > | `D19_cygnus_deep_shed` | `0.775556` | `0.7755556714890822` | `3.285e-7` | pass | pass |
   > | `mccomiskey` | `0.288525` | `0.28852517540516454` | `1.754e-7` | pass | pass |
   > | `toml999` | `0.73944` | `0.7394398714518549` | `1.286e-7` | pass | pass |
   > | `uneven` | `0.711519` | `0.7115189646688828` | `3.533e-8` | pass | pass |
   >
   > Reproduce: `python3 /mnt/d/hf_w21/score_b21_w21.py --gate /mnt/d/hf_w20/gate`.

   So a wave-21 re-run of RULE D20 would have to **repair `D20-B`'s threshold with the measured value already
   in hand**. That is precisely the move the F14 fence forbids, and it is not available. *You cannot re-run a
   rule you have already had to fix.*

### 1.3 Why not (b) alone

(b) leaves two things unpaid that cost ~15 minutes together. [F69](followups.md)(b) stays recorded as
*delivered-by-diagnostic, never demonstrated-by-rule*, permanently. And F74's fix would ship with **no arm that
exercises it** — a plumbing repair whose only evidence is that it compiles. The register has an entry
([F66](followups.md)) for instruments that were never shown to return their own PASS.

### 1.4 What the fence says, exactly, so a later wave cannot read this as an invitation

> **RULE D20 IS PERMANENTLY `D-UNEVALUATED`. DO NOT HARVEST IT.**
>
> 1. Its measurement is published as a diagnostic; no re-run of the same probe on the same dataset can be blind.
> 2. `D20-B`'s tolerance is finer than its own instrument's resolution and **is observed to fail a correct
>    program on 4 of 8 real logs** — so repairing it now is repairing a rule after the data.
> 3. A wave that repairs a failed gate and immediately harvests its verdict teaches the next wave to do the same.
>
> **The diagnostic is never promoted to a verdict by hand, by this wave or any later one.** What a later wave
> may cite is RULE B21's verdict, on RULE B21's population.

---

## §2 — RULE G21: the gate, and the stopping gate

Eight values, `optimize --per-run --max-evals 250`, both pins named in the driver header, sequential, one
`TestApp.exe`, no NINA, no fan-out ([F60](followups.md)). Driver: `/mnt/d/hf_w21/gate_w21.sh`.
**Estimate 42 m.** A partial reproduction **stops the wave**.

```
toml999 0.995784 | CWhiteFocus 0.996068 | uneven 0.996368 | muggsie 0.997195
mccomiskey 0.976746 | D18 0.999882 | D19 0.999487 | D20 0.999738
```

`--settings D:\hf_w11\pinned_settings_w11.json` (md5 `a67ffc06164c81613aef5c4f8324b9b8`, **not re-pinned**,
[F63](followups.md)(b)) and `--profile-id ce3f3e63-8fd3-4b72-a0ca-d90db9441382` (`astrodet`).

### 2.1 Every clause, with the five F68 columns

**P** = population (artifact **and** field) · **W** = *which copy of that field, and is it the live one* ·
**S** = statistic · **A** = aggregation · **E** = the answer on an empty set.

| clause | threshold, fixed here | P / W / S / A / E |
|---|---|---|
| **G21-1** *(stopping)* | all 8 `BestJ` reproduce the eight values to 6 dp; partial ⇒ **stops the wave** | 8 `optimized_settings.json` under `/mnt/d/hf_w21/gate`, field `FinalJ` at top level / one copy per landing / equality to 6 dp / all-of, printed `n of 8` / unreadable ⇒ COULD-NOT-LOOK **and FAIL** |
| **G21-2** | `aggregate_summary.json produced: 8`, asserted **in the driver** before any scorer | `find` over the gate root / — / existence / count `== 8` / 0 ⇒ FAIL |
| **G21-3a** | exactly one `BuildId`, **novel against ELEVEN recorded ids** | `Provenance.BuildId` ×8 / one copy per landing / set / cardinality **and** disjointness / empty ⇒ FAIL. `KNOWN_DLL_SHA256` is a **separate** table ([F66](followups.md)); a `BuildId` equal to a known dll hash FAILs loudly |
| **G21-3b** | `DetectorVersion == 2`, read as **the FIELD** | `Provenance.DetectorVersion` ×8 / one copy / set / `== {"2"}` / empty ⇒ FAIL. `strings`/`strings -el` appears nowhere |
| **G21-3c** | `ProfileId` contains `ce3f3e63-…` **and** cardinality exactly 1 | `Provenance.ProfileId` ×8 / one copy / containment + cardinality / both, evaluated separately / the cardinality clause carries the empty read |
| **G21-3d** | one distinct `FitInputs`, exactly `MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid` | `Provenance.FitInputs` ×8 / one copy / set / equality / empty ⇒ FAIL |
| **G21-3e** | `ConcurrencyCheck == exclusive`, read **across the arm** | `Provenance.ConcurrencyCheck` / a length-8 list with an explicit `None` per unreadable landing / equality / all-of over a **length-8** list / a `None` is a FAIL, never a skip |
| **G21-3f** | `BaselineJ` reproduces wave 11 to 6 dp | `BaselineJ` ×8 / one copy / equality / `n of 8` / missing ⇒ FAIL |
| **G21-4** | `prov_w21.py --self-test` passes on the real arm, fails on a mutated copy with the mutation asserted by read-back, **and its printed id count equals `len(PRIOR_BUILD_IDS)`** | the arm and a `copytree` / `Provenance.ProfileId` on `muggsie` / — / three directions / an unmakeable mutation prints SELF-TEST COULD NOT RUN |
| **G21-P1a** | `optimize/seed.NoiseReductionRadius == 3` on 8 of 8, asserted **in the driver** | the 8 gate logs / the name **inside the `optimize/seed` BEGIN..END range** — *three* blocks now carry it / field value / `== 8` / a missing block ⇒ the driver refuses to hand the arm on |
| **G21-P1b** | `optimize/baseline.NoiseReductionRadius == 4` on 8 of 8 ([F70](followups.md) corroboration) | as above, **the `optimize/baseline` range** / field value / `== 8` / missing ⇒ FAIL |
| **G21-P2a…e** | see §2.2 | see §2.2 |
| **G21-P3a** | `detected.PixelScale` rounds to the console's `detecting at PixelScale <v> arcsec/binned-px` value **at the console's own `G6` precision**, exactly, on 8 of 8 | the 8 gate logs / the `optimize/detected` range's `PixelScale` **against the console line**, two different sources on purpose / `round(v, 6 - floor(log10 |v|) - 1) == float(console)` / `n of 8` / either side missing ⇒ FAIL, named |
| **G21-P3b** | wave 20's `D20-B` rule (`abs(Δ) < 5e-7`) **FAILS on exactly 4 of 8**, and the failing set is exactly `{CWhiteFocus, D18_m24_deep_shed, D20_m24_bright_control, muggsie}` | as `P3a` / same / the wave-20 predicate / **set equality**, not a count / an empty failing set is a FAIL of this clause |

`G21-P1a` is the permanent guard against `/mnt/d/hf_w18/part2_w18.patch` silently converting this wave's binary
into wave 18's B2. **The hazard has had an address for four consecutive waves and the address has stayed empty.**

### 2.2 `G21-P2` — five sub-clauses, and its FAIL end is observed on real artifacts

| sub-clause | threshold | wave 19's gate (**`[PRE-REG MEASURED]`**, the FAIL end) |
|---|---|---|
| **P2a** *reach* | `PARAMS-DUMP optimize/detected BEGIN` **exactly once** per log, 8 of 8 | **0 of 8** — *"F69(b) IS NOT IN THIS BINARY"* |
| **P2b** *shape* | **55** field lines and **55 unique** names, 8 of 8 | 0 of 8 |
| **P2c** *post* | `detected.PixelScale` finite **and** `baseline.PixelScale` the token `NaN`, 8 of 8 | 0 of 8 |
| **P2d** *honest* | `detected.DetectionBinning == 1` — **an equality**, 8 of 8 | 0 of 8 |
| **P2e** *no clash* | `optimize/baseline` and `optimize/seed` still exactly once each, 8 of 8 | **8 of 8** — the control that must not move |

Five-part statement, once for the group: **P** the 8 `*.log` under the gate root, each block matched as a
BEGIN..END range keyed on the **exact tag**; **W** the named block and **no fallback across blocks** — three
blocks carry every field name, so this is the maximum of the [F71](followups.md) hazard; **S** counts,
counts-of-distinct, and value class (finite vs the literal token `NaN`; nothing calls `float()` on `NaN`);
**A** `== 1` per log, aggregated `n of 8`; **E** an unreadable log is COULD-NOT-LOOK, **named**, and the clause
**FAILS**.

> **`P2d` is an equality on purpose and that is the honest half.** The resolved factor is **1 on all eight gate
> datasets**, so a clause demanding that `DetectionBinning` *move* would be **unsatisfiable on this population**
> — the exact [F68](followups.md) defect. The difference is carried by RULE B21, on six datasets chosen because
> their factor is 2.

> **What `G21-P2` proves this wave that it could not prove last wave.** Wave 20 measured D1 on **one** binary.
> `G21-P2` measures it on a **second** one, which is the first evidence that the dump is a property of the
> *code* rather than of that build. It reaches **none of wave 21's own deliverables** — see §0's fifth column,
> and §3.

### 2.3 What `G21` does not prove

Nothing about the landing beyond `FinalJ` and `BaselineJ` — RULE R19 owns that, and on a population of
**C#-identical** builds ([F73](followups.md)); the code axis is still unmeasured. It runs at
`MaxOutlierRejections = 0`, so it says nothing about the rejection path. **And this wave it is not a control on
the change at all**, because the change is a comment.

---

## §3 — Item A: [F69](followups.md)(a), at the address the register never named

### 3.1 What is wrong

`OptimizationDiagnosticRunner.cs:591-594`, the XML doc on `ApplyRunDetectionBinningIfRequested`, unchanged since
`9cd4c02`:

```csharp
/// F39(b) — DETECT this run at its own detection-binning factor, instead of writing the factor to disk and
/// running at the default of 1. No-op unless <c>--apply-run-detection-binning</c> was passed, so the flag's
/// absence leaves the run bit-identical to before it existed (F41's one-binary-is-both-arms rule).
```

Every clause of the second sentence is backwards. The method runs **unless the opt-out
`--no-run-detection-binning`** is passed (`:216`), and `--apply-run-detection-binning` is an accepted no-op —
which the **body three lines below now prints at runtime** (wave 20's D2). A file that tells the truth on the
console and the opposite in its own doc comment is worse than one that is merely stale, and this is the exact
claim that cost RULE P16 a verdict.

### 3.2 The fix, and it is two lines of prose plus a test that makes the class mechanical

Replace the second sentence with: *"Runs by DEFAULT (F39(b), wave 8); the opt-OUT is
`--no-run-detection-binning`. `--apply-run-detection-binning` is an accepted no-op, retained for wave 7's
scripts (F69(c))."*

**And then the part that matters more than the edit.** Wave 20's Lessons #4 says *"when a register entry names
where a false statement lives, the fix is `grep` for the sentence, not an edit at the address."*

> **That lesson is not quite right, and this wave is the counter-example.** The two copies were
> **paraphrases**, not the same string:
>
> | site | text |
> |---|---|
> | `:405-409` (fixed in wave 18) | *"…**only under** `--apply-run-detection-binning`, `DetectionBinning` + `PixelScale` together…"* |
> | `:591-594` (still false) | *"**No-op unless** `--apply-run-detection-binning` was passed…"* |
>
> Their **only** shared substring is the flag name. A `grep` for the sentence would have missed this exactly as
> the edit-at-the-address did. **The rule that works is: `grep` for the IDENTIFIER the false claim is about,
> and read every hit — then leave a test that does it for you.**

**The test** (`ParamsDumpTests`, using the existing `RunnerSource` helper at `:464`):

`ApplyRunDetectionBinning_EveryCommentNamingTheOptInFlag_AlsoNamesTheOptOut`

- **Population:** every comment line (`//` or `///`, trimmed) in `OptimizationDiagnosticRunner.cs` — and, so the
  fix is durable across files, in every `*.cs` under `TestApp/` — that contains the token
  `--apply-run-detection-binning`. The population size is **printed and asserted `>= 2`**.
- **The invariant:** each such comment **block** (a maximal run of adjacent comment lines) must also contain
  `--no-run-detection-binning`. Rationale: the behaviour is decided by the opt-out, so any honest statement
  about the flag must name the flag that actually controls it. Both truthful sites (`:205-216`, `:410-419`)
  already satisfy it; the false one does not.
- **Could-not-look, and it FAILS:** if the identifier appears **zero** times, the test fails with *"the
  identifier this test is about is gone; it can no longer see its subject"* — never a silent pass.
- **Demonstrated RED:** the mutant is the revert. Restore the `:591-594` sentence and the test must go red,
  **asserted by the controller by read-back**, not claimed by the code agent.

### 3.3 Reach

**The gate does not reach item A and cannot.** A comment is not IL; a test does not ship. The check that reaches
it is the **suite, verified by COUNT** (baseline **3838**), with the new test shown red against the pre-change
source. Saying so here is the discipline wave 19 broke and wave 20 repaired.

---

## §4 — RULE B21: the post-mutation property, prospectively, on six datasets D20 never measured

### 4.1 The population, and why it is this one

`synthetic_meta.json`'s `expectedOptimal.detectionBinning` is **2** on exactly seven of the twenty synthetic
datasets. Wave 20 measured **one** of them. RULE B21's population is the other six:

| dataset | on-disk MB | `arcsecPerPixel` × `captureBinning` × 2 = expected `detected.PixelScale` |
|---|---|---|
| `D10_rc16_3250mm_sparse` | 156 | `0.4772650286` |
| `D17_cdk14_oiii5` | 163 | `0.6051936570` |
| `D09_c14_3800mm` | 206 | `0.5026347647` |
| `D08_c11_2800mm` | 450 | `0.5539683368` |
| `D15_cdk20_3454mm_e47` | 450 | `0.4490768219` |
| `D14_cdk14_2563mm_e47` | 1057 | `0.6051936570` |

**`[PRE-REG MEASURED]`** from the bank's `synthetic_meta.json` files, which are **fingerprint class 2** — so the
denominator is itself under a control. Datasets are ordered **cheapest first**, which is also the drop order.

> **`D17_cdk14_oiii5` is in the population on purpose.** It is the known-hazardous member (it finds zero stars
> at short exposures). If it cannot produce a scoreable log that is a **named COULD-NOT-LOOK**, not a quiet
> exclusion, and the validity floor is sized to absorb it plus one timeout.

> **A limit stated in advance: every one of the six has a binned scale below 1.0.** So the corrected `B21-B`
> and the broken `D20-B` return the **same answer on this whole population**. **The tolerance repair is
> therefore NOT demonstrated by this arm.** It is demonstrated by `G21-P3` on the gate — where four datasets
> sit above 1.0 — and by self-test branches 15–18. Naming the clause that reaches a fix is the same discipline
> as naming the check that reaches an item.

### 4.2 The clauses

| clause | threshold | P / W / S / A / E |
|---|---|---|
| **B21-V1** *validity: addressability* | `/mnt/d/hf_w21/B21_ARM_READY` exists **and carries a `manifest=` line**; the manifest parses as 4-column TSV; every row's `log` and `landing` **resolve as written**; **`>= 4` of 6** scoreable | the marker and `b21_manifest.tsv` / the marker's **contents**, never a template / existence + count / `>= 4` / any could-not-look ⇒ `B-UNEVALUATED`, and the **manifest's own string** is quoted |
| **B21-V2** *validity: one binary, pins held* | across the manifest's landings: `BuildId` cardinality 1 **and equal to the gate's**; `ProfileId` contains the pin **and** cardinality 1; `DetectorVersion == 2` as the FIELD; `ConcurrencyCheck == exclusive` on all | the landings the **manifest** names / `Provenance.*`, one copy each / sets and equality / all-of / unreadable ⇒ `B-UNEVALUATED` |
| **B21-V3** *validity: the population really is factor 2* | `detection binning (F39b): 2 from …` **exactly once** per log; a dataset at any other factor is **removed and named**; if that drops the count below 4 ⇒ `B-UNEVALUATED` | the manifest's logs / the console line, not the dump / value + count / per-log, then `n of 6` / removal is named, never silent |
| **B21-A** *the decisive one* | `detected.DetectionBinning == 2` **and** `baseline.DetectionBinning == 1`, **in the same log** | the two BEGIN..END ranges by exact tag / **two different blocks in one file — this is the whole point** / equality pair / **all-of** over the scoreable set, printed `n of m` / a missing block ⇒ named, and the clause fails |
| **B21-B** *the scale followed, at the console's own precision* | `detected.PixelScale` finite, `baseline.PixelScale` the token `NaN`, and the dump **rounds to the console's `G6` value exactly** | as above **plus** the console line / the `optimize/detected` copy, never baseline's / `round(v, 6 - floor(log10 |v|) - 1) == float(console)` / all-of / missing ⇒ FAIL, named |
| **B21-D** *the whole difference set* | the field-by-field difference between `optimize/baseline` and `optimize/detected` is exactly `{DetectionBinning, PixelScale}`; when it is not, the set is **printed in full** | 55 fields × 2 blocks / the two named ranges / set difference / all-of / a missing block ⇒ FAIL |

### 4.3 The branch table, fixed here

```
G21-P3b did not return its pre-registered set          ->  B-UNEVALUATED  (name G21-P3b: B21-B's premise moved)
B21-V1 or V2 or V3 does not pass                       ->  B-UNEVALUATED  (name the gate)
V1..V3 pass, and A, B, D are all-of over the scoreable ->  B-DEMONSTRATED
V1..V3 pass, A passes, B or D fails                    ->  B-PRESENT-PARTIAL  (name which, and the fields)
V1..V3 pass, A fails                                   ->  B-REFUTED
```

`B-REFUTED` is **reachable by construction** and would be the wave's largest finding: it would mean the dump
that wave 20 shipped is not the post-mutation bundle on datasets other than `D12`. Self-test branches 2 and 3
demonstrate that the scorer reaches it.

**Nothing in RULE B21 gates any ship.** Item A ships unconditionally, before the build. Tying a ship to a
measurement rewards not shipping, which was wave 19's failure mode.

---

## §5 — Item C: F74's fix, by construction, and two instrument defects

### 5.1 The fix

| half | what | where |
|---|---|---|
| **strong — by construction** | the driver **asserts** each artifact path exists, then **writes it** into `b21_manifest.tsv` (`dataset  factor  log  landing`); the marker carries `manifest=<path>`; the scorer opens the marker, reads the manifest path **out of it**, and reads every artifact path **out of the manifest's rows** | `b21_arm_w21.sh`, `score_b21_w21.py` |
| **weak — by convention** | **one file** declares every layout this wave uses and both drivers source it | `layout_w21.sh` |

**There is no `os.walk` fallback in the new scorer, deliberately.** Wave 20 had one, rooted at the same wrong
parent as its primary path; *a fallback rooted at the same wrong parent as the primary path is not a second
chance.*

> **The gate's layout is deliberately NOT unified with the arm's, and the reason is a control.**
> `score_b21_w21.py --gate` is run on **three** roots this wave: `/mnt/d/hf_w19/gate` (the FAIL end for
> `G21-P2`, on real artifacts), `/mnt/d/hf_w20/gate` (the PASS end, and the source of `G21-P3b`'s expected
> set), and wave 21's own. Renaming wave 21's gate logs would mean the PASS end and the FAIL end were read by
> two different code paths — the exact confound the two-ended demonstration exists to remove. So the gate keeps
> `<gate>/<name>.log`, and it is **declared** in `layout_w21.sh` instead of re-derived per script.
> **Centralising a convention is the weak half of F74; the manifest is the strong half; neither is asked to
> carry the other's weight.**

### 5.2 The two instrument defects wave 20 recorded, both fixed and both self-tested

| defect | fix | the check that reaches it |
|---|---|---|
| `prov_w20.py` printed *"novel against **NINE** recorded ids"* while `PRIOR_BUILD_IDS` held **ten** | the count is now **derived** from `len(PRIOR_BUILD_IDS)` (**eleven** this wave) | `prov_w21.py --self-test` **direction 3**: it re-reads its own printed report and asserts the number equals the dict size. *A message and its mechanism are two channels, and only one of them had ever been checked.* |
| `score_d20_w20.py --gate` **exited 0** on a `G20-P2` FAIL | the new scorer returns non-zero from `--gate` on any FAIL or could-not-look | `score_b21_w21.py --self-test` **branch 19**: `subprocess.call` on a known-bad root, exit code read from the call, **never through a pipe**; **branch 20** proves the same entry point *can* exit 0, so 19 measures the verdict and not the harness |

**`[PRE-REG MEASURED]`, both directions, before either is quoted:**

```
python3 /mnt/d/hf_w21/score_b21_w21.py --self-test          ->  20 of 20 branches ok
python3 /mnt/d/hf_w21/score_b21_w21.py --gate /mnt/d/hf_w20/gate   ->  PASS, 8 of 8 on P1a/P1b/P2a..e/P3a,
                                                                       P3b = 4 of 8 and the set MATCHES     exit 0
python3 /mnt/d/hf_w21/score_b21_w21.py --gate /mnt/d/hf_w19/gate   ->  FAIL, P2a 0 of 8                     exit 1
python3 /mnt/d/hf_w21/log_fingerprint_w21.py --self-test    ->  7 of 7
python3 /mnt/d/hf_w21/prov_w21.py --self-test <novel-BuildId copy of the w20 gate>  ->  PASS / FAIL / AGREE
bash /mnt/d/hf_w21/gate_w21.sh      --self-test  ->  PASS
bash /mnt/d/hf_w21/b21_arm_w21.sh   --self-test  ->  PASS (population re-verified against the BANK)
bash /mnt/d/hf_w21/f21_probe_w21.sh --self-test  ->  PASS
```

> **`prov_w21.py`'s known-good direction had to be constructed, and the construction is itself an F74
> instance.** Every recorded gate's `BuildId` is now in `PRIOR_BUILD_IDS`, so direction 1 cannot pass on any
> existing arm. It was demonstrated on a `copytree` of wave 20's gate with every `BuildId` rewritten to a novel
> value — and **the first attempt rewrote 6 of 8 landings and reported success**, because the glob
> `*/attempt01/optimized_settings.json` is wrong for the two real-bank runs, which nest one level deeper
> (`CWhiteFocus/AutoFocus_20220429_000244_attempt01/`). Found by asserting the count. **A path template got it
> wrong again, inside the pre-registration written to fix path templates**, which is why `layout_w21.sh`'s
> landing helper is a `find` and not a `printf`.

---

## §6 — Item P: the F21 price probe, rule-free and first in the drop order

**Wave 20 finally learned the real blocker**, and it was not the population and not compute: `synth-validate`
exits **2** because it requires `--spec <json>` and four waves of probes passed none. Nobody had ever invoked
the instrument successfully.

**`[PRE-REG MEASURED]`, at zero compute:**

- The spec exists and **ships with the build**: `Joko.NINA.Plugins/TestApp/SynthBank/synthetic-bank-spec.json`
  is copied to `<exe>\SynthBank\synthetic-bank-spec.json` (verified present in `/mnt/d/hf_w20/exe`). sha256
  **`bf10522e670a119e260a8b3d06c2cc6ec52ce513dde25ca9ca13f426c38672d6`**.
- **Caveat, recorded rather than assumed away:** the bank's `synthetic_meta.json` records
  `generator.specSha256 = 621455a1…`, so **the shipped spec is a LATER revision than the one that rendered the
  bank.** That is a caveat on any convergence **verdict**; it is not a caveat on a **rate**.
- Scenario ids are **S0–S6** (`SynthValidationScenarios.cs`). **S1** — *"step ×0.25, multi-round convergence"* —
  is the scenario that makes a second round happen **by construction**; S0 converged in one round in wave 10,
  so it can price a round but cannot show the population exists.
- `synth-validate` accepts `--settings` (via `HarnessSettingsStore.Resolve(args, …)`, `:241`) and
  `--profile-id`, so **it can be pinned like every other arm**. There is no excuse for an unpinned arm here.

**Invocation:** `synth-validate --spec <shipped> --out D:\hf_w21\f21 --datasets D11_rc10_585_afbin2
--scenarios S1 --max-rounds 2 --settings <pin> --profile-id <pin>`, under `timeout 1200`.
`D11` is the smallest dataset in the bank (38 MB) and is chosen for **price**, not physics.

**No clause is written over it** ([F68](followups.md): F21's own hypothesis was refuted in wave 10 and its
headline case did not reproduce, so a bar over "the population" would be a bar over a population nobody has
shown exists). **Deliverables only:** wall time, the exit code **by name**, the round count, `HalfWidth` and
recommended step per round, and whether a second round occurred at all. The driver prints
*"exit 124 — THE TIMEBOX FIRED; the honest answer is `> 1200 s`, UNMEASURED beyond that"* rather than inferring
anything, and it refuses to read empty report fields as *"no second round occurred"*.

---

## §7 — What this wave rejects, and why

| candidate | decision | reason |
|---|---|---|
| **re-running RULE D20** | **rejected — fenced** | §1. Two independent grounds, the second decisive: the rule's `D20-B` clause is a known defect, so a re-run would repair a rule with the measured value in hand |
| **[F45](followups.md)(b)'s production plumbing** | **rejected on time, and it is not close** | ~3–4 h plus tests against a **3 h 25 m** wave that also owes a 42 m gate. It would consume the entire budget and land nothing. It also remains behind the RULE S16 fence (charter §3a): S16 returned **NO RECOMMENDATION**, so the plumbing would deliver a mechanism no rule has recommended. **Rejecting it on time is the smaller of the two reasons and is stated second on purpose.** |
| **[F59](followups.md)'s five knobs** | **rejected, and this pre-registration AGREES WITH WAVE 20's MEASUREMENT rather than re-deriving it** | `DerivePresetSettings()` assigns four of the five (`MaxDistortion`, `StarCenterTolerance`, `HotpixelThreshold`, `Sensitivity`), so under the pinned file's `UseAdvanced=False` they are overwritten on load and repairing the exporter changes the detector by **exactly nothing** for them. The fifth, `SaturationThreshold`, is **not** preset-owned and **would** bind — which makes it a **coordinate-system move owing a fresh 42 m baseline**, i.e. this wave's entire gate budget spent a second time. **That is the price, and it is the whole objection.** No new measurement was made and none was needed |
| **a `DetectionBinning`-differs clause on the gate** | **not written** | **unsatisfiable there** — factor 1 on all eight. Recorded because writing it would have looked like rigour |
| **re-issuing RULE C19's verdict** | **not done** | `/mnt/d/hf_w19/C19_PROBE_PASSED` exists and reads `C-DEMONSTRATED`. A wave records the artifact-level fact; it does not re-issue a prior wave's verdict |
| **item C / the NINA UI checks A1, A2, A4–A8** | **not scheduled** | ~30 m on a session where NINA is not competing with a pinned arm ([F72](followups.md)); every arm in this wave is pinned, and the wave ends at 12:00Z |
| **the out-of-sample pass over wave 18's 40 arm landings; F73's code axis (~53 m); F70(b′) (~20 m)** | **not scheduled** | they do not fit behind a 42 m gate in a 3 h 25 m wave. Priced in the wave-22 handoff |

---

## §8 — Satisfiability, and the five F68 questions asked of this design

**Every threshold in this document was checked for (i) the value it can reach, (ii) the population it is
computed over, (iii) whether that population is ADDRESSABLE, and (iv) the PRECISION of the instrument it
reads.** (iii) and (iv) are new columns this wave, and they exist because F74 and `D20-B` are the two ways the
previous wave lost a measurement.

| clause | can it PASS? | can it FAIL? | population addressable? | instrument precision checked? |
|---|---|---|---|---|
| `G21-1` | yes — 11 binaries have | yes — a partial reproduction stops the wave | 8 landings, found by `find`, not by template | 16 significant digits in the landing; the 6-dp bar is far coarser |
| `G21-P2a…e` | yes — **8 of 8 on `/mnt/d/hf_w20/gate`** | yes — **0 of 8 on `/mnt/d/hf_w19/gate`**, same scorer, same minute | 8 `*.log` at the gate root, counted | counts, no precision issue |
| `G21-P3a` | yes — **8 of 8 on `/mnt/d/hf_w20/gate`** | yes — self-test 18 constructs a genuine disagreement | as above | **this clause IS the precision fix** |
| `G21-P3b` | yes — **exactly 4 of 8, the named set, on `/mnt/d/hf_w20/gate`** | yes — **0 of 8 on `/mnt/d/hf_w19/gate`** (no dump to compare) | as above | reads `G6` and says so |
| `B21-A` | yes — wave 20 saw the property once on `D12` | yes — self-test 2 and 3 reach `B-REFUTED` | **the manifest's rows, asserted by the driver before they are written** | equality on an integer |
| `B21-B` | yes | yes — self-test 4 | as above | **corrected**; and see the limit below |
| `B21-D` | yes | yes — self-test 5, with the field named | as above | string equality over 55 fields |
| `B21-V1` | yes | yes — self-test 6, 8, 9, 10 | **this clause is the addressability check** | n/a |
| item A's test | yes — the two truthful sites already satisfy it | yes — the mutant is the revert, demonstrated RED | every `*.cs` under `TestApp/`, count printed and asserted `>= 2` | n/a |

**Three limits stated in advance rather than discovered afterwards:**

1. **`B21-B`'s repair is not observable on `B21`'s own population.** All six datasets are sub-arcsecond, so the
   corrected clause and the broken one agree there. `G21-P3` and self-tests 15–18 are what reach the repair.
2. **`G21-P3b` is a clause about wave 20's instrument, not about the product.** It cannot move a product
   verdict; its only consequence is that a failure blocks `B21` from being scored.
3. **`B21`'s expected outcome is a PASS.** A rule everyone expects to pass has low decision value, and the
   honest defence is that its value is in the **denominator** (six datasets instead of one) and in `B21-D`,
   whose result on six *new* datasets — five of which have `captureBinning = 1`, unlike `D12` — is genuinely
   not known.

---

## §9 — Controls: four fingerprint classes, each with a stated job, all written BEFORE the gate

| class | population | count | job this wave |
|---|---|---|---|
| **1** bank landings | `D:\SyntheticAutofocusBank` + `D:\Autofocus Bank` | **42** | preservation; `bank_fingerprint_w15.py` |
| **2** bank aux | `harness_settings.json` ×39 + `synthetic_meta.json` ×20 | **59** | **EVIDENCE.** `expectedOptimal.detectionBinning` in those 20 files is what **selects RULE B21's population**. A file class that becomes a denominator is evidence; `aux_fingerprint_w17.py` |
| **3** prior-wave arms | `/mnt/d/hf_w18/{seedA0, seedA1, gate}` | **48** | preservation — the design promises not to spend `seedA1`, and 48 of 48 is the executable form of that promise; `arm_fingerprint_w19.py` |
| **4** **wave-20 logs (new)** | `/mnt/d/hf_w20/gate/*.log` ×8 + `dprobe/probe.log` + `dprobe/D12_c14_585_afbin2/run.log` | **10** | **EVIDENCE.** `G21-P3b`'s expected set (4 of 8, named) and `G21-P2`'s FAIL/PASS ends were derived on these files at pre-registration time, and `D12`'s exclusion from `B21`'s population rests on the probe log; `log_fingerprint_w21.py`, **7 of 7 self-test** |

All four are re-checked **after the last measurement**, at write-up.

---

## §10 — Budget, with clock times, and the drop order

| # | step | estimate | starts by | drop trigger |
|---|---|---|---|---|
| 0 | commit this pre-registration | 2 m | **09:20Z** | — (it is the wave) |
| 1 | **item A** — the XML doc + the test, demonstrated RED | 20 m | 09:22Z | if not green by **09:50Z**, ship the doc fix alone and record the test as owed |
| 2 | **suite by COUNT** (baseline 3838) | 4 m | 09:45Z | never dropped |
| 3 | build into `D:\hf_w21\exe`; record **both dll sha256** and the `BuildId` | 2 m | 09:50Z | never dropped |
| 4 | four fingerprint classes, `--write` | 2 m | 09:53Z | never dropped |
| 5 | **RULE G21 — the gate** | **42 m** | **09:55Z** | never dropped; a partial reproduction **stops the wave** |
| 6 | score the gate (`score_w12`, `prov_w21` ×3 directions, `score_b21_w21 --gate`) | 8 m | 10:40Z | never dropped |
| 7 | **RULE B21 — the six-dataset arm**, cheapest first, `timeout 600` each | 15–25 m | **10:50Z** | **the driver refuses to START a dataset after `11:15Z`.** `>= 4 of 6` is the validity floor |
| 8 | score RULE B21 | 6 m | 11:15Z | never dropped once the arm ran |
| 9 | **item P — the F21 price probe**, `timeout 1200` | ≤ 20 m | **11:20Z** | **DROP D1 — first to go. Not started after 11:20Z.** |
| 10 | re-check all four fingerprint classes | 2 m | 11:25Z | never dropped |
| 11 | analysis + results document | 25 m | 11:25Z | **DROP D2** — if short, the results doc may cite the design for thresholds and carry only measurements |
| 12 | final suite by COUNT, commit, push, PR section, CI | 25 m | 11:35Z | never dropped |

**Drop order, in the order the cuts happen:**

1. **`D1` — item P (F21).** Rule-free by construction, so nothing loses a verdict. Cost of dropping: the
   instrument stays unpriced for a fifth wave — but **the `--spec` file, the scenario choice and the pinning
   are now recorded at zero compute**, so the next attempt starts from a working invocation instead of from
   exit 2. That is strictly more than wave 20 left.
2. **`D2` — `D14_cdk14_2563mm_e47`, then `D15`, then `D08`** (the three largest). The arm is ordered cheapest
   first for exactly this. Dropping all three still leaves **3 of 6**, which is **below** the validity floor and
   yields `B-UNEVALUATED` — so the real statement is: **`D10`, `D17`, `D09` and `D08` must run, and the
   deadline is set at `11:15Z` to make that near-certain.**
3. **`D3` — the results document's prose.** Measurements and the register entry survive; narrative does not.
4. **Never dropped:** the gate, the suite, the fingerprint classes, the commit and the push. A wave that
   measures and does not commit has produced nothing.

**If the gate over-runs past `10:45Z`, item B is cut to the first four datasets by moving the deadline to
`11:05Z`. If it over-runs past `11:00Z`, item B is dropped entirely and the wave ships item A, the F74 fix, the
instrument repairs, the D20 fence and the handoff** — all of which are already complete at that point, which is
why they were done first.

---

## §11 — Self-audit: this design against its own clauses

Wave 17's self-audit was false and wave 19's claimed an unshipped item, so this one is checked line by line.

| the discipline | does this design obey it? |
|---|---|
| every clause carries P / S / A / **E** | **Yes** — §2.1, §2.2, §4.2. |
| **the fifth column: WHICH COPY of the field, and is it live** | **Yes**, and it is load-bearing: three blocks now carry every field name, so every clause names its BEGIN..END range and forbids cross-block fallback (§2.2, §4.2). |
| prefer rates with named denominators | **Partly, and the exceptions are named.** `G21-*` are `n of 8` against a population asserted in the driver. `B21-A/B/D` are **all-of over the scoreable set with `n of m` printed**, and `m` is itself a clause (`B21-V1`). `G21-P3b` is a **set equality**, not a count — deliberately, because "4 of 8" without the names could be satisfied by the wrong four. |
| a clause about the wave's own deliverable has its FAIL end measured on real artifacts | **The gate does not reach this wave's deliverable at all, and §0 and §3.3 say so rather than implying otherwise.** For the deliverables the gate *can* speak to (wave 20's D1), the FAIL end is `0 of 8` on `/mnt/d/hf_w19/gate`, measured. For item A, the FAIL end is the test **demonstrated RED against the pre-change source**, verified by the controller. |
| every gate demonstrated PASS-on-known-good and FAIL-on-known-bad, with the mutation asserted | **Yes, and it is already done** — §5.2, all eight self-tests run at pre-registration time. |
| check both branches are reachable, not just the PASS value | **Yes** — §8, including `B-REFUTED`, which is reachable and would be the wave's largest finding. |
| an unsatisfiable clause is not written | **Yes** — the `DetectionBinning`-differs clause is explicitly **not** written for the gate (§7), and `B21-B`'s repair is declared **unobservable on `B21`'s own population** (§4.1) rather than being quietly credited to the arm. |
| the satisfiability analysis checks the instrument's precision | **Yes** — §8's fourth column. This is the `D20-B` defect, and it is why `G21-P3` exists. |
| the satisfiability analysis checks the population is ADDRESSABLE | **Yes** — `B21-V1` is that check, and §5.2 records an F74 instance found **inside this pre-registration**. |
| price the payoff in both directions; say what was not run | **Yes** — §7 and §10. |
| pin `--settings` and `--profile-id` on every arm | **Yes, on all three** — the gate, the B21 arm, **and** the F21 probe, which prior waves left unpinned. |
| ONE binary, never rebuilt mid-wave | **Yes.** Item A is a comment and a test; nothing this wave ships can move the search, so all of it goes into the gate's binary and `G21-1` passing *is* the inertness measurement — with the honest caveat that a comment could not have moved it anyway. |
| **a claim in this table that is not true is the failure mode** | The one place this design is weakest: **`B21`'s expected outcome is a PASS**, and §8's third limit says so instead of dressing a replication up as a discovery. |

---

## §12 — Where the controller's brief is wrong, or imprecise

1. **"the exact statement that cost RULE P16 a verdict"** — not exact. The statement that cost P16 its verdict
   was the `:405-409` comment (*"only under `--apply-run-detection-binning`…"*). The surviving copy at
   `:591-594` (*"No-op unless `--apply-run-detection-binning` was passed"*) is the **same claim in different
   words**. This matters, because it means **wave 20's own Lessons #4 — "grep for the sentence" — would not
   have found it either.** §3.2 replaces that rule with one that works: grep for the **identifier**, and leave
   a test.
2. **"fix the plumbing and apply the rule prospectively" reads as if (a) and (b) were exclusive.** They are
   not, and the F14 fence already prescribes their combination: fence the old rule, write a new one, apply it
   prospectively. That is why the answer is (c) and why (c) is not a dodge.
3. **The brief says `D20-B` "would fail a correct program at `PixelScale >= 1`".** It **does** — on **4 of 8**
   real logs that already exist. It is an observation available at zero compute, not a prediction, and this
   design turns it into a two-ended clause (§2.1, `G21-P3b`).
4. **"`prov_w20.py` prints novel against NINE while checking TEN"** — correct, and the brief's own novelty list
   already carries **eleven** ids (wave 20's `1a4dd85a` included). `prov_w21.py` now derives the word from
   `len(PRIOR_BUILD_IDS)`, so the two can no longer disagree.
5. **The brief treats F21 as an open candidate without noting that wave 20's item P DID run** and returned
   `COULD-NOT-LOOK` with a named exit code (2, missing `--spec`) — which is what makes item P cheap this wave
   rather than another blind deferral. §6.
6. **Wave 20's results §2.1 lists the eight finite `PixelScale` values without naming their datasets**, and the
   surrounding prose reads as if they were in **run** order; they are in **file** order. Nothing was concluded
   from it, but an unlabelled vector of eight numbers next to a table of eight datasets is the F68 field
   problem in its smallest form. This design labels every one (§1.2).
7. **"Four fingerprint classes before the gate"** is stated as a count. A count is not the discipline — wave
   17's generalisation is that *a file class that becomes evidence must become a control in the same wave.*
   §9 gives each class a **job** and marks two of the four as evidence and two as preservation. Had the count
   been the requirement, class 3 would have been dropped and class 4 kept twice.
