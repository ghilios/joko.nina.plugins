# Synthetic AF bank — followups wave 20 (results)

Pre-registration: [`docs/synthetic-af-bank-followups-wave20-design.md`](synthetic-af-bank-followups-wave20-design.md)
(commit **`d3d6a9f`**, `2026-08-11T06:19:51Z`, written and committed **before the binary was built**).
Plan: [`plans/synthetic-af-bank-followups-wave20-plan.md`](../plans/synthetic-af-bank-followups-wave20-plan.md).
Charter: [`docs/waves17-21-handoff-prompt.md`](waves17-21-handoff-prompt.md).
Wave 19: [`docs/synthetic-af-bank-followups-wave19-results.md`](synthetic-af-bank-followups-wave19-results.md).
Register: [`docs/followups.md`](followups.md).

> ## THE FIRST LINE OF THIS DOCUMENT IS A TIMESTAMP, BECAUSE WAVE 19's WAS NOT
>
> | | |
> |---|---|
> | **newest artifact under `/mnt/d/hf_w20` at write-up start** | `/mnt/d/hf_w20/dprobe/D12_c14_585_afbin2/run.log`, **`2026-08-11T08:08:51.480Z`** (the controller's byte-identical copy of the probe log, sha256 `c61ef1ec…`) |
> | **newest artifact written by a `TestApp.exe`** | `/mnt/d/hf_w20/dprobe/probe.log`, **`2026-08-11T08:07:42.194Z`** |
> | **write-up began** | `2026-08-11T08:16Z`, and **every scorer in this document was re-run after that time**, by an agent that ran no `TestApp.exe` and wrote nothing under any arm directory |
>
> Wave 19's results doc was falsified by an artifact created **33 seconds before its own commit** (design §0.1).
> The fix adopted here is mechanical rather than exhortatory: **the write-up's first action is `ls -la
> --time-style=full-iso` on the artifact root, and the result is printed above.** One row of this document —
> item P, §9 — is **still open at that timestamp** and says so in place, rather than being reported as a drop
> that has not happened yet.

> ## PROVENANCE
>
> | input | value |
> |---|---|
> | tree | branch `ghilios/synthetic-af-bank-followups-wave13`, PR #191. HEAD **`2678ecd`** (item D3). **The working tree is NOT clean and that is correct**: `ParamsDump.cs`, `OptimizationDiagnosticRunner.cs` and `ParamsDumpTests.cs` carry **D1 + D2, uncommitted at write-up time** — the controller commits them after the final suite. §5.4 |
> | **the binary is the tree, proven rather than assumed** | `find Joko.NINA.Plugins -name '*.cs' -newermt '2026-08-11 07:24:54Z'` (the build's own mtime) returns **nothing**. The three modified sources are stamped `06:59:53Z`, `07:11:25Z`, `07:20:28Z` — all **before** the suite (`07:20:48Z`) and the build (`07:24:35Z`) |
> | code this wave ships | **D3** ([F70](followups.md)) committed at `06:34:02Z`, `2678ecd`, **before the build**. **D1** ([F69](followups.md)(b)) and **D2** (F69(c)) in the working tree, in this binary, executed on 8 of 8 gate runs |
> | binaries — **ONE**, never rebuilt mid-wave ([F53](followups.md)(c)) | `D:\hf_w20\exe`, built `07:24:35Z` in 18.66 s. `TestApp.dll` sha256 **`bec4e0a647bf338b9fe777212bd722bac0cf0fc24dc83755da17cc8f907f0fae`** · `NINA.Joko.Plugins.HocusFocus.dll` sha256 **`cc629ffc570f677ef520862327b73cbaf6e9804d873ff5bd6e8085076835c705`** · `BuildId` **`1a4dd85a9a5b4a508195c2a7dff08f4f`**, novel against all ten recorded ids. **AN ELEVENTH BINARY** |
> | the apphost, again | `TestApp.exe` sha256 **`dd7103c28cc610e72671534cf23fb9a58b5a303a19d8779545cb7418d4ce6ff7`** — **byte-identical to wave 18's B1 and B2**, on a binary that is neither. Wave 18's lesson holds on a third and fourth counter-example: **an exe hash is not a binary identity; hash the dlls** |
> | settings **S0** | `D:\hf_w11\pinned_settings_w11.json` md5 `a67ffc06164c81613aef5c4f8324b9b8`, re-verified at write-up. **Not re-pinned** ([F63](followups.md)(b)) |
> | profile | `astrodet` `ce3f3e63-8fd3-4b72-a0ca-d90db9441382`, pinned on every arm; **one** distinct value across all 9 landings this wave produced |
> | detector | `DetectorVersion` **2**, read as a **FIELD** on all 8 gate landings and the probe landing ([F66](followups.md)). `strings`/`strings -el` appears nowhere |
> | suite | **3838** (`Failed: 0, Passed: 3838, Skipped: 0`), verified **by COUNT** at `07:20:48Z`, **before** the build. Wave 18's baseline was **3831**; this wave adds **7** tests and 3831 + 7 = 3838 |
> | F15 control — landings | **42 of 42 BYTE-IDENTICAL**, 0 changed / added / removed / could-not-look. Seventh consecutive clean wave |
> | F15 control — aux | **59 of 59 byte-identical** (`harness_settings.json` ×39 + `synthetic_meta.json` ×20) |
> | F15 control — prior-wave arms (class 3) | **48 of 48 BYTE-IDENTICAL** across `/mnt/d/hf_w18/seedA0` (20), `seedA1` (20), `gate` (8). This wave **reads none of them**; the class is a *preservation* control here, not an evidence control (design §6.3) |
> | **F15 control — wave-19 logs (class 4, NEW)** | **28 of 28 BYTE-IDENTICAL** (`/mnt/d/hf_w19/gate/*.log` ×8, `/mnt/d/hf_w19/reA0/D*/run.log` ×20). It exists because **every denominator in the pre-registration's satisfiability analysis was counted on those 28 files**, and a denominator is evidence |
> | banks | `D:\SyntheticAutofocusBank` (20 datasets), `D:\Autofocus Bank` (19 runs) |
>
> **Every number in this document was re-derived after `08:16Z`** by re-running each scorer against the arms on
> disk, plus an **independently written** block parser (`/tmp/rederive_w20.py`) that shares no code with
> `score_d20_w20.py` and reproduces its 8-of-8 counts, its field counts and its difference sets exactly.

---

## Status

| item | rule | verdict | what it establishes |
|---|---|---|---|
| the gate | **RULE G20** | **PASS** — 8 of 8 to 6 dp, **bit-identical at all sixteen digits**, on an **eleventh** binary | The coordinate system is unmoved **and** D1 is present, executed and **measurably** inert. §1 |
| the gate's reach into this wave's code | **`G20-P2`** | **PASS** — all five sub-clauses **8 of 8** | Wave 19's defect cannot recur silently: the clause **fails on wave 19's real artifacts** and passes only on a binary containing D1. §2 |
| **D1** — the `optimize/detected` dump | ships | **SHIPPED** | `PARAMS-DUMP optimize/detected` on **8 of 8** gate logs and **1 of 1** probe log, 55 fields, 55 unique names. F69(b) delivered. §5.1 |
| **D2** — the F69(c) no-op notice | ships | **SHIPPED** | Emitted exactly once on the probe log, naming both the flag and the default; **absent on all 8 gate logs**, which is the flag guard working. §5.2 |
| **D3** — the manual's two lines | ships | **SHIPPED, committed `2678ecd` before the build** | `preprocessing.md:23` **and** `:41`, plus one sentence stating the default is *derived, not a literal*. F70 discharged. §5.3 |
| the post-mutation property | **RULE D20** | **`D-UNEVALUATED`**, naming **`D20-V1`** | **The driver and the scorer disagree on where the probe log lives**, so the driver aborted its own post-check and never wrote `D20_PROBE_READY`. The scorer refused a verdict. **The measurement exists and is unambiguous, and it is reported as a DIAGNOSTIC below, never as a passed clause.** §3 |
| the path mismatch itself | — | **NEW REGISTER ENTRY, [F74](followups.md)** | Fourth plumbing slip of this run, and the first that cost a rule its verdict. §4 |
| **P** — the F21 price probe | — (rule-free) | **OPEN at `08:08:51Z`** | Not run as of this document's timestamp; drop trigger `D1` fires at `09:30Z`. §9 |

> **The headline, stated narrowly.** Wave 19 pre-registered item D, claimed the gate measured its inertness, and
> did not ship it — and **its ten thresholded clauses all passed anyway**, because not one of them read the
> shipped code. Wave 20 ships it and gives that reading a threshold. `G20-P2` **is 0 of 8 on wave 19's own gate
> logs and 8 of 8 on this wave's**, scored by the same code in the same minute. That is the shape a clause about
> a wave's own deliverable has to have: *its FAIL end observed on real artifacts, its PASS end reachable only
> through the shipped code.*
>
> **And the wave still lost a verdict — to a filename.** Not to a hard question, not to compute, not to the
> cutoff. Section §4 is the accounting, and it does not soften it.

---

## §1 — RULE G20: **PASS**, on an eleventh binary

Eight values, `optimize --per-run --max-evals 250`, both pins named in the driver header, sequential, one
`TestApp.exe`, no NINA. `07:25:05Z → 08:06:11Z` = **41 m 06 s** against a 42 m estimate.

### 1.1 The eight values

| run | `BestJ` (exact, 16 digits) | 6 dp | K8 expected | bit-identical? | `BaselineJ` | wave 11 |
|---|---|---|---|---|---|---|
| `toml999` | `0.9957838768299878` | 0.995784 | 0.995784 | **YES** | 0.983477 | 0.983477 |
| `CWhiteFocus` | `0.9960675916058808` | 0.996068 | 0.996068 | **YES** | 0.994320 | 0.994320 |
| `uneven` | `0.9963677194179505` | 0.996368 | 0.996368 | **YES** | 0.991794 | 0.991794 |
| `muggsie` | `0.9971948738498605` | 0.997195 | 0.997195 | **YES** | 0.993288 | 0.993288 |
| `mccomiskey` | `0.9767460801208465` | 0.976746 | 0.976746 | **YES** | 0.846082 | 0.846082 |
| `D18_m24_deep_shed` | `0.9998815090506263` | 0.999882 | 0.999882 | **YES** | 0.997405 | 0.997405 |
| `D19_cygnus_deep_shed` | `0.9994870586135448` | 0.999487 | 0.999487 | **YES** | 0.999187 | 0.999187 |
| `D20_m24_bright_control` | `0.9997378027339423` | 0.999738 | 0.999738 | **YES** | 0.999454 | 0.999454 |

**8 of 8 to 6 dp; 8 of 8 bit-identical at all sixteen digits; `BaselineJ` 8 of 8 against wave 11 ([F41](followups.md)).**

### 1.2 Every clause: the threshold fixed before the data, beside its measured value

**P** = population + field, **W** = which copy and is it live, **S** = statistic, **A** = aggregation,
**E** = the answer on an empty set ([F68](followups.md) + wave 18's fifth column).

| clause | threshold (fixed in `d3d6a9f`) | measured | P / W / S / A / E |
|---|---|---|---|
| **G20-1** | all 8 `BestJ` reproduce K8 to 6 dp; partial ⇒ stops the wave | **8 of 8**, and 8 of 8 bit-identical | 8 `optimized_settings.json` under `/mnt/d/hf_w20/gate`, `FinalJ` top level / one copy per landing (the two-copy hazard [F71](followups.md) is a property of converted *settings files*, not landings) / equality to 6 dp / all-of, printed `n of 8` / unreadable ⇒ COULD-NOT-LOOK **and FAIL** |
| **G20-2** | `aggregate_summary.json produced: 8`, asserted in the driver before any scorer | **8**, expected 8 | `find` over the gate root / — / existence / count `== 8` / 0 ⇒ FAIL |
| **G20-3a** | exactly one `BuildId`, **novel** against the ten | **1**, `1a4dd85a…`, novel | `Provenance.BuildId` ×8 / one copy per landing / set / cardinality + disjointness / empty ⇒ FAIL. `KNOWN_DLL_SHA256` is a **separate** table (F66); no `BuildId` began with a known dll hash |
| **G20-3b** | `DetectorVersion` **2**, read as the FIELD | **`{"2"}`** | `Provenance.DetectorVersion` ×8 / one copy / set / `== {"2"}` / empty ⇒ FAIL |
| **G20-3c** | `ProfileId` contains `ce3f3e63-…` **and** cardinality exactly 1 | **contains, cardinality 1** | `Provenance.ProfileId` ×8 / one copy / containment + cardinality / both, evaluated separately / the **cardinality** clause carries the empty read |
| **G20-3d** | one distinct `FitInputs`, exactly `MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid` | **1 distinct, exact match** | `Provenance.FitInputs` ×8 / one copy / set / equality / empty ⇒ FAIL |
| **G20-3e** | `ConcurrencyCheck == exclusive` on all eight, read **across the arm** | **8 × `exclusive`** | `Provenance.ConcurrencyCheck` / length-8 list with an explicit `None` per unreadable landing / equality / all-of over a **length-8** list / a `None` is a FAIL, not a skip |
| **G20-3f** | `BaselineJ` reproduces wave 11 to 6 dp | **8 of 8** | `BaselineJ` ×8 / one copy / equality / `n of 8` / missing ⇒ FAIL |
| **G20-4** | `prov_w20.py --self-test` PASSES on the real arm and FAILS on a mutated copy, the mutation asserted by read-back | **direction 1 PASS; direction 2 FAIL with 2 failures — the pin clause AND the cardinality clause** | the arm and a `copytree` / `Provenance.ProfileId` on `muggsie` / — / both directions / an unmakeable mutation prints SELF-TEST COULD NOT RUN |
| **G20-P1** | `optimize/seed` block reads `NoiseReductionRadius=3` on 8 of 8, asserted **in the driver** | **8 of 8** (re-derived independently: `seed=3` on all 8) | the 8 gate logs / `NoiseReductionRadius` **inside the `optimize/seed` BEGIN..END range**, not baseline, not detected, not the landing's copy — **three blocks now carry this name** / field value / `== 8` / a missing block ⇒ the driver refuses to hand the arm on |

`G20-P1` is the permanent guard against `/mnt/d/hf_w18/part2_w18.patch` silently converting this wave's binary
into wave 18's B2. **The hazard had an address for the third consecutive wave and the address stayed empty.**

### 1.3 What the PASS proves — and the two rows that say "reached: NO"

| shipped piece | reached by the gate? | the clause that reaches it | what the PASS proves |
|---|---|---|---|
| **D1** — the `optimize/detected` dump | **YES**, once per dataset inside `RunPerRun` | `G20-P2a…e` | the code is **in this binary and executed on 8 of 8 gate runs**; and because `G20-1` reproduces K8 **bit-identically with it present**, its inertness on the search is a **measurement, not an assumption** |
| **D2** — the F69(c) notice | **NO.** The gate does not pass `--apply-run-detection-binning` | `D20-C`, on the probe | **nothing.** Corroborated in the other direction: the notice is on **0 of 8** gate logs, which is the guard working |
| **D3** — the two manual lines | **NO.** It is documentation and is in no binary | `D20-M`, a textual assertion, corroborated by `optimize/baseline.NoiseReductionRadius == 4` | nothing about the file. The corroboration establishes **the value the option actually has**, which is what the manual was wrong about: **`baseline=4` on 8 of 8, re-derived** |

### 1.4 What G20 does not prove

Nothing about the landing beyond `FinalJ` and `BaselineJ` (RULE R19 owns that, on a population of C#-identical
builds — [F73](followups.md)). It runs at `MaxOutlierRejections = 0`, so it says nothing about the rejection
path. It is a control on the **binary**, not on the change — except where `G20-P2` explicitly reads the change.

---

## §2 — `G20-P2`: **PASS**, and its FAIL end was observed on real artifacts before the code existed

This is the clause wave 19 lacked. Its value is decided entirely by whether item D is in the binary, and it was
scored **in both directions, by the same scorer, in the same minute**:

| sub-clause | threshold (fixed in `d3d6a9f`) | **wave 20's gate** | **wave 19's gate** (the FAIL end, on real artifacts) |
|---|---|---|---|
| **G20-P2a** *reach* | `PARAMS-DUMP optimize/detected BEGIN` **exactly once** per log | **8 of 8** | **0 of 8**, each named: *"no `optimize/detected` block in `<log>`"* |
| **G20-P2b** *shape* | **55** field lines and **55 unique** names | **8 of 8** | **0 of 8** |
| **G20-P2c** *post-assignment* | `detected.PixelScale` **finite** and `baseline.PixelScale` the token `NaN` | **8 of 8** | **0 of 8** |
| **G20-P2d** *the gate cannot see the binning half, and says so* | `detected.DetectionBinning == 1` (an **equality**) | **8 of 8** | **0 of 8** |
| **G20-P2e** *no substring collision* | `optimize/baseline` and `optimize/seed` still exactly once each | **8 of 8** | **8 of 8** — the control that must **not** move, and did not |

Five-part statement, once for the group: **P** the 8 `*.log` under the gate root, each block matched as a
BEGIN..END range keyed on the **exact tag**; **W** the named block and no fallback across blocks — this is the
wave that adds a *third* copy of every field name to every log, so the hazard is at its maximum here; **S**
counts, counts-of-distinct, and value class (finite vs the literal token `NaN` — nothing calls `float()` on
`NaN`); **A** `== 1` per log, aggregated `n of 8`; **E** an unreadable log is COULD-NOT-LOOK, **named**, and the
clause **FAILS**.

The scorer's own 15 self-test branches were demonstrated first, **15 of 15**, including the two known-bads that
matter — `#3` a detected block byte-identical to baseline (the wrong-site mutant) ⇒ `D-PRESENT-ONLY` naming
`D20-A`/`B`/`D`, and `#4` `PixelScale` moved but `DetectionBinning` not ⇒ `D-PRESENT-ONLY` naming `D20-A`.

> **`G20-P2d` is an equality on purpose, and that is the honest half.** The resolved detection-binning factor is
> **1 on all eight gate datasets**, so a clause demanding that `DetectionBinning` *move* would be **unsatisfiable
> on this population** — the exact [F68](followups.md) defect. It was named in the pre-registration as the clause
> that was *not* written. The difference is carried by RULE D20's probe, on a dataset chosen for the purpose.

### 2.1 The corroborating measurement `G20-P2` did not have to make

Re-derived independently on all eight gate logs: the difference set between `optimize/baseline` and
`optimize/detected` is **exactly `{PixelScale}`** on **8 of 8** — which is the pre-registered expectation of
**`D20-E`**. The finite per-run scales are `1.3257361905796277`, `1.4101012208892405`, `0.7755556714890822`,
`1.4101012208892405`, `0.28852517540516454`, `1.033556334333394`, `0.7394398714518549`, `0.7115189646688828`,
against `NaN` in every `optimize/baseline` block. **This is reported as a diagnostic and not as a clause
result**: `D20-E` lives inside RULE D20, whose verdict is `D-UNEVALUATED`, and a wave does not get to harvest
one clause out of a rule the instrument refused to score. §3.4.

---

## §3 — RULE D20: **`D-UNEVALUATED`**, naming **`D20-V1`**

The branch table, fixed in `d3d6a9f` §5.2, applied as written:

```
D20-V1 or D20-V2 does not pass                ->  D-UNEVALUATED   (name the gate; D1/D2/D3 STILL SHIPPED)
```

**`D20-V1` could not look.** The scorer printed:

```
REFUSING to issue a RULE D20 verdict: /mnt/d/hf_w20/D20_PROBE_READY does not exist.
It is written by detected_probe_w20.sh after it asserts D20-V1 on its own log.
```

and exited **non-zero** (verified: exit 1, read from `$?` and not through a pipe).

### 3.1 Why — the driver and the scorer disagree on where the log lives

The probe **ran and succeeded**. `TestApp.exe` exited 0 at `08:07:42Z` after 16 s, on a real 9-frame run
(`250` evals, `HardFloorPassed: true`, `BestJ 0.9957418877445253`, `BaselineJ 0.9856629870215917`). What failed
is plumbing, in three places, all of the same kind:

| # | the driver did | the post-check / scorer expected | consequence |
|---|---|---|---|
| 1 | redirected the console to **`<probe>/probe.log`** (`detected_probe_w20.sh:100`) | **`<probe>/D12_c14_585_afbin2/run.log`** (`detected_probe_w20.sh:104`, `score_d20_w20.py:281`) | the driver aborted **its own** post-check and **never wrote `D20_PROBE_READY`** |
| 2 | passed `--out "D:\hf_w20\dprobe"` — **without** the per-dataset component the gate driver has (`gate_w20.sh:207` passes `--out "${OUT}\\${r}"`) | a per-dataset subdirectory | outputs landed **flat**: `dprobe/attempt01/`, `dprobe/aggregate_summary.json` |
| 3 | — | `find "$OUT" -mindepth 2 -name aggregate_summary.json` (driver) and `<probe>/<DS>/attempt01/optimized_settings.json` (`score_d20_w20.py:300`, clause `D20-V2`) | both would have missed even after (1) was patched, because the real paths are at depth 1 |

**The controller copied the log to the expected path** — byte-identical, sha256
`c61ef1ecd1f7dcb614ce0b9f5c0029b9c73ac79c9aaff61fe452b17f1fcacdec` on both copies, verified again at write-up —
**and the scorer still refused, correctly, because the marker is the driver's to write.** No marker was
hand-written. Re-verified at write-up: `/mnt/d/hf_w20/D20_PROBE_READY` does not exist.

> **And the copy would not have been enough.** With the marker present, `D20-V2` reads
> `<probe>/D12_c14_585_afbin2/attempt01/optimized_settings.json`, which does not exist either — only `run.log`
> was copied, and item 3 above is a *different* wrong assumption from item 1. **`D-UNEVALUATED` is not an
> artefact of one missing marker; the rule's population is mis-addressed in two independent places.** Anyone
> reading this later should not conclude that a one-line `touch` would have produced a verdict.
>
> The sharpest detail: `score_d20_w20.py:301-305` **already carries a fallback** that `os.walk`s for
> `optimized_settings.json` when the exact path is missing — written precisely to absorb layout drift. It is
> rooted at `<probe>/<DS>`, the directory that does not exist, so **the drift-absorber inherited the drift.**
> *A fallback rooted at the same wrong parent as the primary path is not a second chance.*

### 3.2 The measurement, as a **DIAGNOSTIC** under the `D-UNEVALUATED` verdict

Re-derived at write-up from `/mnt/d/hf_w20/dprobe/probe.log` with a parser sharing no code with the scorer.
**None of these is a passed clause. RULE D20 issued no verdict and none of `D20-A…E` has one.**

| block | `DetectionBinning` | `PixelScale` |
|---|---|---|
| `optimize/baseline` | 1 | `NaN` |
| `optimize/seed` | 1 | `NaN` |
| **`optimize/detected`** | **2** | **`0.6296504611753467`** |

| what the clause *would* have read | pre-registered threshold | measured |
|---|---|---|
| `D20-V1` the probe really is factor 2 | `detection binning (F39b): 2 from ` exactly once | **`2 from synthetic_meta.json expectedOptimal.detectionBinning (physics-derived)`, once** |
| `D20-V2` one binary, the pins held | `BuildId` == the gate's, cardinality 1; `ProfileId` by containment **and** cardinality; `DetectorVersion == 2`; `ConcurrencyCheck == exclusive` | **`1a4dd85a9a5b4a508195c2a7dff08f4f` (== the gate's), `astrodet (ce3f3e63-…)`, `2`, `exclusive`** — read from `dprobe/attempt01/optimized_settings.json`, the path the scorer does not look at |
| `D20-A` the decisive one | `detected.DetectionBinning == 2` **and** `baseline.DetectionBinning == 1`, same log | **2 and 1** |
| `D20-B` the scale followed | `detected.PixelScale` finite and == the console line to 6 dp; `baseline.PixelScale` the token `NaN` | **`0.6296504611753467`** vs console **`0.62965`**, Δ = `4.612e-7`; baseline **`NaN`**. See the caveat in §3.3 |
| `D20-C` F69(c) fired | the notice exactly once, naming the flag **and** the default | **once**, naming `--apply-run-detection-binning` and *"F39(b) was adopted as the DEFAULT in wave 8; the opt-OUT is `--no-run-detection-binning`"* |
| `D20-D` the whole difference set | exactly `{DetectionBinning, PixelScale}` | **exactly `{DetectionBinning, PixelScale}`** — 53 of the 55 fields identical, set equality, printed in full |
| `D20-E` the factor-1 counterpart | exactly `{PixelScale}` on 8 of 8 gate logs | **exactly `{PixelScale}`, 8 of 8** |

