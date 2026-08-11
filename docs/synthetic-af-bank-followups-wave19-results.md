# Synthetic AF bank — followups wave 19 (results)

Pre-registration: [`docs/synthetic-af-bank-followups-wave19-design.md`](synthetic-af-bank-followups-wave19-design.md)
(commit **`e5be699`**, written and committed **before the binary was built**).
Plan: [`plans/synthetic-af-bank-followups-wave19-plan.md`](../plans/synthetic-af-bank-followups-wave19-plan.md).
Charter: [`docs/waves17-21-handoff-prompt.md`](waves17-21-handoff-prompt.md).
Wave 18: [`docs/synthetic-af-bank-followups-wave18-results.md`](synthetic-af-bank-followups-wave18-results.md).
Register: [`docs/followups.md`](followups.md).

> ## PROVENANCE
>
> | input | value |
> |---|---|
> | tree | branch `ghilios/synthetic-af-bank-followups-wave13`, PR #191. HEAD **`e5be699`** — the pre-registration commit itself. **The working tree is clean**, re-verified at write-up time (`git status --short` empty, `git diff --stat` empty) |
> | code shipped by this wave | **NONE. Item D did not land.** This is the wave's largest deviation from its own plan and §4 is the whole accounting of it. `optimize/detected` is absent from **0 of 8** gate logs and **0 of 20** arm logs — the arm-level measurement that F69(b) is not in this binary |
> | binaries — **ONE**, never rebuilt mid-wave ([F53](followups.md)(c)) | `D:\hf_w19\exe`. `TestApp.dll` sha256 **`d45a40ed7beede295283f1e5f05f6d108c688319cc96953fd6d7fdca785d2b70`** · `NINA.Joko.Plugins.HocusFocus.dll` sha256 **`6e8f4d1b7f83295a1f44544a91940223e4022e31943cc6fc7325dd8fad1d72eb`** · `BuildId` **`763d147623ea4dd2bdcff2981107ae4c`**, read from the first landing, novel against the nine recorded ids. **A TENTH BINARY.** The dlls are hashed, not `TestApp.exe` — wave 18 established the apphost is byte-identical across genuinely different binaries |
> | **the tree this binary was built from, against wave 18's B1** | `git diff --stat 93e366a e5be699` = **four markdown files and nothing else**; `git diff --name-only 93e366a e5be699 -- '*.cs' '*.csproj' '*.props' '*.targets'` is **EMPTY**. **The two binaries compared by RULE R19 are built from C#-identical source.** §2.4 is what that does to the reading of the result — and it is not what the pre-registration assumed |
> | settings **S0** | `D:\hf_w11\pinned_settings_w11.json` md5 `a67ffc06164c81613aef5c4f8324b9b8`. **Not re-pinned** ([F63](followups.md)(b), DO-NOT-RE-PIN since wave 17) |
> | profile | `astrodet` `ce3f3e63-8fd3-4b72-a0ca-d90db9441382`, pinned on every arm; **one** distinct value across all 28 landings this wave produced and all 20 it read |
> | banks | `D:\SyntheticAutofocusBank` (20 datasets), `D:\Autofocus Bank` (19 runs) |
> | detector | `DetectorVersion` **2**, read as a **FIELD** on all 8 gate landings and all 40 landings of the R19 pair ([F66](followups.md)). `strings -el` appears nowhere |
> | F15 control — landings | **42 of 42 BYTE-IDENTICAL.** 0 changed / added / removed / could-not-look. Sixth consecutive clean wave |
> | F15 control — aux | **59 of 59 byte-identical** (`harness_settings.json` ×39 + `synthetic_meta.json` ×20). 0 changed / added / removed / could-not-look |
> | F15 control — **prior-wave arms, the third class, new this wave** | **48 of 48 BYTE-IDENTICAL** across all three roots (`/mnt/d/hf_w18/seedA0` 20, `/mnt/d/hf_w18/seedA1` 20, `/mnt/d/hf_w18/gate` 8). It exists because RULE R19 *reads* wave 18's arms as evidence, and a control that reads a prior wave's artifacts must first prove they are the artifacts that wave wrote |
> | suite | **not run this wave, and not owed.** No C# changed, so the tree's test count is wave 18's verified **3831** unchanged. Recorded as "not re-measured", not as "green" |
> | prior-wave artifacts **read** | `/mnt/d/hf_w18/seedA0` (20 landings), `/mnt/d/hf_w18/gate` (8), `/mnt/d/hf_w18/probe` (2 `af-fit` runs). **All read-only; nothing this wave ran wrote under `/mnt/d/hf_w18`**, verified by the third fingerprint class |
> | prior-wave artifacts **deliberately not read** | `/mnt/d/hf_w18/seedA1`. Fingerprinted as a control, present in **no clause, no driver, no scorer path**. The wave could not have harvested F70(a) even by accident |
>
> **Every number in this document was re-derived at write-up time** by re-running each scorer against the arms on
> disk, plus one **independently written** key-by-key differ that shares no code with `score_r19_w19.py` and
> reproduced its 660 of 660 exactly. No arm directory was written to and no `TestApp.exe` was run at write-up.

---

## Status

