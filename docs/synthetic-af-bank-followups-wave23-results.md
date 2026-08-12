# Synthetic AF bank — followups wave 23 (results)

**Artifact root at write-up: `/mnt/d/hf_w23/` — `2026-08-12 15:29:42.641623400 -0400` = `2026-08-12T19:29:42.641Z`.
Analysis began `2026-08-12T19:31:15Z`** (`ls -la --time-style=full-iso /mnt/d/hf_w23/` then `date -u`, in that
order, as the write-up's first action; the filesystem clock is local `-0400`, every driver clock in this document
is UTC).

Pre-registration: [`docs/synthetic-af-bank-followups-wave23-design.md`](synthetic-af-bank-followups-wave23-design.md)
(commit **`839c38b`**, **before any wave-23 measurement existed**).
Plan: [`plans/synthetic-af-bank-followups-wave23-plan.md`](../plans/synthetic-af-bank-followups-wave23-plan.md).
Charter: [`docs/waves22+-handoff-prompt.md`](waves22+-handoff-prompt.md),
[`docs/waves22-27-autonomous-prompt.md`](waves22-27-autonomous-prompt.md).
Wave 22: [`docs/synthetic-af-bank-followups-wave22-results.md`](synthetic-af-bank-followups-wave22-results.md).
Register: [`docs/followups.md`](followups.md).

