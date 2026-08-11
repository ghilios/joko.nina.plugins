# Synthetic AF bank — followups wave 18 (results)

Design / pre-registration: [`docs/synthetic-af-bank-followups-wave18-design.md`](synthetic-af-bank-followups-wave18-design.md).
Plan: [`plans/synthetic-af-bank-followups-wave18-plan.md`](../plans/synthetic-af-bank-followups-wave18-plan.md).
Wave 17: [`docs/synthetic-af-bank-followups-wave17-results.md`](synthetic-af-bank-followups-wave17-results.md).
Charter: [`docs/waves17-21-handoff-prompt.md`](waves17-21-handoff-prompt.md).
Register: [`docs/followups.md`](followups.md).

> ## PROVENANCE
>
> | input | value |
> |---|---|
> | tree | branch `ghilios/synthetic-af-bank-followups-wave13`, PR #191. Pre-registration `6d62e92`, committed **before either binary was built**. Item A Part 1 `93e366a` |
> | code shipped by this wave | **Item A Part 1 only** (`93e366a`). **Part 2 does NOT ship** — RULE N18 returned `N-UNEVALUATED`, §4 |
> | **B1** — the gate's binary and arm A0's | `D:\hf_w18\exe_b1`. HEAD + Part 1. `TestApp.dll` sha256 `3c5b2a5ce175bc4c859f26fb74b27711c2ef513f71d2659a0e4ab3fb4a6a69f7` · `NINA.Joko.Plugins.HocusFocus.dll` sha256 `08aa08094434d417d6f8c54e8f9d9f6b384cf8625c6483d412f6089056353b08` · `BuildId` **`e745c958502244f498950e99ac3d6f00`**. **A NINTH BINARY**, novel against `5cb7e474`, `103d61c4`, `62334f10`, `084e3485`, `df3a867d`, `10bc1b47`, `932a1366` |
> | **B2** — arm A1's binary only | `D:\hf_w18\exe_b2`. B1 + Part 2 (one literal). `TestApp.dll` sha256 `39195befe5a4e128be9a360fcdbb24e008a7efabf9a4f9839848a40fd56e62d4` · `NINA.Joko.Plugins.HocusFocus.dll` sha256 `586a2e806e62dee17e1b80ba4ceceffb5183fbadba8f46b4c98821d53dab423f` · `BuildId` **`7a3a03ba3b39479eaf5a515fb9a76548`**. A **tenth** binary. Neither was rebuilt mid-wave ([F53](followups.md)(c)) |
> | the launcher is **not** the binary | `TestApp.exe` is byte-identical across B1 and B2 — sha256 `dd7103c28cc610e72671534cf23fb9a58b5a303a19d8779545cb7418d4ce6ff7` on both. It is the apphost; the code is in the two dlls. **An `exe` hash would have reported these two binaries as one.** Recorded because this wave's whole structure rests on them being different, and `N18-V3`'s `BuildId` clause — not a file hash — is what established it |
> | detector | `DetectorVersion` **2**, read as a **FIELD** on all 8 gate landings and all 40 arm landings ([F66](followups.md)) |
> | settings **S0** | `D:\hf_w11\pinned_settings_w11.json` md5 **`a67ffc06164c81613aef5c4f8324b9b8`**, re-verified at write-up time. **Not re-pinned** ([F63](followups.md)(b), closed DO-NOT-RE-PIN in wave 17) |
> | profile | `astrodet` `ce3f3e63-8fd3-4b72-a0ca-d90db9441382`, pinned on **every** arm; one distinct value across all 48 landings |
> | banks | `D:\SyntheticAutofocusBank` (20 datasets, `renderRequest.OptimalFocuserPosition` = truth), `D:\Autofocus Bank` (19 runs) |
> | F15 control — landings | **42 of 42 BYTE-IDENTICAL**, after each arm and again at write-up. 0 changed, 0 added, 0 removed, 0 could-not-look. Fifth consecutive clean wave |
> | F15 control — aux | **59 of 59 byte-identical** (`harness_settings.json` ×39 + `synthetic_meta.json` ×20), after each arm and again at write-up. 0 changed / added / removed / could-not-look |
> | suite | **3831 passed, 0 failed, 0 skipped, total 3831** (3824 + 7), verified by COUNT ([F37](followups.md)) — measured twice, with Part 1 alone (3 m 38 s) and with Part 1 + Part 2 (3 m 42 s) |
>
> **No fan-out.** Every arm is `--profile-id`-pinned and a pinned arm cannot fan out at all (wave 11's K3); the
> authorisation is worth 1.33×, not 4× ([F60](followups.md)). Wave 5's φ table is quoted nowhere.
>
> **Every number in this document was re-derived from the artifacts at write-up time**, by re-running each
> scorer and each fingerprint check against the arms on disk. No arm directory was written to, no `TestApp.exe`
> was launched during write-up, and no arm was re-run ([F53](followups.md)(c)).

---

## §0 — Status

| item | rule | pre-registered expectation | outcome | register consequence |
|---|---|---|---|---|
| the gate | **RULE G18** | 8 of 8 `BestJ` to 6 dp, on a ninth binary, with `G18-P1` reading `NoiseReductionRadius=3` on 8 of 8 | **PASS — 8 of 8, bit-identical at all sixteen digits.** `G18-P1` **8 of 8**. 42 m 08 s | the ninth binary reproduces the coordinate system, **and Part 1's inertness is now MEASURED** |
| **A** Part 1 | — | ships unconditionally, inert on every harness path | **SHIPPED** (`93e366a`); inertness measured, not argued | [F70](followups.md)(b) **closes**; F70(a) is **decided in source and unshipped** |
| **A** Part 2 | **RULE N18** | `N-ALIGN` ⇒ ship; `N-HOLD` ⇒ revert; `N-UNEVALUATED` ⇒ nothing ships | **`N-UNEVALUATED`**, naming **`N18-V7`**. Part 2 is **reverted** | **the seed literal's question is still open**, and the wave says so rather than answering it |
| **B** | — | nothing | nothing, all three candidates rejected in writing (design §3) | — |
| **C** | — | BLOCKED, blocker named | **BLOCKED.** Zero minutes spent, no NINA launched, no display touched | §6 |
| new | — | — | — | **[F71](followups.md)** — the conversion control read the copy of the field the loader ignores, and its two verdicts were exactly inverted |

**Wave `TestApp` wall: 2 h 37 m 07 s** against a **6 h** ceiling, estimated at ~2 h 32 m. §7.

> ### The one-sentence version
>
> **The gate passed on a ninth binary, both arms ran to completion, and the wave still issues no verdict on the
> thing it was built to decide — because the control that was supposed to license the out-of-sample measurement
> compared the detector against the wrong one of the two `NoiseReductionRadius` fields the converted file
> carries, and the reason those two fields differ is [F70](followups.md)'s `+1`, the very defect this wave
> exists to fix.** The rule is applied as written. It is not re-decided after the data.

---

## §1 — RULE G18: the gate

Eight runs, `optimize --per-run --max-evals 250`, sequential, both pins named in the driver header, one
`TestApp.exe`, no NINA. Driver `/mnt/d/hf_w18/gate_w18.sh`; window **23:39:58Z – 00:22:06Z**, **42 m 08 s**.
It ran on **B1 and only on B1** — the design's §2.6 is why, and `G18-P1` is the clause that proves it did.

### §1.1 Every clause, its pre-registered threshold, and what it measured

| clause | pre-registered threshold | measured | verdict |
|---|---|---|---|
| **G18-1** | all eight `BestJ` reproduce the K8 table to **6 dp**; a partial reproduction is a FAILURE and stops the wave | **8 of 8 at 6 dp, and 8 of 8 bit-identical at all sixteen digits** | **PASS** |
| **G18-2** | `aggregate_summary.json produced: 8`, asserted **in the driver before any scorer runs** | `aggregate_summary.json produced: 8   expected: 8`, printed by `gate_w18.sh` | **PASS** |
| **G18-3a** | exactly **one** `BuildId`, **novel** against the seven recorded ids | one value, `e745c958502244f498950e99ac3d6f00`; `differs from waves 11/12/13/14/15/16/17: YES` | **PASS** |
| **G18-3b** | `DetectorVersion` **2**, read as the **FIELD** | `DetectorVersion(s): ['2']` | **PASS** |
| **G18-3c** | `ProfileId` contains `ce3f3e63-…` **and** cardinality exactly 1 | both evaluated separately; containment 8 of 8, cardinality 1 | **PASS** |
| **G18-3d** | one distinct `FitInputs`, exactly `MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid` | `distinct FitInputs: 1`, that exact string | **PASS** |
| **G18-3e** | `ConcurrencyCheck == exclusive` on **all eight**, read **across the arm**, over a list carrying an explicit `None` per unreadable landing | eight `'exclusive'`, list length 8, no `None` | **PASS** |
| **G18-3f** | `BaselineJ` reproduces wave 11 ([F41](followups.md)) to 6 dp | 8 of 8 at 6 dp | **PASS** |
| **G18-4** | `prov_w18.py --self-test` PASSES on the real arm and FAILS on a mutated copy, **with the mutation asserted by read-back**, before any G18 number is quoted | direction 1 PASS; direction 2 FAIL on **both** the containment and the cardinality clause, with the rewritten `ProfileId` echoed back | **SELF-TEST PASS** |
| **G18-P1** | the gate's own `optimize/seed` block reads `NoiseReductionRadius=3` on **8 of 8** logs, asserted **in the driver**. *"If this reads 4, Part 2 is in the gate's binary and the gate is not a gate"* | **8 of 8 read `seed=3`** (and `baseline=4`, [F70](followups.md)/D17's offset). Asserted by the driver, re-asserted by `score_n18_w18.py --arm-g18-passed` before it would write `G18_PASSED`, and **re-verified independently at write-up time** | **PASS** |

The eight values, exactly as pre-registered and exactly as measured:

```
toml999    0.9957838768299878   CWhiteFocus 0.9960675916058808   uneven 0.9963677194179505
muggsie    0.9971948738498605   mccomiskey  0.9767460801208465
D18        0.9998815090506263   D19         0.9994870586135448   D20    0.9997378027339423
```

### §1.2 The F68 four-part statement, per clause

The pre-registration names **(P)** population, **(S)** statistic, **(A)** aggregation and **(E)** the empty
answer for every clause. All eleven held as written. The three worth restating:

- **G18-1 (E)** — an unreadable landing is COULD-NOT-LOOK, **named**, and G18-1 **FAILS**. For a *stopping*
  gate, "could not look" is not "undecided"; it is "has not passed". Not exercised: 8 of 8 were readable.
- **G18-3c (E)** — `all()` over an empty list is vacuously true, so the **cardinality** clause carries an empty
  read. Both were evaluated and the self-test's direction 2 failed on **both**, which is the demonstration that
  the cardinality clause is not decoration.
- **G18-P1 (P/S/A)** — population: the `PARAMS-DUMP optimize/seed` block in the 8 gate logs. Statistic: the
  field value. Aggregation: `== 8`. **The 0 end of its range is exactly what a mis-built B1 produces**, which
  is the mistake this wave's two-binary structure makes possible, which is why the clause exists.

### §1.3 What this PASS proves — and it is two things, not one

**(a) A ninth binary reproduces the coordinate system.** Bit-identical on 8 of 8, which is the precondition for
reading anything else in the wave.

**(b) Item A Part 1 is inert on every harness path, and that is now a MEASUREMENT.** Part 1 is *in* the gate's
binary rather than beside it precisely so this could be measured rather than argued: `ResetDefaults()` has
exactly one call site in the whole product (`HocusFocusPlugin.cs:224`, the options-UI "restore defaults"
`RelayCommand`), no `TestApp` command reaches it, the rewritten drift guard is a test, and F69(a) is a comment.
The eight values came back **bit-identical at sixteen digits with Part 1 in the binary**. The design pre-committed
*"if G18-1 fails, Part 1 is the first suspect and the wave stops"*; it did not fail.

**And `G18-P1` proves the two-binary structure did what it was for.** The gate's seed dump reads **3** on 8 of 8
while arm A1's reads **4** on 20 of 20 (§4.2). Part 2 did not leak into the gate's binary; the gate is a gate.
The design's §9.1 said the controller's standing *"code → ONE binary → gate → arms"* order would have destroyed
this gate. That is no longer an argument — `G18-P1` and `N18-V2` measure it from both sides.

### §1.4 What it does not prove

Nothing about Part 2, which is deliberately absent from B1. It runs at `MaxOutlierRejections = 0`, so it says
nothing about the rejection path. And a gate is a control on the *binary*, not on the change.

---

## §2 — The controls

### §2.1 The landings and the aux files

```
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --check /mnt/d/hf_w18/bank_landing_fingerprint_BEFORE.json
python3 /mnt/d/hf_w17/aux_fingerprint_w17.py  --check /mnt/d/hf_w18/bank_aux_fingerprint_BEFORE.json
```

> **42 of 42 landings BYTE-IDENTICAL** and **59 of 59 aux files byte-identical**
> (`{'harness_settings.json': 39, 'synthetic_meta.json': 20}` matching the expected counts), 0 changed,
> 0 added, 0 removed, 0 could-not-look — written BEFORE the gate, re-checked after **each** arm, and re-checked
> again at write-up. Fifth consecutive clean wave for the landings; second for the aux class.

### §2.2 The scorers, both directions, before either was quoted

| scorer | demonstrations | result |
|---|---|---|
| `prov_w18.py --self-test` | 2 directions on a `copytree` of the real gate, mutation asserted by read-back | **SELF-TEST PASS both directions** |
| `score_n18_w18.py --self-test` | **15** — every branch of the N18 table (`N-UNEVALUATED` ×3 routes, `N-HOLD` ×2 routes, `N-ALIGN` on merit and `N-ALIGN` on a null) plus the primitives, including *"a rate over an EMPTY set is UNEVALUATED, not 1.000 and not PASS"* and *"an EMPTY block is COULD-NOT-LOOK, not 'zero fields differ'"* | **15 of 15 ok** |
| `convert_landing_w15.py --self-test` | 8 — round-trip, the flag it sets, and five refusals | **8 of 8 PASS** |
| **the conversion probe** (`score_n18_w18.py --probe`) | 2 directions, mutation asserted by read-back | **FAIL / COULD NOT RUN — §5** |

**The last row is the wave.** Three scorers demonstrated both of their directions and were quoted. The fourth
demonstrated both of its directions, was **refused**, and refused correctly — for the wrong reason. §5.

### §2.3 One disclosed scoring deviation

`score_n18_w18.py` requires `--affit0` and `--affit1` to be existing directories and aborts otherwise. The arms'
`af-fit` pass never ran (§5), so those directories do not exist. To reach the clauses that do **not** depend on
the conversion, the scorer was run at write-up time with two **empty** placeholder directories under `/tmp`:

```
python3 /mnt/d/hf_w18/score_n18_w18.py --a0 /mnt/d/hf_w18/seedA0 --a1 /mnt/d/hf_w18/seedA1 \
        --affit0 /tmp/w18_affit_absent/a0 --affit1 /tmp/w18_affit_absent/a1 \
        --gate /mnt/d/hf_w18/gate --w15 /mnt/d/hf_w15 --out /mnt/d/hf_w18/n18_score_NOAFFIT.txt
```

The placeholders are **outside** `/mnt/d/hf_w18` on purpose: creating `affitA0`/`affitA1` there would have
disarmed `affit_w18.sh`'s F53(c) "an output directory is never silently reused" guard for any later wave. The
output file is named `n18_score_NOAFFIT.txt` so it cannot be mistaken for a full scoring. **`N18-V7` and
`N18-P` read exactly the same COULD-NOT-LOOK as they would have with no placeholder at all** — 20 of 20 named
per arm, denominator 0 of 20 — because the population is empty either way. Recorded because improvised
instrumentation is where this series' discipline has failed before (wave 17 §7.2).

---

## §3 — Item A Part 1: what shipped, and what the gate measured about it

Committed as `93e366a`, in **both** binaries, before either arm.

| piece | what it does | status |
|---|---|---|
| **1a** | `ResetDefaultsImpl` ends in an **unconditional** `ConfigureSimpleSettings()`, so *"after `ResetDefaults()`, the object's property state equals that of a freshly constructed `StarDetectionOptions` over a blank accessor — for every property, from every entry state"* | shipped |
| **1b** | the drift guard rewritten as three tests: **T1** `BuildDefaultStarDetectorParams()` vs `BuildStarDetectorParams(freshly constructed options)` over every option-derived field; **T2** `ResetDefaults()` from four named entry states; **T3** the accessor-fallback / preset-base lockstep guard | shipped, **7 new tests** |
| **1c** | [F69](followups.md)(a) — the four-line comment at `OptimizationDiagnosticRunner.cs:405-409`, every clause of which was backwards | shipped |

**The re-entry was the bug and the value was only its symptom.** `UseOptimizedSettings` is in
`SimplePropertyNames`, so its setter's `RaisePropertyChanged()` re-entered the derivation — but every setter in
the class is change-guarded, so it re-entered **only when the flag was already on**. `ResetDefaults()` therefore
returned `NoiseReductionRadius = 4` for a user who had optimized settings enabled and `3` for one who did not:
**5 of the 9 profiles on this machine and 4 of the 9 respectively** (`N18-R`, §4.3). The drift guard's own
fixture took the minority branch, on a path no profile load takes. *It was green twice over.*

**Five of the seven new tests fail against the pre-change product source**, verified by reverting the one product
line: 7 failures, exactly the expected set. The rest were verified against named mutants, including **M1** — the
accessor fall-back literal changed alone — which is killed by **one** test, because nothing else in the suite
reads that literal. *(That mutation testing was performed and recorded when Part 1 landed, `93e366a`; it is
quoted here from the commit and was **not** re-derived at write-up, because re-deriving it means editing product
source.)*

**And one of the new tests was a dud until a mutant said so.** The idempotence test was first written as
repeated `ResetDefaults()` calls; mutant **M4** exposed that reset re-asserts the bare literal first, hiding the
compounding the test existed to detect. It was rewritten to drive the derivation directly, re-verified, and the
test's own comment records it. *A test that a mutant cannot kill has not been demonstrated.*

**Residual hazard, flagged rather than silently left**: the 20 preset-owned literals in `ResetDefaultsImpl` are
now unobservable through any public path, so editing one is a silent no-op. Kept for a minimal diff, documented
in code, and named as a clean follow-up.

**Part 1's inertness is measured, not argued** — §1.3(b). That was the whole reason it sat in the gate's binary.

---

## §4 — Item A Part 2 / RULE N18: **`N-UNEVALUATED`**

### §4.1 The verdict, and the route to it

```
V1=True V2=True V3=True V4=True V6=True V7=False
>>> RULE N18: **N-UNEVALUATED**  (a validity gate did not pass: V7)
```

The branch table, fixed before the data, reads `V1 and V2 and V3 and V6 and V7 all pass? no -> N-UNEVALUATED
(name the gate; Part 2 does NOT ship)`. **`N18-V7` did not pass. Part 2 does not ship.**

**Two independent routes reach the same verdict**, and the branch table's ordering means the first one fires:

1. **`N18-V7` could not look**, on 20 of 20 datasets in **both** arms, because the `af-fit` pass never ran.
2. **`N18-P` is UNEVALUATED on an empty set** — `denominator: 0 of 20` — which is the branch table's *second*
   line, `N18-P could not be evaluated? yes -> N-UNEVALUATED (the only out-of-sample clause did not run)`.

Both trace to one cause: the conversion control failed, `affit_w18.sh` refused to write `PROBE_PASSED`, and
`affit_w18.sh 0` and `affit_w18.sh 1` therefore aborted before launching anything. §5 is what happened and why
it is the wave's sharpest finding.

> **`N18-V7` "did not pass" in the COULD-NOT-LOOK sense, not the FAIL sense.** No conversion was measured and
> found not to have bound. The three states are unchanged / changed / **could-not-look**, and this is the third.
> The distinction is the difference between *"the conversion did not take"* — which would be a finding about the
> harness — and *"the conversion was never measured"*, which is what happened.

**An unsatisfiable rule is a finding and is not re-decided after the data.** No repaired form of `N18-V7` was
evaluated anywhere in this wave, and no clause was re-scored against a different field to obtain a verdict. That
prohibition is the design's §8 step 11 and [F68](followups.md)(c), and honouring it costs this wave its decisive
answer — which is the price of the register meaning anything.

### §4.2 Every clause, its pre-registered threshold, and what it measured

| clause | pre-registered threshold | measured | verdict |
|---|---|---|---|
| **N18-V1** *population* | `optimized_settings.json produced: 20 / 20` per arm, asserted **in the driver** | A0 **20 of 20**, A1 **20 of 20**; COULD-NOT-LOOK: none. Asserted by `seed_w18.sh` at the end of each arm before any scorer ran | **PASS** |
| **N18-V2** *the treatment took, and only the treatment* | the `optimize/seed` blocks differ in **exactly one field** (`NoiseReductionRadius`, `3`→`4`) and the `optimize/baseline` blocks in **zero**, on **20 of 20**, over the 55 parsed fields | seed blocks differing in exactly `NoiseReductionRadius` 3→4: **20 of 20 = 1.0000**. Baseline blocks differing in zero fields: **20 of 20 = 1.0000** | **PASS** |
| **N18-V3** *provenance* | one `BuildId` per arm, the two **differ**, both novel; `DetectorVersion` 2; profile pinned by containment **and** cardinality; one `FitInputs`; `ConcurrencyCheck` exclusive across each arm. **Two arms sharing a `BuildId` is an explicit FAIL** | A0 `e745c958502244f498950e99ac3d6f00`, A1 `7a3a03ba3b39479eaf5a515fb9a76548` — **different**, both novel; `Det=['2']` on both; one `ProfileId` containing `ce3f3e63-…` on both; `FitInputs` cardinality 1 on both; `exclusive` across both | **PASS** |
| **N18-V4** *`J` is the same function in both arms* | `BaselineJ` **bit-identical** across arms on the common set. *"It is the clause that licenses the cross-arm `FinalJ` comparison instead of assuming it"* ([F62](followups.md)) | **20 of 20 = 1.0000**, exact 17-digit equality, 0 could-not-look | **PASS** |
| **N18-V6** *the gate reproduces inside the arm* | arm A0's `D18/D19/D20` landings equal RULE G18's on `FinalJ` **and** `BaselineJ`, 3 of 3 | **3 of 3 = 1.0000**, both fields | **PASS** |
| **N18-V7** *the conversion took* | the `af-fit/detector` dump carries the converted file's `NoiseReductionRadius`, **20 of 20 per arm** | **UNEVALUATED (empty set)** on both arms; all 20 datasets named COULD-NOT-LOOK per arm. The `af-fit` pass never ran — §5 | **DID NOT PASS (could not look)** |

### §4.3 `N18-A`, `N18-M`, `N18-J`, `N18-R` — **diagnostics under an UNEVALUATED verdict**

> **None of the four below is a result.** RULE N18 issued no verdict, so none of these licenses a statement
> about whether the seed literal should ship. They are reported because the arms ran to completion and the
> numbers are on disk, and because a later wave that pre-registers a repaired rule will want to know what it is
> walking into. **They are labelled here exactly as the design labels them: `N18-A` and `N18-M` are magnitudes
> and can never carry a verdict; `N18-J` is decisive *only* under a passing set of validity gates, which this
> wave does not have; `N18-R` is reach.**

**`N18-A` — the seed anchors the landing.** Status quo pre-registered as OBSERVED at 18 of 20 = 0.900 on wave
15's `land_mor0`; A0 re-measured it on its own artifacts and **reproduced it exactly**.

| | A0 (seed 3) | A1 (seed 4) |
|---|---|---|
| landing `NoiseReductionRadius` == its arm's seed | **18 of 20 = 0.9000** | **15 of 20 = 0.7500** |
| the exceptions | `D01_ultrawide_40mm` 6, `D04_esprit_550mm` 6 | `D18` 7, `D02` 5, `D13` **3**, `D14` 5, `D15` 5 |

Two things fall out that no prior wave could see. **The anchoring is weaker at the treatment seed** (0.750 vs
0.900) — the search leaves 4 more often than it leaves 3. And **A0's two exceptions are not A1's**: `D01` and
`D04` both moved *to* 4 under A1, while `D18`, `D02`, `D14` and `D15` moved *away* from it. The seed is not a
constant offset on the landing.

**`N18-M` — the recommendation moves.** Reported, never a bar. **20 of 20 landings moved** under wave 13 D5's
own definition (any curated knob or the recommended step differs), 4 to 12 fields each. `RecommendedStepSize`
moved on **17 of 20**, by **−8 to +4** steps (`D12` 120→112, `D08` 89→83, `D11` 54→48; `D14` 57→61, `D18` 18→21,
`D06` 36→39); the three that did not move are `D19`, `D20` and `D03`. For scale and not as a bar:
`MaxOutlierRejections` moved 19 of 20 landings (wave 15 `L15-S`).

**`N18-J` — `FinalJ` across the arms.** `N18-V4` passed at 20 of 20 bit-identical, so `J` **is** demonstrably
one function evaluated at two landings, and this comparison is the one thing in the wave that the validity
clause did license. It is still not a result, because the rule that consumes it did not run.

| | value |
|---|---|
| tie band | `1e-9`, fixed in the pre-registration |
| A1 better | **13** |
| A0 better | **7** |
| tie | **0** |
| denominator | **20**, could-not-look none |
| `FinalJ(A1) − FinalJ(A0)` | min **−0.000580880** (`D10`) · median **+0.000085571** · max **+0.002992517** (`D01`) |

The pre-registered `N-HOLD` condition on this clause was *"A0-better count > A1-better count"*. It is not met —
7 is not greater than 13. **That is not a pass and it is not a licence.** The branch table reaches `N18-J` only
*after* the validity gates and *after* `N18-P`'s veto, and neither was available. The largest single movement,
`D01` at **+0.002993**, is on one of the **two** datasets whose `BaselineJ` is exactly **0.0** — `D01` and `D02`
— where `J`'s scale is not comparable to the rest ([F20](followups.md)). It is 2.4× the next-largest movement
(`D17`, +0.001236). *That is exactly the sort of thing the out-of-sample clause exists to arbitrate, and it is
the clause that did not run.*

**`N18-R` — reach on real configurations.** Zero `TestApp`, computed from the 9 `*.profile` files by replaying
the plugin's own load rule.

| | rate | note |
|---|---|---|
| profiles whose **effective** radius Part 2 changes | **0 of 9 = 0.0000** | **by construction, and printed rather than implied**: Part 2 edits `BuildDefaultStarDetectorParams` only, and no profile's loaded detector reads it |
| profiles where `ResetDefaults()` lands on **4** rather than 3 | **5 of 9 = 0.5556** | the non-determinism Part 1 removed |
| profiles carrying an optimizer snapshot | **5 of 9** | stored radii 3, 3, 3, 4, 7 — they run whatever the optimizer last recommended |

### §4.4 What `N18-V4` bought, and why it matters that it passed

`N18-V4` is the clause the design singled out: *"it is the clause that licenses the cross-arm `FinalJ`
comparison instead of assuming it"*. It measured **20 of 20 bit-identical `BaselineJ`** at seventeen digits.

That is worth stating on its own, because it is the failure this wave was most exposed to and it did not
happen: `BaselineJ` is computed from the *baseline* bundle, which Part 2 does not touch, so 1.000 was expected —
but the 0 end was reachable, and if `J` had been normalised against the seed's evaluation the whole column would
have moved and the decisive comparison would have been meaningless. **The design checked it rather than
asserting it, and the check ran.** The comparison it licensed is the one the wave then could not consume,
because a *different* validity clause could not look.

---

## §5 — The conversion control: two verdicts, both exactly inverted

This is the wave's sharpest finding, and it is a finding about the wave's own instrument.

### §5.1 What the control is, and what it returned

`affit_w18.sh probe` builds two settings files from the gate's `D18_m24_deep_shed` landing through the
production Accept path: `good.json` (converted, `UseOptimizedSettings=True`) and `mutant.json` (byte-identical
but `False`, the mutation asserted by read-back). The pre-registered expectation is **good ⇒ TOOK, mutant ⇒
DID-NOT-TAKE**. Both `af-fit` runs exited 0. The scorer said:

```
good    settings NoiseReductionRadius=4   af-fit/detector NoiseReductionRadius=3   -> DID-NOT-TAKE
mutant  settings NoiseReductionRadius=4   af-fit/detector NoiseReductionRadius=4   -> TOOK

>>> conversion control: **FAIL / COULD NOT RUN** -- do not quote it.
```

`PROBE_PASSED` was not written, and `affit_w18.sh 0` and `affit_w18.sh 1` both aborted with
*"the conversion control has not been demonstrated in BOTH directions, so nothing this arm produces may be
quoted ([F66](followups.md)(a))"*.

### §5.2 The diagnosis, verified independently at write-up time

**A converted settings file carries `NoiseReductionRadius` twice**, and the two copies do not agree:

| | `Options.NoiseReductionRadius` | `Options.OptimizedSettingsJson` → `NoiseReductionRadius` |
|---|---|---|
| `good.json` | **4** | **3** |
| `mutant.json` | **4** | **3** |

`Options` is the base file's block, round-tripped verbatim; the landing goes into the snapshot. The base
(`pinned_settings_w11.json`, md5 `a67ffc06…`) carries `NoiseReductionRadius = 4`, `UseAdvanced = False`,
`UseOptimizedSettings = False`, no snapshot.

So the two runs behaved **correctly and oppositely**:

- **`good`** — `UseOptimizedSettings=True` ⇒ `ConfigureSimpleSettings` takes the
  `UseOptimizedSettings && HasOptimizedSettings` branch, the snapshot is applied, the detector runs at the
  landing's **3**. **The conversion took.**
- **`mutant`** — `UseOptimizedSettings=False` ⇒ the snapshot is ignored, `DerivePresetSettings` runs, `Typical`
  ⇒ 3, `+1` for hotpixel thresholding + filtering ⇒ the detector runs at **4**. **The conversion did not take.**

**The probe compared the detector against `Options.NoiseReductionRadius` — the base file's field — instead of
the snapshot's.** `good`: 3 ≠ 4 ⇒ "DID-NOT-TAKE". `mutant`: 4 == 4 ⇒ "TOOK". Both inverted.

**And the reason the two fields differ at all is [F70](followups.md)'s `+1`.** The base's stored 4 is the
derivation's output persisted to disk; the landing's 3 is `BuildDefaultStarDetectorParams`'s pre-compensation
literal, followed by the search on 18 of 20 datasets. **The `mutant` arm's coincidence is the same defect a
second time**: in Simple mode the stored `Options` value is *overwritten* by the derivation, and the derivation
happened to land back on the same 4 that was stored. The control's comparison is not merely reading the wrong
copy — in Simple mode the copy it reads is **dead**, and it agreed with the live value by arithmetic accident.

> ***The wave's own control was built without accounting for the defect under test.*** The design spends §2.1
> through §2.4 establishing that `NoiseReductionRadius` has two shipped defaults that differ by exactly one, and
> then names *"the converted file's `NoiseReductionRadius`"* as the control's signature knob — of the 35 fields
> in the snapshot, the one field guaranteed by F70 to have two different values in the same file.

### §5.3 The independent verification — 8 fields, at zero compute, from the artifacts already on disk

The diagnosis does not rest on reading the scorer. The **same two `run.log` files the scorer read** carry
**56 detector fields each**, of which **8 differ between `good` and `mutant`**:

| field | `good` | `mutant` | base `Options` | snapshot |
|---|---|---|---|---|
| `HotpixelThreshold` | 0.005 | 0.001 | — | **0.005** |
| `MaxDistortion` | 0.10000000000000003 | 0.5 | — | **0.10000000000000003** |
| `MinHFR` | 0.7 | 1.2 | 1.2 | **0.7** |
| `NoiseReductionRadius` | 3 | 4 | 4 | **3** |
| `Sensitivity` | 14.666666666666664 | 10 | — | **14.666666666666664** (as `BrightnessSensitivity`) |
| `StarCenterTolerance` | 0.44999999999999996 | 0.3 | — | **0.44999999999999996** |
| `StarClippingMultiplier` | 0.25 | 2 | 2 | **0.25** |
| `StructureLayers` | 5 | 4 | 4 | **5** |

**All eight of `good`'s values are the snapshot's. All eight of `mutant`'s are the base's.** The conversion took
in `good` and did not take in `mutant`, unanimously, on eight fields, and the control read the one field where
the base's dead copy coincides with the derivation's live output. *The evidence that the control was wrong was
in the same file the control read, and it took no additional compute to find.*

### §5.4 What the defect would have done if the interlock had not fired

`N18-V7`'s scorer implements the *same* comparison as the probe — `want.get("NoiseReductionRadius")` from
`Options` against the `af-fit/detector` dump. Since every converted file's `Options` copy is the base's 4, and
the detector carries the landing's radius, `V7` reduces to *"did this dataset's landing radius happen to equal
4?"*. From the landings on disk:

| | `V7` as coded would have read |
|---|---|
| **A0** (control arm, landings at 3 on 18 of 20) | **0 of 20 = 0.0000** |
| **A1** (treatment arm, landings at 4 on 15 of 20) | **15 of 20 = 0.7500** |

**Neither reaches the 1.000 bar, so `N18-V7` would have failed anyway and the verdict would still have been
`N-UNEVALUATED`.** The wave's verdict is robust to the defect; only the *route* changed, from a failed validity
clause to a refused control.

**But look at the shape of what would have been printed.** A validity gate reading **0.000 on the control arm
and 0.750 on the treatment arm** is a gate whose pass rate is a *function of the treatment*. Read innocently it
says *"the conversion bound on the treatment arm and not on the control arm"* — a conclusion about the arms
drawn entirely from a defect in the instrument, pointing in the direction the wave wanted to go. The
`PROBE_PASSED` interlock cost 8 minutes of `af-fit` and prevented that number from ever being produced.

### §5.5 Where this sits relative to F68 and F66 — the question the design did not ask

**Against [F68](followups.md).** The design's satisfiability section is the most thorough in the series: every
clause carries **P / S / A / E**, every denominator was counted on the artifact the clause reads at
pre-registration time, the one median in the design has its foreclosure computed, and the absolute-count table
lists all six counts with their ranges. `N18-V7`'s row is filled in correctly on all four columns —
**population** (the 2 × 20 `af-fit` `run.log` dumps against the 2 × 20 converted files), **statistic** (field
equality), **aggregation** (rate per arm), **empty answer** (COULD-NOT-LOOK, named, out of both denominators).

**Every one of those four is right, and the clause is still broken, because the four questions do not include
the one that mattered: *which field of the artifact?*** F68's generic form asks for the population, the
statistic and the aggregation — and a "statistic" of *"field equality"* silently assumes the two sides name the
same quantity. Here they named two different fields with the same name in one file. **A denominator error is a
population that is not what you think it is; this is a *quantity* that is not what you think it is**, and it is
invisible to every check F68 currently prescribes. **The design's own §5.2 both-branches table checked that
`N18-V7` could return 1.000 and could return 0** — and it can, and neither value means what the clause says.

**Against [F66](followups.md)(a).** The rule is *"every gate must be demonstrated to PASS on a known-good input
and to FAIL on a known-bad one before it is quoted"*. That was honoured completely: two files, differing in one
asserted-by-read-back byte, two `af-fit` runs, a demonstrated TOOK and a demonstrated DID-NOT-TAKE, and the
scorer refused to write `PROBE_PASSED` when they came out on the wrong sides.

> **A two-directional demonstration proves the two branches are DISTINGUISHABLE. It does not prove they are
> CORRECTLY ASSIGNED — because both directions are evaluated by the same definition, and here the definition was
> wrong.** The probe separated the good input from the bad input perfectly. It just labelled them backwards.
> F66(a) caught it only because the labels were *swapped* rather than *both wrong in the same direction*; a
> definition error that moved both verdicts the same way would have produced a clean PASS.
>
> The addition F66 needs is cheap and this wave demonstrates it for free: **when a control reduces a property to
> one field, corroborate with a second field that must move the same way.** Seven other fields in the same dump
> would have said so at zero cost (§5.3).

---

## §6 — Item C: BLOCKED, and the blocker has a name

**Zero minutes spent. No NINA launched. No display touched.** `gate_w18.sh` and `seed_w18.sh` both abort if any
NINA process is running, and both ran clean.

### §6.1 The state, carried forward

Eight waves recorded `Disc` and read it as *"the machine has no attached display"*. Wave 17 read **`Active`** at
write-up time and the check then ran further than it ever has: composited desktop confirmed, deploy verified
byte-for-byte before launch, NINA launched with a non-zero hwnd and a real window title, and it **wrote a log** —
where wave 13's attempt hung with 5 s of CPU and no log at all. Then NINA crashed during startup:

```
CompositionRoot.cs|Compose|145  System.NullReferenceException
  NINA.Core.Utility.AsyncObservableCollection`1.RunOnSynchronizationContext (:44)
  NINA.Core.Utility.AsyncObservableCollection`1.InsertItem                   (:49)
  NINA.Utility.PluggableBehaviorSelector`2..ctor                             (:39)
```

Two version facts sit beside it. The crashing process is NINA **3.3.0.1048** — the only installed application —
while every other NINA-libraries log that day is **3.2.0.2001**, which is this harness's own `TestApp` runs. The
plugin's `Joko.NINA.Plugins.HocusFocus.csproj:52` reads
`<PackageReference Include="NINA.Plugin" Version="3.2.0.2001-beta" />`, verified in the tree at write-up time,
and it is deployed into that 3.3 install's `Plugins\3.0.0\`.

> **Provenance of the paragraph above.** The stack trace and the two version readings are carried from the
> wave-18 pre-registration, which recorded them from the wave-17 write-up session. **This wave re-read only the
> `csproj` line.** The NINA logs were not re-opened and no NINA process was started, because the pre-registered
> disposition for item C is *zero minutes*. That is the honest state: one line re-verified, the rest carried.

### §6.2 The correction wave 18's pre-registration made, and it stands

The brief recorded *"every frame is NINA core or Microsoft DI; zero occurrences of 'Hocus' in the log"* as
though it exculpated the plugin. **It does not.** `PluggableBehaviorSelector<,>`'s constructor inserting into an
`AsyncObservableCollection` is **precisely** where MEF-imported plugin behaviours are consumed, and HocusFocus
exports two of them — `IStarDetection` and `IStarAnnotator` (`AutoFocusEngineFactory.cs:31-32`,
`InspectorVM.cs:101-102`). A behaviour imported from an assembly built against a different NINA contract can NRE
inside the selector's own frames without the plugin's name appearing anywhere in the trace.

> **The absence of "Hocus" is fully consistent with the plugin being the cause. The blocked isolation test is
> therefore DECISIVE, not optional.**

### §6.3 The blocker, named

> **The decisive test — one launch of NINA 3.3.0.1048 with `…\Plugins\3.0.0\` (or just the HocusFocus
> assemblies) moved aside — was refused by the permission classifier. It is not worked around, and this wave
> designed no arm that needs it.**

That is the blocker: **a permission the agent does not hold and must not route around.** It is not a machine
state, not a display, and not a NINA bug that has been diagnosed. Nine waves of "no display" were, at least in
part, masking it.

| | |
|---|---|
| **what would settle it** | **Crashes anyway** ⇒ the fault is NINA's install and A1–A9 are blocked on that. **Starts clean** ⇒ the plugin is implicated, and the leading hypothesis is a 3.2-built plugin loaded into a 3.3 host |
| **what it costs** | ~3 m of wall time. The cost is **not** compute — it is a permission grant, which is the user's to give |
| **if the answer is "the plugin has not been rebuilt against 3.3"** | **Every UI item in this series' backlog is blocked behind a NINA 3.3 retarget** — a `NINA.Plugin` package bump, whatever API drift it brings, and a full regression pass. That is a project-level decision with its own cost, it is not a wave item, and A1–A9 stay NOT ATTEMPTABLE until it is taken |

---

## §7 — Budget: estimated against actual

| step | estimate | actual | derivation of the estimate, and how it held |
|---|---|---|---|
| build B1 and B2 | ~6 m | **42.6 s** (20.62 s + 21.96 s) | two Release builds, both incremental. Over-priced by ~8× |
| the suite, twice | ~8 m | **7 m 20 s** (3 m 38 s + 3 m 42 s) | wave 17 measured 3 m 38 s. **Exact** |
| **RULE G18** — 8 runs | **~42 m** | **42 m 08 s** | waves 16 and 17 measured 40 m 55 s, 41 m 36 s and 41 m 29 s on the identical arm. **Inside the range, +0.3 % on the estimate** |
| **arm A0** — 20 synthetic | **~51 m** | **54 m 34 s** (3274 s) | wave 15's 40-run all-synthetic pair ⇒ 51 m per arm; the charter's 2.6 m/run gave 52 m independently. **+7.0 %** |
| **arm A1** — the same 20 | **~51 m** | **59 m 57 s** (3597 s) | as A0. **+17.6 %**, and **+9.9 % on A0** |
| the probe | ~1 m | **~28 s** | wave 15's equivalent was ~30 s. **Exact** |
| **`N18-P`** conversions + `af-fit`, 2 × 20 | **~8 m** | **0 — NOT RUN.** §5 | the estimate was measured (wave 15's `affit_mor0`, 3 m 45 s on these exact 20 datasets) and was never spent |
| scoring | < 10 m | ~5 m of Python | |
| **`G18'`**, conditional on `N-ALIGN` | +42 m | **0 — foreclosed.** `N-UNEVALUATED` never reaches that branch | |
| **wave total `TestApp` wall** | **~2 h 32 m** | **2 h 37 m 07 s** | against a **6 h** ceiling |

**The arm-time difference is reported and is never evidence** — the design pre-committed to that phrasing,
because *"a 9 px Gaussian is not a 7 px one, so a small difference is possible"*. A1 was **1.10×** A0 overall.
**Per dataset it is not monotone and therefore is not a kernel-cost effect**: `D07` 2.16×, `D19` 1.96×,
`D16` 1.70× against `D11` 0.54×, `D12` 0.64×, `D05` 0.64×. The search takes different trajectories from
different starting points; that is what an EARLY cache-key parameter does, and wall time is not a proxy for the
treatment having taken. `N18-V2` is, which is why the design made it a validity clause rather than an
observation (wave 17's `D12` lesson, applied).

### The drop order was never invoked

`D1` (skip `G18'`), `D2` (drop datasets from the tail of the pre-registered order) and `D3` (drop the arms
entirely) were all pre-registered with their denominators. **None fired.** `D1` was foreclosed by the verdict
rather than chosen. Every N18 clause that ran was scored at its full 20 (or 3, for `N18-V6`), and no
cross-wave substitution was made for either arm.

---

## §8 — What this wave did NOT run, and what it costs

- **The `af-fit` out-of-sample pass, both arms.** ~8 m, **not run**, and it is the reason RULE N18 has no
  verdict. It was blocked by the `PROBE_PASSED` interlock working exactly as designed on a control that was
  itself mis-specified (§5). **This is the single largest thing wave 18 leaves undone.**
- **Re-running the probe against the snapshot field, and re-scoring `N18-V7`.** **Not done, deliberately.** It
  is Python over data on disk plus ~8 m of `af-fit`, and it would very probably have produced a verdict — which
  is precisely why it is forbidden. [F68](followups.md)(c) and the design's §8 step 11: *"apply the branch table
  as written; do not re-decide a clause — an unsatisfiable rule is a finding."* Re-deciding a pre-registered
  clause after seeing the data, with a repair that the data suggested, is the one move this series has held the
  line on for four waves.
- **`G18'`, the eight-value re-baseline on B2.** ~42 m, foreclosed by the verdict. **This creates no debt**:
  Part 2 does not ship, so the K8 eight-value coordinate system stays valid and **wave 19 does not owe a fresh
  baseline**. The design's largest identified debt was not incurred.
- **Any use of the 40 arm landings as a recommendation.** They are on disk under `/mnt/d/hf_w18/seedA0` and
  `/mnt/d/hf_w18/seedA1`, complete, provenance-clean, and **must not be re-run** ([F53](followups.md)(c)). A
  later wave that pre-registers a repaired out-of-sample clause can score them for ~8 m of `af-fit` — the arms
  are the expensive part and they are already paid for. **That is a new rule with a new pre-registration, not a
  repair of N18.**
- **The other 11 `BuildDefaultStarDetectorParams` call sites.** Moot for now — Part 2 does not ship, so the
  default bundle for `bank-verify`, `golden eval`, `synth-validate`, `bank-donut-meta`, `inspect-align` and
  `tilt-calibration` is unchanged and no cross-wave boundary is created. The reach statement stands for whenever
  Part 2 is taken up: **none of the 11 is measured**, and measuring even one is ~30 m and buys no decision.
- **[F45](followups.md)(b)'s production plumbing.** ~3–4 h + tests. Rejected in the design's §3 on substance as
  well as budget: RULE S16 returned NO RECOMMENDATION, §3a forbids repairing `S16-A(b)`, so the plumbing would
  deliver a mechanism no rule has recommended; and after wave 14 flipped `MaxOutlierRejections`'s default to 0,
  a SEM floor reaches only profiles that explicitly store ≥ 1 — **3 of the 9 on this machine**.
- **[F21](followups.md)'s price probe** — one `synth-validate --scenarios S0 --datasets D17_cdk14_oiii5` run,
  hard-timeboxed at 20 m, no rule attached, whose only deliverable is a measured rate. It was an **optional tail
  item, first to be dropped**, and it was dropped: the wave was at 2 h 37 m when the arms finished and the
  conversion control had already failed, so the remaining effort went to characterising that. **Wave 19 inherits
  the same unmeasured instrument and the same instruction from wave 17 §7.** ~20 m.
- **[F59](followups.md)'s five knobs.** ~1 h. Rejected in the design's §3, and the wave-18-specific reason
  turned out not to apply: the coordinate system did not move after all. The original grounds still hold — the
  knobs are absent from the pinned file so there is no printed value to diff, and nothing in the register
  depends on the answer.
- **Any repaired form of `S16-A(b)`, any re-scoring of wave 16's rungs or wave 14's V4/V4′, and any re-opening
  of RULE F14 or RULE S16.** Zero minutes, deliberately not done, per [F68](followups.md)(c) and the charter's
  §3 and §3a.
- **The wizard-side confirmation of anything in this wave.** Blocked with item C (§6). ~30 m *if the permission
  is granted*, which is not this agent's to grant.
- **Re-pinning the settings file.** [F63](followups.md)(b) closed as DO-NOT-RE-PIN in wave 17; nothing here
  re-opens it.

---

## §9 — What ships, and what the tree looks like

**Part 1 ships** (`93e366a`, already committed): the unconditional `ConfigureSimpleSettings()`, the three-test
drift guard, and F69(a)'s comment. Suite **3831**.

**Part 2 is reverted**, per the design's §8 step 12. It never left the working tree — it was applied, built into
B2, run as arm A1, and rolled back; **no commit ever carried it**, and HEAD is `93e366a`. Verified at write-up
time on all three files:

| file | Part 2's change | state now |
|---|---|---|
| `HocusFocusStarDetection.cs` | `NoiseReductionRadius = 3` → `4` (at `:433` with Part 2's comment block applied) | **`NoiseReductionRadius = 3` at `:428`** |
| `documentation/docs/settings/preprocessing.md:23` | default `3` → `4` | **3** |
| `StarDetectionOptionsTests.cs:394-405` | the drift guard's F70 exception → plain equality at 4 | **the one-named-exception form, as committed by Part 1** |

The patch as applied is preserved at `/mnt/d/hf_w18/part2_w18.patch` — three hunks, one product literal — so
whichever wave takes F70(a) up does not have to re-derive it.

**The drift guard is already in its `N-HOLD` form in the committed tree** and needs no further edit: Part 1
shipped `T1` with `NoiseReductionRadius` as **one named exception asserted in both directions** — `3` on the
seed bundle, `4` on the constructed one, with an F70 comment saying that whether the seed should follow the
shipped default is RULE N18's question and not the test's. Reverting Part 2 restores exactly that. *The design
pre-registered the `N-HOLD` guard form, Part 1 shipped it, and the `N-UNEVALUATED` branch needed the same thing.*

**One disclosure the design owed and the diff corrects.** §2.5 named **two** existing assertions that Part 2
would change: `StarDetectionOptionsTests.cs:513` and `StarDetectionOptionsBufferedModeTests.cs:115`. In the
event, **Part 1 absorbed the second one** (`93e366a` moves it 3 → 4, because Part 1 makes `ResetDefaults()`
derive) and Part 2 changed **neither** — it changed the drift guard instead. The design's prediction was right
about *which assertions encode the old answer* and wrong about *which part changes them*. Recorded so a reader
diffing the wave does not conclude a change went missing.

**And one factual error in the pre-registration, found by re-deriving its own numbers.** Design §2.3 reads:
*"The two exceptions are `D01_ultrawide_40mm` and `D04_esprit_550mm`, both landing at 6 — and both are the
datasets whose `BaselineJ` is exactly **0.0** ([F20](followups.md))."* **The second half is wrong.** Measured on
this wave's own arms: the datasets landing at 6 under A0 are `{D01, D04}`, and the datasets whose `BaselineJ` is
exactly 0.0 are `{D01, D02}`. **They intersect at `D01` only.** `D04_esprit_550mm`'s `BaselineJ` is
**0.997120029953716** in both arms — an entirely ordinary value. The design used the supposed coincidence to
explain away both anchoring exceptions as F20 artifacts; only one of them is, and the other is unexplained.
*Two pairs of size two that share a member are not the same pair, and the wave that quoted the coincidence never
re-derived it.* Nothing downstream depends on it — `N18-A` is a magnitude and carries no verdict — but it is the
kind of number this series inherits, and it is corrected here rather than repeated.

---

## §10 — Lessons

1. **A control that reduces a property to ONE field inherits every ambiguity of that field — and the artifact
   under test carried the field twice.** A converted settings file has an `Options` block and an
   `OptimizedSettingsJson` snapshot; in Simple mode the `Options` copy of every detector knob is **dead**,
   overwritten by `DerivePresetSettings` on load. The control read the dead copy. Seven other fields in the same
   dump would have said so at zero cost. *Corroborate a one-field control with a second field that must move the
   same way.*

2. **A two-directional demonstration proves the branches are distinguishable, not that they are correctly
   assigned.** [F66](followups.md)(a) was honoured to the letter — two inputs differing in one asserted-by-
   read-back byte, two runs, a demonstrated TOOK and a demonstrated DID-NOT-TAKE — and it admitted an inverted
   control, because both directions were evaluated by the same wrong definition. It caught this one only because
   the labels were *swapped*; a definition error that moved both verdicts the same way would have read as PASS.

3. **The wave's own control was built without accounting for the defect under test.** Seven hundred lines
   establishing that `NoiseReductionRadius` has two shipped defaults differing by exactly one — and then a
   control whose correctness required the two copies of that field to agree. *When a wave's subject is a field
   with two values, that field is the worst possible choice of signature knob for the wave's own instrumentation.*

4. **The interlock paid for itself, on a control that was wrong.** `PROBE_PASSED` cost eight minutes of `af-fit`
   that were never spent. Had the pass run, `N18-V7` would have printed **0.000 of 20 on the control arm and
   0.750 on the treatment arm** — a validity gate whose pass rate is a *function of the treatment*, pointing in
   the direction the wave wanted to go. *A refusal that costs eight minutes is cheaper than a number that reads
   as evidence.*

5. **`P / S / A / E` is not enough: name the FIELD, not just the artifact.** The design filled in all four
   columns for `N18-V7` correctly. What it never asked was *which field of the artifact the statistic reads*, and
   the artifact had two with the same name. [F68](followups.md)'s catalogue is about populations and
   aggregations; this is a **quantity** error, invisible to every check the entry currently prescribes.

6. **Two binaries planned up front was right, and it is now measured from both sides.** `G18-P1` reads
   `seed=3` on 8 of 8 gate logs; `N18-V2` reads the seed dumps differing in exactly `NoiseReductionRadius` 3→4
   on 20 of 20 arm pairs, with the baseline dumps differing in **zero** fields. The controller's standing
   *"code → ONE binary → gate → arms"* order would have made RULE G18 a test of the change and stopped the wave
   in its own §1 for the most predictable reason available. That was an argument at pre-registration time; it is
   an observation now.

7. **An `exe` hash is not a binary identity.** `TestApp.exe` — the apphost — is **byte-identical** across B1 and
   B2 while both dlls differ. The whole wave rests on the two binaries being different, and what established it
   was `N18-V3`'s `BuildId` clause reading two distinct values across the arms, not a file hash.

8. **An UNEVALUATED verdict is not a wasted wave, and it is also not a licence.** This one established that a
   ninth binary reproduces the coordinate system, that Part 1 is inert on every harness path *by measurement*,
   that the two seeds produce two genuinely different binaries, that `BaselineJ` is bit-identical across arms so
   `J` is one function, and that the seed anchors the landing at 0.900 and 0.750. It established **nothing**
   about whether the seed literal should ship, and the honest form of that sentence is the one written here
   rather than a number quoted with a caveat attached.

---

## §11 — Reproduce

All scorers take **WSL** paths. *A Windows path yields UNEVALUATED everywhere and looks exactly like a failed
arm* — `score_n18_w18.py` aborts on a backslash for that reason.

```
# the gate
python3 /mnt/d/hf_w12/score_w12.py /mnt/d/hf_w18/gate --rule G18
python3 /mnt/d/hf_w18/prov_w18.py  --self-test /mnt/d/hf_w18/gate

# RULE N18 -- self-test first, then the scoring (section 2.3 explains the /tmp placeholders)
python3 /mnt/d/hf_w18/score_n18_w18.py --self-test
python3 /mnt/d/hf_w18/score_n18_w18.py --reach
mkdir -p /tmp/w18_affit_absent/a0 /tmp/w18_affit_absent/a1
python3 /mnt/d/hf_w18/score_n18_w18.py --a0 /mnt/d/hf_w18/seedA0 --a1 /mnt/d/hf_w18/seedA1 \
        --affit0 /tmp/w18_affit_absent/a0 --affit1 /tmp/w18_affit_absent/a1 \
        --gate /mnt/d/hf_w18/gate --w15 /mnt/d/hf_w15 --out /mnt/d/hf_w18/n18_score_NOAFFIT.txt

# the inverted control, from the artifacts the scorer itself read
python3 /mnt/d/hf_w18/score_n18_w18.py --probe /mnt/d/hf_w18/probe \
        --probe-result /mnt/d/hf_w18/gate/D18_m24_deep_shed/attempt01/optimize_result.csv
python3 -c "import json; d=json.load(open('/mnt/d/hf_w18/probe/good.json'))['Options']; \
  print(d['NoiseReductionRadius'], json.loads(d['OptimizedSettingsJson'])['NoiseReductionRadius'])"   # -> 4 3
grep -A80 'PARAMS-DUMP af-fit/detector' /mnt/d/hf_w18/probe/good/run.log   | grep NoiseReductionRadius  # -> 3
grep -A80 'PARAMS-DUMP af-fit/detector' /mnt/d/hf_w18/probe/mutant/run.log | grep NoiseReductionRadius  # -> 4

# the controls
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --check /mnt/d/hf_w18/bank_landing_fingerprint_BEFORE.json
python3 /mnt/d/hf_w17/aux_fingerprint_w17.py  --check /mnt/d/hf_w18/bank_aux_fingerprint_BEFORE.json
```

Drivers `/mnt/d/hf_w18/gate_w18.sh`, `/mnt/d/hf_w18/seed_w18.sh`, `/mnt/d/hf_w18/affit_w18.sh`; logs
`chain_w18.log`, `gate_w18.log`, `arms_chain.log`, `seed_A0.log`, `seed_A1.log`, `probe_w18.log`,
`affit_A0.log`, `affit_A1.log`; the Part 2 patch as applied is `/mnt/d/hf_w18/part2_w18.patch`.