| item | rule | verdict | what it establishes |
|---|---|---|---|
| the gate | **RULE G19** | **PASS** — 8 of 8 to 6 dp, **bit-identical at all sixteen digits**, on a **tenth** binary | **(a) only.** A tenth binary reproduces the coordinate system. **The pre-registered (b) — "the measurement that item D is inert" — is VOID: item D is not in this binary.** §1.4 |
| **A** | **RULE R19** | **`R-DETERMINISTIC`** — `R19-A` 20 of 20 datasets, **660 of 660 keys**; `R19-B` `FinalJ` 20/20 and `BaselineJ` 20/20; `R19-C` 3 of 3 | **`R19-A` minus `R19-B` is zero.** Nothing in the landing moves that `J` does not. §2 — including the narrower population than the design's prose claims |
| **B** | **RULE C19** | **`C-UNEVALUATED`** — `C19-D1` (retro) is `TOOK-CORRECTLY` with \|D\| = 8; **`C19-D2` was never run** | The corrected control is demonstrated in one of its two required directions-of-evidence. `C19_PROBE_PASSED` **was not written**, correctly, by the scorer's own interlock. §3 |
| **D** | — (was to ship unconditionally) | **DID NOT SHIP** | F69(b), F69(c), F70(b′) and the manual line are all still owed. §4 |
| **E** | — | **RECORDED** | F70(a) is **not decidable by measurement on this bank**; escalated to the owner. §5 |
| **F** | — (rule-free) | **DROPPED** (drop-order `D1`) | `synth-validate` is now **unpriced for three consecutive waves** (17, 18, 19). §7 |
| **C** | — | **UNBLOCKED** ([F72](followups.md)) | The crash that held it nine waves **does not reproduce**. Four confirmations obtained as rendered pixels; A1, A2, A4–A8 still owed. §6 |

> **The headline, stated once and stated narrowly.** For nine waves the gate has certified a new binary by
> checking **one** field. A landing carries **35** top-level keys. This wave checked the other 33, on 20
> datasets, and **660 of 660 key comparisons are identical**. The unmeasured error term the wave was built to
> look for **does not exist on this population**: every cross-wave landing comparison in this series — F63's
> 6-of-8, wave 15's 19-of-20, wave 18's `N18-M` — rests on a vector whose run-to-run noise is now measured at
> exactly zero, not merely assumed from `J`. **What it does not establish is that a CODE change cannot move a
> landing while leaving `J` fixed**, and §2.4 is why: the two binaries turned out to be builds of the same C#.

---

## §1 — RULE G19: **PASS**, on a tenth binary

Eight runs, `optimize --per-run --max-evals 250`, both pins named in the driver header, sequential, one
`TestApp.exe`, no NINA. `03:49:27Z → 04:30:51Z` = **41 m 24 s**.

### 1.1 The eight values

```
run                     BestJ (exact)         6 dp       expected   bit==K8  BaselineJ  w11
toml999                 0.9957838768299878    0.995784   0.995784   YES      0.983477   0.983477
CWhiteFocus             0.9960675916058808    0.996068   0.996068   YES      0.994320   0.994320
uneven                  0.9963677194179505    0.996368   0.996368   YES      0.991794   0.991794
muggsie                 0.9971948738498605    0.997195   0.997195   YES      0.993288   0.993288
mccomiskey              0.9767460801208465    0.976746   0.976746   YES      0.846082   0.846082
D18_m24_deep_shed       0.9998815090506263    0.999882   0.999882   YES      0.997405   0.997405
D19_cygnus_deep_shed    0.9994870586135448    0.999487   0.999487   YES      0.999187   0.999187
D20_m24_bright_control  0.9997378027339423    0.999738   0.999738   YES      0.999454   0.999454
```

**Bit-identical at sixteen digits on 8 of 8** — the bar was 6 dp; the sixteen-digit agreement is reported
because it is what was observed, and it is not the bar.

### 1.2 Every clause: threshold fixed before the data, beside its measured value, with the five-part statement

| clause | threshold, fixed in the design | **measured** | P — population | F — field, **and which copy** | S | A | E — on an empty set |
|---|---|---|---|---|---|---|---|
| **G19-1** | all eight `BestJ` reproduce K8 to **6 dp**; a partial reproduction is a FAILURE that stops the wave | **PASS — 8 of 8 at 6 dp, and bit-identical at 16 digits** | the 8 `optimized_settings.json` under `/mnt/d/hf_w19/gate` | `FinalJ`, top level. **A landing carries each name exactly once** — the two-copy hazard is a property of converted *settings files*, not landings | equality to 6 dp | all-of, printed `n of 8` | unreadable landing ⇒ COULD-NOT-LOOK, named, **and G19-1 FAILS** (for a stopping gate, "could not look" is "has not passed") |
| **G19-2** | `aggregate_summary.json produced: 8`, asserted **in the driver before any scorer** | **PASS — 8, expected 8** | `find` over the gate root | file existence | count | `== 8` | 0 found ⇒ FAIL |
| **G19-3a** | exactly **one** `BuildId`, **novel** against nine recorded ids | **PASS — `763d1476…`, cardinality 1, disjoint from all nine** | `Provenance.BuildId` ×8 | one copy per landing | set | cardinality **and** disjointness | empty ⇒ FAIL. `KNOWN_DLL_SHA256` is a **separate** table and the scorer fails loudly if a `BuildId` starts with a known dll hash ([F66](followups.md)) |
| **G19-3b** | `DetectorVersion` **2**, read as the **FIELD** | **PASS — `{"2"}` ×8** | `Provenance.DetectorVersion` ×8 | one copy per landing | set | `== {"2"}` | empty ⇒ FAIL. `strings -el` appears nowhere as a detector check |
| **G19-3c** | `ProfileId` contains `ce3f3e63-…` **and** cardinality exactly 1 | **PASS on both, evaluated separately** | `Provenance.ProfileId` ×8 | one copy | containment + cardinality | both, separately | `all()` over an empty list is vacuously true, so the **cardinality** clause carries the empty read |
| **G19-3d** | one distinct `FitInputs`, exactly `MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid` | **PASS — 1 distinct value, exactly the expected string** | `Provenance.FitInputs` ×8 | one copy | set | `== {expected}` | empty ⇒ FAIL |
| **G19-3e** | `ConcurrencyCheck == exclusive` on all eight, read **across the arm** | **PASS — a length-8 list, all `exclusive`, no `None`** | `Provenance.ConcurrencyCheck` | one copy, as a list carrying an explicit `None` per unreadable landing | equality | all-of over a length-8 list | a `None` is a FAIL, not a skip — the list cannot be shortened into agreement |
| **G19-3f** | `BaselineJ` reproduces wave 11 ([F41](followups.md)) to 6 dp | **PASS — 8 of 8** | `BaselineJ` ×8 | one copy | equality | `n of 8` | missing ⇒ FAIL |
| **G19-4** | `prov_w19.py --self-test` PASSES on the real arm and FAILS on a mutated copy, mutation asserted by read-back, **before any G19 number is quoted** | **PASS in both directions.** Direction 2 rewrote `muggsie`'s `ProfileId` to `Default (b10b1d6d-…)` and produced **2** failures — the pin clause *and* the cardinality clause | the arm and a `copytree` of it | `Provenance.ProfileId` on one named landing | — | both directions | a mutation that cannot be made prints **SELF-TEST COULD NOT RUN** — neither a pass nor a fail |
| **G19-P1** | the gate's own `PARAMS-DUMP optimize/seed` block reads `NoiseReductionRadius=3` on **8 of 8**, asserted **in the driver** | **PASS — `{'3': 8}`.** Independently re-read at write-up as a regex BEGIN…END range: **8 of 8 read 3**, and the `optimize/baseline` block reads **4 on 8 of 8** | the 8 gate logs | **`NoiseReductionRadius` in the `optimize/seed` block** — *not* `optimize/baseline`, *not* the landing's copy | field value | `== 8` | a missing block ⇒ the driver refuses to hand the arm on |