**What this is.** It is **F69(b) delivered**: wave 16's RULE P16 compared `optimize`'s params **as constructed**
against `af-fit`'s **as detected**, excluded `DetectionBinning` on the strength of a comment that was backwards,
and got a wrong verdict; wave 17 then found the cause to be exactly that field. The table above is the
**post-mutation** dump showing the mutation, on a run whose resolved factor is not 1, with all 55 fields
accounted for. The instrument P16 lacked now exists and prints.

**What this is not.** It is not `D-DEMONSTRATED`, and this document does not compute that verdict by hand from
the diagnostic. The branch table is the instrument's to apply; a wave that reads its own diagnostics and
announces the verdict its rule refused to issue has re-decided a pre-registered rule after the data. **The
register gets `D-UNEVALUATED`, naming `D20-V1`, and the diagnostic beside it.**

### 3.3 One thing in the pre-registration that is wrong: `D20-B`'s tolerance is finer than its own instrument

`D20-B` compares the dump's `PixelScale` against the **console** line as an independent second source,
"to 6 dp" — implemented as `abs(detected − console) < 5e-7`. The console value is printed by
`OptimizationDiagnosticRunner.cs:1938`, `v.ToString("G6")` — **six significant digits, not six decimals.**

- For the probe (`0.6296…`, `< 1`), G6 yields 6 decimals and the worst-case rounding error is exactly `5e-7` —
  **the tolerance equals the instrument's quantum.** The observed Δ is `4.612e-7`, inside it by **8 %**.
