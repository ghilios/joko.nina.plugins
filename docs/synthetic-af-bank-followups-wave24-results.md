# Synthetic AF bank — followups wave 24 (results)

**Artifact root at write-up: `/mnt/d/hf_w24/` — `2026-08-12 20:10:03.393328200 -0400` = `2026-08-13T00:10:03.393Z`.
Re-stamped `2026-08-13T00:12:11Z`** (`ls -la --time-style=full-iso /mnt/d/hf_w24/` then `date -u`, in that order;
the filesystem clock is local `-0400`, every driver clock in this document is UTC).

**The analysis agent listed the root TWICE, and both listings are recorded rather than the later one substituted
for the earlier.** The first was at **`2026-08-12T23:49:33Z`**, and at that instant **item L24 was still running**
(`TestApp.exe` pid 47964 live, `gap_manifest.tsv` 0 bytes) and there was **no `suite.log` at all**. Between the two
listings the controller closed L24, appended a correction to `binary_provenance_w24.txt`, and started the suite.
**Nothing in this document rests on the first listing being the final state**, and every row that was open at
either instant is marked open **in place** below. Wave 19's document was falsified by an artifact written 33
seconds before its own commit; re-listing rather than back-dating is the mechanical guard.

Pre-registration: [`docs/synthetic-af-bank-followups-wave24-design.md`](synthetic-af-bank-followups-wave24-design.md)
(commit **`82a3a0a`**, **before any wave-24 measurement existed**).
Plan: [`plans/synthetic-af-bank-followups-wave24-plan.md`](../plans/synthetic-af-bank-followups-wave24-plan.md).
Predecessor: [`docs/synthetic-af-bank-followups-wave23-results.md`](synthetic-af-bank-followups-wave23-results.md),
**especially its §13 scope correction**, which decides what P2 even is.
Charter: [`docs/waves22+-handoff-prompt.md`](waves22+-handoff-prompt.md),
[`docs/waves22-27-autonomous-prompt.md`](waves22-27-autonomous-prompt.md).
Register: [`docs/followups.md`](followups.md).
Controller's live deviation log: `/mnt/d/hf_w24/CONTROLLER_DEVIATIONS.md` (D1–D9, folded into §9).