### 1.3 The `G19-P1` hazard had an address and the address stayed empty

`/mnt/d/hf_w18/part2_w18.patch` is still on disk, and the brief handed to *this* agent listed
`HocusFocusStarDetection.cs`, `StarDetectionOptionsTests.cs` and `preprocessing.md` as modified — **the same
stale `git status` the pre-registration caught, recurring a second time**, one wave later, in a fresh session.
The tree is clean; the seed dumps read 3 on all 28 logs this wave produced. **The guard fired zero times and is
worth every line it costs**, because the failure it prevents is silent: a binary carrying a moved optimizer seed,
certified by a gate that would have reproduced nothing.

### 1.4 What the PASS proves — and the half of it that is VOID

The design fixed two claims. **(a) holds: a tenth binary reproduces the coordinate system.** **(b) does not.**

> §1.3(b) read: *"Item D is inert on every harness path — and that is a MEASUREMENT, not an assumption. That is
> the whole reason item D sits in the gate's binary rather than beside it."*

**Item D is not in the gate's binary**, because item D was never written (§4). A gate cannot measure the
inertness of code that is not in it. The claim is not weakened, it is **inapplicable**, and it is struck rather
than restated. What survives is the ordinary nine-wave claim: the search is unmoved.

It also says nothing about the landing beyond `FinalJ` and `BaselineJ` — which is exactly what RULE R19 exists
to measure — and it runs at `MaxOutlierRejections = 0`, so it says nothing about the rejection path.

---

## §2 — RULE R19: **`R-DETERMINISTIC`**. The coordinate system is thirty-three numbers, not one

`optimize --per-run --max-evals 250 --settings S0 --profile-id astrodet`, 20 synthetic datasets, sequential, in
wave 18's fixed order (`D18, D19, D20`, then `D01…D17`), into `/mnt/d/hf_w19/reA0`.
`04:32:00Z → 05:24:43Z` = **52 m 43 s**, 20 of 20 landings.

### 2.1 Validity gates — all five pass, so the rule issues a verdict

| clause | threshold, fixed in the design | **measured** | P | F — **and which copy** | S | A | E |
|---|---|---|---|---|---|---|---|
| **R19-V1** *population* | landings present on **both** sides equal the datasets the driver **scheduled** | **PASS — scheduled 20 (read from `reA0/EXECUTED_DATASETS`, written before the first run); readable on both sides 20 of 20** | `optimized_settings.json` under each root vs `EXECUTED_DATASETS` | file existence | count | `== scheduled` on both sides | `EXECUTED_DATASETS` missing ⇒ COULD-NOT-LOOK ⇒ R19 UNEVALUATED — never "0 of 20", never a drop |
| **R19-V2** *a DIFFERENT binary ran* | one `BuildId` per root; the two **differ**; the new one novel against nine; the old one exactly `e745c958502244f498950e99ac3d6f00` | **PASS — new `['763d147623ea4dd2bdcff2981107ae4c']`, old `['e745c958502244f498950e99ac3d6f00']`, cardinality 1 each, disjoint.** §2.4 is what "different binary" turned out to mean | `Provenance.BuildId` in the 2 × 20 landings | one copy per landing | set per root | cardinality **and** disjointness | unreadable landing is its own named state, clause FAILS. **Two roots sharing a `BuildId` is an explicit FAIL** |
| **R19-V3** *the seed did not move* | `NoiseReductionRadius == 3` on N of N | **PASS — 20 of 20.** Independently re-read at write-up: `optimize/seed` = `{'3': 20}`, `optimize/baseline` = `{'4': 20}` | the `PARAMS-DUMP optimize/seed` block in the new root's 20 `run.log` | **the `optimize/seed` block only**, read as a BEGIN…END range | field value | `== N` | a missing block ⇒ COULD-NOT-LOOK by name ⇒ R19 UNEVALUATED |
| **R19-V4** *the pins* | `ProfileId` by containment **and** cardinality; one `FitInputs`; `DetectorVersion` 2; `ConcurrencyCheck` exclusive across the arm — **on both roots** | **PASS on both roots.** `ProfileId` cardinality 1 = `astrodet (ce3f3e63-…)`; `FitInputs` cardinality 1; `Det = ['2']`; `ConcurrencyCheck` a length-20 list of `exclusive`, **twice** | `Provenance.*` in the 2 × 20 landings | one copy each | equality / cardinality | per root, and across the arm for concurrency | a `None` in the concurrency list is a FAIL, not a skip |
| **R19-V5** *both roots read the same settings file* | `SettingsFingerprint` identical across roots on N of N | **PASS — 20 of 20 = 1.0000** | `Provenance.SettingsFingerprint` | one copy | cross-root equality | rate, denominator printed | missing ⇒ COULD-NOT-LOOK, named, clause FAILS |