- For any run with `PixelScale >= 1`, G6 yields **five** decimals and the error can reach `5e-6` —
  **ten times the tolerance.** On `CWhiteFocus`'s scale (`1.3257361905796277` vs `1.32574`) the Δ is `3.8e-6`
  and `D20-B` would have **failed on a correct program**.

All seven factor-2 datasets happen to have binned scales in `[0.449, 0.630]`, so the clause is satisfiable for
every legal choice of probe — **but only by accident of this bank's focal lengths**, and it was one dataset
away from being an unsatisfiable clause of exactly the kind [F68](followups.md) exists to catch. **The
satisfiability analysis checked which values the clause could reach and did not check the precision of the
instrument it reads from.** Fix, for whoever re-runs the probe: compare at the console's own precision
(`round(detected, 6 - floor(log10(|v|)) - 1)`), or drop the console cross-check and read
`DetectionBinningResolver.ApplyFactor`'s inputs from the dump alone.

### 3.4 What survives `D-UNEVALUATED`

| survives | does not survive |
|---|---|
| **D1, D2 and D3 shipped.** Nothing in RULE D20 gated the ship — deliberately, because tying the ship to the measurement rewards not shipping, which was wave 19's failure mode | any **claim** that the dump is demonstrably post-mutation. The register may say *"the diagnostic shows it; the rule did not issue"* and nothing stronger |
| **`G20-P2` PASS, 8 of 8 on all five sub-clauses.** The block is present, correctly shaped, carries the run's real finite `PixelScale` against `baseline`'s `NaN`, and did not disturb the other two tags | the `DetectionBinning` half **as a clause result**. It is measured; it is not scored |
| the probe's artifacts, intact and re-readable: 17,211 bytes of log, a full landing, `aggregate_summary.json` | `D20-E`, even though its entire population (the 8 gate logs) is present and its expected set re-derives exactly. It is inside the rule that refused |
| **the cost of finishing it: ~3 minutes of compute.** The probe is 16 s of `TestApp` plus a two-line path fix | nothing else. This is the cheapest unfinished item in six waves |