> ## THE TIMESTAMP, AND WHAT IS STILL OPEN INSIDE IT
>
> | | |
> |---|---|
> | **artifact root** | **`2026-08-13T00:10:03.393Z`**, re-stamped **`00:12:11Z`** |
> | first listing, recorded and not discarded | **`2026-08-12T23:49:33Z`** — L24 running, no `suite.log` |
> | newest artifact of any kind | `binary_provenance_w24.txt`, **`00:11:30Z`** (the controller's appended CORRECTION block, §0.2) |
> | **newest artifact written by a `TestApp.exe`** | the `gap/` tree, **`23:56:08Z`** (`Panos`); the L24 driver closed at **`23:57:59Z`** |
> | last artifact of a **pre-registered** measurement | `d24_score.txt`, **`23:46:44Z`** |
> | write-up | every number below was read from artifacts by an agent that launched **no** `TestApp.exe`, ran **no** build and **no** test, and wrote nothing under `gate/`, `before/`, `after/`, `regress/`, `census/`, `gap/` or `exe/` |
>
> **OPEN at `00:12:11Z`, marked in place and not reported as done:**
>
> - **the full suite by COUNT.** `suite.log` exists (`00:10:34Z`) and the run is **in flight**: the build has
>   completed, `Test run for …Joko.NINA.Plugins.HocusFocus.Tests.dll` is present, and there is **no COUNT line and
>   no `SUITE_EXIT=`**. **No suite number is claimed anywhere in this document.** The expected value is **3966** =
>   the **3960** baseline (wave 23 §12, controller-verified) + this wave's **6** new tests (§10.4); that is an
>   expectation, not a measurement, and the controller fills it in. Reading it by COUNT out of the log, never by
>   the tick ([F37](followups.md)).
> - **the commit, the push, and the wave-24 section of PR #195.** `HEAD` is `476369a`; this document and the
>   register entries in §11 are uncommitted at the timestamp above.
> - **`v23_report_fingerprint_AFTER.txt` / `binaries_final.txt` under the plan's names.** The class-4 AFTER check
>   and the three other AFTER checks **did run and their output IS kept** — under the names `fp_v23_AFTER.txt`,
>   `fp_bank_AFTER.txt`, `fp_aux_AFTER.txt`, `fp_arm_AFTER.txt` (§8). Only the plan's filenames differ. The
>   separate `binaries_final.txt` re-hash was **not** written; the two dll hashes are however recorded
>   independently in `B24_BEFORE_READY` / `B24_AFTER_READY` / `R24_READY` / `S2_CENSUS_READY`, each written by the
>   driver from the file on disk at run time, which is the same assertion by another route.

> ## PROVENANCE
>
> | input | value |
> |---|---|
> | vessel | **CONTINUED on `ghilios/synthetic-af-bank-followups-wave23`, PR #195 (OPEN, unmerged).** Decided in writing in design §0 **before any measurement**, on one decisive fact: **wave 24's binary must carry `e7a5ee6`** — [F79](followups.md)'s ASCII fix, which wave 23 shipped explicitly *"at the top of wave 24, before that wave's build"*. A fresh branch cut from `develop` would not have it. The pre-registered contingency (*"if the owner merges #195 first, cut fresh from the resulting `develop`"*) did not fire |
> | pre-registration | **`82a3a0a`**, committed **`2026-08-12 16:56:32 -0400`** = `20:56:32Z` — **before the build, before the gate, and before the AFTER arms.** The A-BEFORE arm (`21:03:35Z`) ran on **B13**, whose gate passed in wave 23, so it is not a measurement the pre-registration followed |
> | code commit | **`476369a`**, `21:31:31Z`. The build followed it |
> | binary — **B14, the FOURTEENTH**, built once, never rebuilt mid-wave ([F53](followups.md)(c)) | `D:\hf_w24\exe`. `TestApp.dll` sha256 **`5398582df572485c245c2586cdcdbce3daa59e6291a5b5e95bad9d20cbbb53d8`** · `NINA.Joko.Plugins.HocusFocus.dll` sha256 **`c067fb5057b332001a8507ee42d65df94b291141ff99cb31e95f250bd74985fb`** · `BuildId` **`dd6ca32ef7f941c2a54753398cc2cf6b`**, **novel against thirteen** recorded ids |
> | binary — **B13**, the BEFORE side | `D:\hf_w23\exe`, tree **`839c38b`**. `TestApp.dll` **`82491ac79ce3d9216b4fa0d32c3daeb41aee460e845377c77f4ec9572cfc9ccf`**, plugin **`fe9db95c52f25e7e7fc6d9746a96986cfb545f0eb5011c4f03a8719292ddb998`**. **Not rebuilt, not moved, not written into**; every BEFORE `--out` is under `D:\hf_w24\before\` |
> | the apphost, a **SEVENTH** counter-example | `TestApp.exe` sha256 **`dd7103c28cc610e72671534cf23fb9a58b5a303a19d8779545cb7418d4ce6ff7`** — **byte-identical to wave 23's**, recorded in both waves' provenance files, on a binary that is not it. **An exe hash is not a binary identity; hash the dlls** ([F66](followups.md)) |
> | **two-binary provenance** | `git diff --name-only 839c38b..476369a -- '*.cs'` = **27 files**, of which **21 are F79's ASCII sweep** and **6 are this wave's**. **The artifact first recorded the WRONG BASE and was corrected at `00:11:30Z`; that is a controller error and it is written up as one in §0.2, not as a footnote** |
> | freshness | `find Joko.NINA.Plugins -name '*.cs' -newermt '<B14 build mtime>'` — **empty**, re-checked at correction time |
> | settings | `D:\hf_w11\pinned_settings_w11.json` md5 **`a67ffc06164c81613aef5c4f8324b9b8`**, on every arm and the gate. **Not re-pinned** ([F63](followups.md)(b)) |
> | profile | `astrodet` `ce3f3e63-8fd3-4b72-a0ca-d90db9441382`. **Cardinality 1** across all 8 gate landings and across **both** arms' reports, read as the binary resolved it, never as the driver typed it |
> | detector | `DetectorVersion` **2**, read as a **FIELD** on all 8 landings ([F66](followups.md)). `strings` appears nowhere in this wave |
> | concurrency | `ConcurrencyCheck = exclusive` on all 8, read **across** the arm |
> | `FitInputs` | cardinality **1** on the gate and cardinality **1** within **each** arm, and **equal across arms**: `MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid` |
> | `synth-validate` spec | `<exe>\SynthBank\synthetic-bank-spec.json` sha256 **`bf10522e670a119e260a8b3d06c2cc6ec52ce513dde25ca9ca13f426c38672d6`** — equal to the pre-registered constant and echoed back by the binary in every report on both arms |
> | banks | `D:\SyntheticAutofocusBank` (20 datasets), `D:\Autofocus Bank` (19 runs, and one path contains a SPACE) |

---

## Status

| item | rule | verdict | what it establishes |
|---|---|---|---|
| the gate | **RULE G24** | **PASS** — 8 of 8 to 6 dp, **bit-identical at all sixteen digits**, on a **FOURTEENTH** binary | The coordinate system is unmoved across a product-code change to `StepSizeRecommender`. §1 |
| P1's inertness on the **shipping** path | **`G24-P3b`** — NEW | **PASS — 8 of 8**, eight integers, exact equality | P1 fills two fields and moves **no recommended step size** on any real or synthetic gate run. Its ship rule stands. §1.2 |
| [F79](followups.md)'s fix, one wave after shipping | **`G24-P4`** — NEW | **`P4-ASCII-CLEAN` — 0 non-ASCII bytes in 8 of 8**, against **8 of 8 carrying one `0xE5`** on wave 23's gate | **F79 is VERIFIED in the field**, FAIL end measured first on real published artifacts. §2.1 |
| the same statistic, printed by the driver | — | **the driver still says `0 of 8` — and it is a DIFFERENT BUG** | F79 was **masking** a second defect in the same print. Both produce the same wrong number. **§2.2 is the wave's methodology headline.** |
| P2 on a blind population | **RULE B24** | **`B-UNEXERCISED`** — `B24-C` = **0 of 7** | **Not a pass and not a fail**, by the branch table written before the data. The blind population **did not contain the mechanism**. §4 |
| P2 on the cell it was built for | **labelled control, in NO denominator** | **BRIDGED** — `21` (4 rounds, stuck) → `21 → 36 → 62`, `converged=true` in **3** rounds | Meets **F26's own pre-registered bar** on the cell F26 names. **It cannot carry `B24`'s verdict and does not.** §4.3 |
| do-no-harm across the bank | **RULE R24** | **`R-PRESERVED`. P2 MAY SHIP.** `R24-A` **20/20** on all 17 terminal fields; `R24-B` **20/20** identical assertion sequences | The ship gate for P2, and it had a **predicted answer**, which is the only kind worth running. §3 |
| the degenerate-fit census | **RULE D24** | **`D-REPORTED`** — no bar, by pre-registration, and none is invented | The wave-25 hand-off. §5 |
| the stall's causes | **`D24-A/B` + the labelled controls** | **AT LEAST TWO, and neither is "the fit was bad"** | `D03`/S1 is degenerate at **R² = 0.9835**; `D01`/S1 is **NOT degenerate at all** (`halfWidth = 6.0877`, R² ≈ 1.0) and stalls anyway. **§5.2 is worth more than the ship.** |
| the two named real-bank gaps | **item L24** | **`lumos` `L-REPRODUCED`** (exit=3, and the failure is now QUOTED) · **`Panos` `L-NOT-COMPARABLE`** | Ten waves of folklore, half of it now measured and half of it shown to belong to a **different instrument**. §6 |
| four fingerprint classes | — | **42/42, 59/59, 48/48, 38/38** byte-identical, **output KEPT** | Wave 23 §8's recorded caveat is discharged. §8 |
| derived instruments | — | **EIGHT prose-vs-mechanism drifts in five instruments, one LOAD-BEARING** | D4/D7/D9 are three of eight. **The pattern is the finding.** §9.2 |
| the suite | — | **OPEN at the timestamp** | In flight; expected 3966. No number is claimed |

> ## THE HEADLINE, in the order the evidence supports it
>
> **P2 demonstrably fixes the cell it was built for, and a blind population built to provoke the same mechanism
> never produced it.** Those two sentences are the wave, and neither one is allowed to stand in for the other.
>
> On `D08_c11_2800mm`/S1 — the cell whose four-round livelock wave 23 reproduced and whose round records were read
> to *design* the fix — B13 spends four rounds recomputing the same recommendation and discarding it, ending
> `converged=false` at step **21** against a truth of **82**. B14 takes **three** rounds, `21 → 36 → 62`,
> `converged=true`, with P2's engagement marker `binningDeferralBoundReached=true` on round 1 and the reason
> string *"binning 1 -> 2 (already measured at factor 2; not deferring)"*. That satisfies the bar
> **[F26](followups.md) itself pre-registered** — `roundsUsed <= 3` with `finalStepSize` inside `[49.2, 131.2]` —
> on the very dataset and scenario of F26's own evidence table. **And it is a labelled reproduction control,
> excluded from every denominator in the wave, so it cannot carry a verdict and does not: RULE B24 is
> `B-UNEXERCISED`.**
>
> Because the blind population is the other half of the story. **Scenario S4 — never run in any prior wave, in no
> results document, in no register entry, and whose stated purpose is the ordering P2 changes** — was run on all
> 20 datasets on both binaries. The binary resolved **7** applicable, exactly as the spec-derived expectation said.
> On **0 of 7** did the BEFORE run ever revisit a binning factor (`factors seen=[1]` on every one). So P2's
> engagement marker fired **0 of 7**, and `B24-D`'s do-no-harm read **7 of 7 identical** — precisely how P2 is
> *defined* to behave when there is no revisit. **The finding is that F26's livelock is RARER than `D08` suggests.**
>
> **The wave's most valuable output is neither of those. It is that the step-recommender stall has at least two
> distinct causes and neither of them is "the fit was bad."** P1's new `degenerateReason` field, on its first use,
> **refutes wave 23's own attribution**: of the three cells wave 23 assigned to degenerate fits,
> `D01_ultrawide_40mm`/S1 is **not degenerate** — `halfWidth = 6.0877`, `degenerateReason = null`, `R² ≈ 1.0` — and
> it stalls at step 2 against a truth of 9 regardless, while the harness prints *"a no-op recommendation from a
> degenerate fit"* at it. Meanwhile `D03_redcat_250mm`/S1 **is** degenerate at **R² = 0.9835**. **A fit-quality
> gate keyed on R² — the obvious design for wave 25 — would sail straight past D03 and would never have been
> pointed at D01 in the first place.**
>
> **And the wave's own instruments drifted eight times.** F79's fix, verified clean on this wave's gate, turned out
> to have been **masking a second defect in the same driver print** — one that produces the identical wrong number
> from a completely different cause, so no amount of reading the output could have separated them. Three separate
> derived-instrument defects (D4, D7, D9) plus five more found at write-up say the same thing: **deriving a wave's
> instruments from its predecessor's by textual substitution fails silently and repeatedly, and the `sed` lines are
> themselves untested code.**

---

## §0 — Two things about this document's own honesty, before the rules

### 0.1 P1 is the product change. P2 is not. This is not a presentational choice.

Wave 23 §13 corrected [F26](followups.md)'s scope after wave 23 had already published it the other way: **the
binning-first deferral livelock is in `TestApp/SynthValidateRunner.cs`, the `synth-validate` validation harness.
It ships in `TestApp.exe`. `NINA.Joko.Plugins.HocusFocus.dll` does not contain it and NINA never loads it.** The
shipping wizard builds **one** `OptimizationSummary` carrying `RecommendedStepSize` and the binning recommendation
**together**, in one pass, with no round loop to livelock.

So of this wave's three changes:

| | what it is | reaches a user? |
|---|---|---|
| **P1** | `DegenerateReason` on all three degenerate exits, `SampledHfrRange` measured on the degenerate path, surfaced in the wizard's step block | **YES. This is the wave's product change.** `StepSizeRecommender.cs` is shipping plugin code, called at `StarDetectionOptimizerWizardVM.cs:4062` |
| **P2** | the binning-revisit deferral bound | **NO. Harness only.** It repairs the instrument every step-size measurement in this series is made on |
| **P3** | `currentFactor` / `appliedFactor` / `stepDeferredByBinning` / `binningDeferralBoundReached` | **NO. Harness only.** Observability the register has been owed since 2026-08-03 |

**This document must not, and does not, present P2 as a bridge to a field defect.** It is an instrument repair,
and repairing the instrument *before* wave 25 re-measures goal 2 is the right order.

### 0.2 The mandatory two-binary provenance was recorded against the WRONG BASE. Controller error, corrected.

The pre-registration (§5.1) mandates `git diff --name-only 839c38b..<W24 commit> -- '*.cs'`, recorded verbatim,
because *"a differing `BuildId` proves a rebuild happened; it has never proved the code differs."* B13's own
provenance file records its tree as **`839c38b`**, so that is the correct base.

**`binary_provenance_w24.txt` was first written against `e7a5ee6`** — 10 paths — and the analysis agent caught it
at write-up. The controller appended a labelled CORRECTION block at `00:11:30Z` carrying the correct list.

| | files |
|---|---|
| **CORRECT `839c38b..476369a -- '*.cs'`** | **27** |
| of which **F79's ASCII sweep** (`839c38b..e7a5ee6`) | **21** |
| of which **this wave's own code** (`e7a5ee6..476369a`) | **6 `.cs`** (+ 2 test files, + `DataTemplates.xaml`, + the Tests `.csproj`) |
| what was originally recorded | **10 paths**, `e7a5ee6`-based |

**Why this is a real error and not bookkeeping.** The 21 files it omitted are **exactly** the commit that changes
`SynthValidateRunner.cs:543-545` from U+2014 to `--`, which is **the sole cause of `R24-C`'s single prose
difference** (§3.3). A reader given only the original artifact would have found `R24-C`'s one difference reported
and **no commit in the provenance capable of producing it** — the one question the mandatory provenance exists to
answer. The pre-registration even names the commit and the line numbers in advance (§5.3); the artifact recorded a
delta that does not contain them. *A wave that compares two binaries without recording the diff between them is
comparing two hashes* — and recording the diff against the wrong base is the same failure with more digits.

The plan's `--- counts ---` split (test-project vs not) and the `find -newermt` freshness line were also absent
from the first write; the freshness check is present and **empty** in the correction block.

---

## §1 — RULE G24: **PASS**, on a fourteenth binary

`optimize --per-run --max-evals 250`, mixed real + synthetic, sequential, one `TestApp.exe`, no NINA, no fan-out.
`21:32:23Z → 22:13:38Z`, **41 m 15 s** against a **~42 m** price — a **seventh** consecutive wave at ~5.2 m/run.

### 1.1 The eight values

| run | `FinalJ` (exact) | 6 dp | expected | bit == K8 | `BaselineJ` | w11 |
|---|---|---|---|---|---|---|
| `toml999` | `0.9957838768299878` | 0.995784 | 0.995784 | **YES** | 0.983477 | 0.983477 |
| `CWhiteFocus` | `0.9960675916058808` | 0.996068 | 0.996068 | **YES** | 0.994320 | 0.994320 |
| `uneven` | `0.9963677194179505` | 0.996368 | 0.996368 | **YES** | 0.991794 | 0.991794 |
| `muggsie` | `0.9971948738498605` | 0.997195 | 0.997195 | **YES** | 0.993288 | 0.993288 |
| `mccomiskey` | `0.9767460801208465` | 0.976746 | 0.976746 | **YES** | 0.846082 | 0.846082 |
| `D18_m24_deep_shed` | `0.9998815090506263` | 0.999882 | 0.999882 | **YES** | 0.997405 | 0.997405 |
| `D19_cygnus_deep_shed` | `0.9994870586135448` | 0.999487 | 0.999487 | **YES** | 0.999187 | 0.999187 |
| `D20_m24_bright_control` | `0.9997378027339423` | 0.999738 | 0.999738 | **YES** | 0.999454 | 0.999454 |

**`G24-1`: 8 of 8 to 6 dp, and bit-identical to K8 on 8 of 8 at all sixteen digits.** The **fourteenth** binary to
reproduce the gate, and it has never moved.

**Free controls, read across the arm as FIELDS** (`prov_w24.py`, self-tested in both directions **before** being
quoted): `BuildId` cardinality **1** and **novel against 13 recorded ids**; `DetectorVersion` **2** ×8;
`ProfileId` cardinality **1**; `ConcurrencyCheck` `exclusive` ×8; `FitInputs` cardinality **1**. `G24-2`
aggregate summaries **8 of 8**. `G24-P1` `optimize/seed` reading `NoiseReductionRadius=3` on **8 of 8** — the
anti-leak clause, and `/mnt/d/hf_w18/part2_w18.patch` did not leak into the tree. `G24-P2a/P2e`
`optimize/detected`, `optimize/baseline`, `optimize/seed` each exactly once on **8 of 8** by the scorer, on bytes
in Python (§2.2 is about the driver's disagreeing print). `G24-P3a` `RecommendedStepSize` present and finite on
**8 of 8**.

**Branch table applied as written: `G24-1 == 8 of 8` and `G24-P3b == 8 of 8` ⇒ not `G-STOP`; everything else 8 of
8 ⇒ not `G-PARTIAL`; `G24-P4 == 0` ⇒ not `G-ASCII-INCOMPLETE`. → `G-PASS`. The arms may run.**

### 1.2 `G24-P3b` — P1's inertness, measured on the shipping path, not asserted

P1 modifies `StepSizeRecommender`, the class that produces `RecommendedStepSize`. The pre-registration fixed the
eight integers wave 23 measured and made P1's ship rule void if any moved.

```
CWhiteFocus 101/101 · D18_m24_deep_shed 18/18 · D19_cygnus_deep_shed 35/35 · D20_m24_bright_control 19/19
mccomiskey 31/31 · muggsie 550/550 · toml999 16/16 · uneven 488/488
G24-P3b: 8 of 8   could-not-look: 0
```

**8 of 8, exact integer equality, no tolerance.** P1 fills two previously-`null`/`NaN` fields and adds a line of
UI text; it changes **no recommended step size** on five real-bank runs and three synthetic ones. Its
**UNCONDITIONAL** ship rule (design §3.1) stands, and this document does **not** report P1 as a step-size accuracy
improvement — design §9's first standing obligation.

### 1.3 The interlock, demonstrated FAIL-end-first, and its output kept

`G24_PASSED` is written by `score_g24_w24.py --arm-gate` and by nothing else, on `rc == 0` and only then
([F75](followups.md)).

| direction | input | result | artifact |
|---|---|---|---|
| **FAIL** | `/mnt/d/hf_w19/gate` | **rc=1**, refused on the **novelty** clause (`763d147623ea4dd2bdcff2981107ae4c` is a prior id) — K8 reproduced 8 of 8 and `NoiseReductionRadius=3` ×8, so the refusal is specific | `g24_armfailend.txt` |
| — | assert no marker | **no marker written** | — |
| **PASS** | `/mnt/d/hf_w24/gate` | **rc=0**, `prov_w24.py free controls exit=0`, wrote `G24_PASSED` carrying the BuildId | `g24_armpassend.txt`, `G24_PASSED` |

`score_g24_w24.py --self-test` **24 of 24**, re-run on the derived copy before it was quoted (three times:
`g24_scorer_selftest{,2,3}.txt`). `score_b24_w24.py --self-test` **21 of 21**, including that a missing marker is
`B-UNEVALUATED`/`R-UNEVALUATED` and **never a rate**, and that `fmt_rate(0,0)` says UNEVALUATED and not `1.0000`.
`prov_w24.py` demonstrated **both** directions and its printed word agrees with its mechanism (`printed=13`,
`len(PRIOR_BUILD_IDS)=13`, **AGREE**).

**One control the plan specified and the artifact does not carry:** the plan's
`test ! -f G24_PASSED && echo "ASSERTED: the FAIL end left NO marker" | tee -a` line is **not** in
`g24_armfailend.txt`. The marker's absence at that moment is inferable from the PASS end's later *"WROTE …"* line
(the scorer **refuses if the marker already exists**, so it could not have written one twice), but the explicit
assertion was not recorded. Named rather than assumed.

---

## §2 — [F79](followups.md): VERIFIED one wave after shipping — and it had been MASKING a second defect

### 2.1 `G24-P4` — the predecessor's deliverable, measured, FAIL end first

Wave 23 shipped F79's fix **after** all of its own measurements, so wave 24's is the first gate whose binary
carries it. The pre-registration made this the cheapest possible verification and required the FAIL end to be
demonstrated **on real artifacts first** — and here the FAIL end is a **published prior result**, so the
instrument was checked against a known answer.

| gate | files with ≥ 1 byte ≥ `0x80` | total such bytes | verdict |
|---|---|---|---|
| `/mnt/d/hf_w23/gate` (**FAIL end, run first**) | **8 of 8** | **8** — one `0xE5` each, at offsets 7975, 7977, 7993, 8036, 8055, 8061, 8131, 8153 | `P4-NON-ASCII-PRESENT` |
| `/mnt/d/hf_w24/gate` (**this wave's own**) | **0 of 8** | **0** | **`P4-ASCII-CLEAN`** |

**F79 is VERIFIED.** Its clause was deliberately non-blocking and both branches were reachable; it returned the
clean one. The census was implemented in Python on bytes, never `grep`, *because `grep` is the instrument F79
breaks* — a clause about a fails-closed defect must not use the instrument that fails closed.

### 2.2 The same print still says `0 of 8`, and it is a COMPLETELY DIFFERENT BUG

`gate_w24.sh`, on a gate that had just reproduced K8 bit-identically, printed:

```
[G24-P2] logs with EXACTLY ONE 'optimize/detected' block: 0   expected: 8
[G24-P2] logs with EXACTLY ONE 'optimize/baseline' block: 0   expected: 8
[G24-P2] logs with EXACTLY ONE 'optimize/seed' block: 0   expected: 8
```

**Character-for-character what wave 23 printed, and not one byte of the cause is the same.**

| wave | what `grep` sees | driver prints | why |
|---|---|---|---|
| 23 | **0** hits — the whole file classed binary by one `0xE5` | `0 of 8` | [F79](followups.md): fails **closed** |
| 24 | **2** hits per log | `0 of 8` | the driver counts **LINES MENTIONING** the string; a block is a `BEGIN`/`END` **pair**, so the true count is **2** and *"exactly one"* is never satisfiable |

Measured on `/mnt/d/hf_w24/gate/toml999.log`:

```
136:PARAMS-DUMP optimize/detected BEGIN
192:PARAMS-DUMP optimize/detected END
```

**The pre-registered clause is unaffected and needed no repair.** `G24-P2a/P2e` is scored by
`score_g24_w24.py`, which parses **blocks**, and returns **8 of 8 = 1.0000, VERDICT: PASS** on the same eight
files in the same minute (`g24_p2p3.txt`). The driver's line is a convenience print, exactly as in wave 23.

**Why this is worth its own entry, and it is the wave's methodology finding.**

1. **A wrong number can have more than one cause, and fixing one cause does not validate the instrument.** The
   `σ` had been masking a second bug **in the same statistic, in the same script**, for that driver's whole life.
   Both defects emit the identical string `0 of 8`. **No amount of staring at the output could ever have
   separated them.** Only fixing the first made the second visible.
2. **Wave 23's diagnosis — *"it's the σ"* — was TRUE and INCOMPLETE.** Had the `σ` never existed, that line would
   still have read `0 of 8`, on all three blocks, on every wave from 11 to 24.
3. **The only reason it was caught is that the scorer and the driver compute the same clause by different routes
   and were compared.** That comparison is now a load-bearing control and should be named as one.
4. **And the two must not be assumed to compute the same statistic.** Here one counts **blocks** and the other
   counts **lines** — a difference completely invisible in the output, in a wave whose predecessor had just
   published an entry about a difference completely invisible in the output.

This is deviation **D8**. §11 records it as an extension to [F79](followups.md) rather than a new entry, and says
why.

---

## §3 — RULE R24: **`R-PRESERVED`**. P2 MAY SHIP. This is the clause that could have unshipped it.

**Population:** scenario **S0 over all 20 datasets**. Its BEFORE side is **wave 23's own reports, already on disk,
already published, at ZERO TestApp minutes** (`/mnt/d/hf_w23/v23/<DS>__S0/synth_validate_report.json`). Only the
AFTER side was run: `22:53:54Z → 23:36:50Z`, **42 m 56 s** against a ~45 m price, 20 of 20 cells, 0 skipped.

**`R24-V1`: 20 paired of 20** (minimum 17). Not a shrunken denominator, and drop D4 was never taken.

**This rule had a PREDICTED ANSWER, fixed before the data, which is the only kind worth running.** Wave 23's
census found exactly **one** round carrying a binning deferral in all of S0 and **no** cell with two, so a
*revisit* is impossible in a single round and P2 is *defined* to be inert here. The prediction was: everything
reproduces.

### 3.1 `R24-A` — the unship clause

Seventeen terminal fields per cell — `converged`, `roundsUsed`, `stepToleranceBand`, `stepTheory`,
`stepBehavioral`, `stepBehavioralVsTheoryDeltaFraction`, `finalStepSize`, `finalExposureSeconds`,
`finalDetectionBinning`, `finalCenterPosition`, `expectedStepSize`, `expectedExposureSeconds`,
`expectedDetectionBinning`, `deltaStepVsExpected`, `deltaExposureVsExpectedFraction`, `deltaBinningVsExpected`,
`overallVerdict` — the report's own copy, ints and bools compared exactly, doubles by `repr()` **bit-identity**,
and the string `"NaN"` compared as a string and never coerced to zero.

**Cells with all 17 fields identical: 20 of 20 = 1.0000.**

### 3.2 `R24-B` — the assertion sequence

Every `assertions[]` element's `(id, verdict)` pair, round-by-round and terminal, **in order**, with a length
mismatch counted as a difference and named with both lengths.

**Cells with an identical assertion sequence: 20 of 20 = 1.0000.**

### 3.3 `R24-C` — reported, not thresholded, and the rule predicted its one difference by name

```
prose differs  D16_esprit550_ha3
  BEFORE  '… step_behavioral 15) ? a no-op recommendation from a degenerate fit, not convergence'
  AFTER   '… step_behavioral 15) -- a no-op recommendation from a degenerate fit, not convergence'
identical folded stoppedReason: 19 of 20 = 0.9500   (characters folded: 1)
```

**That is `e7a5ee6`'s em-dash → `--` change at `SynthValidateRunner.cs:543-545`, and nothing else.**

**This is a small piece of very good rule design and it deserves to be said plainly.** The pre-registration
(§5.3) named the commit, named the file **and the line numbers**, predicted that these strings are *guaranteed*
to differ between B13 and B14, explained that it has nothing to do with the wave's fix, and therefore made the
clause **reported, not thresholded** — while keeping `R24-A` and `R24-B`, the numbers and the verdicts,
at 100 %. A regression clause that compared the prose raw would have failed the affected cells and reported a
catastrophe. **The wave's own known noise source was prevented in advance from masquerading as a regression,
without loosening anything that could catch a real one.** That was only possible because the wave read
`git diff 839c38b..e7a5ee6` before writing a clause — which makes §0.2's provenance error the more pointed.

### 3.4 `R24-D` — P1's and P3's schema landed

**Rounds carrying `degenerateReason`: 21 of 21 = 1.0000. Rounds carrying `sampledHfrRange`: 21 of 21 = 1.0000.**
P1 shipped and the fields are readable from the report alone.

### 3.5 The branch table, applied as written

```
R24-V1 passes (20 >= 17)?                      yes
R24-A == 100 % AND R24-B == 100 %?             yes  ->  R-PRESERVED    (P2 MAY SHIP)
```

**RULE R24 VERDICT: `R-PRESERVED`.** P2's conditional ship rule (design §3.2) is satisfied and **P2 ships**. No
byte backup was restored, no P2 test was deleted, and `R-BROKEN`'s unship path was not walked.

---

## §4 — RULE B24: **`B-UNEXERCISED`**. A real result, and not a null.

**The instrument:** `synth-validate`, one invocation per (dataset, scenario) cell, `--max-rounds 4` (the shipped
default), sequential, one `TestApp.exe`, `timeout 600` per cell, `< /dev/null` on every invocation.

**The population, and why it is blind. Scenario S4** — *"detection binning 1 where 2 expected — asserts
binning-first ordering"* — **has never been run in this series. It appears in no results document and in no
register entry.** Its stated purpose is the mechanism P2 changes. It was requested on **all 20 datasets** on both
binaries, from the same driver and the same dataset order, and **the binary resolved applicability**, not the
design.

| arm | binary | window | cells | wall |
|---|---|---|---|---|
| A-BEFORE | **B13** | `21:03:35Z → 21:21:14Z` | 22 scheduled, **22 manifest rows**, 0 skipped | **17 m 39 s** |
| A-AFTER | **B14** | `22:17:51Z → 22:36:03Z` | 25 scheduled, **25 manifest rows**, 0 skipped | **18 m 12 s** |

### 4.1 The validity gates — all six pass

| clause | result |
|---|---|
| **`B24-V1`** | S4 requested **20**; **applicable 7**, and the pre-registered expectation was **7** — the derivation did not move. 13 named `NOT-APPLICABLE` (*"expected detection binning is 1, not 2 — nothing to force-mismatch"*), which is its own **fourth state**, neither could-not-look nor failure. **Paired S4 cells 7; excluding the labelled `D08` 6**, against a minimum of **5** |
| **`B24-V2`** | `profileId`, `specSha256`, `maxRounds`, `fitInputs` — cardinality **1** within each arm, on both arms, read as the binary resolved them. `fitInputs` equal **across** arms |
| **`B24-V3`** | exactly two distinct binaries, each used for exactly one arm; both dll pairs match their published values (recorded in `B24_BEFORE_READY` / `B24_AFTER_READY`) |
| **`B24-V4`** | `appliedFactor` vs `rounds[N+1].bootstrap.detectionBinning`: **UNEVALUATED (empty population)** — rounds with no successor are out of the denominator by construction, and S4's applicable cells produced none with a successor. **Named, and not scored as `1.000`** |
| **`B24-V5`** | `G24_PASSED` present, carrying `BuildId=dd6ca32ef7f941c2a54753398cc2cf6b`. The AFTER driver aborts without it |
| **`B24-V6`** | the `D11_rc10_585_afbin2`/S1 cross-binary control: `trajectory [24, 41]`, `roundsUsed 2`, `converged True`, `finalStepSize 41`, `stepBehavioral 55.0`, `stepToleranceBand 22.0` — **all six reproduce on B14**. `D11` has no binning deferral, so P2 must not touch it, and it did not |

### 4.2 `B24-C` — the counterfactual, and it is what makes `B-UNEXERCISED` honest

For each BEFORE S4 cell: did any round's recommended binning factor equal a value the run had already been at?

| dataset | scenario | revisit | longest binning-only run | factors seen |
|---|---|---|---|---|
| `D08_c11_2800mm` | S4 | False | 1 | `[1]` |
| `D09_c14_3800mm` | S4 | False | 1 | `[1]` |
| `D10_rc16_3250mm_sparse` | S4 | False | 1 | `[1]` |
| `D12_c14_585_afbin2` | S4 | False | 0 | `[1]` |
| `D14_cdk14_2563mm_e47` | S4 | False | 1 | `[1]` |
| `D15_cdk20_3454mm_e47` | S4 | False | 1 | `[1]` |
| `D17_cdk14_oiii5` | S4 | False | 1 | `[1]` |

**BEFORE cells with a revisit: 0 of 7 = 0.0000.** Every cell saw exactly one factor. **The mechanism P2 bounds
never occurred.**

Consequently `B24-B`, P2's engagement marker `applied.binningDeferralBoundReached`, fired on **0 of 7 rounds** and
**0 of 7 cells** — and `B24-D`, do-no-harm on cells whose BEFORE run had no revisit, is **7 of 7 identical**.
**That is exactly, and only, how P2 is defined to behave with no revisit.** `B24-A` reports **0 CHANGED, 0
IMPROVED, 0 REGRESSED, 7 of 7 SAME-DISTANCE** over all applicable cells and **6 of 6** over the minus-`D08`
population the branch table reads, with both denominators printed and neither averaged into the other.

### 4.3 The labelled reproduction control: `D08_c11_2800mm`/S1 is **BRIDGED**

Design §2 declares this cell **not blind** — its four round records were read in full to design P2 — and excludes
it from **every denominator in every rule**. The design also requires it **reported in full**. No scorer prints
it, so it is read here directly from the two reports.

**BEFORE (B13)** — `converged=false`, `roundsUsed=4`, `finalStepSize=21`, `expectedStepSize=82`,
`stepBehavioral=82.0`, band `32.8`, `overallVerdict=2`, `stoppedReason="reached --max-rounds (4)"`:

| round | bootstrap step | bootstrap bin | recommended | step rec | applied reasons |
|---|---|---|---|---|---|
| r0 | 21 | 2 | 1 | 36 | `binning 2 -> 1 (deferring exposure/step this round)` |
| r1 | 21 | 1 | 2 | 36 | `binning 1 -> 2 (deferring exposure/step this round)` |
| r2 | 21 | 2 | 1 | 36 | `binning 2 -> 1 (deferring exposure/step this round)` |
| r3 | 21 | 1 | 2 | 36 | `binning 1 -> 2 (deferring exposure/step this round)` |

Four rounds; **the same recommendation, 36, computed four times and discarded four times**; the bootstrap never
leaves 21. Fits are near-perfect throughout — R² `0.9994`, `0.9997`, `0.9997`, `0.9999`.

**AFTER (B14)** — `converged=TRUE`, `roundsUsed=3`, `finalStepSize=62`, `overallVerdict=1`,
`stoppedReason="converged (step 62 within the 32.8 tolerance band of step_behavioral 82)"`:

| round | bootstrap step | bin | rec / cur / applied | step rec | deferred | **bound** | applied reasons |
|---|---|---|---|---|---|---|---|
| r0 | 21 | 2 | 1 / 2 / 1 | 36 | True | False | `binning 2 -> 1 (deferring exposure/step this round)` |
| r1 | 21 | 1 | 2 / 1 / 2 | 36 | **False** | **True** | `binning 1 -> 2 (already measured at factor 2; not deferring)`, `step 21 -> 36` |
| r2 | 36 | 2 | 2 / 2 / 2 | 62 | False | False | `step 36 -> 62` |

**P2 engaged on round 1, exactly where the revisit occurs, and said so in its own engagement marker** — which is
[F76](followups.md)'s rule honoured: *a fix that cannot report whether it engaged is not finished*. **And P3's
`currentFactor`/`appliedFactor` are populated on the AFTER arm and `null` on the BEFORE arm**, which is the
schema difference the design predicted and is why `B24-C` had to reconstruct the counterfactual from
`bootstrap.detectionBinning` instead.

**[F26](followups.md)'s own pre-registrable bar, quoted from the entry: *"`roundsUsed <= 3` with `finalStepSize`
inside `[49.2, 131.2]`."* Measured: `roundsUsed = 3`, `finalStepSize = 62`. Both met.**

**What this does and does not license.** It licenses saying P2 works, on the mechanism it was built for, with the
engagement marker to prove it engaged, plus four unit tests shown red against the pre-change source. **It does
not license a rate, a bridge claim about the bank, or any adjustment to `B24`'s verdict** — the cell motivated
the fix and cannot be blind, and the design fenced it from every denominator before the data. `B24` is
`B-UNEXERCISED` and stays so.

### 4.4 `B24-E` — S4's first assertion census

**78 of 79 instances at Pass = 0.9873.** Per-`id`: `A1` **15 of 16 = 0.9375**, `A2` 14/14, `A3` 7/7, `A4` 7/7,
`A5` 21/21, `A6` 7/7, `A7` 7/7. Characters ASCII-folded while printing: **0**.

The single flag:

```
FLAG  D12_c14_585_afbin2/S4  A1: detectionBinning: 1 -> 1 made no meaningful progress toward target 2 (|delta| 1 -> 1)
```

`D12_c14_585_afbin2` is the dataset [F26](followups.md) already names for a straddling vertex HFR, and S4 starts
it deliberately mismatched at factor 1. **It never reached factor 2 at all** — consistent with its `B24-C` row,
the only one of the seven with `longest binning-only run = 0`. Reported; no clause thresholds it.

### 4.5 The branch table, applied as written

```
B24-V1..V6 all pass?                              yes -> not B-UNEVALUATED
B24-D < 100 %?                                    no  -> not B-HARMFUL
B24-C == 0 (no BEFORE cell revisits a factor)?    YES -> B-UNEXERCISED
```

**RULE B24 VERDICT: `B-UNEXERCISED`.** The branch table said in advance, in these words, that this is **NOT a
pass and NOT a fail**, that **P2 still ships iff RULE R24 is `R-PRESERVED`, on the strength of `D08`/S1 and the
unit tests**, and that the results document must say so in those words. It does, above and in §10.

**What it licenses:** that **F26's livelock is rarer than `D08_c11_2800mm` suggests** — it did not occur once in
a blind 7-dataset population built specifically to provoke binning mismatches, on the shipped `--max-rounds 4`.
Wave 23 found a revisit in 1 cell of 38 from the other side; this is the strongest evidence yet that F26 is
**conditional, not endemic**, and that whether it fires is decided by where a rig's vertex HFR sits relative to
the `1.5` rounding boundary rather than by the deferral rule being generally wrong.

**What it does NOT license:** any claim that P2 is unnecessary, any claim that P2 is validated on the bank, and
any rate at all. A denominator of 7 with 0 events bounds the incidence loosely and nothing more. **It also does
not license shrinking the scenario's future denominator** — S4 remains the right blind population, and a wave
that wants the mechanism must select datasets whose vertex HFR straddles the boundary rather than by
`expectedDetectionBinning == 2`.

---

## §5 — RULE D24: **`D-REPORTED`**, no bar. The wave-25 hand-off, and it is worth more than the ship.

Population: every round of the AFTER arm plus the S2 census whose `stepRecommendation` is non-null — **32 cells
read, 19 OK** (the 13 not-OK are exactly the 13 `NOT-APPLICABLE` S4 cells, which is addressability, not failure).

The S2 census ran despite being drop D1: `23:38:14Z → 23:46:01Z`, **7 m 47 s** against an ~18 m price, 7 of 7
cells.

### 5.1 The clauses

| clause | result |
|---|---|
| **`D24-A`** — the schema invariant P1 asserts: `halfWidth == "NaN"` **iff** `degenerateReason` is non-null | **23 of 23 = 1.0000.** Null `stepRecommendation` blocks out of the denominator: **0**. **No fourth degenerate path exists that P1 failed to label** |
| **`D24-B`** — which of the three exits the field takes | **`half-width-unresolved` 2 of 2 = 1.0000.** `no-fit` **0**; `non-finite-vertex` **0** |
| **`D24-C` / `D24-D`** — the hand-off, split by scenario, **NO BAR** | below |

```
dataset              sc  rnd  degenerateReason       sampledHfr   R^2        bootstrapStep
D02_rich_135mm       S1  0    half-width-unresolved  1.2216       -0.2741    2
D03_redcat_250mm     S1  0    half-width-unresolved  1.0799        0.9835    4

wide-end (S2) degenerate rounds: 0      narrow-end (S1/S4) degenerate rounds: 2
```

**Only one of P1's three exits fires in the field**, and it is the `:252` half-width exit — the one where
`bestFit.Outputs` **is** present and measurable and was, before P1, thrown away. That is precisely the branch
wave 23's Bridge A needed a decision variable on, and it now has one.

**The wide end is EMPTY.** Seven S2 cells produced **zero** degenerate rounds. Wave 25's directional threshold
would therefore be **one-sided** — written entirely from narrow-end evidence — which is the exact shape the
design warned about, and the S2 census was run specifically to avoid it and could not. **[F25](followups.md)'s
own wide-end example, `D05_tec140_1000mm`/S2, is published and therefore excluded from any blind rule**, so wave
25 cannot simply reach for it either.

### 5.2 The two things this census actually establishes, and they are the wave's hand-off

**(a) "Degenerate" and "bad fit" are not the same thing.**

`D03_redcat_250mm`/S1 is a **degenerate** recommendation — `halfWidth = "NaN"`, `degenerateReason =
half-width-unresolved` — at **R² = 0.9835**. `D02_rich_135mm`/S1 is degenerate at **R² = −0.2741**. Two cells,
the same exit, R² a full 1.26 apart and one of them excellent.

**A fit-quality gate keyed on R² — the obvious design for wave 25, and the one wave 23's Bridge A gestures at —
would sail straight past `D03`.** This corroborates [F21](followups.md)'s *"R² is no defence"* on a second,
independent population: R² measures fit to the **sampled** points and says nothing about whether the vertex is
identifiable from them. `sampledHfrRange` is the field that separates them — `1.2216` vs `1.0799`, both far below
the ~1.5 the geometry needs — and it exists on this branch **only because P1 put it there**.

**(b) `D01_ultrawide_40mm`/S1 is NOT DEGENERATE, and wave 23 said it was.**

Read directly from `/mnt/d/hf_w24/after/D01_ultrawide_40mm__S1/synth_validate_report.json`:

```
r0: bootstrapStep=2  stepRec=2  halfWidth=6.0877461433410645  degenerateReason=None
    sampledHfrRange=1.4627767172066635   R^2=0.9999999999999397
terminal: converged=false roundsUsed=1 finalStepSize=2 expectedStepSize=9 stepBehavioral=8.0 band=3.2 verdict=2
stoppedReason: 'stalled (round applied nothing, but step 2 is outside the 3.2 tolerance band of
                step_behavioral 8) -- a no-op recommendation from a degenerate fit, not convergence'