### 2.2 The measurement

| clause | threshold | **measured** | P | F — **and which copy** | S | A | E |
|---|---|---|---|---|---|---|---|
| **R19-A** *the landing, field by field* | **dataset rate == 1.000** (a rate, never the literal 20); key-level rate reported alongside | **`1.0000` — 20 of 20 datasets with zero differing keys; 660 of 660 key comparisons = `1.0000`.** Every dataset carried exactly **33** comparable keys; the comparable key-set is **one** set across all 20 (verified independently) | the paired landings' comparable top-level keys: union of both sides **minus `CreatedAtUtc`** (a timestamp — excluded **in advance**, wave 15's `G-d` named rather than discovered) **and minus `Provenance`** (whose `BuildId` MUST differ by V2 and whose `CommandLine` MUST differ because `--out` differs) | each key at the landing's top level; one copy each | `repr()` equality — makes `float('nan')` equal to itself while keeping the **string** `"NaN"` distinct | **two rates, both denominators printed** | a landing missing on either side ⇒ COULD-NOT-LOOK, named, out of **both** denominators, which are re-printed. A rate over an empty set is UNEVALUATED, never 1.000 |
| **R19-B** *the objective alone* | rate reported (no bar) | **`FinalJ` 20 of 20 = `1.0000`; `BaselineJ` 20 of 20 = `1.0000`** | the same paired landings | `FinalJ`, `BaselineJ` | `repr()` equality | two rates over the same denominator | as R19-A |
| **R19-C** *confound-splitter, **not decisive*** | rate over 3, **reported**, cannot move the verdict | **3 of 3 = `1.0000`.** Same binary (`BuildId` equal, verified), different invocation, different `--out`; the only differing `Provenance` member is `CommandLine` | this wave's **gate** landings for `D18/D19/D20` vs the new root's | as R19-A | as R19-A | rate over 3 | COULD-NOT-LOOK, named, out of the denominator |

**The exclusions were exactly and only what was pre-registered.** Re-derived independently: across the new/old
pair the differing members are `CreatedAtUtc`, `Provenance.BuildId` and `Provenance.CommandLine` — the three the
design named in advance, and nothing else. Across the gate/new pair (`R19-C`) only `CreatedAtUtc` and
`Provenance.CommandLine` differ. **No exclusion was invented after the data.**

### 2.3 The branch table, applied as written

```
R19-V1..V5 all pass?                        YES  (so no R-UNEVALUATED)
R19-A dataset rate == 1.000?                YES  ->  R-DETERMINISTIC
```