---

## §4 — The path mismatch: it earns a **register entry**, not a Lessons line

**Judgment.** [F74](followups.md), new.

Four things pushed it over the line, and the first is the weakest of them:

1. **It is the fourth of its kind in one run** — after `exeB1`/`exe_b1`, a mis-named fingerprint file, and a
   progress counter with no could-not-look state. A single slip is a Lessons line; **a fourth instance is a
   pattern, and patterns go where the next wave's author will read them.** Lessons sections are read by the
   people who already lived the wave.
2. **It cost a pre-registered rule its verdict.** That is the same cost [F69](followups.md)(a) is in the
   register for. The register does not distinguish "a wrong comment cost a verdict" from "a wrong path cost a
   verdict", and it should not.
3. **The fix is general and it is already half-specified.** The pre-registration's own §8 says
   `D20_PROBE_READY`'s contents are *"the resolved factor and the probe log path"* — and the driver **does**
   write `log=$LOG` into it. **The scorer then ignores the marker's contents and reconstructs the path from a
   template.** The marker was designed as the handoff and used as a boolean.
4. **The wrong path is not even this wave's convention.** `<root>/<dataset>/run.log` is **wave 19's** arm layout
   (`redo_w19.sh:157`). Wave 20's gate writes `<gate>/<name>.log` beside a `<gate>/<name>/` output root; wave
   20's probe writes `<probe>/probe.log` beside a flat output root. **Three layouts across two waves, and the
   scorer was written against the one that is in neither of this wave's drivers.**