```

**The half-width resolved. The fit is essentially perfect. There is no degenerate reason.** And the recommender
still answered *"keep 2"* against a truth of 9, the loop applied nothing, and the run stalled after one round of
four — **while the harness printed a `stoppedReason` that blames a degenerate fit it never checked for.**

Wave 23 (§4.1, and [F34](followups.md)'s tail) grouped `D01`, `D02` and `D03` as three cells that *"all stop on
this entry's own branch"* from degenerate fits. **On P1's first use in the field that attribution is wrong for
`D01`.** It is a *different* mechanism: a **non-degenerate, high-confidence recommendation to hold a step that is
4.5× too narrow**. §11 corrects the register.

**This is exactly the deliverable F34's tail asked for**, verbatim: *"a no-op from agreement and a no-op from
degeneracy are currently byte-identical to the loop … Recording which branch `Recommend` took would make the two
distinguishable from the report alone."* P1 recorded it, and the first thing it distinguished was a
mis-attribution the register had already made.

**So the stall has at least two distinct causes, and neither is "the fit was bad."** That is the sentence wave 25
should be designed around.

**`D24` carries NO BAR by pre-registration, and none is invented here.** There is no prior for what
`sampledHfrRange` should be on a degenerate fit; inventing one now with the values in hand is the `S16-A(b)`
defect and the [F14](followups.md)/[S16](waves22+-handoff-prompt.md)/[D20](followups.md) fence forbids it.
**RULE D24 VERDICT: `D-REPORTED`.**

---

## §6 — Item L24: ten waves of folklore, half measured and half reassigned

`optimize --per-run --max-evals 250`, pinned, on **B14**, `23:47:42Z → 23:57:59Z`, **10 m 17 s** against a ~12 m
price, **2 of 2 manifest rows**. The design specifies **no scorer** and a **by-hand assignment per run**, because
the recorded failure modes are prose in the traps list and a machine comparison would compare against a string
the driver itself wrote. Assigned here, per run, by name.

### 6.1 `lumos` — **`L-REPRODUCED`**, and it stops being folklore

Recorded: *"`lumos` exits rc=3."* **Measured: `exit=3`.** The exit code reproduces on the fourteenth binary, and
for the first time the failure is **quoted rather than remembered**:

```
Optimization complete: currentJ=0 -> bestJ=0 (no improvement over current), evals=88
Optimize phase: 437.8 s wall, early-context builds=506, reuses=1518 (75.0% reuse of 2024 frame detections)
FAIL: at least one frame has < 3 stars under optimized params (min observed = 0 in run
      AutoFocus_20260708_231255/attempt01)
