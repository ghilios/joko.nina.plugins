# Synthetic AF bank — followups wave 21 (results)

**Artifact root at write-up: `/mnt/d/hf_w21/` — `2026-08-11 06:14:17.433510400 -0400` = `2026-08-11T10:14:17.433Z`**
(`ls -la --time-style=full-iso /mnt/d/hf_w21/`; the filesystem clock is local `-0400`, every driver clock in this
document is UTC).

Pre-registration: [`docs/synthetic-af-bank-followups-wave21-design.md`](synthetic-af-bank-followups-wave21-design.md)
(commit **`3a4db7c`**, `2026-08-11T09:11:17Z`, **22 seconds before the build**).
Plan: [`plans/synthetic-af-bank-followups-wave21-plan.md`](../plans/synthetic-af-bank-followups-wave21-plan.md).
Charter: [`docs/waves17-21-handoff-prompt.md`](waves17-21-handoff-prompt.md), superseded by
[`docs/waves22+-handoff-prompt.md`](waves22+-handoff-prompt.md).
Wave 20: [`docs/synthetic-af-bank-followups-wave20-results.md`](synthetic-af-bank-followups-wave20-results.md).
Register: [`docs/followups.md`](followups.md).

> ## THE TIMESTAMP, AND WHAT IS STILL OPEN INSIDE IT
>
> | | |
> |---|---|
> | **artifact root** | **`2026-08-11T10:14:17.433Z`** — the row above, printed as the write-up's first action |
> | newest artifact of any kind | `controller_notes_w21.md`, `10:14:17.423Z` (it is what moved the directory's mtime) |
> | **newest artifact written by a `TestApp.exe`** | `f21b_probe.log` and the `f21b/` tree, **`10:13:45.888Z`** |
> | last artifact of a **pre-registered** measurement | `b21_score.txt`, `10:08:57.567Z` |
> | write-up | began after `10:14:17.433Z`; **every number below was re-derived after that instant**, by an agent that launched no `TestApp.exe` and wrote nothing under `gate/`, `arm/`, `f21/` or `f21b/` |
>
> Wave 19's results doc was falsified by an artifact created 33 seconds before its own commit. The mechanical fix
> adopted in wave 20 and repeated here is that the listing comes first and its timestamp is printed.
>
> **OPEN at `10:14:17.433Z`, marked in place and not reported as done:** step 12 — the **final suite by COUNT**,
> the commit, the push, the PR section and CI. `HEAD` is **`9a49aa6`** (item A) and the working tree carries two
> uncommitted files: `docs/followups.md` (the F75 entry plus this wave's owed entries, §9) and
> `docs/waves22+-handoff-prompt.md` (updated after the arm returned). The suite count quoted below, **3839**, was
> verified at item A's commit (`09:27:48Z`) and has **not** been re-verified at this timestamp.

> ## PROVENANCE
>
> | input | value |
> |---|---|
> | tree | branch `ghilios/synthetic-af-bank-followups-wave13`, PR #191. `HEAD` **`9a49aa6`**, `2026-08-11T09:27:48Z`. Two files modified and uncommitted at write-up — see the box above |
> | binary — **ONE**, never rebuilt mid-wave ([F53](followups.md)(c)) | `D:\hf_w21\exe`, built `09:11:39Z`, `Time Elapsed 00:00:22.26` (`chain_w21.log`). `TestApp.dll` sha256 **`a834abbdec15a286f754cbff78b6f74fe282af9890a4347b7f9693e9d8538105`** · `NINA.Joko.Plugins.HocusFocus.dll` sha256 **`e321ced823ee6141eb7903d575fc9da98f5377ed5da0b140d4692ecd2b9436a2`** · `BuildId` **`58b3b0828eaa494499feec0b1098822f`**, novel against **eleven** recorded ids. **A TWELFTH BINARY** |
> | the apphost, a fifth counter-example | `TestApp.exe` sha256 **`dd7103c28cc610e72671534cf23fb9a58b5a303a19d8779545cb7418d4ce6ff7`** — again byte-identical to wave 18's B1, wave 18's B2 and wave 20's, on a binary that is none of them. **An exe hash is not a binary identity; hash the dlls** |
> | **the binary does NOT contain item A** | and it could not: the build ran `09:11:39Z`, the item-A sources are stamped `09:14:19Z`, `09:14:56Z`, `09:17:13Z`. A comment and a test emit no IL, so nothing executable differs — but the design's §11 claim that *"all of it goes into the gate's binary and `G21-1` passing **is** the inertness measurement"* **did not happen as written**. §8.2 |
> | settings **S0** | `D:\hf_w11\pinned_settings_w11.json` md5 **`a67ffc06164c81613aef5c4f8324b9b8`**, re-verified at write-up. **Not re-pinned** ([F63](followups.md)(b)) |
> | profile | `astrodet` `ce3f3e63-8fd3-4b72-a0ca-d90db9441382`, pinned on the gate, the B21 arm **and** the F21 probe. **One** distinct value across all 14 `optimize` landings this wave produced |
> | detector | `DetectorVersion` **2**, read as a **FIELD** on all 14 landings ([F66](followups.md)). `strings` / `strings -el` appears nowhere |
> | concurrency | `ConcurrencyCheck = exclusive` on all 14, read **across** each arm, never from one landing |
> | suite | **3839** (`3838 + 1`), by COUNT, at item A's commit `09:27:48Z`. **The final suite is OPEN** (box above) |
> | `synth-validate` spec | `<exe>\SynthBank\synthetic-bank-spec.json`, sha256 **`bf10522e670a119e260a8b3d06c2cc6ec52ce513dde25ca9ca13f426c38672d6`**, hashed out of **this wave's own** `exe` |
> | F15 controls — all four re-run **by the write-up**, after `10:14:17.433Z` | landings **42 of 42**, aux **59 of 59**, prior-wave arms **48 of 48** across three roots, wave-20 logs **10 of 10** — byte-identical, **0 could-not-look**. §10 |
> | banks | `D:\SyntheticAutofocusBank` (20 datasets), `D:\Autofocus Bank` (19 runs) |
>
> **Independent re-derivation.** Every clause count in §1–§3 was reproduced by `/tmp/rederive_w21.py`, a block
> parser written at write-up that shares no code with `score_b21_w21.py`: it re-reads the 8 gate logs and the 6
> arm logs, re-counts the `PARAMS-DUMP` ranges, re-computes the `G6` rounding and the 55-field difference sets,
> and returns **the same numbers on every clause**, including `G21-P3b`'s failing set.

---

## Status

| item | rule | verdict | what it establishes |
|---|---|---|---|
| the gate | **RULE G21** | **PASS** — 8 of 8 to 6 dp, **bit-identical at all sixteen digits**, on a **twelfth** binary | The coordinate system is unmoved. §1 |
| the gate's reach into wave 20's code | **`G21-P1/P2/P3`** | **PASS** — `P1a`, `P1b`, `P2a–e`, `P3a` all **8 of 8** | Wave 20's `optimize/detected` dump is a property of the **code**, not of that build: a second binary, same 8-of-8. §2 |
| wave 20's tolerance defect | **`G21-P3b`** | **MATCH** — fails on exactly `{CWhiteFocus, D18_m24_deep_shed, D20_m24_bright_control, muggsie}` | The `D20-B` defect is an **observation on this wave's own artifacts**. With `P3a` at 8 of 8 the repair is a **correction, not a loosening**. §2.2 |
| the post-mutation property, **prospectively** | **RULE B21** | **`B-DEMONSTRATED`** — 6 of 6 on `V1`, `V2`, `V3`, `A`, `B`, `D` | `optimize/detected` **is** the post-mutation bundle on six datasets `D20` never touched. `B-REFUTED` was reachable and did not occur. §3 |
| the old rule | **RULE D20** | **PERMANENTLY `D-UNEVALUATED` — FENCED** | Third permanent fence, after RULE F14 and RULE S16. Not re-scored, not re-run, diagnostic not promoted. §4 |
| **A** — [F69](followups.md)(a) | ships | **SHIPPED, `9a49aa6`** | There were **three** false copies across **two** files, not the two the design named; the new test found the two the design missed. §5 |
| **C** — [F74](followups.md)'s fix | ships (drivers) | **FIXED BY CONSTRUCTION, and exercised** | The manifest carried six real paths into a scorer that rebuilt none of them — and a **fourth** name mismatch was caught in the same wave by a driver that refused. §6 |
| **P** — [F21](followups.md)'s price | — (rule-free) | **52 s, exit 0 by name, `roundsUsed=2`, second round occurred** | Four waves of "unpriced" ends. The working invocation is recorded. §7 |
| the interlock asymmetry | — | **NEW REGISTER ENTRY, [F75](followups.md)** | The wave held two standards for its two interlocks; the one the arm blocks on had no writer in any script. §8.1 |
| step 12 | — | **OPEN at `10:14:17.433Z`** | Final suite by COUNT, commit, push, CI. Box above |

> **The headline, stated narrowly.** Wave 20 measured the post-mutation property **once**, on `D12`, and then
> lost the rule that would have certified it to a filename. Wave 21 did **not** repair-and-harvest that rule. It
> fenced it, wrote a new rule with the tolerance defect corrected, and applied it to the **six factor-2 datasets
> `D20` never touched** — and the property holds 6 of 6 on all three substantive clauses. The denominator went
> from **1** to **6**, and the answer did not have to be taken from a rule that had been fixed after seeing its
> own data.
>
> **And the wave's own interlocks were not symmetric.** `B21_ARM_READY` is driver-written under an explicit
> *"never hand-write the marker"*; `G21_PASSED` — **the one the arm actually blocks on** — was a controller
> `printf` named only in the plan. Nothing in the wave coupled it to the check it attests. §8.1.

---

## §1 — RULE G21: **PASS**, on a twelfth binary

Eight values, `optimize --per-run --max-evals 250`, both pins named in the driver header, sequential, one
`TestApp.exe`, no NINA, no fan-out. `G21_START 09:12:51Z → G21_DONE 09:55:18Z` = **42 m 27 s** against a 42 m
estimate (`gate_w21.log`).

### 1.1 The eight values

Re-derived at write-up with `python3 /mnt/d/hf_w12/score_w12.py /mnt/d/hf_w21/gate --rule G21`:

| run | `BestJ` (exact, 16 digits) | 6 dp | K8 expected | bit-identical? | `BaselineJ` | wave 11 | wall |
|---|---|---|---|---|---|---|---|
| `toml999` | `0.9957838768299878` | 0.995784 | 0.995784 | **YES** | 0.983477 | 0.983477 | 1 m 59 s |
| `CWhiteFocus` | `0.9960675916058808` | 0.996068 | 0.996068 | **YES** | 0.994320 | 0.994320 | 8 m 36 s |
| `uneven` | `0.9963677194179505` | 0.996368 | 0.996368 | **YES** | 0.991794 | 0.991794 | 6 m 12 s |
| `muggsie` | `0.9971948738498605` | 0.997195 | 0.997195 | **YES** | 0.993288 | 0.993288 | 1 m 07 s |
| `mccomiskey` | `0.9767460801208465` | 0.976746 | 0.976746 | **YES** | 0.846082 | 0.846082 | 10 m 26 s |
| `D18_m24_deep_shed` | `0.9998815090506263` | 0.999882 | 0.999882 | **YES** | 0.997405 | 0.997405 | 6 m 09 s |
| `D19_cygnus_deep_shed` | `0.9994870586135448` | 0.999487 | 0.999487 | **YES** | 0.999187 | 0.999187 | 5 m 53 s |
| `D20_m24_bright_control` | `0.9997378027339423` | 0.999738 | 0.999738 | **YES** | 0.999454 | 0.999454 | 2 m 05 s |

`>>> RULE G21 A1: PASS, 8 of 8 to 6 dp. bit-identical to K8 on 8 of 8 (all 16 digits).`

### 1.2 The free controls, and the instrument repair that made them quotable

`prov_w21.py --self-test /mnt/d/hf_w21/gate`, re-run at write-up, **three directions**:

| direction | result |
|---|---|
| **1** the real arm | **PASS** — `BuildId` one value, novel against 11; `DetectorVersion` `2` ×8 as a FIELD; `ProfileId` pinned ×8 by containment **and** cardinality; `FitInputs` one value; `ConcurrencyCheck` `exclusive` **across** the arm |
| **2** a `copytree` with `muggsie`'s `ProfileId` rewritten | **FAIL as expected, on BOTH clauses** — the pin clause and the cardinality clause, each named, with the mutation asserted by read-back |
| **3** the report's own count against the table it describes | **`printed=11   len(PRIOR_BUILD_IDS)=11   AGREE`** |

Direction 3 is wave 20's defect repaired: `prov_w20.py` printed *"novel against **nine** recorded ids"* while its
table held **ten**. The word is now derived from `len(PRIOR_BUILD_IDS)` and the self-test re-reads its own printed
report and compares. *A message and its mechanism are two channels, and only one of them had ever been checked.*

### 1.3 What `G21` does not prove

Nothing beyond `FinalJ` and `BaselineJ` about the landing — [F73](followups.md) owns that, on a population of
C#-identical builds, and the code axis is still unmeasured. It runs at `MaxOutlierRejections = 0`, so it says
nothing about the rejection path. **And it does not reach item A at all**, because item A is a comment and a test
and was not even in this binary (§8.2). The check that reaches item A is the suite.

---

## §2 — `G21-P1/P2/P3`: **8 of 8 on every clause**, and the FAIL end on real artifacts

`score_b21_w21.py --gate /mnt/d/hf_w21/gate` → `g21_p2p3.txt`, written `09:59:30.366Z`, exit **0** read directly.

| clause | threshold, fixed in the design | measured |
|---|---|---|
| `G21-P1a` | `optimize/seed.NoiseReductionRadius == 3`, asserted **in the driver** | **8 of 8** |
| `G21-P1b` | `optimize/baseline.NoiseReductionRadius == 4` ([F70](followups.md) corroboration) | **8 of 8** |
| `G21-P2a` *reach* | `PARAMS-DUMP optimize/detected BEGIN` exactly once per log | **8 of 8** |
| `G21-P2b` *shape* | 55 field lines and 55 unique names | **8 of 8** |
| `G21-P2c` *post* | `detected.PixelScale` finite **and** `baseline.PixelScale` the literal token `NaN` | **8 of 8** |
| `G21-P2d` *honest* | `detected.DetectionBinning == 1` — an **equality**, because factor is 1 on all eight | **8 of 8** |
| `G21-P2e` *no clash* | `optimize/baseline` and `optimize/seed` still exactly once each | **8 of 8** |
| `G21-P3a` | `detected.PixelScale` rounds to the console's `G6` value **exactly**, at the console's own precision | **8 of 8** |
| `G21-P3b` | wave 20's `D20-B` (`abs Δ < 5e-7`) fails on exactly `{CWhiteFocus, D18_m24_deep_shed, D20_m24_bright_control, muggsie}` | **MATCH**, set equality |

**The FAIL end, same scorer, same minute, on real prior-wave artifacts.** `score_b21_w21.py --gate
/mnt/d/hf_w19/gate` → `w19end.txt`, exit **1**, byte-identical to the write-up's re-run:

```
G21-P1a  8 of 8      G21-P2a  0 of 8      G21-P2e  8 of 8      G21-P3a  0 of 8
G21-P1b  8 of 8      G21-P2b  0 of 8
                     G21-P2c  0 of 8      G21-P3b returned [], expected the four -- RULE B21 is NOT scored
                     G21-P2d  0 of 8
```

**The FAIL end is selective, and that is what makes it a control.** `P1a`, `P1b` and `P2e` — the three clauses
that read blocks wave 19's binary *does* have — stay **8 of 8** on the failing root. Only the clauses that read
`optimize/detected` collapse to 0. A scorer that returned zeros everywhere would prove nothing about which code
is present.

**What `G21-P2` buys that wave 20 could not buy.** Wave 20 measured its `optimize/detected` dump on **one**
binary. `G21-P2` measures it on a **second**, built from a different tree, with the same 8-of-8 result — the
first evidence that the dump is a property of the *code* rather than of that build.

### 2.1 `G21-P3b` — the F68 defect measured, not argued

The pre-registration predicted, at zero compute and from wave 20's frozen logs (fingerprint class 4), that wave
20's `D20-B` predicate would fail on exactly the four datasets whose `PixelScale >= 1`. On **this wave's own gate
logs**, produced by a different binary a day later, it fails on exactly those four:

| dataset | console (`G6`) | dump | Δ | `D20-B` (`5e-7`) | `G21-P3a` (corrected) |
|---|---|---|---|---|---|
| `CWhiteFocus` | `1.32574` | `1.3257361905796277` | `3.809e-6` | **FAIL** | pass |
| `D18_m24_deep_shed` | `1.4101` | `1.4101012208892405` | `1.221e-6` | **FAIL** | pass |
| `D20_m24_bright_control` | `1.4101` | `1.4101012208892405` | `1.221e-6` | **FAIL** | pass |
| `muggsie` | `1.03356` | `1.033556334333394` | `3.666e-6` | **FAIL** | pass |
| `D19_cygnus_deep_shed` | `0.775556` | `0.7755556714890822` | `3.285e-7` | pass | pass |
| `mccomiskey` | `0.288525` | `0.28852517540516454` | `1.754e-7` | pass | pass |
| `toml999` | `0.73944` | `0.7394398714518549` | `1.286e-7` | pass | pass |
| `uneven` | `0.711519` | `0.7115189646688828` | `3.533e-8` | pass | pass |

`G6` is **six significant digits**. Below 1.0 the `5e-7` tolerance *equals* the instrument's quantum; at or above
1.0 the quantum is `5e-6`, **ten times the tolerance**. So `D20-B` fails a correct program, and it does so on 4 of
8 real logs in two different waves.

**Why this ordering matters.** `G21-P3a` — the repaired clause, which rounds the dump to the console's own
precision — is **8 of 8 on the same eight logs**. A repair that widened a tolerance until failures disappeared
would show up here as a clause that passes everything including genuine disagreements; self-test branch 18
constructs a genuine disagreement and the corrected rule **fails** it, and branch 17 shows both rules agree below
1.0. *The repair is a correction, not a loosening, and the evidence is on this wave's artifacts rather than in
its prose.*

---

## §3 — RULE B21: **`B-DEMONSTRATED`**, 6 of 6, on a population RULE D20 never touched

`b21_score.txt`, written `10:08:57.567Z`, exit **0** read directly (not through a pipe). The write-up's re-run is
byte-identical to it.

### 3.1 The population, and why the denominator is itself controlled

`synthetic_meta.json`'s `expectedOptimal.detectionBinning` is **2** on exactly **7 of the 20** synthetic datasets
— re-counted at write-up over the bank's own files: `D08, D09, D10, D12, D14, D15, D17`. Wave 20 measured `D12`.
RULE B21's population is **the other six**, fixed at pre-registration, ordered cheapest first (which was also the
drop order):

| dataset | on-disk MB | wall | expected `detected.PixelScale` (pre-registered) | measured |
|---|---|---|---|---|
| `D10_rc16_3250mm_sparse` | 156 | 51 s | `0.4772650286` | `0.477265028608666` |
| `D17_cdk14_oiii5` | 163 | 23 s | `0.6051936570` | `0.6051936570340087` |
| `D09_c14_3800mm` | 206 | 28 s | `0.5026347647` | `0.5026347646968716` |
| `D08_c11_2800mm` | 450 | 46 s | `0.5539683368` | `0.5539683367779159` |
| `D15_cdk20_3454mm_e47` | 450 | 101 s | `0.4490768219` | `0.4490768219392485` |
| `D14_cdk14_2563mm_e47` | 1057 | 255 s | `0.6051936570` | `0.6051936570340087` |

**All six matched the value predicted from `arcsecPerPixel × captureBinning × 2` before the arm ran.** Those
`synthetic_meta.json` files are fingerprint class 2 and are **59 of 59 byte-identical** at write-up, so the
denominator is under a control (§10).

`D17_cdk14_oiii5` — the known-hazardous member, which finds zero stars at short exposures — was included on
purpose, with a validity floor sized to absorb it. It ran in 23 s and scored.

`B21_START 10:00:22Z → B21_DONE 10:08:46Z` = **8 m 24 s**, against a 15–25 m estimate and an `11:15Z` deadline
the driver would have enforced per dataset. The whole arm finished **before the plan's own window opened**.

> **A correction to the controller's notes.** They read *"four datasets in the first 102 seconds"*. The log's
> own timestamps give **three** in the first 102 s (`10:00:22Z → 10:02:04Z`); the fourth completed at `10:02:50Z`,
> 148 s in. The 8 m 24 s total is confirmed and the per-dataset splits above sum to 504 s exactly.

### 3.2 The clauses

| clause | what it demanded | measured |
|---|---|---|
| **`B21-V1`** *addressability* | `B21_ARM_READY` exists **and carries a `manifest=` line**; the manifest parses as 4-column TSV; every row's `log` and `landing` **resolve as written**; `>= 4` of 6 scoreable | marker names `manifest=/mnt/d/hf_w21/b21_manifest.tsv`, `rows=6`, `written=2026-08-11T10:08:46Z`; **6 of 6 scoreable** |
| **`B21-V2`** *one binary, pins held* | across the manifest's landings: `BuildId` cardinality 1 **and equal to the gate's**; `ProfileId` contains the pin and cardinality 1; `DetectorVersion == 2` as the FIELD; `ConcurrencyCheck == exclusive` on all | **PASS.** Independently re-read at write-up: `BuildId {58b3b0828eaa494499feec0b1098822f}` (= the gate's), `DetectorVersion {2}`, `ConcurrencyCheck` `exclusive` ×6, `ProfileId {astrodet (ce3f3e63-…)}` |
| **`B21-V3`** *the population really is factor 2* | `detection binning (F39b): 2 from …` exactly once per log, from the **console line, not the dump** | **6 of 6 confirmed at factor 2**; nothing removed |
| **`B21-A`** *the decisive one* | `detected.DetectionBinning == 2` **and** `baseline.DetectionBinning == 1`, **in the same log** | **6 of 6** |
| **`B21-B`** *the scale followed* | `detected.PixelScale` finite, `baseline.PixelScale` the token `NaN`, and the dump rounds to the console's `G6` value exactly | **6 of 6** |
| **`B21-D`** *the whole difference set* | the field-by-field difference between `optimize/baseline` and `optimize/detected` is **exactly** `{DetectionBinning, PixelScale}` over 55 fields | **6 of 6** |

```
>>> RULE B21 VERDICT: B-DEMONSTRATED
```

Branch table (design §4.3), applied as written: `V1..V3` pass and `A`, `B`, `D` are all-of over the scoreable set
⇒ `B-DEMONSTRATED`. **`B-REFUTED` was reachable by construction** — self-test branches 2 and 3 drive the scorer
into it — **and did not occur.**

### 3.3 A reading trap, named because it is easy to fall into

`factor=2` appears on each of the six per-dataset lines in `b21_arm_w21.log`. **That is `B21-V3`'s population
check, not clause `B21-A`.** V3 says the dataset really is a factor-2 member of the bank; `A`, `B` and `D` are
about what the `optimize/detected` block contains. **Six `factor=2` lines are not six passes of `A`.** The six
passes of `A` are in `b21_score.txt`, quoted above, and were reproduced by the independent parser.

### 3.4 What B21 does **not** demonstrate — measured, not asserted

The design stated in advance that **all six datasets have a binned scale below 1.0**, so the corrected `B21-B`
and the broken `D20-B` return the **same answer on this whole population**, and the tolerance repair is therefore
*not* demonstrated by this arm. The write-up's independent parser confirms it: applying the `D20-B` predicate to
the six arm logs gives a **failing set of `[]`** — the broken rule passes all six. The repair is reached by
`G21-P3` on the gate (where four datasets sit above 1.0) and by self-test branches 15–18, and by nothing else.

Also unchanged: B21 says nothing about whether the resolved factor is *correct*, only that the dump is the
post-mutation bundle; and it ran at `MaxOutlierRejections = 0` like every arm in this series.

### 3.5 What it buys, stated precisely

Wave 20 saw the property **once**, on `D12`, and that observation is fenced inside a `D-UNEVALUATED` rule. B21
asked the same question of a **disjoint population of six**, selected at pre-registration from files under a
fingerprint control. The answer: `optimize/detected` **is** the post-mutation bundle on all six — binning moves
`1 → 2`, `PixelScale` moves `NaN →` finite and agrees with the console at the console's own precision, and the
difference from `optimize/baseline` is **exactly** `{DetectionBinning, PixelScale}` across 55 fields. Nothing
else in the bundle moved on any of the six.

The honest discount, which the design also stated in advance: **B21 was expected to pass.** Its value is in the
denominator (6 rather than 1) and in `B21-D` on six *new* datasets — five of which have `captureBinning = 1`,
unlike `D12` — where the result was genuinely not known beforehand.

---

## §4 — RULE D20 is **FENCED**. Design §1.4, verbatim

RULE D20 was **not** re-scored, **not** re-run, and its wave-20 diagnostic is **not** promoted. This is the third
permanent fence in the series, after **RULE F14** (charter §3) and **RULE S16** (charter §3a). The text below is
design §1.4 as committed at `3a4db7c`, before any wave-21 measurement, and it goes into the register verbatim:

> **RULE D20 IS PERMANENTLY `D-UNEVALUATED`. DO NOT HARVEST IT.**
>
> 1. Its measurement is published as a diagnostic; no re-run of the same probe on the same dataset can be blind.
> 2. `D20-B`'s tolerance is finer than its own instrument's resolution and **is observed to fail a correct
>    program on 4 of 8 real logs** — so repairing it now is repairing a rule after the data.
> 3. A wave that repairs a failed gate and immediately harvests its verdict teaches the next wave to do the same.
>
> **The diagnostic is never promoted to a verdict by hand, by this wave or any later one.** What a later wave
> may cite is RULE B21's verdict, on RULE B21's population.

Reason 2 stopped being an argument this wave: `G21-P3b` **observed it again**, on wave 21's own logs (§2.1). The
option the fence leaves open — a new rule, a new population, applied prospectively — is exactly what RULE B21
did, and it returned a verdict without anyone having to re-decide a broken clause.

---

## §5 — Item A: [F69](followups.md)(a) is **CLOSED**, and there were **three** copies, not two

Committed `9a49aa6`, `2026-08-11T09:27:48Z`. **Suite 3839** (`3838 + 1`), by COUNT.

The design named one surviving false copy, at `OptimizationDiagnosticRunner.cs:591-594`. The test written to make
the class mechanical found **two more**, one of them in a file [F69](followups.md) has never mentioned:

| site | what it said |
|---|---|
| `OptimizationDiagnosticRunner.cs:591-594` (the XML doc, the one the design named) | *"**No-op unless** `--apply-run-detection-binning` was passed…"* |
| `OptimizationDiagnosticRunner.cs:562` (the field comment) | *"`--apply-run-detection-binning`: F39(b); **false => bit-identical**"* |
| `HarnessSettingsStore.cs:419` (a **different file**) | *"**Used by** `optimize --apply-run-detection-binning`"* |

All three are **paraphrases sharing only the identifier**. The behaviour is `!HasFlag(args,
"--no-run-detection-binning")` at `:216` — on by default since wave 8.

**This is the counter-example that overrides wave 20's Lessons #4.** That lesson read *"when a register entry
names where a false statement lives, the fix is `grep` for the sentence, not an edit at the address."* A grep for
the sentence finds **none** of the two extra copies, because they share no sentence — only the flag name. The
rule that works, and that this wave shipped as executable rather than as prose:

> **Grep for the IDENTIFIER the false claim is about, read every hit — then leave a test that does it for you.**

The test, `ApplyRunDetectionBinning_EveryCommentNamingTheOptInFlag_AlsoNamesTheOptOut` in `ParamsDumpTests`:
every comment **block** in every `*.cs` under `TestApp/` that names `--apply-run-detection-binning` must also name
`--no-run-detection-binning`; it prints its population (**5 blocks over 49 files**) and asserts `>= 2`; a **zero**
population fails with *"the identifier this test is about is gone from TestApp's comments; it can no longer see
its subject"* rather than passing vacuously.

**Two RED demonstrations were run and observed, not asserted** (commit message, `9a49aa6`): restoring the `:591`
sentence turns it red and names the block **with the population still 5** — the block *moves* from the satisfying
set to the offending set rather than vanishing, which is what makes the count meaningful; and inserting a
one-line opt-in-only comment into a previously uninvolved file (`DiagnosticUtil.cs`) also turns it red and names
that file, so the test guards future additions and not only today's three. `git status` at write-up shows
`DiagnosticUtil.cs` clean, confirming the mutant was removed.

---

## §6 — Item C: [F74](followups.md) fixed **by construction**, and exercised by a real arm

| half | what shipped | evidence it worked |
|---|---|---|
| **strong — by construction** | `b21_arm_w21.sh` asserts each artifact exists, then **writes its path** into `b21_manifest.tsv` (`dataset  factor  log  landing`); the marker carries `manifest=<path>`; `score_b21_w21.py` opens the marker, reads the manifest path **out of it**, and reads every artifact path **out of the manifest's rows**. **No `os.walk` fallback, deliberately** | The scorer resolved **6 of 6** logs and **6 of 6** landings on the first attempt, and self-test branch **7** proves it scores a deliberately **flat** layout — because the path came from the manifest and not from a template |
| **weak — by convention** | one `layout_w21.sh` declares every layout and both drivers source it | self-tested through both drivers |

Two failure directions are self-tested rather than assumed: branch **8** — a manifest path that does not resolve
is `COULD-NOT-LOOK` **and the manifest's own string is quoted**; branch **10** — a marker carrying no `manifest=`
is `COULD-NOT-LOOK`, not an existence check.

**The reinforcement the pre-registration itself supplied.** Building `prov_w21.py`'s known-good direction required
a `copytree` of wave 20's gate with every `BuildId` rewritten; the first attempt rewrote **6 of 8** landings and
**reported success**, because the glob `*/attempt01/optimized_settings.json` is wrong for the two real-bank runs,
which nest one level deeper (`CWhiteFocus/AutoFocus_20220429_000244_attempt01/`). It was caught by asserting the
count. *A path template got it wrong again, inside the pre-registration written to fix path templates* — which is
why `layout_w21.sh`'s landing helper is a `find` and not a `printf`.

**And a fourth instance occurred during execution and was caught by a driver that refused** — see §8.3. That is
the first time in this series the F74 family has been stopped by a precondition instead of discovered in a
post-mortem.

---

## §7 — Item P: the [F21](followups.md) price is **52 seconds**, and a second round happened

Rule-free by pre-registration, hard-timeboxed at `timeout 1200`, pinned like every other arm. `f21_chain.log`:

| deliverable | value |
|---|---|
| **wall time** | **52 s** — `F21_START 10:09:19Z → F21_DONE 10:10:11Z` |
| **exit code, BY NAME** | **0** — *"the instrument ran."* Not 124 (the timebox), not 2 (wave 20's missing `--spec`) |
| **round count** | `maxRounds=2`, **`roundsUsed=2`** |
| **did a second round occur at all** | **Yes** — two `round_*/attempt01/` trees, **9 `.fits` frames each**, on disk under `f21/D11_rc10_585_afbin2/S1/` |
| dataset / scenario | `D11_rc10_585_afbin2` (38 MB, the bank's smallest — chosen for **price**, not physics) / `S1`, *"step ×0.25 — multi-round convergence; WasCapped true early"* |

The working invocation, which is the durable half of this item:

```bash
timeout 1200 "$EXE" synth-validate \
  --spec 'D:\hf_w21\exe\SynthBank\synthetic-bank-spec.json' \
  --out 'D:\hf_w21\f21' \
  --datasets D11_rc10_585_afbin2 --scenarios S1 --max-rounds 2 \
  --settings 'D:\hf_w11\pinned_settings_w11.json' \
  --profile-id ce3f3e63-8fd3-4b72-a0ca-d90db9441382
```

**Four waves of "the instrument is unpriced" end here.** Wave 20's finding — that the blocker was `--spec`, not
compute and not the population — was correct, and the price is 52 s for one dataset × one scenario × two rounds.

### 7.1 The probe's own reader printed four `None`s, and three of them are the probe's bug

The driver instructed: *"if the two lines above are empty, the fields are named differently in this build: SAY
SO, and record the field names actually present rather than inventing a reading."* Doing exactly that, from
`f21/synth_validate_report.json`:

| the probe printed | why | the field that actually exists |
|---|---|---|
| `dataset=None scenario=None` | it looked for `dataset` / `scenario` | **`datasetId`** / **`scenarioId`** |
| `round N: HalfWidth=None step=None` | it scanned **flat** keys; these are **nested** | `rounds[N].stepRecommendation.{halfWidth, stepSize}` |
| `assertions: None -> None` | it looked for `name` / `passed` | `terminal.assertions[].{id, verdict, detail}` |

*The per-round data was there the whole time.* This is F74's family — a reader and a writer disagreeing about a
name — in the **probe's reader**, not in the product. Read out properly:

| round | bootstrap step | recommends step | `halfWidth` | `wasCapped` | `cappedGrowthRatio` | seed |
|---|---|---|---|---|---|---|
| 0 | 14 | **24** | 84.0 | **True** | 1.7142857142857142 | `-25215000` |
| 1 | 24 | **41** | 144.0 | **True** | 1.7142857142857142 | `1539450862` |

`terminal`: `converged=True`, `roundsUsed=2`, `finalStepSize=41`, `expectedStepSize=55`,
`deltaStepVsExpected=-14`, `stepTheory=55`, `stepBehavioral=55` (delta fraction `0.0`), `stepToleranceBand=22.0`,
`overallVerdict=0`, `stoppedReason=` **`converged (step 41 within the 22 tolerance band of step_behavioral 55)`**.
The three terminal assertions (`A2` step, `A2` exposure, `A3`) all read `verdict=0`; `A3`'s detail is *"final step
41 within `[33,88]` = `[0.6,1.6]x` step_behavioral (55)"* — note that `A3`'s band is **multiplicative** and wider
than the `±22` band named in `stoppedReason` (`[33,77]`). Two different bands, both satisfied.

### 7.2 An unregistered, rule-free follow-on: the stop is real, and the controller's suspicion was wrong

**Status: this is NOT item P and it carries NO rule.** Item P's deliverables were complete and recorded before it
was conceived; it wrote to a fresh `/mnt/d/hf_w21/f21b` and `f21/` was not rebuilt. Nothing it returns can move a
pre-registered verdict, because no pre-registered rule covers it.

The suspicion, written down **before** the follow-on ran (controller notes §4b): the run declared `converged` at
`roundsUsed == maxRounds == 2`, while `wasCapped` was `True` in both rounds and the step was still climbing at a
pinned 1.714× (`14 → 24 → 41`). A `±22` band around 55 spans `[33, 77]`, and 41 enters it **on the way up** — so
"converged" and "ran out of rounds inside the band" were not distinguished. The prediction: **(a)** genuine
convergence ⇒ stops again at `roundsUsed=2`; **(b)** a still-growing step ⇒ `roundsUsed > 2` and the step climbs
past 55.

`--max-rounds 5`, everything else identical, **exit 0**, wall **≈45 s** (`f21b_probe.log` first line `10:13:01.44Z`,
last `10:13:45.42Z`, file closed `10:13:45.888Z`; there is no `F21B_START/DONE` pair, because this run was made
without the probe driver's chain wrapper):

```
roundsUsed=2   converged=True
stoppedReason=converged (step 41 within the 22 tolerance band of step_behavioral 55)
finalStepSize=41   expected=55   delta=-14   overallVerdict=0
  round 0: bootstrap=14 -> step=24  halfWidth=84.0   capped=True  ratio=1.7142857142857142
  round 1: bootstrap=24 -> step=41  halfWidth=144.0  capped=True  ratio=1.7142857142857142
```

**Given three more rounds it did not take them. Prediction (b) is refuted; the stop is a convergence test firing,
not `maxRounds` running out.** That is the finding, and it cost 45 seconds.

> ### The reproducibility half of the claim does NOT hold, and this document corrects it
>
> The controller's notes conclude that the two runs had *"different per-round seeds"* and that the trajectory is
> therefore seed-independent. **The artifacts say otherwise.** `f21b/synth_validate_report.json` records
> `round 0 seed = -25215000` and `round 1 seed = 1539450862` — **the same two seeds as `f21`**. The seeds differ
> *between rounds within* a run; they do not differ *between* the two runs, because they are derived from the
> spec and scenario rather than drawn per invocation.
>
> Stronger still, and checked at write-up: the rendered frames are **byte-identical** across the two runs
> (`sha256` of frames 00/04/08 in both rounds — six pairs, six matches), and the two `synth_validate_report.md`
> files differ **only** in `Generated:` and `Out:`.
>
> So what the follow-on establishes is **bit-exact determinism at identical inputs**, plus the refutation of
> prediction (b). It does **not** establish that the trajectory is insensitive to the seed — **nothing in this
> wave varied a seed**. That measurement is still owed, and it costs ~45 s if `synth-validate` ever exposes a
> seed override.

---

## §8 — Deviations and slips, recorded

### 8.1 The two interlocks were held to two standards — [F75](followups.md), new

At `09:56:29Z` the arm aborted on its first launch: *"`ABORT: /mnt/d/hf_w21/G21_PASSED does not exist. The gate is
the stopping gate; the arm does not run before it.`"* `grep -rn G21_PASSED /mnt/d/hf_w21/*.sh /mnt/d/hf_w21/*.py`
returns **exactly one hit — the arm's own `[ -f … ] || ABORT`**. A reader and no writer.

| marker | written by | the plan says |
|---|---|---|
| `B21_ARM_READY` | the **driver**, last, only if `>= 4` manifest rows exist | *"**Never hand-write the marker.**"* |
| `G21_PASSED` | a **controller `printf`**, specified only in the plan at line **173** | *"If `G21-1` is 8 of 8, write the marker the arm requires"* |

The second is the one the arm blocks on. **The controller's first reading of this was itself wrong** — it recorded
"an interlock with no writer at all", when the writer *was* named, in the plan; the grep had covered `*.sh` and
`*.py` and not the plan. *A step a human performs is still a writer; it is just the weakest kind.* **Grep the plan
too.**

**The checklist WAS run before the marker was written, and the file mtimes prove it independently of anyone's
word:** `st.txt` (`--self-test`, 20 of 20) `09:59:30.080Z`, `w19end.txt` (the FAIL end on `/mnt/d/hf_w19/gate`,
exit 1) `09:59:30.240Z`, `g21_p2p3.txt` (the PASS end, exit 0) `09:59:30.366Z` — and `G21_PASSED` **`10:00:12.974Z`**,
42 s later, carrying `BuildId=58b3b0828eaa494499feec0b1098822f`. All three exit codes were read from `$?`
directly, not through a pipe (wave 18's lesson). [F66](followups.md) is satisfied for this scorer: both ends
demonstrated, the FAIL end on real artifacts.

**The finding is not that the marker was forged — it was not. It is that nothing prevented it from being.** The
repair is one line and it belongs in the code that already knows the answer: `score_b21_w21.py --gate` computes
`rc` and already reads the gate `BuildId`, so **it** should write the marker on `rc == 0` — which also means the
FAIL end cannot leave one behind. Register entry written; §9.

### 8.2 The binary was built **before** item A, so the design's inertness claim did not happen as written

Design §10 orders item A (step 1) → suite (step 2) → build (step 3). The controller built first: `chain_w21.log`
records `=== BUILD 09:11:39Z ===`, while the item-A sources are stamped `HarnessSettingsStore.cs 09:14:19Z`,
`ParamsDumpTests.cs 09:14:56Z`, `OptimizationDiagnosticRunner.cs 09:17:13Z`, and the commit lands at `09:27:48Z`.

**Consequence, stated rather than smoothed:** design §11 claims *"all of it goes into the gate's binary and
`G21-1` passing **is** the inertness measurement."* It did not. Item A is **not in the gate binary at all**. The
substantive defence is sound — a comment and a test emit no IL, so no reachable behaviour could differ — but the
inertness *measurement* the design promised was not made, and the correct statement is the weaker one: **the gate
is a control on the binary, and the binary predates the change; the check that reaches item A is the suite.**

Item A's work (edits, two RED demonstrations, the 3839-count suite) also ran **while the gate was in flight**
(`09:12:51Z → 09:55:18Z`). `dotnet test` launches no `TestApp.exe`, so the gate's exclusivity precondition
([F55](followups.md)) was not violated, and `G21-1` reproducing **bit-identically at sixteen digits** is the
evidence that the concurrency did not perturb the measurement. Recorded because it was not planned.

### 8.3 The gate's **first** launch aborted on a filename — F74's family, caught by a precondition

Not in the controller's notes; found at write-up in `chain_w21.log`:

```
=== RULE G21 09:12:11Z ===
  driver exit=3
   python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --write /mnt/d/hf_w21/bank_landing_fingerprint_BEFORE.json
   python3 /mnt/d/hf_w17/aux_fingerprint_w17.py  --write /mnt/d/hf_w21/bank_aux_fingerprint_BEFORE.json
   python3 /mnt/d/hf_w19/arm_fingerprint_w19.py  --write /mnt/d/hf_w21/w18_arm_fingerprint_BEFORE.json
   python3 /mnt/d/hf_w21/log_fingerprint_w21.py  --write /mnt/d/hf_w21/w20_log_fingerprint_BEFORE.json
CHAIN STOP: gate did not finish
```

Those four lines are the body of `gate_w21.sh:178-186`'s **missing-fingerprint ABORT**, echoed verbatim. The cause
is a **name mismatch**: the chain wrote the class-3 fingerprint as `prior_arm_fingerprint_BEFORE.json`
(mtime `09:12:11Z`), while the gate driver's `$FP_W18` reads `w18_arm_fingerprint_BEFORE.json`. The two files are
**byte-identical in content** (verified at write-up) — only the names disagreed. The correctly-named copy appears
at `09:12:48Z`, and the gate started `09:12:51Z`. **Cost: 40 seconds.**

**This is the F74 family's fourth instance in two waves, and the first one a driver caught before it could do
damage.** Wave 20's version silently cost RULE D20 its verdict. This one aborted with exit 3, named the four
files it wanted, and refused to run an arm whose controls did not exist. *A reader that refuses beats a reader
that guesses, and both beat a reader that falls back to the same wrong parent.*

### 8.4 The arm's `--self-test` ran **after** the arm launched

The plan's step 6 orders `b21_arm_w21.sh --self-test` **then** the arm. The controller launched the arm at
`10:00:22Z` and ran the self-test at ~`10:02Z`, mid-flight; it returned *"SELF-TEST PASS (population verified
against the BANK, layout and manifest round-tripped)"*. It was safe — the self-test launches no `TestApp.exe` and
only reads — **but a self-test that runs after the thing it guards is not a guard.** Had it failed, the arm would
already have been four datasets deep. Recorded as a deviation, not laundered into *"the self-test passed."*

> **Two of these four have no surviving artifact**, and the doc says so rather than implying otherwise. The
> `09:56:29Z` abort (§8.1) and the mid-flight self-test (§8.4) are quoted from the controller's notes; the arm
> driver is launched with `> b21_arm_w21.log`, so the aborted launch's output was **truncated away** by the
> successful relaunch, and the self-test was not redirected to a file. §8.1's *ordering* claim, by contrast, is
> confirmed by file mtimes and does not rest on anyone's word.

---

## §9 — Register entries this wave owes, and makes

All are written into [`docs/followups.md`](followups.md) in the same working tree as this document, uncommitted at
its timestamp.

| entry | what changed |
|---|---|
| **[F69](followups.md)(a)** | **CLOSED** (`9a49aa6`). Three false copies across two files, not the one the design named. Carries the corrected *"grep the IDENTIFIER, then leave a test"* lesson, which **overrides wave 20's Lessons #4** |
| **[F74](followups.md)** | **Fixed by construction** in `b21_arm_w21.sh` + `score_b21_w21.py`, and **exercised by a six-dataset arm**. Records the pre-registration's own instance (the 6-of-8 landing glob) as reinforcement, and §8.3's caught-by-a-precondition instance as the first stop |
| **[F68](followups.md)** | Two new clauses on the standing discipline: **check the instrument's PRECISION** (`D20-B` vs `G6`, now twice-observed) and **check the population is ADDRESSABLE** (`B21-V1` is that check; F74 is what happens without it) |
| **RULE D20** | **FENCED**, design §1.4 verbatim, as a Process entry. Third permanent fence after RULE F14 and RULE S16 |
| **[F21](followups.md)** | The **working invocation** and the **45–52 s** price; the real report field names; `roundsUsed=2` with a second round on disk; the determinism result and the explicit note that **seed sensitivity is still unmeasured** |
| **[F59](followups.md)** | Re-affirmed as **rejected on wave 20's measurement**, with the price named: four of the five knobs are preset-owned and inert under the pin, and the fifth (`SaturationThreshold`) **would** bind — making it a coordinate-system move that owes a fresh **42 m** baseline. This wave's gate took **42 m 27 s**, so the estimate is now a measured price |
| **[F75](followups.md)** | Written by the controller before this document; §8.1 is its evidence, and this document adds the mtime corroboration of the ordering |

---

## §10 — Controls

All four fingerprint classes were written **before** the gate (`chain_w21.log`, `09:12:02Z → 09:12:48Z`) and
re-checked **by the write-up**, after `10:14:17.433Z`:

| class | population | count | result |
|---|---|---|---|
| **1** bank landings | `D:\SyntheticAutofocusBank` + `D:\Autofocus Bank` | **42** | **42 of 42 BYTE-IDENTICAL**, 0 changed / added / removed / could-not-look. Eighth consecutive clean wave |
| **2** bank aux — **evidence** | `harness_settings.json` ×39 + `synthetic_meta.json` ×20 | **59** | **59 of 59 byte-identical**; kind counts re-asserted `{harness_settings.json: 39, synthetic_meta.json: 20}`. These 20 files **select RULE B21's population**, so this class is a denominator under control, not preservation |
| **3** prior-wave arms | `/mnt/d/hf_w18/{seedA0, seedA1, gate}` | **48** | **48 of 48 BYTE-IDENTICAL across all three roots** (20 / 20 / 8). The design promised not to spend `seedA1`; this is that promise in executable form |
| **4** wave-20 logs — **evidence** | `/mnt/d/hf_w20/gate/*.log` ×8 + `dprobe/probe.log` + `dprobe/D12_c14_585_afbin2/run.log` | **10** | **10 of 10 BYTE-IDENTICAL**. `G21-P3b`'s expected set and `G21-P2`'s two ends were derived on these files at pre-registration time |

No arm in this wave wrote to the bank, and no prior wave's arm directory was rebuilt.

---

## §11 — What was NOT run, and what it costs

| candidate | status | price |
|---|---|---|
| **re-running RULE D20** | **rejected — fenced**, §4 | n/a. The question was answered by RULE B21 on a new population instead |
| **[F45](followups.md)(b)'s production plumbing** | rejected on time **and** on the RULE S16 fence (charter §3a) | ~3–4 h against a 3 h 25 m wave |
| **[F59](followups.md)'s five knobs** | rejected on wave 20's measurement, not re-derived | 4 of 5 are preset-owned and inert; the fifth owes a fresh **42 m** baseline — this wave's gate took 42 m 27 s |
| **a `DetectionBinning`-differs clause on the gate** | not written — **unsatisfiable** there (factor 1 on all eight). Recorded because writing it would have looked like rigour | 0 |
| **the NINA UI checks A1, A2, A4–A8** | not scheduled — every arm this wave was pinned ([F72](followups.md)) | ~30 m on a session with no pinned arm |
| **out-of-sample pass over wave 18's 40 arm landings; [F73](followups.md)'s code axis; [F70](followups.md)(b′)** | not scheduled | ~53 m and ~20 m; priced in the wave-22 handoff |
| **seed sensitivity of `synth-validate`** | **newly owed**, §7.2 — the follow-on measured determinism, not seed independence | ~45 s if a seed override exists; unknown if it does not |
| **the final suite by COUNT, commit, push, CI** | **OPEN at this document's timestamp** | ~25 m |

---

## §12 — Lessons

1. **A rule you have had to fix cannot be re-run; write a new one and apply it prospectively.** RULE D20 had two
   independent defects — a published diagnostic that makes a re-run non-blind, and a `D20-B` tolerance finer than
   its own instrument. Fencing it cost nothing and RULE B21 answered the same question on **six** datasets instead
   of one. *The fence is not a way of avoiding the measurement; it is what makes the replacement measurement
   worth reading.*
2. **A satisfiability analysis must check the instrument's PRECISION and whether the population is ADDRESSABLE.**
   Those are the two ways wave 20 lost measurements — `D20-B`'s `5e-7` against a `G6` console, and a path template
   that pointed at a file nobody wrote. This wave's design added both columns, and both fired: `G21-P3b`
   re-observed the precision defect, and `B21-V1`'s addressability check resolved 6 of 6 paths out of a manifest.
3. **`grep` for the IDENTIFIER, not the sentence — then leave a test.** Wave 20's Lessons #4 is **overridden**.
   F69(a)'s copies were paraphrases sharing only a flag name; the sentence-grep would have found none of the two
   extra ones, and the test found both. *Prose fixes decay; a test that enumerates its own population does not.*
4. **An interlock whose writer is a human is a note, not an interlock.** The wave protected the marker written by
   a driver with an explicit prohibition, and left the marker the arm actually blocks on to a `printf`. The
   checklist was in fact run first — but that was discipline, not mechanism. §8.1.
5. **A driver that refuses on a name mismatch converts a lost verdict into 40 seconds.** The same class of defect
   cost wave 20 RULE D20; this wave it cost the length of one `--write`. §8.3. *Assert the artifact exists at the
   name you will read it by, at the moment you write the name down.*
6. **A follow-on run is only a reproducibility measurement if something actually varied.** The `--max-rounds 5`
   run refuted a real prediction and it was worth 45 seconds — but its frames are byte-identical to the first
   run's, so it measured determinism, not seed sensitivity, and the notes' stronger claim does not survive contact
   with the report's `seed` fields. §7.2.
7. **Say which check reaches which item, before the wave runs.** The gate reached wave 20's code and **not** this
   wave's item A — the design said so in §0 and §3.3, and §8.2 records the one place the design over-claimed
   (item A was not even in the gate binary). *The failure mode is not an unreached item; it is an unreached item
   reported as covered.*

---

## §13 — Reproduce

Everything below is read-only. No `TestApp.exe` is launched; no arm directory is written.

```bash
# 0. the listing this document's first line is derived from
ls -la --time-style=full-iso /mnt/d/hf_w21/

# 1. RULE G21 -- the eight values, and the free controls in three directions
python3 /mnt/d/hf_w12/score_w12.py /mnt/d/hf_w21/gate --rule G21
python3 /mnt/d/hf_w21/prov_w21.py  --self-test /mnt/d/hf_w21/gate   # PASS / FAIL / printed=11 AGREE

# 2. the scorer, both ends, and its 20 branches. Read $? DIRECTLY, never through a pipe.
python3 /mnt/d/hf_w21/score_b21_w21.py --self-test                       # 20 of 20
python3 /mnt/d/hf_w21/score_b21_w21.py --gate /mnt/d/hf_w19/gate; echo "exit=$?"   # FAIL end, exit 1
python3 /mnt/d/hf_w21/score_b21_w21.py --gate /mnt/d/hf_w21/gate; echo "exit=$?"   # PASS end, exit 0

# 3. RULE B21 -- the verdict, addressed entirely through the manifest
cat /mnt/d/hf_w21/B21_ARM_READY /mnt/d/hf_w21/b21_manifest.tsv
python3 /mnt/d/hf_w21/score_b21_w21.py --gate /mnt/d/hf_w21/gate \
        --arm /mnt/d/hf_w21/B21_ARM_READY; echo "exit=$?"

# 4. the four fingerprint classes
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --check /mnt/d/hf_w21/bank_landing_fingerprint_BEFORE.json
python3 /mnt/d/hf_w17/aux_fingerprint_w17.py  --check /mnt/d/hf_w21/bank_aux_fingerprint_BEFORE.json
python3 /mnt/d/hf_w19/arm_fingerprint_w19.py  --check /mnt/d/hf_w21/w18_arm_fingerprint_BEFORE.json
python3 /mnt/d/hf_w21/log_fingerprint_w21.py  --check /mnt/d/hf_w21/w20_log_fingerprint_BEFORE.json

# 5. B21-A/B/D by hand on one dataset, sharing no code with the scorer
awk '/PARAMS-DUMP optimize\/(baseline|detected) BEGIN/,/PARAMS-DUMP optimize\/(baseline|detected) END/' \
    /mnt/d/hf_w21/arm/D10_rc16_3250mm_sparse/run.log | grep -E 'PARAMS-DUMP|DetectionBinning=|PixelScale='
grep -n 'detection binning (F39b)' /mnt/d/hf_w21/arm/D10_rc16_3250mm_sparse/run.log

# 6. item P, and the follow-on that is NOT item P
cat /mnt/d/hf_w21/f21_chain.log
python3 -c "import json;d=json.load(open('/mnt/d/hf_w21/f21/synth_validate_report.json'));\
s=d['datasets'][0]['scenarios'][0];print(s['terminal']['stoppedReason']);\
print([(r['roundIndex'],r['bootstrap']['seed'],r['stepRecommendation']['stepSize']) for r in s['rounds']])"
# the same three lines against f21b/ print the SAME seeds -- which is why §7.2 corrects the notes
diff <(grep -v 'Generated:\|Out:' /mnt/d/hf_w21/f21/synth_validate_report.md) \
     <(grep -v 'Generated:\|Out:' /mnt/d/hf_w21/f21b/synth_validate_report.md)   # empty

# 7. the gate's first launch, and the filename that stopped it
cat /mnt/d/hf_w21/chain_w21.log
sed -n '137,140p;178,186p' /mnt/d/hf_w21/gate_w21.sh
ls -la --time-style=full-iso /mnt/d/hf_w21/*arm_fingerprint_BEFORE.json   # two names, one content

# 8. item A: three sites, one test
git show 9a49aa6 --stat
grep -rn 'apply-run-detection-binning' Joko.NINA.Plugins/TestApp/*.cs
```

---

# §14 — ADDENDUM, written after the document's timestamp: how step 12 closed

**Everything above was written at the artifact-root timestamp `2026-08-11T10:14:17.433Z` and is left exactly as
it stood then.** Step 12 was correctly marked `OPEN` in place at §0, §1's suite row, the step table and §11.
This addendum records how it closed; it does **not** edit those rows, because a document that quietly converts
its own open items into done ones is the defect wave 19 was falsified by.

| step 12 item | outcome | when |
|---|---|---|
| **final suite, by COUNT** | **`Failed: 0, Passed: 3839, Skipped: 0, Total: 3839`** (duration 3 m 32 s) | `10:16Z`, before the wave commit |
| **commit** | `d0afb0f` — the wave; preceded by `9a49aa6` (item A) | `10:37Z` |
| **push** | `9a49aa6..d0afb0f` | `10:38Z` |
| **CI** | `31481763955` (item A) → **success**. The runs for `d0afb0f`, `82bade6` and `c41f308` were still in flight when this addendum was written — **read the branch's checks for their conclusions** | — |
| **PR #191** | retitled to *"Waves 13-21…"*, body extended with the wave-21 section, the item C section and F76 | — |

## Two further commits followed, and they are NOT part of RULE B21 or RULE G21

Neither carries a pre-registered rule; both are recorded so the wave's ledger is complete.

- **`82bade6` — [F76](followups.md), a product defect.** The A1–A9 *rendered pixels* check (item C, owed since
  wave 8) reached the Star Detection Optimizer wizard for the first time in ten waves and drove it to
  completion. **A4** (in-run guidance) and **A6** (abort advice) confirmed as rendered pixels; **A1** and **A2**
  confirmed earlier in the same session *against the report JSON on disk*; **A3/A9** upgraded from the
  placeholder to a real recommendation. Then the completed summary's entire action row —
  `Back / Review frames / Continue optimizing / Accept / Close` — proved to be **clipped off the bottom of the
  dialog**: window height `1392` == monitor work area `1392`, exactly, on a `SizeToContent` window with no
  ScrollViewer and no resize. All five buttons sit in the visual tree at `(0,0)`. **180
  `StarDetectionOptimizerWizardVMTests` pass on it**, correctly, because the ViewModel is fine — the failing
  half is the XAML, which is the half wave 13's results named as untested. **A5 and A7 are blocked on F76; A8
  was not exercised** (this run's step was `55 → 53`, not capped).
- **`c41f308`** — carries F76 and item C's real state into `docs/waves22+-handoff-prompt.md`, including the
  point that matters for pricing F76: **the suite does not reach it.**

**The bank was protected throughout.** The wizard was pointed at a **copy** of `D11_rc10_585_afbin2` at
`D:\hf_w21\uicheck_run`, never at `D:\SyntheticAutofocusBank`. All four fingerprint classes were re-run **after
NINA had run and exited** and are unchanged: **42 of 42** bank landings, **59 of 59** aux files, **48 of 48**
prior-wave arm landings across three roots, **10 of 10** wave-20 logs — byte-identical, **0 could-not-look**.
Nothing was written to the NINA profile: the wizard was closed with the title-bar X, which discards.

## A correction this addendum owes

§7's follow-on was reported by the controller as showing the trajectory is **seed-independent**. **It does not.**
The per-round seeds `[-25215000, 1539450862]` are **identical in both runs** — they are derived from the spec
and scenario, not drawn — so the repeat is ordinary determinism under identical inputs. The analysis agent
caught this before the document was committed and §7 states it correctly; the correction is repeated here
because the controller's own notes and the first draft of the PR body both carried the overstatement.
**Seed sensitivity remains UNMEASURED.**