**The fix, in one sentence:** *the scorer must read the path the driver printed, not rebuild it* — concretely,
parse `log=` out of `D20_PROBE_READY` (and have the driver write the landing path into it too), so the marker's
existence and the marker's contents are the same handoff. Cost: about five lines on each side. The
belt-and-braces half — make the probe driver's `--out` and log naming identical to the gate driver's — is worth
doing as well, but it is the weaker fix, because it re-establishes agreement by convention where the other one
establishes it by construction.

**What it is not.** It is not a compute failure, not a cutoff casualty, and not a hard problem. The wave had
**3 h 52 m** of slack when it happened.

---

## §5 — What shipped

### 5.1 D1 — the `optimize/detected` dump (F69(b))

`ParamsDump.OptimizeDetected = "optimize/detected"`, plus `ParamsDump.AllSources` (the tag **set**, so the
non-collision property is asserted over the set rather than over a list a later wave must remember to extend),
and a third `ParamsDump.Write` at `OptimizationDiagnosticRunner.cs:688` — **immediately after**
`ApplyRunDetectionBinningIfRequested` at `:675`, inside `RunPerRun`.

- **Arm-level evidence:** the block is on **8 of 8** gate logs and **1 of 1** probe log, **55 field lines / 55
  unique names** on all nine, through the same shared reflective formatter as the other two blocks and into the
  same sink (the console — never a summary file).
- **The tag was chosen, and the choice is now tested.** `optimize/baseline-resolved` would have broken wave 17's
  and wave 18's saved `grep -c "PARAMS-DUMP optimize/baseline BEGIN"` drivers on populations they had already
  scored. `NoSourceTag_ContainsAnyOther_BecauseEveryScorerCountsThemBySubstring` pins the pairwise property over
  `AllSources`; the controller independently re-verified that mutating the tag to `optimize/baseline-resolved`
  turns **3 tests red**, that one among them.
- **Inertness is measured, not asserted:** `G20-1` reproduces K8 **bit-identically at sixteen digits** with the
  dump present.

### 5.2 D2 — the F69(c) no-op notice

Emitted behind `DiagnosticUtil.HasFlag(args, "--apply-run-detection-binning")`:
*"`--apply-run-detection-binning`: ACCEPTED NO-OP. F39(b) was adopted as the DEFAULT in wave 8; the opt-OUT is
`--no-run-detection-binning`. This flag is retained so wave 7's scripts keep running, and it is not read
(F69(c))."* **Once on the probe log; zero times on all eight gate logs**, which is the guard, measured in both
directions. The text is ASCII-only, and deliberately so: a Unicode character in a **redirected** log arrives as
the single byte `0x1A` on this machine's console code page.

### 5.3 D3 — the manual (F70), committed **before** the build

`documentation/docs/settings/preprocessing.md` at **both** sites: the table row at `:23` (`3` → `4`) **and** the
prose at `:41` (`**Default:** 3` → `4`), plus a new sentence: *"The default is **derived, not a literal**: the
Simple-mode preset sets a base radius of `3` and adds `1` when hot-pixel filtering is on, which it is by
default."*