-> AutoFocus_20260708_231255/attempt01: currentJ=0 bestJ=0 hard-floor FAIL
annotated …_Focuser124056_optimized.png (0 accepted)
annotated …_Focuser295414_optimized.png (3 accepted)
```

**`rc=3` is `optimize`'s hard-floor failure exit, not a crash and not a missing dataset.** The search ran 88 of
250 evaluations, found **no** improvement over current (`bestJ = currentJ = 0`), and failed the ≥ 3-stars-per-frame
floor because at least one frame yields **zero** accepted stars under every parameter set the search reached.
This is the same family as the trap list's other named case (`D17_cdk14_oiii5` finds zero stars at short
exposures) and it is now a **detected gap with a mechanism** rather than a rumour: `lumos` contains at least one
frame that is not detectable at any point in the searchable parameter space.

**A costed bridge proposal.** Nothing here argues for a code change yet. The cheap next question is whether the
zero-star frame is a *frame* property or a *parameter-space* property — `af-fit` or `review` on
`AutoFocus_20260708_231255/attempt01` at the two focuser positions named above, **~10 m**, no rule needed. If the
frame is genuinely blank, `lumos` is an unusable run and should be labelled in the trap list as such (a
`bank-clean` candidate); if it is not, the hard floor is rejecting a frame the detector could reach and that is a
gate question. **Do not fix anything on the strength of one run.**

### 6.2 `Panos` — **`L-NOT-COMPARABLE`**, and saying so is the point

Recorded: *"`Panos` has a degenerate σ fit and must be UNEVALUATED **by name**."* **Measured: `exit=0`,
hard-floor PASS, `bestJ = 0.935582`, improved over current, 218 of 250 evaluations, 360 accepted stars on one
frame and 4 on the other.**

**This is NOT a refutation of the recorded claim, and reporting it as one would be the register's own trap
re-committed.** The recorded failure mode is about the **σ / focus-curve fit**, which is an `af-fit` /
`bank-verify` property — `optimize` does not compute σ_focus at all. **The probe ran a different instrument from
the one that produced the record**, so neither `L-REPRODUCED` nor `L-CHANGED` is assignable. The honest state is
**`L-NOT-COMPARABLE`**, and the register entry should carry the instrument name so the next wave does not have to
rediscover this.

**What it does establish, and it is a genuine coverage gain:** `Panos` is **addressable by `optimize --per-run`**,
completes in ~96 s of optimize wall, passes the hard floor, and produces a landing at `bestJ = 0.935582`. The
implicit reading that `Panos` is unusable to this series is wrong. **Settling the σ claim costs one `af-fit` run,
~5 m, and is now the only part of that trap entry still folklore.**

### 6.3 A free goal-3 data point, from two runs never before measured

Both logs print, independently:

```
sensitivity=10 (effective gate 10) (not at floor)
```

**Two real-bank runs, outside the synthetic bank and outside every population wave 23's RULE P23 scored, and
neither lands `Sensitivity` at the floor.** P23 measured `24/440 = 0.0545` pinned on its licensed primary; these
two are consistent with that and are not in its denominator. `detections kept vs seed` (F32) is **1** on `lumos`
and **1.17514** on `Panos`. Reported; no clause thresholds either, and **they are not averaged into any P23 rate**.

---

## §7 — What the wave can say, goal by goal

The owner's three goals, and this wave against each. **Design §9 fixed these obligations before the data and they
are honoured, including the two that forbid claims.**

### Goal 1 — "works across a wide range of setups, while making improvements to bridge detected gaps"

**What this wave CAN say:**

- **The first census of scenario S4 across the bank** — 20 datasets requested, 7 applicable (binary-resolved,
  matching the spec-derived expectation exactly), **78 of 79 assertion instances passing = 0.9873**, with per-`id`
  rates and both denominators printed. A scenario that had never been run in this series now has a baseline.
- **The gate reproduces on a fourteenth binary**, bit-identical at sixteen digits, across a change to shipping
  product code. Nineteen months of coordinate system, unmoved.
- **A detected gap became a measurement**: `lumos`'s `rc=3` is reproduced **and explained** — a hard-floor
  failure with a frame yielding zero accepted stars — after ten waves as folklore (§6.1).
- **A second "gap" was shown not to be one, and to belong to a different instrument** (`Panos`, §6.2), which is a
  correction to the trap list rather than a bridge.
- **F26's livelock is rarer than the register implies** — 0 of 7 blind cells, in a population selected to provoke
  it (§4.5).

**What it CANNOT say:**

- **Nothing about setups outside the bank.** Two real-bank runs were touched and neither was scored by a rule.
- **Nothing about S1 at the new binary.** The S1 rate was **not** re-measured, so wave 23's `V23-G` S1
  **13 of 17** stands exactly as wave 23 measured it — **on the pre-P2, starved instrument.**
- **`B-UNEXERCISED` is not coverage.** Seven blind cells with zero events bounds an incidence loosely and is not
  a demonstration that the bank is now well-served.

### Goal 2 — step-size accuracy

**What this wave CAN say:**

- **P1 ships and it is this wave's product change.** On a fit the recommender could not use, the wizard no longer
  presents a held step as a measured recommendation with no qualification: `DegenerateReason` names which of three
  exits was taken, `SampledHfrRange` is now **measured** on the degenerate path where `bestFit` exists, and both
  are surfaced in the wizard's step block. That is [F79](followups.md)'s failure shape — *fails closed to a value
  instead of reporting "I could not look"* — repaired **in the product**.
- **The instrument that measures goal 2 has been repaired** (P2), verified do-no-harm across 20 S0 cells on 17
  terminal fields (§3), and demonstrated to bridge the cell it was built for (§4.3).
- **The stall has at least two causes and neither is "the fit was bad"** (§5.2) — with the concrete consequence
  that an R²-keyed gate is the wrong design.

**What it CANNOT say — and this is a pre-registered prohibition, not modesty:**

- **NO NEW ACCURACY RATE. This document quotes none, and must not.** P1 changes no recommended number
  (`G24-P3b`, 8 of 8). P2 is not scored on an accuracy population.
- **Wave 23's `13 of 17` is not superseded and is not comparable to any future number.** It was taken on the
  starved instrument. **When wave 25 re-measures S1 on B14 or later, it is measuring a different instrument, and
  the comparison must be stated as such rather than presented as an improvement.**

### Goal 3 — exposure, and parameters pinned to extreme values

**What this wave CAN say:** almost nothing, and that was the plan. Two incidental observations, neither
thresholded: both L24 runs report `sensitivity=10 (effective gate 10) (not at floor)` (§6.3).

**What it CANNOT say:** wave 23's `P23`/`V23-E`/`V23-F` stand untouched. **The P23 flat-direction perturbation
arm remains unspent and still priced at ~30 m**, and it is still the deepest open question in goal 3(a).

### The honest summary line

**This wave's product change is P1, an observability change that alters no recommended value. Its behaviour
change is P2, which lives in the harness and never reaches a user. P2 shipped on `R-PRESERVED` plus one labelled
control plus four unit tests — NOT on blind evidence, because the blind population never contained the
mechanism.** Anyone reading this document for a bridge to a field defect should read §0.1 and stop.

---

## §8 — Controls

### 8.1 The four fingerprint classes — and this time the output is KEPT

Wave 23 §8 recorded a caveat that four AFTER checks left no artifact. Discharged:

| class | population | result | artifact |
|---|---|---|---|
| 1 | 42 bank landings | **42 of 42 BYTE-IDENTICAL**, 0 changed / added / removed / could-not-look | `fp_bank_AFTER.txt` |
| 2 | 59 aux (`harness_settings.json` ×39 + `synthetic_meta.json` ×20) | **59 of 59**, by-kind counts asserted | `fp_aux_AFTER.txt` |
| 3 | 48 prior-wave arm landings across three roots | **48 of 48**, each root's count asserted separately | `fp_arm_AFTER.txt` |
| 4 | **wave 23's `synth_validate_report.json` files — RULE R24's entire BEFORE side** | **38 of 38** | `fp_v23_AFTER.txt` |

**Class 4 is new and it is load-bearing**: `R24-A`'s BEFORE side *is* those files, so a file class that becomes a
denominator became a control in the same wave. `synth-validate` cannot disturb classes 1 or 2 even in principle —
it is spec-driven and never opens a bank artifact — but it absolutely can disturb class 4, which is why every
driver asserts its `--out` is under `D:\hf_w24\` and refuses otherwise.

**The class-4 population is 38, not the plan's 40** — deviation **D3**, caught **before** the arm. Wave 23 ran 40
cells but **two timed out at 600 s and produced no report** (`D05_tec140_1000mm`/S1 and
`D19_cygnus_deep_shed`/S1), which wave 23 already recorded as NOT-RUN with 38 manifest rows. The plan counted
cell **directories** (40 exist), not reports (38 exist). Asserting 40 would have raised a POPULATION FAILURE and
blocked RULE R24's entire BEFORE side on an **addressability** error the plan made — [F74](followups.md)'s half
wave 20 skipped. `EXPECTED` was set to 38, the two absent cells were named in a `NOT_RUN` constant, and a **new
self-test branch 5** asserts that `EXPECTED` equals the addressable population **and** that the gap is exactly
those two names. Self-test **5 of 5**.

**But the class-4 control's own output names the wrong evidence** — see §9.2, drift #7.

### 8.2 The other controls

- **`prov_w24.py`** demonstrated in **both** directions on real artifacts, with its printed count computed from
  `len(PRIOR_BUILD_IDS)` (`printed=13`, `len=13`, **AGREE**). Its FAIL end (`--self-test /mnt/d/hf_w23/gate`)
  correctly **refused**, because `7927513b…` is now a prior id — *a novelty list that does not refuse the binary
  it was just extended with is not a novelty list* (deviation **D6**, and the refusal is the half that matters).
- **The G24 interlock** ran FAIL-end-first and its output was kept (§1.3).
- **The ASCII census** ran FAIL-end-first on wave 23's real, published gate (§2.1).
- **Every `--self-test` ran BEFORE the thing it guards**, and the derived scorers were re-self-tested **on the
  copy** before being quoted.
- **The four failing tests were demonstrated red against pre-change behaviour by named mutants and byte backups,
  never a VCS revert** (§10.4), with `cmp`-verified restoration afterwards.

---

## §9 — The controller's nine deviations, and the pattern behind three of them

All nine of `/mnt/d/hf_w24/CONTROLLER_DEVIATIONS.md` are folded in. D3 is at §8.1, D5 at §12, D6 at §8.2, D8 at
§2.2. The remainder here.

### 9.1 D1 + D2 — a control that could not fail, inside the guard of the wave's own arm

**D1.** `b24_arm_w24.sh` **aborted in one second on its first launch** at `21:01Z`, skipping all 22 cells, each
reported as *"SKIPPED … past the 03:00Z start deadline"*. The cause is at `b24_arm_w24.sh:260`:

```bash
NOW=$(date -u +%H:%M)
if [ "$NOW" \> "$DEADLINE_UTC" ]; then    # "21:01" \> "03:00"  ->  lexically TRUE
```

**Wall-clock times compared as STRINGS, against a deadline that is tomorrow.** A same-day `HH:MM` comparison
cannot express "tomorrow", so at `21:01Z` every cell read as past a deadline nearly six hours in the future.

**This is exactly the shape the charter's §2 status sweep exists to catch** — *"a background job reporting
`exit 0` has repeatedly meant a driver aborted in one second"* — and it is why the sweep demands a `*_START` line
**and** a live `TestApp.exe`. **The `START` line alone was present and would have been believed.**

Fixed by resolving the deadline to an **absolute epoch once at launch**, rolling to tomorrow when the `HH:MM` has
already passed, and comparing numerically per cell. The driver now **prints its own resolution**, so the fix
reports whether it engaged:

```
B24_before_START 2026-08-12T21:03:35Z   deadline=03:00Z   binary=/mnt/d/hf_w23/exe   maxRounds=4
  deadline resolved to 2026-08-13T03:00:00Z (356 min from now)