> ### `R19-A` minus `R19-B` = **0**
>
> This is the number nobody in this series has had. `R19-B` is what nine waves of gates have actually been
> checking — `J`, one field. `R19-A` is the whole landing. They agree, and the difference is **zero on 660
> comparisons**. **Nothing moves that `J` does not.**
>
> `J` is saturated near 1.0 on this bank ([F32](followups.md)) and the search crosses a flat valley
> ([F6](followups.md), [F8](followups.md)), so *two knob vectors with the same `J` to sixteen digits is exactly
> what a flat valley produces.* That was the `R-J-ONLY` branch. **It did not fire.** Had it fired, the
> consequence was fixed in advance and would have been severe: every cross-wave landing comparison in this
> series would carry an unmeasured error term, wave 18's 40 arm landings would be a **sample** rather than a
> measurement and no rule could consume them, and the charter's *"search noise measured at zero"*
> ([F63](followups.md), from wave 15's `G-e`) would be a statement about `J` and not about the landing.

### 2.4 What this licenses — and the population is NARROWER than the design's prose

**This is the correction the wave owes its own pre-registration, and it is not a detail.**

The design's §2.3 called the treatment *"a different binary,"* and its §11.3 argued that putting item D in the
gate's binary *"is what turns the inertness claim into a measurement."* **Item D never landed.** So:

| what the design assumed the two roots differed by | what they actually differ by |
|---|---|
| wave 18's B1 tree **vs** that tree **+ item D** (a new `PARAMS-DUMP` block, a flag notice, ~20 deleted literals, a manual line) | **nothing in C#.** `git diff --stat 93e366a e5be699` is four markdown files; the `*.cs` / `*.csproj` / `*.props` / `*.targets` name-only diff is **empty** |

`BuildId` still separates them, and `R19-V2` passes **as written** — but by the field's own documented semantics.
`OptimizedStarDetectionSettings.cs:386` says `BuildId` is *"the plugin assembly's Module Version ID, which the
compiler regenerates on every build **even when the source is byte-identical**."* **A differing `BuildId` proves
a rebuild happened. It has never proved the code differs, and here it does not.**

**So R19 measured, at n = 20 on 33 fields:** two independent Release builds of the **same C# source**, two
invocations, two output directories, roughly ten hours apart, at `--max-evals 250`, on 20 synthetic datasets,
`--settings`- and `--profile-id`-pinned, one `TestApp.exe`, no concurrency. **Zero differences.**

**And the accident improves the instrument rather than damaging it.** For measuring an *error term* you want the
treatment **absent**: R19 as executed is a clean **null arm**, and a null arm is the correct control for the
question "does a cross-wave landing comparison carry noise?" The confound the design worried about — binary and
invocation varying together — is gone, because the binary axis collapsed to build identity, and `R19-C` splits
invocation off anyway at 3 of 3. *The wave got a better control than it designed, by failing to ship the code
that would have made it a treatment.* That is luck, and it is recorded as luck.

**What it therefore does NOT license, stated plainly:**

- **It is not evidence that a CODE change cannot move a landing while `J` stays fixed.** Nothing measures that.
  The `R-J-ONLY` branch could only have fired here from run-to-run or build-stamp nondeterminism, never from a
  code difference, because there was no code difference. **The hypothesis the wave was built around was tested
  against a weaker treatment than the one it was written for.**
- Not a claim about determinism **under concurrency** — [F55](followups.md) owns that, every arm here ran
  `ConcurrencyCheck = exclusive`, and the clause verified it rather than assuming it.
- Not a claim across **detector changes** (`DetectorVersion` was 2 on both roots, by clause).
- Not a claim about the **real bank** — the population is 20 synthetic datasets. The gate's 5 real runs
  reproduced `FinalJ`/`BaselineJ` only.
- Not a claim at other `--max-evals`, other settings, or other profiles.
- The **17 datasets `D01…D17` still have no cross-*code* `J` check**, only this cross-build one. `D18/D19/D20`
  have K8.

**What it does license, precisely.** The series' cross-wave landing comparisons treat a code difference as the
**treatment** and everything else as **error**. R19 measures that error term directly and finds it **exactly
zero on 33 fields at n = 20**. So F63's 6-of-8, wave 15's 19-of-20 and wave 18's `N18-M` are retro-validated
*against noise*: whatever moved in them was not the harness moving under its own feet. That is a real and
previously-unmeasured underpinning for a large part of the register — and it is the *only* thing R19 underwrites.

### 2.5 The disambiguation was not needed

Design §2.3's fallback — re-run each failing dataset on `/mnt/d/hf_w18/exe_b1`, capped at five — is triggered
only by `R19-A < 1.000`. `R19-A` is `1.0000`. **`/mnt/d/hf_w19/reA0_b1` does not exist and no dataset was
re-run.** Recorded because a cap not spent is worth as much as a cap enforced.

---

## §3 — RULE C19: **`C-UNEVALUATED`**, and the interlock refused correctly

| clause | threshold | **measured** |
|---|---|---|
| **C19-V1** | `convert_landing_w19.py --self-test` passes, including F71(c)'s four new assertions | **PASS — 11 of 11**, including all three two-copy states (`BOTH-DIFFER`, `SNAP-ONLY`, `BOTH-AGREE`) and all four refusals |
| **C19-D1** *retro, zero compute* | good agrees with the snapshot on **all** of `D ∩ snapshot`, mutant on **none**, with **\|D\| ≥ 3** | **`TOOK-CORRECTLY`. \|D\| = 8; \|D ∩ snapshot\| = 8; good 8 of 8; mutant 0 of 8.** Re-run at write-up against `/mnt/d/hf_w18/probe`, reproducing the pre-registered numbers exactly |
| **C19-D2** *live, ~1 m* | as D1, on a probe built from this wave's own gate `D18` landing | **NOT RUN.** `/mnt/d/hf_w19/probe` does not exist; `probe_w19.sh` was never invoked |
| scorer self-test | 9 branches | **PASS — 9 of 9**, including the inverted pair, \|D\| = 0, \|D\| = 1, missing dump, missing snapshot (**no fallback to `Options`**), mismatched snapshots, and an unaliased field **named** rather than dropped |

The eight corroborating fields, with the alias applied (`Sensitivity → BrightnessSensitivity`):

```
field (dump name)        good                  mutant   snapshot (the LIVE copy)
HotpixelThreshold        0.005                 0.001    0.005
MaxDistortion            0.10000000000000003   0.5      0.10000000000000003
MinHFR                   0.7                   1.2      0.7
NoiseReductionRadius     3                     4        3
Sensitivity              14.666666666666664    10       14.666666666666664
StarCenterTolerance      0.44999999999999996   0.3      0.44999999999999996
StarClippingMultiplier   0.25                  2        0.25
StructureLayers          5                     4        5
```

These reconcile **exactly** with the converter's independent two-copy table: 5 `BOTH-DIFFER`
(`BrightnessSensitivity`, `MinHFR`, `NoiseReductionRadius`, `StarClippingMultiplier`, `StructureLayers`) plus the
3 `SNAP-ONLY` knobs whose code default differs from the snapshot (`HotpixelThreshold`, `MaxDistortion`,
`StarCenterTolerance`). **Two instruments derived independently, agreeing on 8.** The other three `SNAP-ONLY`
knobs equal their code defaults and therefore cannot discriminate — which is why \|D\| is 8 and not 11.

**The verdict is `C-UNEVALUATED` and the machinery produced it without being asked.** The scorer requires
`{"C19-D1", "C19-D2"} ⊆ states` before writing `C19_PROBE_PASSED`; with only D1 present it printed *"marker NOT
written: only ['C19-D1'] ran. BOTH demonstrations are required."* and downgraded its own verdict.
**`/mnt/d/hf_w19/C19_PROBE_PASSED` does not exist.** A scorer that refuses to bless itself on half its evidence
is the design working; **the wave still owes the live half.** Wave 20 inherits it at **~1 m**.

*(Note the asymmetry deliberately: `C-DEMONSTRATED` would have licensed nothing in this wave either — §3.4 of
the design. Not writing the marker costs this wave nothing at all and costs wave 20 one minute.)*

---

## §4 — Item D: **it did not ship**, and this is the wave's largest deviation

The design shipped item D **unconditionally** (§0, §5, §12 step 2). It is not in the tree and it is not in the
binary. The evidence is threefold and none of it is inference:

| what was owed | state in the tree at `e5be699` | arm-level evidence |
|---|---|---|
| **F69(b)** — a second `ParamsDump.Write` after `ApplyRunDetectionBinningIfRequested`, tag `optimize/detected` | `ParamsDump.cs` declares `OptimizeBaseline` and `OptimizeSeed` **and no third constant** | **`PARAMS-DUMP optimize/detected` appears on 0 of 8 gate logs and 0 of 20 arm logs.** Every log carries exactly two blocks |
| **F69(c)** — a one-line notice that `--apply-run-detection-binning` is an accepted no-op | no notice in `OptimizationDiagnosticRunner.cs` | — |
| **F70(b′)** — delete the preset-owned dead literals from `ResetDefaultsImpl`, behind mutant **M-R1** | not done | — |
| **the manual line** | `documentation/docs/settings/preprocessing.md` still reads `3` at the table row **and** at the prose `**Default:** \`3\`` | **the `optimize/baseline` block reads `NoiseReductionRadius=4` on 8 of 8 gate logs and 20 of 20 arm logs** — the option's shipped value, measured on 28 logs this wave, against a manual that says 3 |

**Three consequences, and they are not symmetric.**

1. **RULE G19's claim (b) is void, not weakened** (§1.4). The gate cannot measure the inertness of absent code.
2. **RULE R19's treatment collapsed to build identity** (§2.4) — which, by luck, made it a better control.
3. **The documentation bug is now two waves old.** Wave 18 made the manual fix conditional on RULE N18; N18
   returned `N-UNEVALUATED`; the revert left a line that **the same wave's own Part 1 (`93e366a`) had just made
   unambiguously wrong**, because after Part 1 `ResetDefaults()` derives too and **nothing in the product
   produces 3 for this option**. Wave 19 pre-registered the fix unconditionally and did not make it. *The manual
   documents the option; F70(a) is about `BuildDefaultStarDetectorParams`'s seed. Two quantities sharing a name
   — [F71](followups.md)'s own defect, in the manual instead of a settings file — and it has now survived two
   waves that both diagnosed it correctly.*

**None of this is reported as a machine problem and none of it invalidates a measured number.** Every clause
that ran, ran on the binary the provenance names, and every threshold was fixed before the data. What changed is
**what the numbers are about**, and §2.4 says so rather than letting the design's prose stand.

---

## §5 — Item E: **F70(a) is not decidable by measurement on this bank**

Recorded as pre-registered (design §3.5), at zero compute. Every class of evidence that could decide the
`BuildDefaultStarDetectorParams` seed literal, and its state:

| evidence class | state | why it cannot satisfy a measurement rule |
|---|---|---|
| **source / counting argument** | **COMPLETE and free** — wave 18 §2.4; the register already says *"(a) IS DECIDED AND UNSHIPPED. The answer is 4."* | it decides the question but **is not a measurement** |
| **in-sample objective (`FinalJ`)** | **SPENT** — published at A1-better **13**, A0-better **7**, 0 ties | a rule over it now has a known outcome (charter §3) |
| **anchoring rate (`N18-A`)** | **SPENT** — published at 0.900 / 0.750 | same |
| **out-of-sample `e`** | **UNMEASURED, and preserved by this wave** | and structurally weak even unspent: the largest landing-driven out-of-sample movement ever measured on this bank is **0.02584 step**, the largest of any kind **0.00993**, against a **0.10-step** materiality floor. **`e` can be a VETO; a veto is not a decider**, and a rule whose only measurement clause is a foreclosed veto cannot fail to ship — [F66](followups.md)(a)'s defect |
| **reach on real profiles** | **0 of 9, by construction.** Free, published | measures that the change is nearly inert; argues neither way |
| **the other 11 `BuildDefaultStarDetectorParams` call sites** | unmeasured, ~30 m each | wave 18: *"buys no decision"* |

> **The only clause with a live range was in-sample `FinalJ`, and it is spent.** F70(a) is a decision to be taken
> **on the source argument, by the owner**, and this series has the precedent: wave 14's `MaxOutlierRejections`
> default was set to 0 by the owner, overriding wave 13's pre-registration, and the register recorded it as such.
> The charter's standing authorisation already provides for it: a change no rule licenses is *"recorded as a
> costed recommendation."* **Cost: one literal, plus F69(b)'s dump so the next wave can see which value ran.**

And the decision **not to look** was honoured completely: no `af-fit` driver was built, `/mnt/d/hf_w18/seedA1`
appears in no clause and no scorer path, and the third fingerprint class proves all 48 prior-wave landings are
byte-identical — *the population is unspent and provably untouched.* **That is the one thing this wave was
supposed to protect, and it protected it.**

---

## §6 — Item C: **UNBLOCKED** ([F72](followups.md))

Carried from the wave 18/19 boundary; nothing was re-derived here and no NINA was launched by this wave. The
register entry is complete; the durable points:

- **The crash does not reproduce.** NINA 3.3.0.1048 **with** the plugin: *"Successfully loaded plugin Hocus Focus
  version 4.0.0.12"*, **0 ERROR, 0 NRE**. An earlier log the same day shows the same NINA running fine with the
  plugin for 169 KB and **148 Hocus mentions**. The host is **3.3 NIGHTLY #048**.
- **The control that mattered was REPETITION, not isolation.** Run alone the isolation test reads as decisive —
  *works without, crashes with* — and **would have produced a confident wrong attribution**. It costs one extra
  launch. *n = 1 on a crash is not an attribution, and a blocker recorded from a single sample held an item for
  nine waves.*
- **Rendered-pixel confirmations obtained:** the plugin loads; all four option tabs render unclipped with every
  control bound; **`Max Outlier Rejections: 0` is visible in the AF panel** — wave 14's product change confirmed
  in the running app for the first time; A3/A9's row renders as *"Run an auto-focus to get a recommendation"*.
- **A1, A2, A4–A8 still need a loaded AF run and the wizard driven, ~30 m.** The blocker is gone; only the work
  remains.

---

## §7 — Budget: estimated against actual

| step | instrument | estimate | **actual** | note |
|---|---|---|---|---|
| build | `dotnet build -c Release` | ~25 s | **32.09 s** | wave 18's own estimate for this was ~6 m, 8× high; the measured-rate estimate is 28 % low, which is the right kind of wrong |
| **RULE G19** | `optimize --per-run --max-evals 250`, 8 runs mixed | **~42 m** | **41 m 24 s** (`03:49:27Z→04:30:51Z`), **5.17 m/run** | 44 s under wave 18's 42 m 08 s on the identical arm; the mixed rate holds at ≈ 5.2 m/run for a fifth wave |
| **RULE R19 arm** | `optimize --per-run --max-evals 250`, 20 all-synthetic | **~55 m** | **52 m 43 s** (`04:32:00Z→05:24:43Z`), **2.64 m/run** | wave 18's identical arm took 3274 s / 2.73 m/run — **111 s faster, −3.4 %**, on the same 20 datasets. The all-synthetic rate is now measured twice |
| **RULE C19-D2** | `af-fit` ×2 | ~1 m | **not run** | §3 |
| RULE C19-D1 | Python, retro | 0 | **0** | re-run at write-up; reproduced exactly |
| **item F** | `synth-validate` | ≤ 20 m hard timebox | **not run** | dropped, drop-order `D1` |
| item D + the suite | code, `dotnet test` | ~1 h 30 m–2 h | **not run** | §4 |
| scoring | Python | < 10 m | < 10 m | all scorers re-run at write-up |
| **`TestApp` wall** | | **~1 h 38 m** | **1 h 34 m 07 s** of arm time; **1 h 35 m 16 s** gate-start to arm-end | against a **6 h** ceiling |

**Three instruments, three rates, never crossed**: `optimize` mixed ≈ 5.2 m/run, `optimize` all-synthetic
≈ 2.64–2.73 m/run, `af-fit` ≈ 11 s/dataset. An `af-fit` 39-run rung is ~11 m and must never be priced from the
`optimize` rate.

---

## §8 — What was NOT run, and what it costs

**In cost order, cheapest first.**

- **`C19-D2`, the live half of the corrected conversion control — ~1 m.** The only *cheap* thing this wave left
  undone, and it is the reason RULE C19 has no verdict. Wave 20 gets it for a minute:
  `bash probe_w19.sh` then `score_c19_w19.py --retro --probe /mnt/d/hf_w19/probe`. **Everything else it needs is
  demonstrated**: the converter's self-test 11/11, the scorer's 9/9, and D1 `TOOK-CORRECTLY` at \|D\| = 8.
- **The out-of-sample pass over wave 18's 40 arm landings — ~8 m. Deliberately not run, and no driver exists
  that could.** This is the wave's central decision and it was honoured completely (§5). *Publishing a
  diagnostic spends the population it reads* — wave 15's `V4′` is the precedent that cost RULE F14 permanently.
  **The population is preserved and provably untouched (48 of 48 byte-identical).** And R19's
  `R-DETERMINISTIC` is exactly the precondition design §3.6 named: **wave 18's 40 landings are a MEASUREMENT,
  not a sample**, so the wave-20 item is **live at ~8 m** — subject to §2.4's narrowing.
- **Item F, the F21 price probe — ≤ 20 m.** Dropped. **`synth-validate` has now been unpriced for three
  consecutive waves** (17 asked, 18 dropped, 19 dropped). Recorded in [F21](followups.md).
- **Item D and the suite — ~1 h 30 m–2 h.** §4. Costs: F69(b)/(c) and F70(b′) stay owed, the manual line stays
  wrong for a second wave, and G19 claim (b) is void.
- **Fresh arms at the two seeds — ~2 h.** Refuted by measurement, not declined for budget, and **the refutation
  got stronger**: the design refuted it at n = 3 on the invocation axis; R19 now has 20 of 20 on 33 keys. *(a)
  is (b) plus two hours and a false claim of freshness.*
- **Re-running arm A1, or reading `/mnt/d/hf_w18/seedA1` in any clause.** Zero minutes, deliberately.
- **Any repaired form of `N18-V7`, `S16-A(b)`, wave 16's rungs or wave 14's `V4`/`V4′`.** Zero minutes, per
  [F68](followups.md)(c) and the charter's §3 and §3a.
- **[F45](followups.md)(b)'s production plumbing — ~3–4 h + tests. Rejected on substance.** (i) RULE S16
  returned NO RECOMMENDATION and §3a forbids repairing `S16-A(b)`, so the plumbing would deliver a mechanism
  **no rule has recommended** — the decisive reason. (ii) After wave 14 flipped `MaxOutlierRejections` to 0,
  `RejectionTest` does not run on a default profile, so a SEM floor reaches **3 of 9** profiles here.
  (iii) `N*` is not reachable in `AutoFocusEngine` — `:901` drops the count into `MeasureAndError`, a NuGet
  struct of two doubles — so it needs a parallel count map, a pooling rule that does not double-divide against
  the existing `/√frames`, the same again in the optimizer's path, and a choice between `DetectedStars` and
  `hfrStars.Count`.
- **[F59](followups.md)'s five knobs — ~1 h. Rejected**, because it is a **re-pin** and re-pinning was closed
  DO-NOT-RE-PIN in wave 17 under [F63](followups.md)(b). *Refuting an argument is not refuting its conclusion.*
  **"It is cheap right now" is not a reason to move a coordinate system.**
- **The other 11 `BuildDefaultStarDetectorParams` call sites.** Moot while the seed literal does not ship.
- **A fresh eight-value baseline. Not owed** — K8 stays valid and G19 measured it rather than assuming it.
- **A1, A2, A4–A8 — ~30 m** on a connected session with NINA not competing with a pinned arm (§6).

---

## §9 — Register corrections this wave owes

1. **The `af-fit/detector` dump carries 55 fields, not 56.** [F71](followups.md) and wave 18's results §5.3 both
   say 56. Counted directly on both saved logs at write-up: **55 on `good`, 55 on `mutant`, 55 unique names, no
   duplicates.** Nothing downstream depends on it — `C19-D1`'s \|D\| = 8 is unaffected — and it is corrected
   rather than repeated.
2. **F69(b), F69(c) and F70(b′) did not ship in wave 19**, contrary to the pre-registration. Recorded in the
   register so a later wave does not read the design as evidence they exist. Arm-level evidence: 0 of 28 logs
   carry `PARAMS-DUMP optimize/detected`.
3. **[F21](followups.md)'s instrument is unpriced for a third consecutive wave.**
4. **[F8](followups.md) is BOUNDED** by RULE R19 — with §2.4's population stated in the bound, not omitted.

---

## §10 — Lessons

1. **A gate cannot measure the inertness of code that is not in its binary.** The design's best structural
   idea — put the inert change in the gate's binary so that "inert" becomes a *measurement* — is sound, and it
   silently degrades to a claim about nothing when the change does not land. **The gate passed; the claim it was
   built to make is void.** Nothing in the artifacts announces this: `G19-1` reads exactly the same on a binary
   with item D and without it. The only thing that caught it was the driver's *reported, never-a-bar*
   `optimize/detected` count — **a number with no threshold attached did the work that ten thresholds could
   not**, because it is the only clause whose value depends on the shipped code.
2. **A treatment that fails to arrive turns a confounded experiment into a clean control — and you only get the
   benefit if you check what actually varied.** R19 was designed to vary binary-and-invocation together and
   ended up varying build identity alone. Reported as designed, it would have read as *"the landing is
   deterministic across binaries"*, which is a claim about **code** the data cannot support. Reported honestly,
   it is a **null arm** measuring an error term at exactly zero — which is more useful for the register than
   what was designed, and less useful than what was claimed. **`git diff --name-only <old-tree> <new-tree> --
   '*.cs'` is now a mandatory provenance line for any wave whose rule compares two binaries.**
3. **Three controller slips this wave, and the DRIVERS caught all three — not the controller.** A binary written
   to `exeB1` when the driver wanted `exe_b1`; a third fingerprint written under the wrong filename
   (`arm_fingerprint_BEFORE.json` rather than `w18_arm_fingerprint_BEFORE.json` — the two files are
   byte-identical, sha256 `3cae052a…`, so the cost was one abort and zero data); and, in wave 18, a progress
   counter with no could-not-look state that reported a fully successful arm as `summaries=0`. **Each time the
   driver refused rather than guessed**, and the refusal is legible in `chain_w19.log`:
   *"ABORT: /mnt/d/hf_w19/w18_arm_fingerprint_BEFORE.json does not exist"* followed by the three exact commands.
   *An abort that prints the fix costs one minute; a guess costs a wave.*
4. **The stale `git status` recurred.** The pre-registration caught it once (design §11.6). The brief handed to
   the *write-up* agent, a fresh session later, listed the same three Part 2 files as modified with the same
   wrong tip. HEAD is `e5be699`, `git diff` is empty. **A stale status describing a live hazard is not a
   one-time event and the guards must be permanent**, which `G19-P1` and `R19-V3` are: 28 of 28 logs read
   `NoiseReductionRadius=3` in the `optimize/seed` block.
5. **A scorer that refuses to bless itself on half its evidence is worth more than a verdict.** `score_c19_w19.py`
   had `C19-D1` at `TOOK-CORRECTLY` and could have printed `C-DEMONSTRATED`. Its own interlock required both
   demonstrations, downgraded to `C-UNEVALUATED`, and did not write the marker. **No human decided that.**
6. **The cheapest thing left undone was the one-minute one.** C19-D2 costs ~1 m and is the sole reason RULE C19
   has no verdict, in a wave that used 1 h 35 m of a 6 h ceiling. *A drop order protects against spending too
   much; nothing protects against skipping something that costs nothing.* The drop order should carry a floor:
   **an item under two minutes is never dropped.**
7. **"Do not look" is executable, and it executed.** No `af-fit` driver was built, `seedA1` appears in no clause,
   and 48 of 48 prior-wave landings are byte-identical. *The surest way not to look is to not build the thing
   that looks* — and the fingerprint class that proves it was written the same wave the temptation existed.

---

## §11 — Reproduce

Drivers and scorers live under `D:\hf_w19\` (not committed; no wave has committed them). WSL paths throughout;
every scorer aborts on a backslash.

```bash
# RULE G19 -- the gate (already run; re-scoring is read-only)
python3 /mnt/d/hf_w12/score_w12.py /mnt/d/hf_w19/gate --rule G19
python3 /mnt/d/hf_w19/prov_w19.py  --self-test /mnt/d/hf_w19/gate      # both directions

# the three F15 fingerprint classes
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --check /mnt/d/hf_w19/bank_landing_fingerprint_BEFORE.json
python3 /mnt/d/hf_w17/aux_fingerprint_w17.py  --check /mnt/d/hf_w19/bank_aux_fingerprint_BEFORE.json
python3 /mnt/d/hf_w19/arm_fingerprint_w19.py  --check /mnt/d/hf_w19/w18_arm_fingerprint_BEFORE.json

# RULE R19
python3 /mnt/d/hf_w19/score_r19_w19.py --self-test                     # 19 of 19
python3 /mnt/d/hf_w19/score_r19_w19.py --new /mnt/d/hf_w19/reA0 --old /mnt/d/hf_w18/seedA0 \
        --gate /mnt/d/hf_w19/gate --out /mnt/d/hf_w19/r19_score.txt

# RULE C19 -- D1 is free and read-only; D2 is the ~1 m that is still owed
python3 /mnt/d/hf_w19/score_c19_w19.py --self-test                     # 9 of 9
python3 /mnt/d/hf_w19/convert_landing_w19.py --self-test               # 11 of 11
python3 /mnt/d/hf_w19/score_c19_w19.py --retro                         # |D| = 8, good 8/8, mutant 0/8
# still owed: bash /mnt/d/hf_w19/probe_w19.sh && \
#             python3 /mnt/d/hf_w19/score_c19_w19.py --retro --probe /mnt/d/hf_w19/probe

# the provenance line this wave learned to need
git diff --name-only 93e366a e5be699 -- '*.cs' '*.csproj' '*.props' '*.targets'   # EMPTY
```

The arms themselves — `gate_w19.sh` (41 m 24 s) and `redo_w19.sh` (52 m 43 s) — **must not be re-run**
([F53](followups.md)(c)); an artifact directory keeps only the last build, and both drivers abort on a populated
output root.