Wave 18's `/mnt/d/hf_w18/part2_w18.patch` was **explicitly not used as a template**: it fixes the table row and
leaves the prose saying `3`. **A half-corrected manual is worse than an uncorrected one, because the two halves
then disagree with each other** — and the reader who greps the source, finds the literal `3`, and believes the
prose is misled a third time. Arm-level corroboration, re-derived: `optimize/baseline.NoiseReductionRadius == 4`
on **8 of 8** gate logs (and `optimize/seed == 3`, which is the pre-compensation base — `G20-P1`).

### 5.4 The state of the tree at write-up

D1 and D2 are **in the working tree and in the binary, not yet committed**. The provenance table above proves
the binary is that tree (no `.cs` newer than the build) and the suite was green on it by COUNT **before** the
build. The controller owes: final suite by COUNT, commit, push, PR section. **This document is written against
an uncommitted tree and says so rather than implying a clean one** — wave 19's provenance table claimed a clean
tree correctly; wave 20's would have been a lie.

---

## §6 — Two source observations the arms could not make

### 6.1 `ApplyRunDetectionBinningIfRequested` has **exactly one call site**

`OptimizationDiagnosticRunner.cs:675`, inside `RunPerRun`. **The joint (non-`--per-run`) path never calls it**,
so on the joint path the two existing dumps *are* the detecting bundle and `optimize/detected` is correctly not
emitted there. This is a **source counting argument, not a measurement** — every arm in this series is
`--per-run` — and it is now pinned by a test that asserts the call-site count is 1
(`ParamsDumpTests.cs:530-561`), so a later wave that adds a second call site is told rather than left to
discover the block is missing somewhere and read it as a regression.

*(The pre-registration's §9.3 cites `:660` for this call site; D1's own insertion moved it to `:675`. The line
number is post-ship; nothing else about the paragraph changed.)*

### 6.2 [F69](followups.md)(a) is **still false in the tree**, three lines above the code that corrects it

F69(a)'s next step said *"fix the `:405-409` comment — it is four lines and it is the piece that did the
damage"*, and that site **is** fixed (commit `93e366a`, wave 18): `:410-419` now carries an explicit *"THIS
PARAGRAPH USED TO SAY THE OPPOSITE, AND WAS BELIEVED"* correction. But the **same false sentence exists a second
time**, as the XML doc on the method itself, `OptimizationDiagnosticRunner.cs:591-594`, where it has sat since
`9cd4c02` — the commit that introduced F39(b):

```csharp
/// F39(b) — DETECT this run at its own detection-binning factor …
/// No-op unless <c>--apply-run-detection-binning</c> was passed, so the flag's
/// absence leaves the run bit-identical to before it existed (F41's one-binary-is-both-arms rule).
```

It is backwards in exactly the way that cost RULE P16 its verdict: the method runs **unless the opt-out
`--no-run-detection-binning`** is passed, and `--apply-run-detection-binning` is an accepted no-op — which the
body three lines below now *prints at runtime* (D2). **A file that tells the truth on the console and the
opposite in its own doc comment is worse than one that is merely stale.**

It was **outside this wave's pre-registration and was therefore not touched.** It is recorded as owed, with its
fix: replace the second sentence with *"Runs by DEFAULT (F39(b), wave 8); the opt-OUT is
`--no-run-detection-binning`. `--apply-run-detection-binning` is an accepted no-op, retained for wave 7's
scripts (F69(c))."* **~2 minutes, no behaviour change, no gate.**

> **The generalisable half, and it is [F71](followups.md)'s lesson wearing documentation's clothes.** The
> register named a **line range** as the defect. The defect was a **sentence**, and it existed in two copies.
> Fixing the copy that was cited made the entry look discharged while the other copy went on being true-looking
> and false. *When a register entry names where a false statement lives, the next step must be `grep` for the
> statement, not an edit at the address.*

---

## §7 — Controls

| control | required | measured |
|---|---|---|
| fingerprint class 1 — bank landings | 42 byte-identical | **42 of 42**, 0 changed / added / removed / could-not-look |
| fingerprint class 2 — aux | 59 byte-identical (`harness_settings.json` 39 + `synthetic_meta.json` 20) | **59 of 59**, kinds match exactly |
| fingerprint class 3 — prior-wave arms | 48 byte-identical across three roots | **48 of 48** (`seedA0` 20, `seedA1` 20, `hf_w18/gate` 8) |
| **fingerprint class 4 — the 28 wave-19 logs (new)** | 28 byte-identical | **28 of 28** |
| `prov_w20.py --self-test` | 2 directions, mutation asserted by read-back, direction 2 ≥ 2 failures | **PASS / FAIL as expected, 2 failures — the pin clause and the cardinality clause** |
| `score_d20_w20.py --self-test` | ≥ 14 branches | **15 of 15** |
| `log_fingerprint_w20.py --self-test` | 7 branches | **7 of 7** at pre-registration time |
| driver self-tests (`gate`, `detected_probe`, `f21_probe`) | argument/path/timebox handling, no `TestApp` launched | **all PASS** at pre-registration time |

All four fingerprint classes were re-checked **at write-up**, after the last measurement: **4 of 4 clean.**
Class 3's job this wave is *preservation*, not evidence — §3.1 of the design promises not to spend
`/mnt/d/hf_w18/seedA1`, and 48 of 48 byte-identical is the executable form of that promise.

**Two instrument notes, both verified harmless, both recorded because this run has already produced four defects
of the "the message and the mechanism disagree" family.**