```

**Is this a mid-wave repair?** No. No rule, threshold, population or bar was touched, and **no data existed** —
the arm had produced nothing. It is a driver that could not execute at all.

**D2, and this is the serious half.** The self-test branch guarding that comparison was, as pre-registered:

```bash
# the clock guard must be able to REFUSE
if [ "$(date -u +%H:%M)" \> "23:59" ]; then echo "  FAIL: the clock comparison is inverted"; rc=1; fi
```

**Nothing is lexically greater than `"23:59"`. The branch could never fire, at any time of day, against any
defect.** `--self-test` returned **rc=0** immediately before the launch that skipped all 22 cells. **A control
that cannot fail is not a control** — this series' oldest standing rule — **and it was violated inside the
instrument guarding the wave's own arm.**

Replaced with a branch that resolves a deadline **two hours in the past** and asserts it rolls to tomorrow, plus
a future-deadline branch and a refusal branch, and **demonstrated in both directions by byte-level mutation**:
deleting the `tomorrow` roll gives **rc=1** with *"a deadline 2 h in the PAST did not roll to tomorrow -- the
midnight wrap is broken"*; restoring from the byte backup and verifying `cmp`-identical gives rc=0. **`cp` and a
byte backup, never a VCS revert** — the tree carried uncommitted work.

### 9.2 D4 + D7 + D9, and five more: the derivation-drift pattern is the finding

The plan derives this wave's instruments from wave 23's by `sed`. **Eight prose-vs-mechanism disagreements
resulted, across five instruments, one of them load-bearing.** Three were caught live by the controller; five
more were found at write-up.

| # | instrument | the drift | severity |
|---|---|---|---|
| **D4** | `prov_w24.py` | active log labels read `RULE G23` after derivation while the wave's rule is `G24` | cosmetic; fixed live |
| **D4b** | `prov_w24.py:75` | a **historical** label reads *"wave 21 (RULE G23's …)"* — **wave 21's rule was G21**; wave 23's own `sed` rewrote its predecessor's history | cosmetic; **each wave's derivation corrupts its predecessors' labels** |
| **D7** | `gate_w24.sh:221` | header prints *"binary=/mnt/d/hf_w24/exe (ONE binary, the **thirteenth**)"* — it is the **fourteenth** | cosmetic; the ordinal is prose the `sed` cannot know advanced |
| **D9** | `score_g24_w24.py:275` | resolves `os.path.join(HERE, "prov_w23.py")` → a file that does not exist | **LOAD-BEARING — see below** |
| **5** | `prov_w24.py:179` | prints a hardcoded *"differs from waves 11/12/13/14/15/16/17/18-B1/18-B2: YES"* — **nine waves named while thirteen are checked** | **this is wave 20's exact defect, still alive inside the instrument the charter cites as having fixed it** |
| **6** | `prov_w24.py:227,304` | the self-test banner and usage string print **`prov_w23.py`** — the file's own printed name disagrees with its filename, in the published artifact `prov_w24.txt` | cosmetic, but it is the artifact a reader identifies the instrument by |
| **7** | `v23_fingerprint_w24.py:2,171` | docstring says *"WAVE 23 … the **wave-21** synth-validate reports"* and the PASS line prints **"38 of 38 wave-21 f21 files BYTE-IDENTICAL"** — the population is **wave 23's** 38 reports | **more than cosmetic: the published class-4 control output names the wrong wave's evidence** |
| **8** | `gapprobe_w24.sh:3` | header reads *"WAVE 23 -- ITEM L"*, and its comment says *"NINE waves"* where the design says ten | cosmetic |

**D9 is the one that mattered, and the instrument behaved perfectly.** After the gate PASSED 8 of 8
bit-identical, `score_g24_w24.py --arm-gate` returned **rc=1** and wrote **no marker**, blocking RULE B24's arm:

```
[4] prov_w23.py free controls exit=2   (BuildIds seen in the landings: ['dd6ca32ef7f941c2a54753398cc2cf6b'])
    >>> REFUSING: the free controls did not pass.