> ## THE TIMESTAMP, AND WHAT IS STILL OPEN INSIDE IT
>
> | | |
> |---|---|
> | **artifact root** | **`2026-08-12T19:29:42.641Z`** — the row above, printed as the write-up's first action |
> | newest artifact of any kind | `p23_score_AFTER.txt`, **`19:29:42.643Z`** (the AFTER re-read; it is what moved the directory's mtime) |
> | **newest artifact written by a `TestApp.exe`** | the `v23/` tree, **`19:27:33Z`** (`D17_cdk14_oiii5/S1`, the arm's last cell); the driver closed at `19:28:34Z` |
> | last artifact of a **pre-registered** measurement | `v23_score.txt`, `19:28:59.424Z` |
> | write-up | began at **`19:31:15Z`**; every number below was re-derived after that instant by an agent that launched **no** `TestApp.exe`, ran **no** build and **no** test, and wrote nothing under `gate/`, `v23/` or `exe/` |
>
> Wave 19's results doc was falsified by an artifact created 33 seconds before its own commit. The mechanical fix
> adopted in wave 20 and repeated here is that the listing comes first and its timestamp is printed.
>
> **OPEN at `19:29:42.641Z`, marked in place and not reported as done:**
>
> - **the full suite by COUNT.** The pre-registration's own §11(8) records that its baseline of **3958** was taken
>   from the controller and *not verified by the design agent*. It has not been verified by this one either — the
>   analysis agent runs no measurement. **No suite claim is made anywhere in this document.** The last verified
>   count in the series is wave 22's **3933**, and `develop` post-merge **3922**.
> - **the commit, the push and the PR.** `HEAD` is the pre-registration `839c38b`; this document and the register
>   entries in §10 are uncommitted at the timestamp above.
> - **item L** (`lumos` / `Panos`) — **NOT RUN**, confirmed against the artifact listing: there is no
>   `/mnt/d/hf_w23/gap` directory and no `gap_manifest.tsv`. Only the unexecuted driver `gapprobe_w23.sh`
>   (`16:31:48Z`) exists. §9.2.

> ## PROVENANCE
>
> | input | value |
> |---|---|
> | vessel | **fresh branch `ghilios/synthetic-af-bank-followups-wave23`, cut from `develop` @ `3d370ff`.** PR #191 merged `12:50:41Z` as `d2f3400` (waves 13–21), PR #193 as `2623771`, PR #194 as `3d370ff`. **PR #192 (F70(b′)) was CLOSED — owner-rejected — and is not re-proposed anywhere in this wave.** There is no standing vessel to continue and no fourteenth section to drift into |
> | pre-registration | **`839c38b`, committed before any measurement.** The build followed it |
> | binary — **ONE**, never rebuilt mid-wave ([F53](followups.md)(c)) | `D:\hf_w23\exe`. `TestApp.dll` sha256 **`82491ac79ce3d9216b4fa0d32c3daeb41aee460e845377c77f4ec9572cfc9ccf`** · `NINA.Joko.Plugins.HocusFocus.dll` sha256 **`fe9db95c52f25e7e7fc6d9746a96986cfb545f0eb5011c4f03a8719292ddb998`** · `BuildId` **`7927513b6493438aa73da5b91715fe0d`**, novel against **twelve** recorded ids. **A THIRTEENTH BINARY** |
> | the apphost, a **sixth** counter-example | `TestApp.exe` sha256 **`dd7103c28cc610e72671534cf23fb9a58b5a303a19d8779545cb7418d4ce6ff7`** — byte-identical to wave 18's B1, wave 18's B2, wave 20's and wave 21's, on a binary that is none of them. **An exe hash is not a binary identity; hash the dlls** ([F66](followups.md)) |
> | freshness | `find Joko.NINA.Plugins -name '*.cs' -newermt '<build mtime>'` — **empty** |
> | the code delta this binary carries | `git diff --name-only 2623771..3d370ff -- '*.cs'` — **14 files, recorded verbatim** in `binary_provenance_w23.txt`; 8 outside the test project. Two of them are on the headless `optimize` path (§1.2) |
> | settings | `D:\hf_w11\pinned_settings_w11.json` md5 **`a67ffc06164c81613aef5c4f8324b9b8`**. **Not re-pinned** ([F63](followups.md)(b)) |
> | profile | `astrodet` `ce3f3e63-8fd3-4b72-a0ca-d90db9441382`. **Cardinality 1** across all 8 gate landings *and* all 38 arm reports, read as the binary resolved it, never as the driver typed it |
> | detector | `DetectorVersion` **2**, read as a **FIELD** on all 8 landings ([F66](followups.md)). `strings` appears nowhere in this wave |
> | concurrency | `ConcurrencyCheck = exclusive` on all 8, read **across** the arm, never from one landing |
> | `FitInputs` | cardinality **1** on the gate and cardinality **1** across the arm: `MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid` |
> | `synth-validate` spec | `<exe>\SynthBank\synthetic-bank-spec.json` sha256 **`bf10522e670a119e260a8b3d06c2cc6ec52ce513dde25ca9ca13f426c38672d6`** — **equal to the pre-registered constant**, and echoed back by the binary in all 38 reports (`specSha256` cardinality 1) |
> | banks | `D:\SyntheticAutofocusBank` (20 datasets), `D:\Autofocus Bank` |
>
> **Independent re-derivation.** Every clause count in §1, §3, §5 and §6 was re-read from the artifacts at
> write-up by a reader that shares no code with `score_v23_w23.py` or `score_p23_w23.py`, and returns the same
> numbers on every clause — **with one exception, which is a correction to the controller's own summary and not
> to the scorer**: `V23-H` is exactly zero on **34 of 36** cells, not 35 (§3.3).

---

## Status

| item | rule | verdict | what it establishes |
|---|---|---|---|
| the gate | **RULE G23** | **PASS** — 8 of 8 to 6 dp, **bit-identical at all sixteen digits**, on a **thirteenth** binary | The coordinate system is unmoved. §1 |
| PR #194's written inertness claim | **`G23-1`, free** | **MEASURED — it holds** | First measurement of `RunEvaluationData.cs:919-925`, on a binary carrying a per-candidate SHA256 **inside the search loop**. §1.2 |
| the three `PARAMS-DUMP` blocks | **`G23-P2a/P2e`** | **PASS — 8 of 8**, and the driver's own line printed **0 of 8** | Not a regression. A **`grep`** defect, one byte wide, latent since wave 11. **NEW ENTRY [F79](followups.md)**. §2 |
| the step recommender's field | **`G23-P3`** | **PASS — 8 of 8** present and finite | Establishes the field exists on the shipped `optimize` path. It thresholds no value |
| step-size accuracy across the bank | **RULE V23** | **`V-GAPPED`** — `V23-G` **S0 18/19 = 0.9474**, **S1 13/17 = 0.7647** | The named gap list **is** the deliverable (design §4.5). Five cells, **three distinct mechanisms**. §3, §4 |
| the reproduction control | **`V23-V5`** | **REPRODUCES** — all five values, across a binary change | `synth-validate` is stable from the twelfth binary to the thirteenth. Without this, nothing else in V23 is attributable. §3.1 |
| the recommender's target | **`V23-H`** | **`stepBehavioral == stepTheory` on 34 of 36 cells** | **The target is right. The recommender's own fixed point IS the physics.** What fails is the walk to it. §3.3 |
| pinned extreme values | **RULE P23** | **`P-SENSITIVITY-PINNED`** on P1, P2 and P3 | `P23-A` **24/440 = 0.0545** on the licensed primary; the owner's named case at **8 of 40 = 0.2000**, and **inert on 9 of 40 = 0.2250**. §5 |
| exposure / binning population | **`V23-E`, `V23-F`** | **REPORTED, NO BAR** — by pre-registration, and it is honoured | `sensitivityAtFloor` **8/53 = 0.1509** from a second instrument; binning **31/51 = 0.6078**. §6 |
| **item L** | — | **`L-NOT-RUN`** — named and priced | Two trap entries stay folklore for a tenth wave. **~12 m.** §9.2 |
| the σ / `grep` trap | — | **NEW REGISTER ENTRY, [F79](followups.md)** | A recorded trap in a **new and worse form**: `grep` fails **closed to zero**. §2 |
| F21's price | — | **CORRECTED — the published rate is not a `synth-validate` arm rate** | The arm was priced at ~35 m and took **2 h 04 m**. §9.1 |
| the suite | — | **CLOSED AFTER the timestamp: 3960 by COUNT** | Was OPEN in the box above; resolved by the controller at `20:19Z`. §12 |

> **The headline, stated narrowly and in the order the evidence supports it.**
>
> **The step recommender knows the right answer and fails to walk to it.** `V23-H` measures the recommender's own
> fixed point against the bank's render-spec physics on a **noiseless** curve and finds them **exactly equal on 34
> of 36 cells** — delta_fraction `0.0000`, not "close". The one dataset that differs, `D01_ultrawide_40mm`, differs
> by one focuser step (8 vs 9) in **both** scenarios. So the target is not in dispute and the arithmetic that
> computes it is not in dispute.
>
> Yet on the scenario that starts at **×0.25 of the correct step** — the far-from-target case, which is exactly the
> field case on an unfamiliar rig — the final recommendation lands inside the tolerance band of truth on only
> **13 of 17** setups, and the four misses undershoot by **4.5×, 3×, 4× and 3.9×**. All four had rounds left to spend.
> **Three different mechanisms produce those four misses, and all three are already in the register** — F25's
> missing fit-quality gate, F34's stop-on-no-op policy, and **F26's binning-first starvation, which this wave
> reproduces in its recorded unbounded form on the exact dataset and scenario of F26's own evidence table, after a
> 2026-08-03 re-measurement had downgraded the entry's title on the grounds that it did not reproduce.**
>
> **And the wave's own gate printed a false zero at itself.** `gate_w23.sh` reported `G23-P2 = 0 of 8` for all
> three `PARAMS-DUMP` blocks while the scorer read **8 of 8** on the same files in the same minute. One byte —
> a CP437 `σ` at `OptimizationDiagnosticRunner.cs:866` — makes `grep` classify the log as binary and report **no
> match for strings elsewhere in it**. The byte is in **8 of 8 gate logs of every wave from 11 through 23**. It is
> not a PR #194 regression, the product's search is unchanged, and **the pre-registered clause passed on its
> designated instrument and needed no repair.**

---

## §1 — RULE G23: **PASS**, on a thirteenth binary

`optimize --per-run --max-evals 250`, mixed real + synthetic, sequential, one `TestApp.exe`, no NINA, no fan-out,
both pins named in the driver header. `G23_START 16:38:49Z → G23_DONE 17:20:00Z` = **41 m 11 s** against a ~42 m
estimate (`gate_w23.log`) — a sixth wave at ~5.2 m/run.

### 1.1 The eight values

**`G23-1`: 8 of 8 to six decimals, and bit-identical at all sixteen digits.** Re-read from the landings at
write-up, `repr()` against the pre-registered K8 constants:

| run | `FinalJ` as stored | K8 | bit-identical |
|---|---|---|---|
| `toml999` | `0.9957838768299878` | `0.9957838768299878` | **yes** |
| `CWhiteFocus` | `0.9960675916058808` | `0.9960675916058808` | **yes** |
| `uneven` | `0.9963677194179505` | `0.9963677194179505` | **yes** |
| `muggsie` | `0.9971948738498605` | `0.9971948738498605` | **yes** |
| `mccomiskey` | `0.9767460801208465` | `0.9767460801208465` | **yes** |
| `D18_m24_deep_shed` | `0.9998815090506263` | `0.9998815090506263` | **yes** |
| `D19_cygnus_deep_shed` | `0.9994870586135448` | `0.9994870586135448` | **yes** |
| `D20_m24_bright_control` | `0.9997378027339423` | `0.9997378027339423` | **yes** |

`G23-2` aggregate summaries **8 of 8**, asserted in the driver before any scoring. `G23-P1` — the anti-leak
clause — reads `NoiseReductionRadius=3` in the `optimize/seed` block on **8 of 8**: `/mnt/d/hf_w18/part2_w18.patch`
has **not** leaked into the tree. `G23-P3` finds `RecommendedStepSize` present and finite on **8 of 8**
(`16, 101, 488, 550, 31, 18, 35, 19`) — the field goal 2 is about is produced by the shipped `optimize` path.
**P3 thresholds no value**, by pre-registration; that is RULE V23's job.

`G23-3a…3f`, the free controls read **across** the arm: `BuildId` cardinality 1, `ProfileId` cardinality 1,
`FitInputs` cardinality 1, `DetectorVersion` = 2 on all eight, `ConcurrencyCheck = exclusive` on all eight.

### 1.2 The free measurement: PR #194's inertness claim, tested for the first time

The design's §1.2(c) put the believed-inert change into the gate's binary on purpose, so that the gate's PASS
**is** the inertness measurement. Two files on this path changed between `2623771` and `3d370ff`:

| file | what changed | reaches the gate? |
|---|---|---|
| `StarDetection/Optimization/StarDetectionOptimizer.cs` (+142/−24) | a `StarDetector.ComputeEarlyCacheKey(p)` **SHA256 computed for every candidate inside the search loop**; `Emit` now fires per evaluation instead of per ten | **yes** — new work inside the search loop |
| `StarDetection/Optimization/RunEvaluationData.cs` (+43/−2) | `CreateEvaluator` gains an optional `IProgress<RunLoadProgress>`; the null path still calls the unchanged `EvaluateAsync(p, token)` | **yes** — the gate's caller passes no progress |

The source states its own belief at `RunEvaluationData.cs:919-925`: *"Passing null (every headless caller) leaves
the evaluation byte-identical."* **This is the first measurement of that sentence, and it holds — to sixteen
digits, on eight runs, five of them real-bank optics.** A per-candidate SHA256 added inside the search loop moved
no landing's objective by one bit.

**What it is not.** It is 8 runs × **1 field**. [F73](followups.md)'s code axis is 20 datasets × **33 fields**.
This is a slice, it is free, and it is reported as a slice. §9.3 carries F73's corrected price.

---

## §2 — The gate printed `0 of 8` at itself, and the cause is one byte

**This is the wave's tooling finding and it is registered as [F79](followups.md).**

`gate_w23.log` and `g23_p2p3.txt` were written eight minutes apart, over the same eight files, and disagree
completely:

| clause | `gate_w23.sh` (driver) | `score_v23_w23.py --gate` (the **designated** instrument) |
|---|---|---|
| exactly one `optimize/detected` block | **0 of 8** | **8 of 8 = 1.0000** |
| exactly one `optimize/baseline` block | **0 of 8** | **8 of 8 = 1.0000** |
| exactly one `optimize/seed` block | **0 of 8** | **8 of 8 = 1.0000** |

**The scorer is right and the driver's line is an artifact.** Verified at write-up, by bytes:

```
/mnt/d/hf_w23/gate/toml999.log : 29041 bytes, exactly ONE byte 0xE5, at offset 7975
context: b'UMP optimize/detected END\r\n  objective: marginalSnr strength=0 floor=6\xe5 threshold=0.05; searchable S'
```

- The byte is **CP437 `σ`**. `σ`.encode(`cp437`) is `b'\xe5'`; CP850 and CP1252 have no `σ` at all and would have
  produced `?`. So `Console.OutputEncoding` on this machine is the OEM console code page **437**, and the emitter
  is a bare `Console.WriteLine` at **`Joko.NINA.Plugins/TestApp/OptimizationDiagnosticRunner.cs:866`**
  (`floor={…}σ`), with a second `σ` ten lines later at `:875`.
- `0xE5` alone is **invalid UTF-8** (a continuation-range byte with no lead byte), so `grep` classifies the whole
  file as binary.
- **The three `BEGIN` lines are all *before* the offending byte** — offsets 1813, 3551 and 6179 against 7975 — and
  `grep` still finds nothing. The file is smaller than one read buffer, so the classification applies to the whole
  file regardless of where the match sits. **This makes the defect size-dependent**: on a larger log, matches in
  earlier buffers would survive and the same clause would silently start working again.

**The failure mode is what makes this worth an entry, and it is worse than the trap already on the books.**

```
grep -c 'PARAMS-DUMP optimize/baseline BEGIN' toml999.log   ->  prints NOTHING, rc=1
grep -ac 'PARAMS-DUMP optimize/baseline BEGIN' toml999.log  ->  prints 1,      rc=0
```

`grep` does not say *"binary file matches"*, does not print `0`, and does not warn. It exits 1 — **"no lines
selected"** — for a string that is demonstrably present. The driver's idiom
`grep -c "…" "$f" || true | grep -c '^1$'` then turns that silence into the number **0**, indistinguishably from
*"the block is genuinely absent"*. **It fails closed to zero: it reports "the feature is absent" rather than "I
could not look",** which is precisely the state every scorer in this series is built to keep separate.

The register's existing trap says a Unicode character *"arrives as the single byte `0x1A`"*. Both are true and
they are **different bytes with different consequences**, from one root cause:

| character | in CP437? | byte in the log | valid UTF-8? | effect on `grep` |
|---|---|---|---|---|
| `σ` U+03C3, `°`, `µ`, `²`, `±`, `≥` | **yes** | the high byte (`0xE5`, `0xF8`, `0xE6`, …) | **no** | **file classified binary — every match suppressed, rc=1** |
| `—` U+2014 (the recorded case) | **no** | `0x1A` (SUB, best-fit) | yes | text corrupted, `grep` keeps working |

**Why P1 passed while P2 failed, in the same script.** `G23-P1` extracts the block with `awk` **first** and pipes
the result to `grep`; `awk` does no binary classification, and the offending byte lies outside the extracted
range, so `grep`'s stdin is clean. `G23-P2` greps the raw file. *One clause was written awk-first by accident of
style and is immune; the other is not.*

**It is not a PR #194 regression, and the check is stronger than the controller's.** The byte is present in
**8 of 8 gate logs in every wave root that has one — waves 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21 and 23**
(twelve roots; wave 22 built no binary and has no gate). It has been latent since **wave 11**, not six waves. The
product's search is unchanged and **no pre-registered clause was harmed**: `G23-P2` passed on its designated
instrument, which reads bytes in Python, and needed no repair.

> **The trap caught the write-up too, and that is recorded rather than quietly fixed.** The first per-wave census
> written for this section used `grep -c $'\xe5'` and reported **0 logs containing the byte, in all twelve
> waves** — including the ones it was reading. The number was produced by the instrument, not by the data. The
> census was re-run in Python, on bytes, and is the table above.

**The fix, priced, and the judgement.** Two options, both **log-only**: the `σ` at `:866` sits inside a bare
`Console.WriteLine`; the statements that mutate search inputs are above it and the optimizer is constructed after
it (`:869`); nothing parses stdout, and no test or doc asserts on the string.

| option | change | reach | risk |
|---|---|---|---|
| **(i) ASCII the two literals** | `OptimizationDiagnosticRunner.cs:866,875`, **2 lines** | fixes the `optimize` console stream — the one every gate log is made of | none. Log text only |
| **(ii) `Console.OutputEncoding = new UTF8Encoding(false)`** | `TestApp/Program.cs`, **1 line** | fixes all **32** non-ASCII console sites across 9 files, and every future one | the setter can throw `IOException` with no valid console handle, so it wants a `try`/`catch`; existing CP437 logs stay broken |

**Judgement: it should NOT ship in this wave, and the reason is the fence, not the risk.** This wave's binary is
the one every measurement ran on; a thirteenth binary that differs from the one under test is not the binary
under test ([F53](followups.md)(c)), and no arm here is blocked by the defect. **Option (i) is the right change
and it belongs at the top of wave 24, before that wave's build** — 2 lines, and it retires a trap that has been
one `grep` away from falsifying a clause for twelve waves.

---

## §3 — RULE V23: **`V-GAPPED`**

`synth-validate`, one invocation per (dataset, scenario) cell, `--max-rounds 4` (the **shipped** default, not
wave 21's probe value of 2), 20 datasets × 2 scenarios = 40 cells, sequential, one `TestApp.exe`.
`V23_START 17:24:28Z → V23_DONE 19:28:34Z` = **2 h 04 m 06 s**. Priced at ~35 m; §9.1 has the correction.

### 3.1 The validity gates — all five pass

| gate | result |
|---|---|
| **`V23-V1`** | 38 manifest rows of 40 scheduled. **S0 scoreable 19, S1 scoreable 17** (`D11` excluded from every denominator by design), both **≥ 15**. Readable 38, COULD-NOT-LOOK 0 |
| **`V23-V2`** | `profileId`, `fitInputs`, `specSha256`, `maxRounds` — **cardinality 1 each**, read from the report as the binary resolved them. `specSha256` equals the pre-registered `bf10522e…`; `maxRounds` = 4 |
| **`V23-V3`** | one binary across the whole wave; the dll pair equals the gate's |
| **`V23-V4`** | `G23_PASSED` present, carrying `BuildId=7927513b…`, written by the scorer at `17:24:01Z` — **27 seconds before the arm started** |
| **`V23-V5`** | **the `D11` reproduction control REPRODUCES, all five values** |

**`V23-V5` in full, because the rest of the rule depends on it.** Wave 21 published `D11_rc10_585_afbin2`/`S1` on
the **twelfth** binary; this is the **thirteenth**:

| | wave 21, published | wave 23, measured |
|---|---|---|
| trajectory | `14 → 24 → 41` | bootstrap `[14, 24]`, recommended `[24, 41]` |
| `roundsUsed` | 2 | **2** (so the control is COMPARABLE, not the third state) |
| `converged` | true | **true** |
| `finalStepSize` | 41 | **41** |
| `stepBehavioral` / band | 55 / 22 | **55 / 22** |

**The step recommender does not move under a rebuild.** Everything V23 reports about accuracy on the other
nineteen setups is therefore attributable to the recommender and not to the binary.

### 3.2 The measurement clauses

| clause | S0 | S1 |
|---|---|---|
| **`V23-G`** — `\|finalStepSize − expectedStepSize\| ≤ max(0.5, 0.4·\|expected\|)`, **against TRUTH** | **18 of 19 = 0.9474** | **13 of 17 = 0.7647** |
| **`V23-A`** — within the harness's own band of `stepBehavioral` (self-consistency) | 18 of 19 = 0.9474 | 13 of 17 = 0.7647 |
| **`V23-B`** — converged **with a finite band** | 18 of 19 = 0.9474 | 13 of 17 = 0.7647 |
| **`V23-D`** — `overallVerdict == Pass` | 15 of 19 = 0.7895 | 13 of 17 = 0.7647 |

**`V23-C`, the coverage census: 519 of 538 assertion instances at Pass = 0.9647**, per `id`:

| id | rate | the failures, named |
|---|---|---|
| `A1` | 103 of 112 = 0.9196 | `D12/S0` exposure and detectionBinning left their bands from target; **`D16/S0` step: started at target (15 vs 15) but recommended 22**, then `22 → 22` made no progress |
| `A2` | **72 of 72 = 1.0000** | — |
| `A3` | 32 of 36 = 0.8889 | the four S1 misses, each `final step X outside [0.6,1.6]× step_behavioral` |
| `A4` | 52 of 53 = 0.9811 | `D03/S1` `WasCapped=False` but truth predicts True (halfWidth 46.1 vs cap boundary 24, ratio 1.92) |
| `A5` | **159 of 159 = 1.0000** | — |
| `A6` | 50 of 53 = 0.9434 | `D01/S0` R²=0.8858, `D02/S0` R²=0.9156 (flags); **`D02/S1` R² = −0.2741** (fail) |
| `A7` | 51 of 53 = 0.9623 | `D01/S1` and `D02/S1`, `\|vertex−center\| = 8` against a tolerance of 4 |

**The branch table, applied as written:** all validity gates pass, `V23-G` is not 1.000 on both scenarios, and it
is not below 0.500 on either. **`V-GAPPED`.** Per design §4.5 the named list **is** the deliverable, and §4 below
is that list.

> **The trap the pre-registration caught was worth the whole rule.** `synth-validate` exits **1** when any cell
> ends `overallVerdict == Fail`. Wave 21's driver pattern is *"non-zero exit ⇒ no manifest row ⇒ NOT-RUN"*.
> The four rows carrying exit 1 in `v23_manifest.tsv` are **exactly** `D01/S1`, `D02/S1`, `D03/S1` and `D08/S1` —
> **all four S1 misses**. Inheriting wave 21's rule would have deleted every one of them, printed
> **`S1 13 of 13 = 1.000`**, and reported that the recommender is perfect in the correct-a-bad-step direction.
> The entire finding in §4 would have been invisible. *A scoreability rule inherited from a different command is
> not a scoreability rule.*

### 3.3 `V23-H` — the recommender's target is right, and this is the load-bearing number

`stepBehavioralVsTheoryDeltaFraction` compares the shipping recommender's **own fixed point**, iterated on the
analytic **noiseless** truth curve, against the bank's render-spec physics. Detector removed, noise removed.

**Exactly `0.0000` on 34 of 36 cells.** The two exceptions are the **same dataset in both scenarios**:
`D01_ultrawide_40mm`, `delta_fraction = −0.1111`, `stepTheory 9.0` vs `stepBehavioral 8.0` — one focuser step, on
the 40 mm ultrawide.

> **This corrects the controller's summary, which reported 35 of 36 and named one differing cell.** It is 34 of
> 36, and `D01` differs in **S0 and S1 both**. The substance is unchanged and slightly strengthened: exactly one
> *dataset* disagrees with the physics, and it disagrees identically in both directions, which is what a fixed
> property of that dataset's geometry should look like.

**So the recommender is not aiming at the wrong target.** `V23-A` and `V23-G` return the *same* rates on both
scenarios, which the design fixed in advance as the diagnostic decomposition: the recommender is failing to reach
its own fixed point, and that fixed point is the physics. **The defect is in the walk, not in the arithmetic.**

---

## §4 — The named gaps, with costed bridge proposals

`V-GAPPED`'s obligation (design §4.5). **Five named cells, and they are not one defect — they are three, each
already owned by a register entry.** The mechanism for each was read from the round records at write-up and
confirmed against source.

| cell | truth | final | ratio | rounds used / 4 | mechanism |
|---|---|---|---|---|---|
| `D01_ultrawide_40mm`/S1 | 9 | **2** | 0.22× | **1** | **A** — degenerate-fit stall |
| `D02_rich_135mm`/S1 | 6 | **2** | 0.33× | **1** | **A** — degenerate-fit stall (R² = −0.2741) |
| `D03_redcat_250mm`/S1 | 16 | **4** | 0.25× | **1** | **A** — degenerate-fit stall (halfWidth `NaN`) |
| `D08_c11_2800mm`/S1 | 82 | **21** | 0.26× | **4** | **B** — binning-first starvation ([F26](followups.md) verbatim) |
| `D16_esprit550_ha3`/S0 | 15 | **22** | 1.47× | 2 | **C** — do-no-harm drift |

### 4.1 Mechanism A — the degenerate-fit stall: the loop stops with three quarters of its budget unspent

Three cells, all S1, all `roundsUsed = 1` of a permitted 4:

```
D01/S1  r0: bootstrap 2 -> recommended 2   halfWidth 6.088   A7: |vertex-center|=8 (tol 4)
D02/S1  r0: bootstrap 2 -> recommended 2   halfWidth NaN     A6: R^2 = -0.2741   binning: hasMeasurement FALSE
D03/S1  r0: bootstrap 4 -> recommended 4   halfWidth NaN     A4: truth predicts a CAPPED growth to 46.1
stoppedReason: "stalled (round applied nothing, but step X is outside the N tolerance band of
                step_behavioral Y) - a no-op recommendation from a degenerate fit, not convergence"
```

**The chain, end to end, and every link is either measured here or quoted from source:**

1. S1 sets the bootstrap step to **×0.25** of correct. With `DefaultOffsetSteps = 4`, the sweep spans
   `±4 × step`, so at a quarter step the modelled HFR across the whole sweep reaches only ≈ **1.28×** the minimum.
2. A fit over a 1.00 → 1.28× HFR range is ill-conditioned. **`R² is no defence`** — [F21](followups.md)'s wave-10
   diagnosis already established that *"R² measures fit to the SAMPLED points and says nothing about whether the
   vertex is identifiable from them, which on a sweep that never leaves the focus zone it is not."* On `D02/S1`
   R² goes **negative** — the fit is worse than a horizontal line.
3. `StepSizeRecommender.Recommend` therefore returns `Degenerate(currentStepSize, …)`
   (`StepSizeRecommender.cs:410-416`), from one of four conditions (`:233`, `:239`, `:252`, and the `NaN` from
   `FindHalfWidth:470`). **`Degenerate` is literally `StepSize = ClampStep(currentStepSize)` — it HOLDS the step
   that produced the unusable fit.**
4. The driver applies a step on a bare integer inequality — `if (stepRec.StepSize != state.StepSize)`
   (`SynthValidateRunner.cs:843-847`). **There is no deadband and no hysteresis**; "applied nothing" means
   precisely "the recommender returned the current step".
5. `AppliedAnything == false` **always breaks the loop** (`SynthValidateRunner.cs:532-548`). Rounds 2, 3 and 4 are
   never spent.

**This is [F34](followups.md)'s fix behaving exactly as it was written** — *"a no-op round still STOPS the loop —
the recommender is not going to move on its own"*. That reasoning is correct when the no-op means agreement. **It
is exactly wrong when the no-op means the fit was unusable**, because the reason the fit was unusable is that the
sweep is too narrow, and holding the step guarantees the next sweep is just as narrow. The recommender refuses to
widen precisely when widening is the only thing that can help.

**And [F25](followups.md) already names the missing guard from the other side.** F25 is the too-**wide** case —
*"there is nothing that recognises 'this fit is garbage, retreat'"* — and its next step is *"gate the
recommendation on fit quality … a fit below some floor should either hold the current step or shrink it, never
widen it."* **Wave 23 measures the mirror image and shows that "hold" is the wrong default at the narrow end.**
The correct rule is directional: *a degenerate fit should move the sweep toward more information, which is wider
when the sampled HFR range is too small and narrower when it is too large.* The sampled range needed to decide
that is already in the report — `stepRecommendation.sampledHfrRange`.

**Bridge A — proposed, priced, and measurable.**

| | |
|---|---|
| **shape** | In `StepSizeRecommender`, when a `Degenerate` path is taken, return a **widened** step (the existing growth cap, ×1.714) instead of the current one **when `sampledHfrRange` shows the sweep never left the focus zone**; keep the hold/shrink behaviour otherwise. Add the taken branch to the round record as a first-class field so a scorer can read it. Separately, in `SynthValidateRunner`, do **not** break the loop on a no-op that came from a degenerate fit — a no-op from *agreement* and a no-op from *degeneracy* are different states and are currently byte-identical, which is [F34](followups.md)'s own recorded observation |
| **price** | ~15–20 lines of product code across `StepSizeRecommender.cs` and the runner's stop branch, plus 2 unit tests. **~1 h** including tests |
| **how it is measured** | Re-run the three named cells (**~5 m**) for the mechanism, then the full S1 arm for the **rate** (~1 h at this wave's measured rate, §9.1). The bar is pre-registrable from this wave: `V23-G` on S1 must exceed **13 of 17**, and the three named cells must reach a `roundsUsed > 1` |
| **risk** | Widening on a degenerate fit is the behaviour [F25](followups.md) recorded as harmful **at the wide end**, so the gate must be on `sampledHfrRange`, not on R². The two halves must ship together or the fix trades one entry's defect for the other's |

### 4.2 Mechanism B — `D08_c11_2800mm`/S1 is [F26](followups.md), reproduced in the form the register had retired

**The controller's summary describes this cell as having *"reached `--max-rounds (4)` while still climbing"*.
It was not climbing. It never moved at all.**

```
r0: bootstrap 21 -> recommended 36   halfWidth 126.0  wasCapped=True  ratio 1.7142857  binning factor 1  vertexHfr 4.455
r1: bootstrap 21 -> recommended 36   halfWidth 126.0  wasCapped=True  ratio 1.7142857  binning factor 2  vertexHfr 4.661
r2: bootstrap 21 -> recommended 36   halfWidth 126.0  wasCapped=True  ratio 1.7142857  binning factor 1  vertexHfr 4.149
r3: bootstrap 21 -> recommended 36   halfWidth 126.0  wasCapped=True  ratio 1.7142857  binning factor 2  vertexHfr 4.750
terminal: finalStepSize 21, expected 82, converged=false, "reached --max-rounds (4)"
```

**Four rounds. The same recommendation, 36, computed four times and discarded four times. The bootstrap never
leaves 21.** Contrast the control `D11`/S1, where the recommendation *is* applied: `r0` bootstrap 14 → rec 24,
`r1` bootstrap **24** → rec 41.

**The mechanism is in the code and in the data, and they agree.** `SynthValidateRunner.cs:829` branches on
`binningDiffers`; its `else` branch (`:836-848`) holds **both** exposure and step, under the comment *"Binning
first: apply ONLY the binning change this round and defer exposure (and step) by one round"*. The binning
recommendation is `clamp(round(vertexHfr/3), 1, 4)`, and this dataset's `vertexHfr/3` is
**1.485, 1.554, 1.383, 1.583** — straddling the **1.5** rounding boundary. So the factor oscillates `1, 2, 1, 2`,
`binningDiffers` is true on **every** round, and the step is deferred on every round, forever.

That is [F26](followups.md)'s title — *"A stuck binning recommendation starves the step update indefinitely"* —
and [F22](followups.md)'s population, *"boundary rigs get the wrong factor"*, both verbatim. **F26's own evidence
table is this same `D08_c11_2800mm` S1, bootstrap 21, correct 82, four rounds, step held at 21.**

> **The register must be corrected, not merely extended.** On 2026-08-03 F26 was re-measured, the livelock did not
> reproduce (`21 → 21 → 36 → 62`, converged in 3 rounds), and the entry's **Status revision** downgraded the word
> *"indefinitely"* in its title to *"for at least one round, unbounded in principle"*. **Wave 23 reproduces the
> unbounded form on the thirteenth binary, at the shipped `--max-rounds 4`, on the same dataset and scenario, with
> the same numbers.** The downgrade was the outlier, not the headline. §10 reverts it.
>
> F26's re-measurement explains its own miss, and this wave supports the explanation: *"the landed Sensitivity
> varied round to round here (16.7 / 15.7 / 10) rather than sitting at the floor, so the binning recommendation
> settled instead of being persistently wrong."* Here it does not settle. `D08/S1` round 2 carries
> `sensitivityAtFloor = true`. **That link is one round of four and is offered as consistent, not as
> established** — the oscillation across the 1.5 boundary is measured directly and needs no help from it.

**Bridge B — and it is the cheapest fix in this document.**

| | |
|---|---|
| **shape** | F26's own next step, still owed since 2026-08-03: **bound the deferral.** If the binning recommendation has not changed the *applied* value for N consecutive rounds (N = 2), stop deferring and let the step update proceed. Independently worth adding hysteresis to `RecommendFromHfr` so a value oscillating across a rounding boundary cannot flip the factor every round ([F22](followups.md)) |
| **price** | The deferral bound is **~10 lines and one test, ~30 m**. The `RecommendFromHfr` hysteresis is a separate, larger change that owes a coordinate-system re-baseline and should **not** be bundled |
| **how it is measured** | `D08`/S1 alone reproduces it in **~10 m** and is a complete before/after. The pre-registrable bar: `roundsUsed ≤ 3` with `finalStepSize` inside `[49.2, 131.2]` |
| **caveat that must be recorded** | **The report cannot currently prove the mechanism from its own fields.** `binningRecommendation` carries only `{hasMeasurement, recommendedFactor, vertexHfr}` — there is **no `appliedFactor` and no `currentFactor`**. The starvation is inferred from the bootstrap step never moving. Adding `appliedFactor` to the round record is ~2 lines and makes this readable instead of inferable |

### 4.3 Mechanism C — `D16_esprit550_ha3`/S0: the do-no-harm direction, and the only S0 miss

```
r0: bootstrap 15 (= TRUTH) -> recommended 22   halfWidth 78.056   [applied]
r1: bootstrap 22           -> recommended 22   halfWidth 78.407   -> no-op -> "stalled"
```

**Here the loop works correctly and the recommender is simply wrong.** Handed the exactly-correct step, it walks
away from it to 22 (+47 %) and then holds there, stably, on two independent noise realisations (78.06 and 78.41 —
the answer is reproducible, not jittery). `A1` fires twice, in the two forms the design wanted separated:
*started at target but recommended 22*, then *22 → 22 made no meaningful progress toward target 15*.

**This is [F21](followups.md)'s family, not the loop's.** The measured half-width is ~1.49× the half-width truth
implies for a step of 15, on a **3 nm Hα** dataset — the lowest-SNR member of the bank. It is not a stall in the
mechanism-A sense even though it reports the same `stoppedReason`, and conflating the two would cost a later wave
its diagnosis.

**Bridge C — and the honest recommendation is to NOT fix it yet.** A half-width bias on one low-SNR dataset is
n = 1. F21's own headline case did not reproduce, and [F68](followups.md) forbids a bar over a population nobody
has shown exists. **What this wave licenses is the population, not the fix**: `V23-H` proves the target is right,
so the residual is a measurement bias in `FindHalfWidth` under low SNR, and the arm that would size it is *S0
across the bank at several noise seeds* — which needs a seed override `synth-validate` does not currently expose
([F21](followups.md), *"seed sensitivity remains UNMEASURED"*). **Priced at ~1 h of arm plus an unknown amount of
product work to expose a seed, and named as not-this-wave.**

### 4.4 What is common to all three, stated once

**Every one of these five cells is a case where the product had the information it needed and did not use it.**
`V23-H` says the target is right on 34 of 36 cells. The growth cap allows a 4× climb in **three** rounds
(`log 4 / log 1.714 = 2.57`), and the budget was **four** — so on `D08` the budget was never the binding
constraint, and on `D01`/`D02`/`D03` three quarters of it went unspent. **No gap in this list is a gap in the
physics or in the arithmetic. All five are policy: what the recommender does when it cannot see.**

---

## §5 — RULE P23: **`P-SENSITIVITY-PINNED`**, on all three populations

Zero TestApp minutes. The bounds table is **source-derived**; the fence in design §2 forbade reading any landing
value before the rule was committed, and it bound this agent too.

### 5.1 The licensed primary, P1 — wave 18's 40 arm landings, 20 setups, one pinned configuration

| clause | result |
|---|---|
| **`P23-V1`** | 40 found, 40 readable, 40 expected |
| **`P23-A`** | **24 of 440 = 0.0545** pinned axis-instances / (resolved searched axes × landings) |
| **`P23-B`** | **NEVER MOVED (landed == seed): 0.** DRIVEN TO THE BOUND: **22.** Seed-indeterminate (`MinHFR`): 2 |
| **`P23-C`(i)** | `BrightnessSensitivity` exactly at the resolved floor: **8 of 40 = 0.2000** |
| **`P23-C`(ii)** | at floor by the **product's own** predicate (`≤ 1.0`, `ExposureRecommender.cs:462`): **8 of 40 = 0.2000** |
| **`P23-C`(iii)** | **`EffectiveSensitivityGate` EXCEEDS it — the nominal pin is INERT: 9 of 40 = 0.2250** |

Per axis: `BrightnessSensitivity` **9**, `StarClippingMultiplier` **9**, `HotpixelThreshold` 2, `MinHFR` 2,
`MaxDistortion` 1, `MinStarBoundingBoxSize` 1. **11 of 20 setups carry at least one pinned axis.**

**Branch table, applied as written:** V1–V3 pass on P1; `P23-A` is not 0; `P23-C`(i) is > 0 →
**`P-SENSITIVITY-PINNED`**. The owner's named case is real and it is reported as itself.

### 5.2 The three things this rule actually establishes

**(1) `P23-B` is the clause that makes the number mean anything, and its answer is unambiguous: 0 never-moved,
22 driven.** Not one pinned instance is a seed sitting where it started. **Every pin is a search outcome** — the
optimizer walked each of these axes to its bound. A pinning rate over landings could have been an artifact of
seeding; it is not.

**(2) The two most-pinned axes are exactly the pair [F6](followups.md) says cannot act alone.** F6's ablation on
`mccomiskey` σ_focus:

| | `clip 2` | `clip 10` |
|---|---|---|
| **`sens 10`** | 1.648 | 2.960 |
| **`sens 33.3`** | 1.503 | **0.441** |

*"Neither knob does anything useful alone — the objective surface is a **diagonal valley**, not two independent
axes."* `BrightnessSensitivity` and `StarClippingMultiplier` pin **9 times each**, more than every other axis
combined. **A coordinate-wise search on a diagonal valley walks to the walls**, and that is what 22-of-22 driven
pins on those two axes look like.

**(3) And on 9 of 40 landings the pin is INERT — which is the part the clause-as-posed would have got wrong.**
`EffectiveSensitivityGate = max(Sensitivity, PeakResponse × MinEffectiveClipMultiplier)`
(`StarDetector.cs:1532-1533, 1552-1553`); the source's own worked example is a landing reading `Sensitivity 0.0`
with `PeakResponse 0.98 × StarClip 10.0` **enforcing a gate of 9.81**. So *"sensitivity is pinned at 0"* is, on
these landings, **more often a cosmetic reading than a behavioural one** — (iii) at 0.2250 slightly exceeds (i) at
0.2000. **The honest answer to the owner's goal 3(a) is two numbers, and it always was.**

> **Where this could over-claim, and does not.** It is tempting to conclude that the search walks an *inert* axis
> to its bound because the axis is a flat direction — that `J` is indifferent, so the optimizer's coordinate
> descent slides to the wall and the landing records a value that changes nothing.
> [F32](followups.md) (*`J` saturated near 1.0*), [F6](followups.md) (the diagonal valley) and
> [F8](followups.md)/[F73](followups.md) (two knob vectors with the same `J` to sixteen digits is exactly what a
> flat valley produces) all point that way, and it would explain both (2) and (3) with one mechanism.
> **This wave cannot say it.** The landings carry no per-axis objective sensitivity, so *"pinned because flat"*
> and *"pinned because the bound is genuinely optimal"* are not separable from any field P23 reads. **What would
> separate them is a one-axis-at-a-time perturbation of the landed vector, re-evaluating `J` at each bound
> ±1 step** — an `af-fit`-shaped arm, ~30 m over the 40 landings, with no new product code. It is registered as
> the arm this finding owes, and §11 prices it against the alternatives.

### 5.3 The two labelled secondaries, which cannot change P1's verdict

| | P2 — 42 class-1 bank landings, **heterogeneous and historical** ([F15](followups.md)) | P3 — this wave's 8 gate landings, **the only population produced by the binary under test** |
|---|---|---|
| `P23-A` | 5 of 429 = **0.0117** | 3 of 88 = **0.0341** |
| `P23-B` | 0 never-moved, 5 driven | 0 never-moved, 3 driven |
| `P23-C`(i)/(iii) | 1 of 39 = 0.0256 / 0.0256 | 1 of 8 = 0.1250 / 0.1250 |
| verdict | `P-SENSITIVITY-PINNED` | `P-SENSITIVITY-PINNED` |

**Three P2 landings are COULD-NOT-LOOK by name** — all three under `_prior_reports/deleted_example_runs_20260730/`
— with no `Provenance.CommandLine`, so the searched-axis set is not resolvable. They are **outside every
denominator above, and the denominators are printed**. They are never defaulted to 12 silently.

`P23-D` (the 13 unsearched keys) and `P23-E` (the boolean axes) are reported as **labelled diagnostics with no
bar, ever** — both have values foreclosed by arithmetic on 100 % of any population that can exist. That is
design §7.3's deliberately foreclosed branch, and it is honoured here rather than quietly turned into a rate.

---

## §6 — Goal 3(b): the first denominatored exposure and binning populations. **No bar, and none is invented.**

Design §4.4 fixed in advance that `V23-E` and `V23-F` carry **no threshold**, because there is no prior for what
fraction of setups *should* be capped and inventing one now is the `S16-A(b)` defect. **This section reports and
does not act.**

### 6.1 `V23-E` — exposure, and it corroborates P23 from a different instrument on a different population

| statistic | value |
|---|---|
| rounds with `sensitivityAtFloor` (denominator = **all** rounds) | **8 of 53 = 0.1509** |
| of those 8: `hasRecommendation` | 8 of 8 = 1.0000 |
| `exposureIsNotTheLimit` | **6 of 8 = 0.7500** |
| `wasCapped` | 1 of 8 = 0.1250 (`D10_rc16_3250mm_sparse`/S0 r0) |
| `cappedByAbsoluteLimit` | 1 of 8 = 0.1250 |
| `increasesExposure` | 1 of 8 = 0.1250 |
| `capLimitsRecommendation` (reconstructed, `[JsonIgnore]`) | 0 of 8 = 0.0000 |
| `starCountIsTheLimit` / `starFieldIsExhausted` | 0 of 8 |

The two denominators are **never pooled**, because `computed == sensitivityAtFloor` gates the whole
recommendation (`SynthValidateRunner.cs:794, 800`).

**What this licenses about goal 3, said exactly.** P23 measured the sensitivity floor on **landings** (20 % of 40,
population P1, wave 18's arm) using the *product's own* `≤ 1.0` predicate. `V23-E` measures it on **rounds** of a
different instrument, on a different population (53 rounds of this wave's own arm, thirteenth binary), and finds
**15.09 %**. *Two instruments, two populations, the same direction and the same order of magnitude.* Design §4.4
predicted the connection from source — the exposure recommender only computes anything when
`SensitivityIsAtFloor`, the same predicate P23-C(ii) applies — and the data is consistent with it.

**What it does NOT license.** These are not the same denominator and must never be averaged: one counts landings,
the other counts rounds, and 6 of the 8 at-floor rounds come from just three datasets (`D10` ×3, `D16` ×2,
`D08` ×1, plus `D09` and `D12`). **Neither number is a rate over *setups*.** And on **6 of 8** of them the product
itself reports `exposureIsNotTheLimit` — i.e. where sensitivity does bottom out, the product's own diagnosis is
that longer exposures are not the remedy, which is [F49](followups.md)'s recorded shape. **No bar. A later wave
writes one, on this population.**

> **One co-occurrence worth naming and not over-reading.** `D16_esprit550_ha3`/S0 carries `sensitivityAtFloor` on
> **both** of its rounds — and it is also the **only S0 cell that misses the step target** (§4.3). Goal 2's
> do-no-harm failure and goal 3's pin land on the same cell. **n = 1.** It is recorded as a hypothesis for the
> low-SNR arm in §4.3, not as a finding.

### 6.2 `V23-F` — binning, a large number nobody asked for, and what it would take to bar it

**Rounds with a usable recommendation: 51 of 53 = 0.9623.** Of those, **PINNED at a clamp bound: 31 of 51 =
0.6078**. Two rounds are COULD-NOT-LOOK (the R² < 0.9 gate or a null block), **named and out of the denominator**.

**Reported as the scorer emitted it, under the predicate fixed in the pre-registration.** And because design §4.4
says in terms that this clause exists so that *a later wave can write a bar on it*, the number is decomposed here
for that wave — **this is a decomposition of an unbarred reported number, not a re-score, and it changes no
verdict.**

`RecommendFromHfr` is `clamp(round(hfr/3), 1, 4)`. The clamp **actually clips** only when `round(hfr/3)` falls
outside `[1, 4]` — i.e. when `hfr/3 < 0.5` or `hfr/3 ≥ 4.5`. The pre-registered floor predicate is
`factor == 1 && hfr/3 ≤ 1.5`, which also admits `round(hfr/3) == 1`, a **free interior answer**:

| of the 31 | |
|---|---|
| ceiling, the clamp genuinely clipped (`f = 4`, `hfr/3 ≥ 4.5`) | **0** |
| floor, the clamp genuinely clipped (`f = 1`, `hfr/3 < 0.5`) | **17** |
| floor, `round(hfr/3) == 1` was the free answer (`0.5 ≤ hfr/3 ≤ 1.5`) | **14** |

So **0.6078 is a rate of "the answer sat at the extreme of the output range"**, and roughly **half** of it is the
recommender freely choosing factor 1 on rigs whose stars are small — which is correct behaviour, not a pin. **A
later wave's bar should be written on the 17, not the 31**, and the predicate it needs is `hfr/3 < 0.5`.
*Nothing here is a defect in the clause: design §4.4 warned that "nothing in the report distinguishes a clamp
from a free interior answer, so the clamp must be reconstructed", and this is the reconstruction one level
finer.*

**What barring it would cost.** A bar needs a prior for how often a rig *should* land at factor 1, which does not
exist. The cheap way to get one is the pre-registered `--detection-binning` re-score of an existing arm, which
scores a bank run at another software binning without re-running it — **~0 TestApp minutes over saved landings**,
plus a rule. That is a genuine wave-24 candidate and §11 prices it.

---

## §7 — What the wave can now say, goal by goal

> 1. optimization + autofocus works on a wide range of setups, while making improvements to bridge detected gaps
> 2. accuracy of recommendations for step size
> 3. appropriate exposure-time adjustments, and specifically avoiding parameters pinned to extreme values

### Goal 1 — coverage, and the gaps

**CAN say.** A per-setup census over **19 synthetic setups × 2 scenarios**, 38 cells, on a thirteenth binary at
one pinned configuration. **`V23-C`: 519 of 538 assertion instances Pass = 0.9647**, with per-`id` rates and every
failure named. **`V23-D`: overall Pass on 15 of 19 (S0) and 13 of 17 (S1).** Two assertion families —
`A2` and `A5` — are **1.000 across the whole bank**. `D17_cdk14_oiii5`, nine waves of folklore as the
finds-zero-stars dataset, **completed both scenarios, passed both, and is measured rather than remembered.**
Five gaps are named, mechanised to source, and carry costed bridge proposals (§4).

**CANNOT say.** Nothing about setups outside the synthetic bank. Nothing about the 22 real-bank runs beyond the
gate's 5 — **item L did not run**, so `lumos` and `Panos` remain folklore for a tenth wave (§9.2). Nothing at
other `--max-evals`, other profiles, or unpinned. And **two cells never produced a report** (§9.1) — `D19`/S1 and
`D05`/S1 are NOT-RUN and named, in no denominator.

### Goal 2 — step-size accuracy

**CAN say, and this is the wave's product result.** A rate with a named denominator, per scenario, never pooled:
**S0 18 of 19 = 0.9474** (do-no-harm), **S1 13 of 17 = 0.7647** (correct-a-bad-step). Against **TRUTH**, not
against the harness's own fixed point — and `V23-A` returns the same numbers, so the two are not in tension.
**`V23-H` establishes that the recommender's fixed point IS the render-spec physics on 34 of 36 cells**, which
localises every failure to the walk rather than the target. The four S1 misses undershoot **3–4.5×** with rounds
to spare, and decompose into **three** mechanisms, each already owned by a register entry (F25/F34, F26, F21). A
cross-binary reproduction control (`D11`) passes on all five published values, so none of it is a rebuild artifact.

**CANNOT say.** Nothing about `optimize`'s `RecommendedStepSize` **value** — `G23-P3` establishes only that the
field exists and is finite. Nothing across binning factors, where no `BestJ` may be quoted ([F62](followups.md)).
Nothing about seed sensitivity: both of wave 21's runs shared per-round seeds derived from the spec, and **nothing
in this wave varied one either**. And the S1 rate is measured at **×0.25**; a rig starting ×4 too wide is
[F25](followups.md)'s direction and is **not** in this wave's population.

### Goal 3 — exposure, and parameters pinned to extremes

**CAN say (a).** The first denominatored pinning audit, on a **licensed, pinned, 40-landing, 20-setup**
population: **24 of 440 = 0.0545** axis-instances pinned; per-axis and per-setup breakdowns; the owner's named
case at **8 of 40 = 0.2000**; and — the part that changes the answer — **the nominal pin is INERT on 9 of 40 =
0.2250**, because `EffectiveSensitivityGate` already gates harder. **`P23-B` settles the causal question: 0 of the
pins never moved, 22 were driven to the bound.** The two most-pinned axes are exactly [F6](followups.md)'s
inseparable pair.

**CAN say (b).** The first denominatored population of exposure and binning outcomes across the bank:
`sensitivityAtFloor` **8 of 53 = 0.1509** from a second instrument on a second population, corroborating (a) in
direction and magnitude; binning **31 of 51 = 0.6078** at an extreme of the output range, of which **17** are a
genuine clamp.

**CANNOT say.** **(b) carries no bar and this document does not act on it**, by pre-registration. Whether the
inert pins are a flat-direction artifact is **not decidable from any field P23 reads** (§5.2) — the perturbation
arm that would decide it is priced in §11. And nothing here says whether the *product's* exposure advice is
*good*; `V23-E` counts what the recommender emitted, not whether following it would have helped.

---

## §8 — Controls

| control | result | evidenced by |
|---|---|---|
| fingerprint class 1 — 42 bank landings | **42 of 42 byte-identical** BEFORE and AFTER | `bank_landing_fingerprint_BEFORE.json` (`16:38:02Z`); the AFTER check's **stdout was not captured to a file** — see the caveat below |
| class 2 — 59 aux files | **59 of 59** | `bank_aux_fingerprint_BEFORE.json` (`16:38:07Z`); same caveat |
| class 3 — 48 prior-wave arm landings (40 of them are P23's P1) | **48 of 48** | `w18_arm_fingerprint_BEFORE.json` (`16:38:08Z`); same caveat |
| class 4 — wave 21's 4 `f21`/`f21b` reports (new this wave) | **4 of 4** | `w21_f21_fingerprint_BEFORE.json` (`16:38:08Z`); same caveat |
| **P23 re-read after the arm** | **byte-for-byte identical.** `md5 d53631c10baf5ac11548505f12349311` on both | **`p23_score.txt` (`17:25:46Z`) vs `p23_score_AFTER.txt` (`19:29:42Z`) — `cmp` rc=0, verified at write-up.** This is the strongest preservation evidence in the wave: the *entire measurement*, not a hash list, reproduces after every arm |
| `--update-run-folder` | passed **nowhere** in this wave | the drivers |

> **A provenance caveat, recorded rather than smoothed over.** Four controls above are attested by their BEFORE
> snapshots plus the controller's report of the AFTER runs. **The AFTER checks left no artifact in
> `/mnt/d/hf_w23/`** — the only files in the root are the 23 listed at write-up, and none is a fingerprint AFTER
> log. The same applies to three demonstrations the design required: `prov_w23.py --self-test`,
> `score_v23_w23.py --self-test`, and **the G23 interlock's FAIL end** (`--arm-gate /mnt/d/hf_w19/gate`, and the
> `G23-P2` FAIL end whose `--out` was to be `g23_failend.txt`). **`g23_failend.txt` does not exist.** What *is*
> preserved, and is real: `G23_PASSED` exists, carries the BuildId, and was written at `17:24:01Z` by the scorer
> that re-read the gate — and the design's §8.2 makes that marker writable **only** on `rc == 0`. The FAIL end is
> therefore attested by the controller and **not** by an artifact. **A demonstration whose output is not kept is
> not a preserved demonstration**, and the fix is one `--out` path and one `tee` in the next wave's plan. This is
> [F75](followups.md)'s family: the interlock itself was held to the right standard this time; its
> *demonstrations* were not.
>
> **P23's byte-identical re-read is not subject to this caveat** and independently establishes that P1's and P2's
> populations did not move under this wave's arms — which is the property the fingerprints exist to show.

---

## §9 — Deviations, prices, and what did not run

### 9.1 The F21 rate is **not** a `synth-validate` arm rate, and the pre-registration walked into a documented trap from the wrong side

**Priced at ~35 m for 40 cells. Took 2 h 04 m 06 s.** `V23_START 17:24:28Z → V23_DONE 19:28:34Z`.

| | |
|---|---|
| pre-registered estimate | ~75–125 s **per dataset** (both scenarios) ⇒ **21–35 m** for 20 datasets |
| measured | **7 446 s** for 40 scheduled cells = **186 s/cell**, **372 s/dataset** |
| overrun | **×3.5** against the 35 m estimate (×3.0 against the 125 s/dataset upper bound, ×5.0 against the 75 s lower) |

> **The controller's summary records this as *"~8× over"*. It is **×3.5**.** The 8× figure does not reconcile with
> any denominator in the artifacts: 40 cells × 52 s = 34.7 m, against 124.1 m measured. The per-cell *maximum*
> approaches 8× (the two 600 s timeouts are ×11.5), but the arm's rate is 3.5×.

**Where the estimate came from and why it could not transfer.** Wave 21 measured F21 at **45–52 s** on
`D11_rc10_585_afbin2` — chosen explicitly *"for price, not physics"*, the bank's **smallest** dataset (38 MB) and
a **factor-2** dataset, i.e. detection on a **quarter of the pixels** — at **`--max-rounds 2`**. This arm ran
**mixed factor-1** datasets at **`--max-rounds 4`**.

**The handoff's own budget table warns about this axis, and only in one direction.** §5 records: *"a **factor-2**
dataset — neither rate describes it. Wave 20's probe came in at 16 s against a 3 m estimate, because a factor-2
dataset detects on a quarter of the pixels → budgeting a factor-2 arm at the synthetic rate **over**-reserves by
~10×."* **The inverse — pricing a factor-1 arm from a factor-2 measurement **under**-reserves — was never written
down**, and the pre-registration read the warning, cited the rate, and walked into its mirror image.

**The corrected row, with the denominators named, for the handoff's §5 table:**

| instrument | rate | caveat |
|---|---|---|
| `synth-validate`, **factor-2, `--max-rounds 2`, 1 dataset × 1 scenario** | **45–52 s** (wave 21, n = 1) | this is a **probe** price. It is not an arm rate |
| `synth-validate`, **mixed factor-1, `--max-rounds 4`, per (dataset, scenario) cell** | **~186 s/cell**, **~372 s/dataset** (wave 23, n = 40 scheduled / 38 landed) | S0 is one round, S1 is 1–4; the spread is **23 s to > 600 s** per cell. **Budget 2 h for a 40-cell arm**, and keep `timeout 600` |
| the general rule | **a rate measured at one binning factor does not transfer to another, in EITHER direction** | ×10 over at factor 2 from a factor-1 rate; **×3.5 under** at factor 1 from a factor-2 rate |

### 9.2 Two cells timed out, and item L did not run

**`D19_cygnus_deep_shed`/S1 and `D05_tec140_1000mm`/S1** both hit `timeout 600` (exit 124). **Neither produced a
`synth_validate_report.json`** — verified at write-up: 38 report files exist for 40 cell directories, and those
two directories contain only `run.log`. They therefore got **no manifest row**, are **NOT-RUN and NAMED**, and are
in no denominator. **38 of 40 rows.** Both scenarios still cleared their pre-registered minimum of 15 (S0 = 19,
S1 = 17), so the losses cost the rule nothing and shrank no denominator silently. *This is the driver contract
working exactly as pre-registered.*

**Item L — `L-NOT-RUN`, confirmed against the listing.** No `/mnt/d/hf_w23/gap`, no `gap_manifest.tsv`; only the
unexecuted `gapprobe_w23.sh`. Item L was **D1 in the drop order**, the first thing to be cut, and the arm's ×3.5
overrun consumed the window it would have used. **Price to run it: ~12 m** (~6 m per run, `optimize --per-run
--max-evals 250`, pinned, on `lumos` and `Panos`), plus the by-hand outcome assignment the design deliberately
specified instead of a scorer. `lumos` (recorded rc=3) and `Panos` (recorded degenerate σ fit, expected
**UNEVALUATED BY NAME**) stay folklore for a tenth wave.

### 9.3 F73's code axis remains open, and it is now correctly priced

The design's §1.2(a) correction stands and is recorded in the register (§10). Both authority documents price
F73's code axis at **~53 m**; that price silently assumes wave 19's `/mnt/d/hf_w19/reA0` can serve as the "old"
side, which it cannot — `reA0` was built from wave-19-era source, so the treatment would be *"everything shipped
between wave 19 and now"* (13 + 14 files across three waves plus F76's eight rounds, F77 and the replay work).
That is a confounded arm.

**The honest form is two binaries built from one tree and two 20-dataset passes: 2 × 52 m 43 s ≈ 1 h 45 m, plus
builds and provenance ≈ 1 h 50 m, plus a ~42 m gate.** And design §1.2(b) settles the *shape*: an **authored**
inert change (an unused method, a `Logger` line off the search path) would reproduce wave 19's own defect one
level up — it is a code difference that **cannot execute**, so the expensive branch is again reachable only from
noise, which R19 measured at exactly zero. **Only a real shipped delta whose changed code executes inside the
search loop makes the branch reachable by construction.** This wave's §1.2 gives the 8-run × 1-field slice of
that for free; it is not the arm.

### 9.4 Two deviations considered and DECLINED, recorded as considerations rather than as cleverness

**(1) Extending the arm's pre-registered deadline.** At `17:53Z`, with the rate visibly over budget, extending
`V23_DEADLINE_UTC` past `21:30Z` was considered and **declined**, on the grounds that adjusting a pre-registered
parameter with the data in hand is the [F14](followups.md)/D20 fence. **The record shows the deadline was never
reached**: the arm finished at `19:28:34Z`, **2 h 01 m 26 s inside it**, and all 40 scheduled cells were attempted
— no cell was refused for the clock. **So the decision cost nothing and was still the right one**; a deadline
adjusted under pressure is not a deadline, whether or not it later turns out to have been unnecessary.

**(2) Taking the pre-registered drop D2 (scenario S0).** Considered and **declined** because it would have made
the branch table's *"`V23-G` rate == 1.000 on **BOTH** scenarios"* clause unsatisfiable — a rule whose
`V-ACCURATE` branch cannot be reached is not a rule. Dropping S0 would also have removed the **do-no-harm**
direction entirely, and S0 is where the wave's only `A1` "started at target and left it" failure lives (§4.3).

---

## §10 — Register entries

**One new entry, four corrections, and a judgement about a fifth that is recorded rather than made.**

| entry | action | why |
|---|---|---|
| **[F79](followups.md)** — *A σ in a redirected log makes `grep` report zero matches for strings elsewhere in the file* | **NEW** | §2. A recorded trap in a **new byte and a worse failure mode**: `0x1A` corrupts text and `grep` keeps working; `0xE5` makes `grep` **fail closed to zero**. Twelve waves latent. Tooling, not product |
| **[F26](followups.md)** | **CORRECTED** — the 2026-08-03 status revision is **reverted** | §4.2. The unbounded livelock **does** reproduce, on the same dataset and scenario as the entry's own evidence table, on the thirteenth binary at the shipped `--max-rounds 4`. The word *"indefinitely"* in the title is supported again |
| **[F34](followups.md)** | **EXTENDED** | §4.1. Its fix — *"a no-op round still STOPS the loop"* — is measured with a denominator for the first time, and the stop is **wrong at the narrow end**: 3 cells stop at round 1 of 4, 3–4× below target |
| **[F25](followups.md)** | **EXTENDED** | §4.1. The still-owed fit-quality gate is now measured from the **mirror** side: at the narrow end the recommender *holds*, and holding guarantees the same degenerate fit next round. The gate must be directional |
| **[F21](followups.md)** | **COSTING CORRECTED** | §9.1. Extended rather than re-entered: it is the same subject (the price of this instrument) and the entry already carries three waves of price history. A new entry would split one number's provenance across two places |
| **[F73](followups.md)** | **COSTING CORRECTED** | §9.3. ~1 h 50 m of arms + a ~42 m gate, not ~53 m; and the arm's shape must be a real shipped delta |

> **A judgement recorded rather than acted on: the step-recommender stall does NOT get its own new entry, and
> here is why.** The obvious move is a fresh entry for *"the recommender applies a no-op while sitting 4× below a
> target it computed correctly"*. **F26's own text forbids it**, in terms: *"F18, F21, F25 and F26 are the same
> component read four ways, and a passing bank must not be read as four entries closed."* A fifth reading of the
> same component would make the register **harder** to act on, not easier — the three mechanisms in §4 map
> one-to-one onto F25/F34, F26 and F21, and each correction lands on the entry that owns the fix. **The new
> measurement is the denominator, and denominators belong in the entries whose claims they size.** If a later
> wave finds a mechanism that none of F18/F21/F25/F26/F34 owns, that is when a new number is earned.

---

## §11 — What wave 24 should be

**Does this wave produce a finding worth a register entry? Yes — three, and the register is not dry.**
One new entry ([F79](followups.md)), one **reverted** downgrade on a live product defect ([F26](followups.md)),
and the first denominatored measurements of both goal-2 accuracy and goal-3 pinning. The two-consecutive-dry-waves
stop condition is not armed.

**Recommendation: wave 24 is the BRIDGE wave — ship the §4 fixes and measure them.** Chosen by decision value
against the owner's three goals, not by proximity.

| | |
|---|---|
| **why** | Goal 1 is *"…while making improvements to bridge detected gaps"*. This wave produced five named gaps, three mechanisms, three costed bridges and a **pre-registrable bar for each** (§4). **Bridging them is the only item that advances goals 1 and 2 simultaneously**, and the measurement that scores it is already written, already pinned, and already has a baseline: `V23-G` on S1 must exceed **13 of 17**, `D08`/S1 must reach `roundsUsed ≤ 3`, and the three mechanism-A cells must reach `roundsUsed > 1` |
| **order** | **(0)** [F79](followups.md) option (i), 2 lines, **before the build** — it must be in the binary under test or it is not measured. **(1)** Bridge B (F26's deferral bound, ~30 m + a test) — cheapest, most certain, and `D08`/S1 alone is a complete before/after. **(2)** Bridge A (the directional degenerate-fit gate, ~1 h + 2 tests) — highest value, and it must ship with the runner's stop-branch half or it trades F25's defect for F34's. **(3)** add `appliedFactor` to `binningRecommendation`, ~2 lines, so mechanism B is *readable* instead of *inferable* |
| **price** | ~2 h of product work and tests; a ~42 m gate on the fourteenth binary; **~2 h** for the full 40-cell re-run at this wave's **corrected** rate (§9.1) — or **~1 h** for S1 alone, which is where the bar is. **≈ 5 h against a ~6 h ceiling**, and it is a full disciplined wave: pre-registration → gate → arm |
| **the control it must carry** | `D11`/S1 stays the reproduction control and its five published values must still reproduce **after** the fix, or the fix moved something it was not supposed to |

**Two alternatives, priced, and why each loses.**

- **F67's residual thread** (~30 m of `af-fit` at both binning factors + a rule) — design §10.1 names it *"the
  best candidate for wave 24"*. **It was, before this wave ran.** It serves goal 1 only indirectly, it carries an
  [F62](followups.md) landmine (**no `BestJ` across factors**), and it does not touch a single named gap. It stays
  the best *second* item and should be the tail of wave 24 if the bridges land early.
- **The P23 flat-direction perturbation arm** (§5.2, ~30 m of one-axis-at-a-time re-evaluation over the 40
  landings, no new product code) — it would settle whether the inert pins are a flat-direction artifact, which is
  the deepest open question in goal 3(a). **It loses on decision value, not on interest**: it changes what we
  *understand* about the pins, but nothing the owner would *do* differs until it is known, whereas the §4 bridges
  change what the product does on the next autofocus run. It is the strongest candidate for wave 25, and the
  cheapest genuinely-new arm now on the table.

**And two things wave 24 must NOT do.** It must not re-score or convert **RULE F14**, **RULE S16** or **RULE D20**
— three permanent fences. And it must not re-open **PR #192 / F70(b′)**, which the owner rejected.

---

## §12 — What was OPEN at the write-up timestamp, and how each closed

The box at the top of this document listed three things still open at `2026-08-12T19:29:42.641Z`. They are
resolved here rather than by editing that box, so the record of what was open **when the analysis was written**
stays intact. Everything below was done by the **controller**, after the analysis agent had finished and run
no measurement.

| was open | closed | evidence |
|---|---|---|
| **the full suite by COUNT** | **`3960` passed / `0` failed / `0` skipped**, `3 m 35 s`, read by **COUNT** out of the log ([F37](followups.md)) | `dotnet.exe test <sln> -c Debug --nologo`, finished `20:19Z`. **`3958` baseline + the 2 new `TestAppOutputAsciiTests`**, and the baseline itself was verified by COUNT **twice** on this tree — `15:59Z` (pre-wave, before the pre-registration commit) and `19:35Z` (after it, unchanged at 3958, proving the pre-registration moved no test) |
| **the commit, the push, the PR** | see the commit this document lands in | — |
| **item L** (`lumos` / `Panos`) | **still NOT RUN.** Unchanged, still priced at ~12 m | §9.2 stands as written |

**The suite baseline correction.** The box records the last verified count in the series as wave 22's **3933**
and `develop` post-merge **3922**. Both are now stale: PR #194 merged **25** further tests after the merge that
produced 3933. The current `develop` number is **3958**, controller-verified by COUNT, and **3960** on this
branch. Waves 24+ should baseline from 3960 and treat every number below it in older documents as stale.

### §12.1 — What SHIPPED, and the ship rule it was decided under

Wave 23 ships one change: **[F79](followups.md)'s fix** — every non-ASCII character outside a comment in
`TestApp`'s sources replaced with an ASCII spelling — plus a guard test.

**Under which rule?** **None of this wave's.** No clause of `G23`, `V23` or `P23` reads the σ, so the ship is
not what a rule decides, and §0.1 of the handoff permits an unconditional ship in exactly that case (wave 20
shipped D1/D2/D3 the same way). **It is explicitly NOT a verdict harvested from the wave's data**, and no
pre-registered bar was moved to accommodate it.

| | |
|---|---|
| scope | **120 lines, 158 characters, 20 files, 19 distinct characters** (`— σ ² ≥ ≤ → ⇒ χ Δ × µ ° ε θ ± « · … §`) |
| guard | `TestAppOutputAsciiTests`, 2 tests: the property, plus `TheScanner_FindsEveryShapeOfViolation_AndExemptsComments`, which pins the scanner against a synthetic snippet so the property cannot pass vacuously |
| the rule the guard asserts | **"outside a comment, every character is ASCII"** — deliberately NOT "on a `Console.Write` line". See the failure below for why |
| could-not-look | **four separate explicit failures**, all asserted **before** the violation list is read: directory missing, zero files, fewer than 40 files, four named sentinel files absent, fewer than 200 000 non-comment characters scanned |
| shown to FAIL pre-change | **verified by the controller, independently of the code agent.** One file (`OptimizationDiagnosticRunner.cs`) restored to `HEAD`'s version by byte copy: `Failed: 1, Passed: 1`, naming `OptimizationDiagnosticRunner.cs:866 col 143: 'sigma' (U+03C3)` — **the exact byte this wave measured** — with 14 violations from that file alone. The scanner self-test passed throughout, so the failure is the property and not the instrument. Restored from a byte backup and verified `cmp`-identical |
| mutation safety | **byte backup + `cp`, never `git checkout --`.** The tree carried 287 uncommitted register lines and an untracked 68 KB results document; a VCS revert would have taken them ([F53](followups.md)'s trap, and wave 20's) |
| TestApp compiles | **built separately, 0 errors 0 warnings** — because `dotnet test <sln>` **does not build TestApp**, so a compile break there would never surface in the suite. That is worth knowing on its own |
| the wave's binary | **untouched.** `D:\hf_w23\exe\TestApp.dll` still hashes `82491ac79ce3d9216b4fa0d3…`, checked after the TestApp build ([F53](followups.md)(c)) |

### §12.2 — The controller's own grep failed the way the defect does

This belongs in the record because it is the same failure twice, and the second one nearly set the fix's scope.

The controller scoped the change from `grep -P "[^\x00-\x7F]" *.cs | grep -E "Console\.(Write|WriteLine)|Emit\(|AppendLine\("` and got **35 lines**. The real answer is **120**. The 85 it missed fall into two classes, and neither is incidental:

1. **Continuation lines** of a multi-line `Console.WriteLine(… + …)` carry no `Console.Write` text of their own. **`OptimizationDiagnosticRunner.cs:866` — the very line emitting the `σ` this wave measured — was not in the 35.**
2. **Interpolation holes**: 7 violations live inside `{"tilt°",8}`-shaped holes, where a quote-parity scan flips polarity at the nested `"` and stops seeing the literal.

**A line-keyed grep, hunting a defect whose signature is failing closed to zero, failed closed on the defect's own line.** That is why the shipped guard asserts the property over *every non-comment character* rather than over lines that look like they print: **a guard against a fails-closed defect must not be able to fail the same way.** [F79](followups.md) carries this.

### §12.3 — One claim the controller made that was wrong, corrected here

The controller reported that `ParamsDump.cs`'s class doc — *"Every character this class prints is printable
ASCII, values included"* — was **false in the file that made it**. **It was not.** `Lines`/`Write` were already
guarded by an `Ascii()` helper with a test covering it, so the sentence was true **as literally worded**. The
single violation in that file is at line 182, in an `ArgumentException` message, which is **thrown, not
printed**. A near-miss, not a self-refuting file. The doc comment has since been widened to cover every string
literal in the class including that throw, which makes it unambiguous rather than merely lucky.