1. `prov_w20.py` prints *"BuildId novel against **NINE** recorded ids"* and lists nine wave labels, while
   `PRIOR_BUILD_IDS` actually holds **ten** (wave 19's `763d1476…` is present and **is** checked). The check
   iterates the dict; only the sentence is stale.
2. `score_d20_w20.py --gate <root>` **exits 0 even when `G20-P2` FAILS** — verified against
   `/mnt/d/hf_w19/gate`: `0 of 8`, the word `FAIL`, exit `0`. The full-verdict invocation does exit non-zero, and
   wave 20's driver asserts `G20-P2a/b/e` itself before handing the arm on, so nothing here was decided by an
   exit code. But **a `set -e` chain would read that FAIL as success**, which is the same shape as
   [F74](followups.md) one layer up: *the report and the exit status are two channels, and only one of them was
   wired to the verdict.*

---

## §8 — Budget: estimated against actual

| # | step | estimate | actual | Δ |
|---|---|---|---|---|
| 0 | commit the pre-registration | 2 m | `06:19:51Z` (`d3d6a9f`) | — |
| 1 | **D3, committed on its own before anything else** | 2 m | `06:34:02Z` (`2678ecd`) | on the floor, as designed |
| 2 | D1 + D2 + tests (incl. 11 mutants applied, built and run) | 35 m | ~`06:34Z → 07:20Z` = **46 m** | **+11 m** |
| 3 | suite, verified by COUNT | 4 m | **3 m 24 s**, 3838 passed | −0.6 m |
| 4 | build into `D:\hf_w20\exe` | 1 m | **18.7 s** | −0.7 m |
| 5 | four fingerprint classes, `--write` | 2 m | **8 s** | −1.9 m |
| 6 | **RULE G20 — the gate** | **42 m** | **41 m 06 s** (`07:25:05Z → 08:06:11Z`) | −0.9 m |
| 7 | score G20 (`score_w12`, `prov_w20` both directions, `score_d20 --gate`) | 8 m | **25 s** in the chain; ~4 m including the write-up re-runs | −4 m |
| 8 | the detected probe | **3 m** | **16 s** of `TestApp` (`08:07:26Z → 08:07:42Z`) | **−2.7 m** |
| 9 | score RULE D20 | 10 m | **refused**, ~1 m | — |
| 10 | **item P — the F21 price probe** | ≤ 20 m | **NOT RUN as of `08:08:51Z`**; `/mnt/d/hf_w20/f21` does not exist | open |
| 11 | re-check all four fingerprint classes | 3 m | **~40 s**, 4 of 4 clean | −2 m |
| 12 | analysis + results document | 60 m | started `08:16Z` | — |
| 13 | final suite by COUNT + commit + push + PR + CI | 45 m | pending | — |
| | **`TestApp` wall** | ~65 m | **41 m 22 s** | −24 m |

Per-dataset gate times: `toml999` 1 m 53 s, `CWhiteFocus` 8 m 07 s, `uneven` 5 m 56 s, `muggsie` 1 m 05 s,
`mccomiskey` 10 m 07 s, `D18` 6 m 09 s, `D19` 5 m 46 s, `D20` 2 m 03 s.

**The probe came in at 16 s against a 3 m estimate**, because a factor-2 dataset detects on a quarter of the
pixels and `D12` is small. The rate table's `~2.64–2.73 m/run` for all-synthetic `optimize` was measured at
factor 1; **it does not describe factor-2 datasets and should not be quoted for them.** Nothing was harmed by
the over-estimate, but the next wave that budgets a factor-2 arm at the synthetic rate will over-reserve by 10×.

---

## §9 — What was NOT run, and what it costs

Cheapest first — the ordering that surfaced wave 19's one-minute gap.

- **The remaining ~3 minutes of RULE D20 — the probe re-run under a driver/scorer pair that agrees on the
  path.** §3, §4. **Cost: `D-UNEVALUATED` in the register, and F69(b) is recorded as delivered-by-diagnostic
  rather than demonstrated-by-rule.** This is the cheapest unfinished item in six waves and the only one whose
  blocker is a filename.
- **F69(a)'s surviving copy at `OptimizationDiagnosticRunner.cs:593` — ~2 minutes, zero compute.** §6.2. Outside
  the pre-registration, so not touched. **Cost: the sentence that cost RULE P16 a verdict is still in the tree,
  three lines from the code that contradicts it.**
- **Item P, the F21 price probe — ≤ 20 m, hard-timeboxed, rule-free. OPEN at this document's timestamp.**
  `f21_probe_w20.sh` is written and self-tested; `/mnt/d/hf_w20/f21` does not exist. The drop trigger `D1`
  fires at `09:30Z`. **If it is dropped, `synth-validate` is unpriced for a fourth consecutive wave and no
  verdict moves; if it runs, this row and §8 step 10 are the two places that need updating** — and the wave that
  updates them owes a fresh artifact listing at the top, by the same rule that produced this document's.
- **Re-issuing RULE C19's verdict — 0 m, deliberately.** `/mnt/d/hf_w19/C19_PROBE_PASSED` exists, 572 bytes,
  reading `C-DEMONSTRATED`, written `05:43:28Z`. **Wave 20 records the artifact-level fact and does not re-issue
  a prior wave's verdict.** Cost: RULE C19 stays formally unre-issued until a wave pre-registers a rule that
  consumes it.
- **The out-of-sample pass over wave 18's 40 arm landings — ~8 m, and no driver exists that could.** Design
  §3.1. **Cost: F70(a) stays with the owner.** The population is provably unspent (48 of 48).
- **F70(b′), the 20 preset-owned dead literals — ~20 m + a mutant-demonstrated parametrized test whose property
  set is derived from source.** **Cost: editing one of the 20 remains a silent no-op.**
- **A `DetectionBinning`-differs clause on the gate — 0 m, because it is UNSATISFIABLE there** (factor 1 on all
  eight). **Cost: none — the probe carries it.** Recorded because writing it would have looked like rigour.
- **The joint-path behaviour of the new dump — unmeasured**, settled in source (§6.1). **Cost: ~5 m of
  `optimize` if a later wave wants it measured; nothing in the register depends on it.**
- **[F59](followups.md)'s five knobs — ~1 h, rejected on the merits** with a new costing: **4 of the 5
  (`MaxDistortion`, `StarCenterTolerance`, `HotpixelThreshold`, `Sensitivity`) are assigned by
  `DerivePresetSettings()` and are overwritten on load** under the current `UseAdvanced=False` pin, so repairing
  the exporter changes the detector by exactly nothing for them. The fifth, `SaturationThreshold`, **would**
  bind — and is therefore a coordinate-system move owing a fresh 42 m baseline. **Cost:
  `pinned_settings_w11.json` still does not describe `SaturationThreshold`.**
- **[F45](followups.md)(b)'s production plumbing — ~3–4 h, rejected on substance:** RULE S16 returned NO
  RECOMMENDATION and charter §3a forbids repairing `S16-A(b)`, so the plumbing would deliver a mechanism no rule
  has recommended. **Cost: the SEM criterion stays unshipped, which is what NO RECOMMENDATION means.**
- **Re-pinning the settings file — 0 m.** [F63](followups.md)(b), DO-NOT-RE-PIN.
- **Item C / the NINA UI items A1, A2, A4–A8 — ~30 m** on a session where NINA is not competing with a pinned
  arm. The blocker is gone ([F72](followups.md)); only the work and the scheduling remain.

---

## §10 — Register corrections this wave owes, and makes

1. **[F69](followups.md)(b) and (c) are DELIVERED** — with arm-level evidence (`optimize/detected` on 8 of 8
   gate logs and 1 of 1 probe log, 55/55 fields; the no-op notice once on the probe and zero times on the gate).
   **(a) is still owed** and its address is now `OptimizationDiagnosticRunner.cs:593`, not `:405-409`. §6.2.
2. **[F70](followups.md): the manual is fixed at BOTH sites**, `:23` and `:41`, with the derivation named and
   the default stated as *derived, not a literal*. Two waves overdue; shipped at `06:34:02Z`, before the build.
3. **[F74](followups.md), new: a driver and a scorer that reconstruct the same artifact path independently will
   eventually disagree, and the marker between them already carried the answer.** §4.
4. **[F71](followups.md): `C19-D2` is NOT still owed.** It ran at `05:43:28Z`; `/mnt/d/hf_w19/C19_PROBE_PASSED`
   exists and reads `C-DEMONSTRATED`. Wave 19's §3, §8 and §10.6 are wrong about it, and §3.5 of that document
   already carries the correction.
5. **[F59](followups.md): 4 of the 5 missing knobs are preset-owned** and inert under the current pin. A
   costing, not a verdict — so the next wave starts from *"4 of 5 change nothing"* rather than from *"~1 h"*.
6. **The charter's suite baseline (§6 step 8, "3781") is stale.** It was **3831**; it is now **3838**.

---

## §11 — Lessons

1. **A clause about a wave's own deliverable must have its FAIL end measured on real artifacts.** `G20-P2` is
   `0 of 8` on wave 19's gate logs and `8 of 8` on wave 20's, scored by the same code in the same minute. Wave
   19 had the same count with no threshold on it, and its ten thresholded clauses passed with the code absent.
   *A number that is reported but not thresholded is a number the wave has agreed in advance not to act on.*
2. **A mutation harness that restores from VCS destroys the uncommitted work under test.** This wave's harness
   first reverted with `git checkout -- <file>` — and the working tree carried D1/D2 while `HEAD` did not, so
   the first mutant silently deleted the feature and **every later mutant would have measured a different
   program**. It was caught, the edits were reapplied, and the harness now keeps a **byte backup** taken before
   each mutation, restores it in a `finally`, and asserts **four source sentinels after every mutant** — aborting
   with `WORKING TREE DAMAGED` if any is missing. *Restore what you saved, not what the repository remembers.*
3. **The write-up's first action must be a directory listing, and its timestamp must be printed.** Wave 19's doc
   was falsified 33 seconds before its own commit. This document opens with `08:08:51.480Z` and marks the one
   row (item P) that is still open at that instant, rather than reporting a drop that has not happened.
4. **When a register entry names where a false statement lives, the fix is `grep` for the sentence, not an edit
   at the address.** F69(a) named `:405-409`; the same sentence lived at `:593` and survived the fix, in the XML
   doc of the very method the entry is about. §6.2. This is [F71](followups.md)'s two-copies lesson applied to
   prose.
5. **A satisfiability analysis must check the precision of the instrument a clause reads, not only the values
   the clause can reach.** `D20-B`'s `5e-7` tolerance is the exact quantum of a `G6`-formatted console line
   below 1.0, and is **ten times too tight** above it. It is satisfiable here only because every factor-2
   dataset in this bank is sub-arcsecond. §3.3.
6. **A verdict lost to plumbing is still a verdict lost.** The wave had 3 h 52 m of slack, a working binary, a
   completed 16-second run and a correct measurement sitting in a log — and RULE D20 is `D-UNEVALUATED` because
   a driver wrote `probe.log` and a scorer read `D12_c14_585_afbin2/run.log`. **The interlock behaved
   perfectly**: the driver refused to certify a log it could not find, the scorer refused to score without the
   certificate, and no marker was hand-written to paper over it. *The right response to a refusing interlock is
   to fix the handoff, not to satisfy the interlock by hand.*
7. **Ship unconditionally; let the rule decide only what may be claimed.** D1, D2 and D3 shipped, the manual
   before the build. If RULE D20 had gated the ship, this wave would have shipped nothing for the second time
   running — and the thing that failed had nothing to do with the code.

---

## §12 — Reproduce

Everything below is read-only. No `TestApp.exe` is launched, no arm directory is written.

```bash
# 0. the listing this document's first row is derived from
ls -la --time-style=full-iso /mnt/d/hf_w20/

# 1. RULE G20 -- the eight values and the free controls
python3 /mnt/d/hf_w12/score_w12.py  /mnt/d/hf_w20/gate --rule G20
python3 /mnt/d/hf_w20/prov_w20.py   --self-test /mnt/d/hf_w20/gate     # both directions

# 2. G20-P2 -- the same scorer, both ends
python3 /mnt/d/hf_w20/score_d20_w20.py --self-test                     # 15 of 15
python3 /mnt/d/hf_w20/score_d20_w20.py --gate /mnt/d/hf_w20/gate       # PASS,  8 of 8 x5
python3 /mnt/d/hf_w20/score_d20_w20.py --gate /mnt/d/hf_w19/gate       # FAIL,  0 of 8 (the code is not in that binary)

# 3. RULE D20 -- refuses, and exits non-zero. Read the exit code directly, NOT through a pipe.
python3 /mnt/d/hf_w20/score_d20_w20.py --gate /mnt/d/hf_w20/gate --probe /mnt/d/hf_w20/dprobe; echo "exit=$?"

# 4. the four fingerprint classes
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --check /mnt/d/hf_w20/bank_landing_fingerprint_BEFORE.json
python3 /mnt/d/hf_w17/aux_fingerprint_w17.py  --check /mnt/d/hf_w20/bank_aux_fingerprint_BEFORE.json
python3 /mnt/d/hf_w19/arm_fingerprint_w19.py  --check /mnt/d/hf_w20/w18_arm_fingerprint_BEFORE.json
python3 /mnt/d/hf_w20/log_fingerprint_w20.py  --check /mnt/d/hf_w20/w19_log_fingerprint_BEFORE.json

# 5. the D20 diagnostic, by hand, sharing no code with the scorer
awk '/PARAMS-DUMP optimize\/(baseline|detected) BEGIN/,/PARAMS-DUMP optimize\/(baseline|detected) END/' \
    /mnt/d/hf_w20/dprobe/probe.log | grep -E 'PARAMS-DUMP|DetectionBinning=|PixelScale='
sha256sum /mnt/d/hf_w20/dprobe/probe.log /mnt/d/hf_w20/dprobe/D12_c14_585_afbin2/run.log   # identical

# 6. the path mismatch, in four lines of source
sed -n '100p;104p' /mnt/d/hf_w20/detected_probe_w20.sh    # writes probe.log, reads <DS>/run.log
sed -n '281p'      /mnt/d/hf_w20/score_d20_w20.py         # rebuilds <DS>/run.log
sed -n '207p'      /mnt/d/hf_w20/gate_w20.sh              # the gate's --out carries the dataset; the probe's does not
```