>>> NO MARKER WRITTEN.
```

The gate driver's `sed` line carried `s#prov_w23#prov_w24#g`; the **scorer's** did not. So the scorer called a
nonexistent module, caught the exception, recorded `exit=2` — **a could-not-look correctly refusing to certify.**

**That is the [F75](followups.md) discipline working exactly as designed:** the writer is the code that computes
the verdict, and it wrote nothing because it could not compute one. **A hand-written marker here would have
licensed the arm on an unrun control.** The underlying control had already passed independently minutes earlier
(`prov_w24.py --self-test /mnt/d/hf_w24/gate` → rc=0, *"BuildId novel against 13 recorded ids"*), so nothing was
certified that had not been measured. After `sed -i 's#prov_w23\.py#prov_w24.py#g'`: self-test **24 of 24**, FAIL
end **rc=1 leaving no marker**, PASS end **rc=0**.

**The pattern is the finding, not the individual bugs.** Deriving a wave's instruments from its predecessor's by
textual substitution **fails silently and repeatedly**, and the plan's `sed` lines are themselves **untested
code** that runs before any measurement. Drift #5 is the sharpest evidence: the charter records wave 20's
*"printed NINE while checking ten"* as fixed, and lists the fix — computing the word from `len(PRIOR_BUILD_IDS)`
— as standing discipline. **The count line was fixed. A second line, twenty lines away in the same function,
still hardcodes a nine-wave list and has been carried forward through four derivations.** Fixing the instance is
not fixing the class.

**Recommended remedy for wave 25 — take (b), and take it first.** D9 proposes two:

> (a) parameterise the wave id **once** at the top of each instrument and derive nothing;
> (b) add a **post-derivation check** that every `os.path.join(HERE, …)` target and every imported sibling
> **exists on disk**, run before any measurement.

**(b) is the one to ship, for three reasons.** It is ~10 lines and runs in one second; it would have caught D9
**before** the gate rather than after it; and it costs nothing when it passes. **(a) is the better end state and
the wrong thing to attempt in a single wave** — it requires editing five instruments that are deliberately
carried forward unchanged, and *editing an instrument mid-series is how it stops being the same instrument*. Do
(b) in wave 25 as a pre-flight step, and extend it cheaply to the class of drift the wave actually suffered:

> **Ship in wave 25:** a `--verify-derivation` pre-flight that asserts, for every instrument under
> `/mnt/d/hf_w<N>/`, (i) every `os.path.join(HERE, …)` target and every imported sibling exists on disk, and
> (ii) **the file contains no reference to the PREVIOUS wave's id** (`w23`, `G23`, `prov_w23`, `hf_w23`) outside
> an explicitly-declared allow-list of intentional cross-wave paths. Run it before the gate. Both halves are
> mechanical, both would have fired this wave, and (ii) catches drifts 4b, 5, 6, 7 and 8 as well as D4 and D7.

**Move (a) to wave 26 or later**, and note that any ordinal or count in a derived instrument's output must be
**computed, never typed** — the durable rule D7 names.

---

## §10 — What SHIPPED, and under which rule

### 10.1 The three changes

| | change | files | ship rule | outcome |
|---|---|---|---|---|
| **P1** | `DegenerateReason` on all three degenerate exits (`no-fit`, `non-finite-vertex`, `half-width-unresolved`); `SampledHfrRange = MeasureSampledHfrRange(bestFit)` when `bestFit != null`; mirrored into `StepRecommendationSnapshot`; surfaced as `OptimizationSummary.StepSizeDegenerateReason` in the wizard's step block | `StepSizeRecommender.cs`, `StarDetectionOptimizerWizardVM.cs`, `DataTemplates.xaml`, `SynthValidationReport.cs`, `SynthValidateRunner.cs` | **UNCONDITIONAL** (design §3.1) — no clause thresholds `degenerateReason` | **SHIPPED.** Inertness measured, not asserted: `G24-P3b` 8 of 8 |
| **P3** | `binningRecommendation.currentFactor` / `.appliedFactor`; `applied.stepDeferredByBinning` / `.binningDeferralBoundReached` | `SynthValidationReport.cs`, `SynthValidateRunner.cs` | **UNCONDITIONAL** (design §3.3) | **SHIPPED.** `R24-D` 21 of 21 |
| **P2** | the binning-**revisit** deferral bound | `SynthValidateRunner.cs` + **`SynthBank/BinningRevisitPolicy.cs`** | **CONDITIONAL on RULE R24 returning `R-PRESERVED`** (design §3.2) | **SHIPPED**, licensed by `R-PRESERVED` (§3.5) |

**The ship rules are quoted from the pre-registration, which was committed at `82a3a0a` before any measurement.
None was harvested from the data.** P1 and P3 are wave 20's D1/D2/D3 shape and wave 23's F79 shape: the ship is
not what a rule decides, and the charter's §0.1 permits it in exactly that case.

### 10.2 One pre-registration deviation in P2's shape, not recorded live

Design §3.2 and plan §3b say **"P2 must be confined to `SynthValidateRunner.cs` so a P2 revert cannot take P1 or
P3 with it"**, and design §5.3 says *"the code agent's byte backups of `SynthValidateRunner.cs` (P2's only file)
are restored."* **P2 in fact landed as a new file, `Joko.NINA.Plugins/TestApp/SynthBank/BinningRevisitPolicy.cs`,
plus its call site in the runner.** Not in `CONTROLLER_DEVIATIONS.md`; named here.

**It did no harm and it is arguably better** — a policy in its own class makes the revert *cleaner* (delete the
file, restore the runner) rather than harder, and the mutation harness took byte backups of **all four** files
including it, so the unship path remained mechanically available throughout. But **the pre-registration said one
file and the code is two**, and a design that specifies a revert boundary should have it honoured or amended in
writing, not silently widened.

### 10.3 What did NOT ship

**Bridge A, the directional degenerate-fit gate, does not ship**, on the three independent grounds fixed in
design §1.3 before any data: its threshold cannot be chosen blind from anything then on disk; its decision
variable (`SampledHfrRange`) was **NaN on the branch it must decide**; and its risk population costs a second
paired arm the window could not buy. **§5 is the instrument that makes wave 25's version choosable blind, and it
also shows the obvious version would be wrong** (§5.2).

### 10.4 The tests, and the failing four demonstrated red

Six new tests (design §3.4). **Four must fail against pre-change behaviour and all four were demonstrated red by
named mutants and byte backups** — never `git checkout --`, because the tree carried the uncommitted
pre-registration and now carries this document:

```
Failed OscillatingRecommendation_AppliesTheStepWithinThreeRounds   [52 ms]
Failed RevisitedBinningFactor_DoesNotDeferTheStep                  [5 ms]
Failed RoundRecord_CarriesTheBinningAndDeferralFields              [3 ms]
Failed Degenerate_ReportsItsReason_AndMeasuresSampledHfrRange      [18 ms]
Failed!  - Failed: 4, Passed: 40, Skipped: 0, Total: 44, Duration: 1 s
```

with the assertion messages captured (`prechange_test_evidence.txt`) — including `SampledHfrRange` *"Expected:
1.05d … But was: NaN"* on the `:252` exit, which is design §1.2's claim measured rather than argued. The other
two (`NonDegenerateRecommendation_HasNoDegenerateReason`,
`FirstVisitToANewFactor_StillDefersTheStep`) are **companions that pass pre-change and are labelled as such**.
Restoration was verified `cmp`-identical on all four files.

**The suite by COUNT is OPEN** (see the timestamp box). Expected **3966** = 3960 + 6. **No number is claimed.**

---

## §11 — Register entries this wave owes

Design §12 fixed these in advance so the analysis agent could not invent them, and so a wave that finds nothing
says so. **This wave did not find nothing.** Entries are written into [`docs/followups.md`](followups.md).

| entry | action | on what evidence |
|---|---|---|
| **[F79](followups.md)** | **VERIFIED, and EXTENDED with D8's masked-defect finding** — extended rather than a new entry | `G24-P4` 0 of 8 clean against 8 of 8 on wave 23's gate; and the same driver print still reads `0 of 8` from a **line-vs-block counting bug** the σ was masking (§2.2). **Why extend and not open F80:** the finding is *about F79's own statistic, in F79's own script, discovered by F79's own fix* — a reader who lands on F79 must learn that fixing it did not validate the print, and a separate entry would let them leave without that. It also does not name a new mechanism: it is F79's *"a wrong number is not self-explaining"* one level up |
| **NEW — the derivation-drift pattern** | **NEW ENTRY** | Eight prose-vs-mechanism drifts across five instruments in one wave, one load-bearing (§9.2). This **is** a mechanism none of F66/F74/F75 owns: F66 is about a check that cannot return its own PASS, F74 about two components rebuilding a path, F75 about who writes an interlock. **This is about instruments being *derived by textual substitution* and their prose silently ceasing to describe them** — and drift #5 shows the register's existing fix for one instance did not fix the class |
| **[F26](followups.md)** | **STATUS from `B24`, plus the `D08` bridge** — **and wave 23 §13's scope correction is left exactly as it stands** | `B-UNEXERCISED`, 0 of 7 blind cells: **the livelock is rarer than `D08` suggests**. And on `D08`/S1 itself, the guard F26 asked for **meets F26's own pre-registered bar**. The entry's harness-not-product scope correction is **not** disturbed |
| **[F25](followups.md) / [F34](followups.md)** | **EXTENDED with D24's hand-off, and F34's `D01` attribution CORRECTED** | `D03`/S1 is degenerate at **R² = 0.9835** and `D02`/S1 at **−0.2741**, so an R²-keyed gate misses D03; the **wide end is empty** so wave 25's threshold is one-sided; and **`D01`/S1 is not degenerate at all**, which corrects wave 23's grouping of D01/D02/D03 (§5.2) |
| **[F21](followups.md)** | **COSTING EXTENDED** | first per-scenario `synth-validate` rates and the first measurement of how far a *derived* scenario price misses (§12) |
| **[F76](followups.md)** | **CITED, not extended** | P2 shipped with `binningDeferralBoundReached`, and it is what proves P2 engaged on `D08`/S1 r1 |
| **the trap list** (`lumos` / `Panos`) | **CORRECTED** | `lumos` `rc=3` reproduced **and quoted**; `Panos`'s recorded failure belongs to **`af-fit`**, not `optimize`, and is `L-NOT-COMPARABLE` from this probe (§6) |

**Is there a finding worth a register entry? Yes — several, and one is a NEW entry.** Stated explicitly because
the charter requires it: **wave 24 is not a dry wave.** Wave 23 was not dry either (it opened F79 and produced the
§13 scope correction), **so the two-consecutive-dry-waves stop condition is not armed and does not come into
view.**

---

## §12 — Timing: what was priced, what it cost, and the lesson that is NOT "reserve more"

| step | priced | actual | ratio |
|---|---|---|---|
| A-BEFORE arm (S4 × 20 + `D08`/S1 + `D11`/S1) | ~45 m | **17 m 39 s** | **0.39×** |
| RULE G24 gate, 8 runs | ~42 m | **41 m 15 s** | **0.98×** |
| A-AFTER arm (+ 3 labelled controls) | ~50 m | **18 m 12 s** | **0.36×** |
| R-AFTER arm (S0 × 20) | ~45 m | **42 m 56 s** | **0.95×** |
| S2 census (7 cells) | ~18 m | **7 m 47 s** | **0.43×** |
| item L24 (2 runs) | ~12 m | **10 m 17 s** | **0.86×** |
| **TestApp wall total** | **~3 h 32 m** | **2 h 18 m 06 s** | **0.65×** |

**Deviation D5 records the A-BEFORE under-run and asks that wave 23's 3.5× under-reserve not now be over-corrected
into an over-reserve. The full table says something sharper than either.**

**Where the price came from measured data, it was excellent. Where it came from an extrapolation, it was 2.3–2.8×
too high.** The gate (0.98×) and the S0 arm (0.95×) were priced from **the same instrument on the same scenario**
— the gate from six prior waves at ~5.2 m/run, S0 from wave 23's own per-cell `run.log` mtime deltas. Both landed.
**S4 and S2 were priced by *deriving* from a scenario that had never been run** — S4 at 2× those datasets' S1 sum,
S2 at 1× — and both came in at ~0.4×.

**The rule this supports, and it is the honest form of both corrections:** *price from the same instrument **and
the same scenario**; when a scenario has never been run, say the price is a derivation and give it a band, not a
number.* The 13 inapplicable S4 cells returning in 1–2 s each was predicted correctly; what was over-priced was
the **applicable** cells, which ran 13 s – 6 m 39 s rather than the assumed ~270 s each.

**What the spare 1 h 14 m bought:** the S2 census (drop D1) and item L24 (drop D2) **both ran** despite being
first and second on the drop list. Two waves' worth of deferred items closed because the budget was conservative
in the right direction — **which is an argument for keeping a derived price conservative, not for tightening it.**

---

## §13 — What was NOT run, and priced

| item | price | why not, and what it would buy |
|---|---|---|
| **the S1 re-measurement on B14** | **~1 h 20 m** (S1 × 19) | It is the *consequence* of a working P2, not a precondition, and running it in the fix's own wave would score the fix on the population that motivated it. **Wave 25 owes it** if it wants to quote a goal-2 number, and must state that it is a different instrument from wave 23's `13 of 17` |
| **Bridge A / the directional degenerate-fit gate** | ~1 h code + 2 tests, **plus a paired arm ≈ 2 h** | Design §1.3, three independent grounds. §5.2 now adds a fourth: **the obvious R²-keyed version would be wrong**, and the wide end is empty so the threshold would be one-sided |
| **the P23 flat-direction perturbation arm** | **~30 m**, no product code | Untouched. Still the deepest open question in goal 3(a) and still wave 25/26's strongest non-product candidate |
| **`lumos`'s zero-star frame** | **~10 m** of `af-fit` / `review` | §6.1. Would settle whether `lumos` is an unusable run or a gate question |
| **`Panos`'s σ fit** | **~5 m** of `af-fit` | §6.2. The only part of that trap entry still folklore, and now precisely scoped to one instrument |
| **F67's residual** | ~30 m of `af-fit` at both binning factors + a rule | An [F62](followups.md) landmine (**no `BestJ` across factors**) and it touches no named gap |
| **F73's code axis** | ~1 h 50 m of arms + a ~42 m gate | 0 for 3 on the owner's goals. Wave 23's correction stands |
| **the `RecommendFromHfr` hysteresis** ([F22](followups.md)) | unpriced; owes a coordinate-system re-baseline | F26's own next step says it *"is a separate, larger change and should not be bundled."* **Not bundled** |
| **F70(b′) / PR #192** | — | **REJECTED by the owner. Not re-proposed anywhere in this wave** |
| **RULE F14 / RULE S16 / RULE D20** | — | **Three permanent fences. Not re-scored, not converted, not harvested.** No clause in this wave reads any of them |

---

## §14 — Wave 25, recommended

**Recommendation: wave 25 is the degenerate-fit / stall bridge — a PRODUCT change on goal 2 — but NOT the gate
this wave arrived expecting to hand it, and the difference is the whole value of §5.2.**

| | |
|---|---|
| **goal served** | **2 (primary)**, 1 (secondary — it is a bridge for a detected gap) |
| **price** | **~3 h 15 m**: ~1 h code + tests, ~42 m gate, ~1 h 20 m paired S1 arm across the bank, ~15 m scoring |
| **ships product?** | **Yes** — `StepSizeRecommender`, the shipping plugin class |

**What it must NOT be.** A fit-quality gate keyed on **R²**. `D03_redcat_250mm`/S1 is degenerate at
**R² = 0.9835** and `D01_ultrawide_40mm`/S1 stalls at **R² ≈ 1.0 with no degeneracy at all**. An R² gate misses
both. **This is the single most useful thing wave 24 hands forward and it would not have been visible without
P1.**

**What it should be, in three parts, and the third is the new one:**

1. **Key the gate on `sampledHfrRange`, directionally** — widen when the sweep sampled too little curve, shrink
   when it sampled too much. The decision variable now **exists on the branch it must decide** (P1), which is
   what made wave 23's Bridge A unimplementable.
2. **Declare the threshold one-sided, in writing, before the data.** The wide end is **empty** (0 of 7 S2 cells).
   Wave 25 can pre-register a narrow-end threshold honestly and must state that the wide-end half is
   **unmeasured**, not merely untriggered. [F25](followups.md)'s own wide-end example is published and cannot be
   used blind.
3. **Handle the NON-degenerate stall separately, because it is a second mechanism.** `D01`/S1 holds a 4.5×-too-narrow
   step from a perfect fit with a resolved half-width. A gate on degeneracy will not touch it. **The cheap
   companion fix is in the harness and costs almost nothing: `SynthValidateRunner`'s stall message asserts
   *"from a degenerate fit"* without checking `degenerateReason`, which is now a field it can read.** Fix the
   message, then decide whether the *product* should refuse to converge on a no-op that is 4.5× from
   `stepBehavioral` — a question wave 25 can pre-register a bar on because `stepBehavioral` is measured.

**Blindness, and it is buyable.** `D01`/`D02`/`D03` S1 are published and cannot score the fix. **Score it on
S1 across the other 17 datasets**, paired BEFORE/AFTER, with those three as labelled controls in no denominator —
the exact structure wave 24 used for `D08`. That also **discharges the S1 re-measurement wave 25 already owes**
(§13) in the same arm, which is why the price is 3 h 15 m and not 4 h 35 m.

**Add two pre-flight items, both cheap:** the `--verify-derivation` check from §9.2 (**~15 m**, would have caught
D9 in one second), and `lumos`'s zero-star frame (**~10 m**, §6.1).

### On the proposed 25 / 26 / 27 order

**Keep it, with one swap and one addition.**

| wave | proposed | recommendation |
|---|---|---|
| **25** | stall bridge + degenerate-fit gate | **Agree** — highest decision value per hour, ships product, serves goal 2, and wave 24 built its instrument. **But re-scope per §14: not an R² gate, one-sided by declaration, and the non-degenerate stall is a separate mechanism** |
| **26** | P23 flat-direction perturbation arm | **Agree, and it is under-priced at ~30 m.** It is the only goal-3 question left with real decision value. At ~30 m of arm it leaves room to pair it with **F67's residual** (~30 m) in one wave, if and only if the [F62](followups.md) no-`BestJ`-across-factors landmine is pre-registered around |
| **27** | instrument hygiene + F67's residual | **Swap.** Move **instrument hygiene into wave 25's pre-flight** where it costs 15 minutes and prevents a repeat, and move **F67's residual into 26**. That frees wave 27 for what wave 25's result will actually demand: **the S1 re-measurement's follow-up if the gate bridges**, or the wide-end S2 arm if it does not |

**The argument for the swap in one line:** hygiene is worth 15 minutes as a guard and is not worth a wave as a
subject, and wave 27 is more valuable held open for wave 25's consequence than pre-committed to a backlog item
that scores 0 for 3 on the owner's goals.

**One thing to watch.** If wave 25's bridge lands, **wave 23's `V23-G` S1 `13 of 17` becomes uncomparable to
everything after it** — it was measured on the starved instrument, and wave 25 will change both the instrument
(P2, already shipped) and the product (the gate). **Pre-register that the comparison is against wave 24's
post-P2 baseline, which does not exist yet** — which is a reason for wave 25 to run its S1 BEFORE arm on B14
rather than reaching for wave 23's numbers.

---

## §15 — What was OPEN at the write-up timestamp, and how it closed

The box at the top of this document lists what was open at **`2026-08-13T00:12:11Z`**. Resolutions are recorded
here rather than by editing that box, **so the record of what was open when the analysis was written stays
intact.** This is wave 23 §12's pattern and it exists because a results document that quietly back-fills its own
open rows cannot be audited afterwards.

| was open | closed | evidence |
|---|---|---|
| **the full suite by COUNT** | **`3966` passed / `0` failed / `0` skipped**, `3 m 35 s`, `SUITE_EXIT=0`, read by **COUNT** out of the log ([F37](followups.md)) | `/mnt/d/hf_w24/suite.log`, finished **`00:14:19Z`** — **two minutes AFTER the timestamp above**, which is precisely why it was marked open rather than anticipated |
| **the commit, the push, the PR #195 section** | see the commit this document lands in | — |
| **`binaries_final.txt`** | **still not written.** The two dll hashes are recorded independently in all four driver-written markers, taken from the files on disk at run time | `B24_BEFORE_READY`, `B24_AFTER_READY`, `R24_READY`, `S2_CENSUS_READY` |

**The suite delta is exactly as pre-registered: 3960 + 6 = 3966**, and all six are named in §10.4 — four shown
red against pre-change behaviour by named mutants, two labelled companions that pass pre-change. **No test was
skipped, marked expected-to-fail, or weakened.** The known-flaky `SendAsync_WritesOnABackgroundThread` did not
fire.

**Waves 25+ baseline from 3966.** Every number below it in any document — 3922, 3933, 3958, 3960 — is stale.
