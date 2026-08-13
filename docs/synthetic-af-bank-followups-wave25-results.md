# Wave 25 — results

**Write-up timestamp: `2026-08-13T05:06:53Z`.** Everything below is the state of `/mnt/d/hf_w25/` at that
instant. Anything still open then is marked **OPEN in place** in §15 and its resolution is recorded there rather
than by editing the row — wave 23 §12's pattern, kept because a results document that back-fills its own open
rows cannot be audited afterwards.

Pre-registration: [`docs/synthetic-af-bank-followups-wave25-design.md`](synthetic-af-bank-followups-wave25-design.md)
(committed `352cf26`, **before any wave-25 measurement**).
Plan: [`plans/synthetic-af-bank-followups-wave25-plan.md`](../plans/synthetic-af-bank-followups-wave25-plan.md).
Code: `29665a6`. Controller's live deviation log: `/mnt/d/hf_w25/CONTROLLER_DEVIATIONS.md` (**D1–D10**).
Predecessor: [`docs/synthetic-af-bank-followups-wave24-results.md`](synthetic-af-bank-followups-wave24-results.md),
whose §5.2 and §14 decided what this wave is.

---

## Status

| | |
|---|---|
| **vessel** | **CONTINUED** on `ghilios/synthetic-af-bank-followups-wave23`, **PR #195** (open). Decided in writing in design §0, before any measurement |
| **binary** | **B15, the FIFTEENTH.** `BuildId d79dae73582e4a6c95e5c81f1f143ffc`, **novel against all fourteen** recorded prior ids |
| **RULE V25** (pre-flight, blocking) | `V-DRIFT` on `/mnt/d/hf_w24` (170 findings) → **`V-CLEAN`** on `/mnt/d/hf_w25` after fixes. **Two gaps in the checker itself, both closed and demonstrated** |
| **RULE G25** | **`G-PASS`.** `FinalJ` bit-identical to K8 on **8 of 8** at all sixteen digits; `G25-P2a`/`P2e`/`P3` 8 of 8; `P4-ASCII-CLEAN`; **`G25-P3c`: 3 WIDENED, 5 UNCHANGED, 0 NARROWED** |
| **RULE W25** | **`W-UNEXERCISED`** — **0 of 13** paired blind cells floored. All 13 converged on **both** arms |
| **RULE N25** | **`N-PRESERVED`. P4 SHIPS.** `N25-A` **15 of 15**, `N25-D` **18 of 18**, `N25-B` **0**, `N25-C` REGRESSED **0** |
| **RULE M25** | **`M-CORRECTED`.** FAIL end on wave 24's published arm: 25 cells, **exactly 1** contradiction, `D01` by name. AFTER arm: **0** |
| **ships** | **P4** (product: `StepSizeRecommender`'s missing floor) under G25 + N25. **P5** (harness stall message) unconditionally |
| **suite** | **`3973` passed / `0` failed / `0` skipped**, `SUITE_EXIT=0`, read by **COUNT** ([F37](followups.md)). Exactly the pre-registered `3966 + 7`. **OPEN at the timestamp; closed `05:10:24Z`** — §15 |

**Open at `05:06:53Z`:** the full suite (closed `05:10:24Z`); `RULE N25-E` (**not run** — drop-ladder rung D2's
04:40Z trigger had already fired when the AFTER arm ended at 05:03:49Z); the scorer's own `--out` capture (§0.2);
the commit, the push and the PR #195 section.

---

## §0 — Three things about this document's own honesty, before the rules

### 0.1 What this document is allowed to say

Design §11 fixes it in advance and it is obeyed here. **No comparison to wave 23's `V23-G` S1 `13 of 17`** — it
was measured on B13 and `N25-E`, the clause that would have made it comparable again, was not run. **No wide-end
claim of any kind**; the wave-shrinking half of [F25](followups.md) is **unmeasured, not untriggered**. **No rate
over a denominator containing a labelled control.** **No bar invented for a "reported, no bar" clause.**
**`RULE F14` / `RULE S16` / `RULE D20` are three permanent fences** and no clause in this wave reads any of them.

### 0.2 The wave's central scorer left no output file, and every number here was re-derived from the arms' own reports

At the write-up timestamp `/mnt/d/hf_w25/` contains **no** `w25_score.txt`, `w25_scorer_selftest.txt`,
`m25_failend.txt`, `n25e.txt`, `p3c_selftest.txt`, `v25_selftest.txt`, `layout_selftest.txt` or
`g25_asciifailend.txt`. The plan's step 7 `tee`s existed as commands; the files are not on disk. The scorer
plainly **ran** — `CONTROLLER_DEVIATIONS.md` D10 quotes its per-round output, and plan §0 records its self-test
reproducing `M25`'s FAIL end at *"25 cells scanned, exactly 1 contradiction, `D01_ultrawide_40mm__S1`"* — but its
evidence exists only as console scrollback.

**Every W25 / N25 / M25 / W25-K number in this document was therefore verified by reading
`/mnt/d/hf_w25/{before,after}/*__S1/synth_validate_report.json` directly**, field by field, against the branch
tables as written. **All of them hold.** No verdict in this document rests on an uncaptured console. `RULE G25`,
`G25-P3c`, `G25-P4`, `RULE V25`, the four fingerprint classes and the provenance check all left their `--out`
files and are quoted from them.

**This is the same defect family the wave itself spent its pre-flight on.** [F74](followups.md)'s standing rule is
that an interlock which transmits one bit where it could transmit the payload is a handshake with the payload
thrown away; a scorer whose verdict reaches the next stage only through a human's terminal is the same shape one
level up. It is recorded in §11 and it costs the series nothing this time only because the inputs are still on
disk and the arithmetic is reproducible.

### 0.3 One pre-registered input moved between the arms, and it is named here rather than absorbed

`terminal.stepBehavioral` — the truth the bar is measured against — is **not arm-invariant on `D01`**. It reads
`8.0` (band `3.2`) on the BEFORE arm and `9.0` (band `3.6`) on the AFTER arm. It is identical across arms on the
**other 17** paired cells, including every blind cell and the other four controls, so no denominator is affected;
but design §8's arithmetic bar was written against `8.0`, and the cell it was written for is the one that moved.
**`D01` misses the bar under either truth** (final `3`, versus `[4.8, 11.2]` at truth 8 and `[5.4, 14.4]` at
truth 9), so the verdict does not turn on it — but "the truth is spec-derived and identical across arms" is an
assumption this wave can no longer make for free, and §11 records it.

---

## §1 — The vessel: CONTINUE on PR #195, decided before the data

Design §0's argument was arithmetic, not preference: **the paired arm's BEFORE side is B14, the binary built at
`476369a`**, already on disk at `/mnt/d/hf_w24/exe`. `476369a` carries wave 24's P1 (`DegenerateReason`,
`SampledHfrRange` on the degenerate path), P2 (the binning revisit bound) and P3. A branch cut from `develop`
would not contain them, so the AFTER binary would have differed from the BEFORE binary by **four** changes and
every clause in `RULE W25` and `RULE N25` would have been confounded.

The contingency (owner merges #195 before the build → cut fresh and verify P1/P2/P3 **by content**) did not fire;
#195 was still open. **The two-binary provenance was taken against the correct base this time**, reading B14's
tree out of wave 24's own provenance file rather than assuming it:

```
B14 tree (from wave 24's provenance): 476369ac978334c14969810576fc0c218e85e7c6
```

Wave 24 recorded the wrong base (`e7a5ee6`) and had to append a correction to its own provenance file. Wave 25
read the base **out of the predecessor's artifact** instead of retyping it, and it was right first time. That is
the cheapest possible fix for a defect that cost the previous wave a correction block, and it is the pattern
worth carrying: *when a prior wave published a value, read it from the publication.*

**The `476369a..29665a6` delta is eight paths, and every one is declared:**

```
Joko.NINA.Plugins.HocusFocus.Tests/Bank/SynthValidateRunnerBinningDeferralTests.cs   T4's home
Joko.NINA.Plugins.HocusFocus.Tests/Joko.NINA.Plugins.HocusFocus.Tests.csproj         P5's <Compile Include> line
Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/Optimization/StepSizeRecommenderTests.cs  T1-T3, T5-T7
Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/StarDetectionOptimizerWizardVM.cs  P4: StepSizeText + summary
Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/StepSizeRecommender.cs             P4a + P4b
Joko.NINA.Plugins/TestApp/SynthBank/StallReason.cs                                   P5, new, pure, testable
Joko.NINA.Plugins/TestApp/SynthValidateRunner.cs                                     P4's snapshot + P5's call site
Joko.NINA.Plugins/TestApp/SynthValidationReport.cs                                   P4: wasBandFloored
```

Design §3.3 named five; **D5 recorded the other three before the data existed** — the invalidated wave-24 test
fixture that forced `StepSizeRecommenderTests.cs` to revert with P4, and P5's three-artefact shape. `DataTemplates.xaml`
is **absent** from the delta, which is the check that design §3.1's *"no new XAML row"* claim was true, so the
`StarDetectorMetrics`/options UI invariant is not triggered.

**Binary identity is the dll sha256 pair, never the apphost.** `/mnt/d/hf_w24/exe/TestApp.exe` and
`/mnt/d/hf_w25/exe/TestApp.exe` are **byte-identical** (`dd7103c28cc610e72671534cf23fb9a58b5a303a19d8779545cb7418d4ce6ff7`)
across two binaries whose `TestApp.dll` differs — an **eighth** counter-example to exe-hash-as-identity
([F66](followups.md)).

---

## §2 — RULE V25: the pre-flight was blocking, it fired, and it had two gaps of its own

This is the wave's methodology story and it cuts both ways.

### 2.1 The FAIL end, on wave 24's real published instruments

`verify_derivation_w25.py /mnt/d/hf_w24` → **`V-DRIFT`, 170 findings, exit 1** over 10 files: **168** unlicensed
previous-wave tokens and **2** typed ordinals, including the two the design named in advance —
`gate_w24.sh:221`'s *"the thirteenth"* on a fourteenth binary, and `prov_w24.py:179`'s hardcoded
*"differs from waves 11/12/13/14/15/16/17/18-B1/18-B2"* while thirteen were checked. Both are wave 24's own
[F80](followups.md) evidence rows, reproduced by a mechanical check in one second. The pre-flight also reports
**86 references in pure non-printing comments**, which it counts and does **not** flag — documentation is not
drift, and a checker that cannot tell them apart would be unusable.

### 2.2 The PASS end, and what it cost to get there — 21 findings on the wave's own instruments

On its **first** run against wave 25's own freshly-derived instruments the pre-flight returned **`V-DRIFT`, 21
findings**: `V25-A` sibling targets resolving **5 of 15**, `V25-B` **9** surviving tokens, `V25-C` **2** typed
ordinals. **It was right about all of them.**

**The largest group was a `sed` ORDERING bug in the plan itself.** The substitution list runs

```
-e 's#g24_#g25_#g'   ...   -e 's#score_g24_w24#score_g25_w25#g'
```

so by the time the second rule fires the text already reads `score_g25_w24` and it **never matches**. **Ten of
fifteen sibling references pointed at `/mnt/d/hf_w25/score_g25_w24.py`, which does not exist** — [F74](followups.md)'s
addressability failure arriving through a new door, and exactly the shape that cost wave 24 its interlock mid-run.

| finding | what it was |
|---|---|
| `gate_w25.sh` ×2 | **`G24_START` / `G24_DONE` survived.** The rename list covered `G24_PASSED` explicitly and dropped the `_START`/`_DONE` rules |
| `gate_w25.sh` ×2 | `layout_w24.sh`, `v23_fingerprint_w24.py` — no rule covered a bare `_w24` |
| `gate_w25.sh:221` | typed ordinal *"the thirteenth"* on a **fifteenth** binary — wave 24's D7, repeated |
| `prov_w25.py:180` | the typed nine-wave enumeration — **wave 20's exact defect, still alive four derivations later** |

**`G24_START` was load-bearing.** The driver would have announced `G24_START` while the controller's `until` loop
grepped for `G25_START`: the wave would have hung on a completed arm, or been read as never started. All fixed
before any measurement; `prov_w25.py`'s enumeration is now **computed** from `len(PRIOR_BUILD_IDS)`, and the
re-run reads **`V-CLEAN`: 15 of 15 siblings resolve, 0 tokens, 0 typed ordinals.**

### 2.3 THE DURABLE FINDING: an identifier boundary is not a word boundary, and `\b` cannot see `_`

**The checker had two gaps, both the same root cause, on opposite affixes. Neither was theorised; both were
demonstrated.**

**Gap 1 — the SUFFIX form, and it was found BY ACCIDENT (D2).** `V25-B`'s pattern used `\bG24\b`. `_` is a word
character, so there is **no boundary after the digits** and `\bG24\b` cannot match `G24_START`. The only reason
the surviving marker appeared in the report at all is that **`V25-C`'s typed-ordinal clause happened to fire on
the same physical line** (`gate_w25.sh:221` carried both `G24_START` and the word *"thirteenth"*). Had that line
not also carried an ordinal, the load-bearing marker would have passed the pre-flight **silently**.

> **A checker that finds a defect by accident has not checked for it.** The register's standing rule is that
> "could not look" needs its own state; this is the neighbouring case — *"looked with an instrument that could
> not resolve the thing"* — and it was visible only because two independent clauses overlapped on one line.

**Gap 2 — the PREFIX form, and it passed a file that could not run (D8).** After gap 1 was closed, `gate_w25.sh`
called three functions — `w24_gate_log_wsl`, `w24_gate_out_win`, `w24_layout_self_test` — and **`RULE V25` passed
the file as `V-CLEAN`**. Same cause, other side: `\bw24\b` cannot match `w24_gate_log_wsl`. The gate then aborted
at its own self-test:

```
gate_w25.sh: line 169: w24_layout_self_test: command not found
```

**And this was not a rename.** Wave 25's `layout_w25.sh` was written for the S1 arm and exposes a **different
API** (`w25_layout`, `w25_cell_dir`, `w25_resolve_deadline`), so the three gate functions did not exist under any
name. **No token-renaming scheme could have fixed it — the two files were never the same interface.** The three
functions were ported into `layout_w25.sh` with a self-test of their own.

Both gaps closed with:

```python
r"\bw%d(?:_[a-z][a-z0-9_]*)?\b"       # prefix form: w24_gate_log_wsl
r"\b[GBRD]%d(?:_[A-Z][A-Z0-9_]*)?\b"  # suffix form: G24_START
```

and **both closures demonstrated on synthetic files** that now report, with the checker's own `--self-test` still
exiting 0. Byte backups taken before each edit; no VCS revert.

**Two more, recorded rather than quietly fixed:**

1. **`gate_w25.sh` still printed `=== gate_w23.sh --self-test ===`** — a **w23** label surviving **two**
   generations. `V25` checks only the *immediately previous* wave's tokens. **Drift skips generations; the
   checker's `PREV` is a single wave.**
2. **The controller's own added guard tripped the checker, correctly.** It asserted the ported path did *not*
   contain `hf_w24` — itself a surviving previous-wave literal. Rewritten to assert **positively** that the path
   contains this wave's root: a stronger assertion that needs no reference to the past at all.

**The standing lesson, paid for three times in two waves: every marker, function and path in this series is
underscore-joined, and `\b` cannot see `_`.**

---

## §3 — RULE G25: `G-PASS` on a fifteenth binary, and the clause where efficacy actually showed

`optimize --per-run --max-evals 250`, mixed real + synthetic, sequential, one `TestApp.exe`, pinned
`D:\hf_w11\pinned_settings_w11.json` md5 `a67ffc06164c81613aef5c4f8324b9b8`, **not re-pinned** ([F63](followups.md)(b)).
`G25_START 02:59:19Z → 03:42:02Z`, **42 m 43 s** against a ~42 m measured price.

| clause | result |
|---|---|
| **`G25-1`** | **8 of 8** `FinalJ` **bit-identical to K8 at all sixteen digits.** `BaselineJ` reproduces w11 on 8 of 8 |
| **`G25-2`** | aggregate summaries present, **8 of 8** |
| **`G25-P1`** | `optimize/seed` reads `NoiseReductionRadius=3`, **8 of 8** — the anti-leak clause |
| **`G25-P2a`/`P2e`** | exactly one `optimize/detected`, `optimize/baseline` and `optimize/seed` **block** each, **8 of 8**, parsed as `BEGIN`/`END` pairs in Python **on bytes**, never `grep` ([F79](followups.md)) |
| **`G25-P3a`** | `RecommendedStepSize` present and finite, **8 of 8** |
| **`G25-P4`** | **0 of 8** files carry a byte `>= 0x80`; **0** such bytes total → **`P4-ASCII-CLEAN`** |
| **`G25-P3c`** | **3 WIDENED, 5 UNCHANGED, 0 NARROWED** — below |

Provenance across the arm: one `BuildId` (`d79dae73…`), **novel against all fourteen** recorded prior ids;
`DetectorVersion` **2** read as a **field** on 8 of 8; one `ProfileId` by containment **and** cardinality; one
`FitInputs` value (`MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid`);
`ConcurrencyCheck` `exclusive` on all eight. `prov_w25.py --self-test` demonstrated **both directions on real
artifacts before the check was quoted** — the real arm PASSes, a copy with one landing's `ProfileId` rewritten
FAILs on **both** the pin clause and the cardinality clause, and the printed count agrees with
`len(PRIOR_BUILD_IDS) = 14`.

### 3.1 `G25-P3c` is where the change was shown to work on data it had never seen

This is the only clause in the wave that put P4 in front of **unseen** landings and got a movement.

```
CWhiteFocus                 101 -> 101     UNCHANGED
D18_m24_deep_shed            18 ->  26     WIDENED
D19_cygnus_deep_shed         35 ->  35     UNCHANGED
D20_m24_bright_control       19 ->  26     WIDENED
mccomiskey                   31 ->  34     WIDENED
muggsie                     550 -> 550     UNCHANGED
toml999                      16 ->  16     UNCHANGED
uneven                      488 -> 488     UNCHANGED
```

**Three widened; zero narrowed.** The change is a `Math.Max` against an existing ceiling and has no code path
that lowers a recommendation, so `NARROWED >= 1` was pre-registered as *"proof the shipped implementation is not
the pre-registered one"* and an immediate STOP. It did not occur. Each movement is named with **both integers**,
and the baseline agrees with wave 24's eight published integers on **8 of 8**, checked before the gate ran.

**`mccomiskey` is a REAL-bank dataset.** The floor engages on a **real optical train**, not only on synthetic
ones — which is the single most transferable thing this wave measured, because every other place P4 engaged was
a labelled synthetic control.

**And D4 predicted exactly this before the data existed.** The code agent, implementing, recorded that P4a's
blast radius is wider than the three named cells: the rule fires on *any* sweep with `sampledHfrRange < 3` whose
fitted half-width lands below `1.5 ×` the sampled half-span, and for a standard hyperbola of edge ratio `e`,

```
halfWidth / halfSpan = sqrt(8 / (e^2 - 1))
```

so the floor engages across **`e` ∈ (~2.135, 3.0)** as well as on genuinely under-reaching fits — at `e = 2.5` a
**1.215×** widening, just below `e = 3` a **1.5×** widening, at `e = 3.001` nothing. **A discontinuity at the
threshold that moves a NUMBER, not merely wording.** Three widened landings out of eight is what that rule
predicts. It reads as the rule working, not as a surprise, **because the prediction is on the record with a
timestamp earlier than the data.**

**Two things D4 also fixed in advance and they must not be re-litigated now.** A high `WasBandFloored` count is
**not** evidence of over-reach, and a low one is **not** evidence the fix failed to engage — `RULE N25` is the
clause that decides whether any of it is harm, and it was fixed before any of this was known. It is applied as
written in §5.

**D4 turned up one thing worth its own line:** three rows of the existing parametric recommender test
(`e = 2.19, 2.30, 2.50`) now receive a floored step **and still pass**, because they assert only `WasCapped` and
legality. **An existing test survived a real behavioural change, so it was never a control on this quantity.**

`G25-P3c` says nothing about the wide end. The `UNCHANGED` five are recorded as unchanged and nothing is inferred
from them; `UNCHANGED == 8` would have been recorded as `P3c-INERT-ON-THE-GATE`, neither pass nor fail.

### 3.2 The interlock, both ends, with the absent-marker assertion captured this time

- **FAIL end first**, on `/mnt/d/hf_w19/gate`: `exit=1` on the BuildId-novelty clause, **no marker**, and the
  artifact carries the assertion wave 24 specified and lost —
  `ASSERTED at 2026-08-13T03:44:09Z: the FAIL end left NO G25_PASSED marker`.
- **PASS end**, on `/mnt/d/hf_w25/gate`: `exit=0`, `G25_PASSED` written **by the scorer** carrying
  `BuildId=d79dae73…`.
- `G25_P3C_OK` written **by `score_p3c_w25.py`** on `rc == 0` only, carrying `gate=`, `baseline=`,
  `unchanged=`/`widened=`/`narrowed=`.
- The AFTER driver parsed both markers and logged them before starting. **No marker in this wave was written by a
  human** ([F75](followups.md)), and every consumer parsed the path **out of** the marker rather than rebuilding
  it ([F74](followups.md)).

`score_g25_w25.py --self-test`: **24 of 24** branches, including *"a rate over an EMPTY set is UNEVALUATED, never
1.000"*, *"the STRING 'Fail' is UNKNOWN-VERDICT and FAILS"*, and *"`--arm-gate` refuses to overwrite an existing
marker."*

### 3.3 Four fingerprint classes, all PASS, before and after

| class | population | result |
|---|---|---|
| bank landings | 42 | **42 of 42 byte-identical**, 0 changed / added / removed, 0 could-not-look |
| aux (`harness_settings.json` ×39, `synthetic_meta.json` ×20) | 59 | **59 of 59 byte-identical** |
| prior-wave arm landings (`hf_w18` seedA0/seedA1/gate) | 48 | **48 of 48 byte-identical** across all three roots |
| wave-23 `synth-validate` reports | 38 | **38 of 38 byte-identical**, CNL 0 |

The gate **refused to start twice** (`exit 3`) because two of those BEFORE fingerprints had not been written yet
(D9). **Both refusals were correct**: *a control written after the arm is not a control*, and the driver enforces
it. An `exit 3` with a `START`-less log looks exactly like the aborts the charter's status sweep warns about, and
the difference is that **an abort that names the missing control and tells you how to create it is the guard
working.** The fourth fingerprint needed a new instrument — `score_w25.py --fingerprint-v23` *prints* `N25-E`'s
read-only evidence, while the gate wants a **JSON preservation fingerprint**; two instruments over one population
and only one existed — so `v23_fingerprint_w25.py` was derived, and `V25` re-run to `V-CLEAN` afterwards.

---

## §4 — RULE W25: `W-UNEXERCISED`, for the second wave running, and it is a real result

**Population: the paired blind S1 cells. `D01`, `D02`, `D03`, `D08` and `D11` are labelled controls, excluded
from every denominator in this rule.**

### 4.1 Validity gates — all pass

| clause | result |
|---|---|
| **`W25-V1`** | **13 paired blind cells** ≥ minimum 12. The two absentees are `D05_tec140_1000mm` and `D19_cygnus_deep_shed`, **NAMED in advance** in design §6 and NAMED on **both** arms: each hit `timeout 600` (`exit=124`) on **both** BEFORE and AFTER, produced no report, and got **no manifest row**. Cells requested but not run are named, never subtracted |
| **`W25-V2`** | `profileId` `astrodet (ce3f3e63-…)`, `maxRounds` `4`, `fitInputs` all cardinality **1** within each arm; `fitInputs` **equal across arms**; `specSha256` = `bf10522e670a119e260a8b3d06c2cc6ec52ce513dde25ca9ca13f426c38672d6` on **both** |
| **`W25-V3`** | exactly **two** distinct binaries, one per arm, both dll pairs matching the recorded values in `W25_BEFORE_READY` / `W25_AFTER_READY` |
| **`W25-V4`** | `G25_PASSED` (carrying B15's BuildId) **and** `G25_P3C_OK` both present and parsed by the driver |
| **`W25-V5`** | **37 of 37** AFTER rounds with a non-null `stepRecommendation` carry `wasBandFloored` as a **bool** — 0 missing, 0 null, 0 non-bool. The field does **not** exist on the BEFORE arm and **no clause reads it there** ([F68](followups.md)) |

### 4.2 The clauses

| clause | BEFORE | AFTER |
|---|---|---|
| **`W25-A`** — `terminal.converged`, paired blind | **13 of 13** | **13 of 13** |
| **`W25-B`** — cells with ≥1 floored AFTER round | — | **0 of 13**; **0** floored rounds of 37 |
| **`W25-C`** — landed in band, paired blind | **13 of 13** | **13 of 13** |
| **`W25-D`** — distance, exact `repr()`, no epsilon | IMPROVED **0** · SAME **13** · REGRESSED **0** | |

`W25-A` BRIDGED **0**, BROKEN **0**, SAME **13**.

```
W25-B == 0 cells (the floor never engaged)  ->  W-UNEXERCISED
```

**`RULE W25` VERDICT: `W-UNEXERCISED`.** Pre-registered in design §7.3 **in these words** as a real result and
not a null.

### 4.3 What `W-UNEXERCISED` means, said plainly, and it is the same shape as wave 24's `B-UNEXERCISED`

**The blind population did not contain the mechanism.** All 13 blind cells converged, landed inside the tolerance
band, and moved by exactly zero on both arms. There was nothing for the floor to bridge because there was nothing
stalled.

So the wave bought **do-no-harm evidence, not efficacy evidence, from its blind arm.** P4's efficacy is
demonstrated on (a) the **labelled controls**, which are published and in no denominator, (b) the **unit tests**,
four of which are shown red by named mutants, and (c) **`G25-P3c`**, which is the one place the change moved a
number on data it had never seen. That is a real epistemic result and it should be dressed up as neither success
nor failure.

**Does "blind cells that all already converge" mean the blind population was chosen wrongly? No — and here is the
reasoning, because the question deserves an answer rather than a shrug.**

- **The population was fixed by an exclusion rule, not by an outcome.** Blind = the 20 spec datasets minus the
  five whose S1 `stepRecommendation` blocks were already published (wave 24 §4.3 and `B24-V6` published `D08` and
  `D11` in full; `D01`/`D02`/`D03` are quoted in wave 24 §5.2 and in [F34](followups.md)'s tail). **Design §1.2
  corrected the count from 17 to 15 by auditing what had been published, before the arm ran.** A population
  chosen this way cannot be chosen *for* an outcome — the only alternative would have been to pick blind cells
  by whether they stall, which is the [F14](followups.md)/`S16`/`D20` harvesting defect exactly.
- **The mechanism's known instances were already spent.** Every cell ever observed to stall on this bank —
  `D01`, `D02`, `D03` — was published before wave 25 opened. **The blind set is the complement of the finding
  set, so a blind arm on this bank could only ever measure whether the pathology recurs elsewhere.** It does not,
  on S1, on this instrument.
- **Therefore the honest reading is rarity, not mis-selection.** 0 of 13 blind cells exhibited a sweep whose
  `sampledHfrRange` sat below 3.0 *and* whose fitted half-width came back under the sweep bound. **The pathology
  these fixes target is rare on the synthetic bank at scenario S1.**
- **But `G25-P3c` says rarity is scenario- and population-specific, and that is the important qualifier.** On
  the gate's eight `optimize` landings — a different instrument, a different population, three real datasets —
  the floor moved **3 of 8**, including one real optical train. **A mechanism that is 0 of 13 in one place and 3
  of 8 in another is not rare in general; it is rare *here*.** The blind arm measured S1 on the synthetic bank
  and nothing wider, and this document claims nothing wider.

**What `W-UNEXERCISED` licenses**, exactly as pre-registered: saying the mechanism is **rarer on this population
than the three published cells suggest**. It licenses **nothing** about a rate, about coverage, or about P4 being
unnecessary — and it does **not** decide the ship. Design §3.1's ship rule says so in advance:
*"`RULE W25`'s verdict does not decide the ship. A `W-UNEXERCISED` still ships, on the labelled controls plus the
unit tests"* — the identical licence wave 24 gave P2 for the identical reason.

---

## §5 — RULE N25: `N-PRESERVED`. This is the clause that could have unshipped P4

**Population: all paired S1 cells, controls INCLUDED.** Do-no-harm is not blind and must not be: a safety net
with a shrunken population is a smaller net.

| clause | result |
|---|---|
| **`N25-V1`** | **18 paired cells** ≥ minimum 17. `D05` and `D19` absent from **both** arms, named on both sides |
| **`N25-A`** — round-0 inertness | **15 of 15 = 1.0000.** Denominator printed. Population = paired cells whose **BEFORE** r0 is capped, band-reached, or `"NaN"` — which is **exactly** the 18 minus `D01`/`D02`/`D03`, the three stall cells. `stepSize` (int), `halfWidth` (double by `repr()`, `"NaN"` compared **as a string**), `wasCapped` (bool) and `degenerateReason` all identical across arms |
| **`N25-B`** — convergence not lost | **0.** No cell converged BEFORE and failed AFTER |
| **`N25-C`** — distance not worsened | **IMPROVED 3 · SAME 15 · REGRESSED 0**, by exact `repr()`, no epsilon |
| **`N25-D`** — assertion `A4` untouched | **18 of 18 = 1.0000.** Round-0 ordered `A4` `(verdict, detail)` sequence identical across arms on every paired cell |
| **`N25-E`** — comparability against wave 23 | **NOT RUN.** Drop-ladder rung D2's 04:40Z trigger had fired before scoring could start (AFTER arm ended 05:03:49Z). §13 |
| **`N25-F`** — `stoppedReason` prose, reported, no threshold | **3 differences**, all attributed: `D01` (P5's message change **and** a cell whose round count moved), `D02` and `D03` (cells that no longer stall). **0 unattributable** |

```
N25-V1 passes; N25-A == 100 %; N25-B == 0; N25-D == 100 %; N25-C REGRESSED == 0  ->  N-PRESERVED
```

**`RULE N25` VERDICT: `N-PRESERVED`. P4 MAY SHIP, and it ships.**

**`N25-A` and `N25-D` were the two clauses with answers predicted from the code before the arm**, which wave 24
rightly calls the only kind worth running. Both landed at 100 %, and both predictions are provable from the
source that shipped:

- The floor branch is entered only when `halfWidth < maxHalfWidth`, which is **exactly** when the cap branch was
  not entered, and it is guarded by `BandDemonstrablyUnsampled`, which is `false` for `NaN`. The two are mutually
  exclusive **by construction**, and `N25-A`'s population is defined as the complement of the floor's trigger.
- The floor branch **never writes `wasCapped`**, which is all `A4` reads at round 0. Hence `N25-D` = 100 %.

**A failure in either would have been unambiguous evidence that the shipped code is not the designed code** —
not a judgement call about how much change is acceptable. That is what makes them worth the denominator.

**One honest qualification on `N25-C`, and it matters for how the number reads.** The **3 IMPROVED are exactly
the three labelled controls** `D01`, `D02`, `D03`. Over the **blind 13** the same statistic is **IMPROVED 0 ·
SAME 13 · REGRESSED 0** (`W25-D`). `N25-C`'s population is all-paired by design — do-no-harm must not be blind —
so `IMPROVED 3` is the correct answer to `N25-C`'s question. **It is not blind evidence of improvement, and this
document does not offer it as such.**

`D01`'s "IMPROVED" additionally sits on a truth that moved (§0.3): `|2−8|/8 = 0.75` BEFORE against `|3−9|/9 =
0.667` AFTER. Held at the BEFORE truth it would read `|3−8|/8 = 0.625`, still improved — **the classification is
robust to the moving denominator, and it is named rather than absorbed.**

---

## §6 — RULE M25: `M-CORRECTED`. An instrument validated against a known answer before being pointed at unknown data

| clause | result |
|---|---|
| **`M25-B`** — the FAIL end, **run first, on real published artifacts** | `/mnt/d/hf_w24/after/*/synth_validate_report.json`: **25 reports, 25 cells scanned, exactly 1 contradiction**, and it is **`D01_ultrawide_40mm__S1` by name** — the pre-registered expectation, measured |
| **`M25-A`** — AFTER-arm contradictions | **0.** No AFTER cell says *"from a degenerate fit"* while the last round's `degenerateReason` is `null` |
| **`M25-C`** — the new message, quoted verbatim, on every AFTER cell that still stalls | **one cell** — below |

```
M25-B finds >= 1 and D01 is among the names; M25-A == 0  ->  M-CORRECTED
```

**`RULE M25` VERDICT: `M-CORRECTED`.**

The single remaining stall, quoted verbatim:

```
D01_ultrawide_40mm/S1:
  stalled (round applied nothing, but step 3 is outside the 3.6 tolerance band of step_behavioral 9)
  -- a no-op recommendation from a NON-degenerate fit (sampled HFR range 1.459x, band 3x), not convergence
```

**This is the loop closed properly.** [F34](followups.md)'s tail asked for exactly this deliverable; wave 24 §5.2
measured the consequence — the harness blamed a degenerate fit on a cell with `degenerateReason = null`,
`halfWidth = 6.0877` and `R² = 0.99999999999994`, **and a wrong message became a wrong register entry** when wave
23 grouped `D01` with `D02` and `D03` on the strength of it. Wave 25 measured the FAIL end on that very published
artifact **first**, got exactly 1 and the right name, then pointed the same clause at unknown data and got 0.
**A clause about the wave's own deliverable, with its FAIL end measured on the published prior result whose defect
the wave exists to fix.** The message now names a cause it checks, and quotes the number that explains it.

---

## §7 — The labelled controls (`W25-K`): the arithmetic bar, two hits and one instructive miss

**In NO denominator. Reported in full, against a bar fixed in design §8 before the data.**

### 7.1 The pre-registered bar and the measurement

Design §8's arithmetic: while the floor binds, `halfWidth = 1.5 × halfSpan` and `halfSpan = offsetSteps × step =
4 × step`, so `step' = round(1.5 × 4 × step / 3.5) = round(1.714 × step)`.

| cell | predicted trajectory | **measured trajectory** | truth | band | predicted terminal | **verdict** |
|---|---|---|---|---|---|---|
| `D01_ultrawide_40mm` | 2 → 3 → **5** | **2 → 3 → final 3** | 9.0 (was 8.0) | `[5.4, 14.4]` | converged by r2, in band | **MISSES** |
| `D02_rich_135mm` | 2 → 3 → **5** | **2 → 3 → final 5** | 6.0 | `[3.6, 8.4]` | converged by r2 | **MEETS** |
| `D03_redcat_250mm` | 4 → 7 → **12** | **4 → 7 → final 12** | 16.0 | `[9.6, 22.4]` | converged by r2 | **MEETS** |
| `D08_c11_2800mm` | — | unchanged | 82.0 | — | **UNCHANGED** on every field | **MEETS** |
| `D11_rc10_585_afbin2` | — | unchanged | 55.0 | — | **UNCHANGED** on every field | **MEETS** |

`D02` and `D03` reproduce the pre-registered per-round arithmetic **exactly**, digit for digit, two rounds deep,
from a prediction committed in `352cf26` before any wave-25 binary existed. `D08` and `D11` are identical across
arms on **all 20 terminal fields** in this schema (a superset of `R24-A`'s 17) **and** on their full round-0
assertion arrays — the floor is a literal no-op where every round was already capped, as predicted.

BEFORE, all three stall cells stopped after **1 round of 4** at `2`, `2` and `4`. AFTER, all three took a second
round; two converged.

### 7.2 The engagement marker and its corroboration (`W25-B`, F66(c))

Every floored round must **also** satisfy `halfWidth == 1.5 × offsetSteps × bootstrapStep` to `repr()`
bit-identity — a second field moving the same way, because a two-directional demonstration proves branches are
*distinguishable*, not *correctly assigned*.

| cell | round | `wasBandFloored` | `halfWidth` | `1.5 × 4 × boot` | corroborated |
|---|---|---|---|---|---|
| `D01` | r0 | **true** | `12.0` | `1.5 × 4 × 2 = 12.0` | **yes** |
| `D02` | r0 | **true** | `12.0` | `1.5 × 4 × 2 = 12.0` | **yes** |
| `D03` | r0 | **true** | `24.0` | `1.5 × 4 × 4 = 24.0` | **yes** |

**3 of 3 corroborated. `D24-A`'s schema invariant survives**: all three floored rounds carry a **finite**
`halfWidth` **and** a **null** `degenerateReason`, so `halfWidth == NaN` **iff** `degenerateReason != null` still
holds. And `wasCapped` is `false` on every floored round and `wasBandFloored` is `false` on every capped round —
**mutually exclusive in the data, as the code says they must be.**

### 7.3 D10 — WHY `D01` MISSED, and it is the sharpest thing this wave found

**All three stall cells floored at r0 and capped at r1. The hand-off from floor to cap is exactly the designed
behaviour, and on two of three it converges.**

| cell | r0 | r1 | outcome |
|---|---|---|---|
| `D02_rich_135mm` | **floored**, `halfWidth` **12.0**, step 3 | **capped**, `halfWidth` **18.0**, step 5 | converged, final **5** (truth 6) |
| `D03_redcat_250mm` | **floored**, `halfWidth` **24.0**, step 7 | **capped**, `halfWidth` **42.0**, step 12 | converged, final **12** (truth 16) |
| `D01_ultrawide_40mm` | **floored**, `halfWidth` **12.0**, step 3 | **capped**, `halfWidth` **9.0**, step 3 | **STALLED**, final **3** (truth 9) |

**`D01`'s half-width SHRANK between rounds: 12.0 → 9.0.** The floor lifted it to `1.5 ×` the round-0 half-span;
the cap at round 1 — which is an **upper** bound — then recomputed a **lower** value than the floor had already
established, the step never grew past 3, and `sampledHfrRange` stayed below the band on both rounds (`1.4628`,
`1.4587`) so nothing else could rescue it.

**The defect is narrow and nameable: the floor is not STICKY across rounds.** P4a bounds the half-width from
below *within* a round, against that round's own bound. It does not prevent the next round from re-deriving a
smaller bound. **The cap and the floor are computed from the same quantity, so a shrinking bound drags both down
together.**

**The mechanism, one level deeper than D10 states it, and it changes the fix.** `maxHalfWidth =
MaxHalfWidthSampledHalfSpanMultiple × 0.5 × SearchSpan(bestFit)`, and `SearchSpan` is
`max(x) − min(x)` **over `bestFit.Inputs` — the points that actually entered the fit**, not over the positions
the sweep requested. On `D01`:

| | r0 | r1 |
|---|---|---|
| requested sweep | centre 6000, step **2**, ±4 → span **16** | centre 6008, step **3**, ±4 → span **24** |
| **fitted span** (`SearchSpan`) | **16** → `maxHalfWidth` **12.0** | **12** → `maxHalfWidth` **9.0** |
| `worstFrameStarCount` | **0** | **0** |
| `R²` | 0.99999999999994 | **0.7603** (`A6` FAILs: *"below 0.8"*) |

**The round-1 sweep was requested WIDER — 16 → 24 focuser units — and its FITTED span NARROWED, 16 → 12**,
because the outer frames of a widened sweep on a 40 mm ultrawide stopped yielding usable HFR points. The harness's
own `A4` assertion measures the divergence from the other side: it computes the cap boundary from the **requested**
sweep and reports *"WasCapped=True but truth predicts False (halfWidth=10.4 vs cap boundary **18**, ratio 0.58)"*
— **18** from the requested span against the product's **9** from the fitted span. The `1.5 × 4 × boot` identity
in §7.2, which every floored round satisfies, is **exactly the identity `D01` r1 fails** (`1.5 × 4 × 3 = 18 ≠
9.0`), and it fails for the reason design §8bis anticipated in advance: *"a round whose fit dropped points has a
shorter span and is a legitimate near-miss."*

**So D10's remedy list needs one correction before wave 26 acts on it.** D10 offers *"either make the floor
monotone across rounds, or stop the round-1 sweep narrowing when the band is still unreached."* **The second
option is mis-framed: the round-1 sweep did not narrow — it widened, and the FIT narrowed inside it.** The two
live candidates are:

1. **Make the floor monotone across rounds** — carry the previous round's floor forward as a lower bound on the
   next round's `maxHalfWidth`. Simple, and it fixes `D01` directly.
2. **Make the bound robust to a sweep that loses its outer points** — `SearchSpan` measured over the *fitted*
   points penalises exactly the sweeps that most need widening, because on a star-poor field widening is what
   costs you the outer points. A bound derived from the *requested* span (which is what the harness's own truth
   model already uses) would not have shrunk.

**Neither is attempted here.** Both are new mechanisms and both belong behind their own pre-registration. §14
recommends them, priced.

**This is not a reason to doubt the ship.** `N25` is `N-PRESERVED` on its own pre-registered terms, `D02` and
`D03` both meet the bar, no cell regressed, `D01` still moved 2 → 3 rather than staying put, and `G25-P3c` moved
three real landings. **It is the honest account of a control that missed, and it is the strongest single lead
this wave hands forward.**

---

## §8 — What the wave can say, goal by goal

### Goal 2 — step-size accuracy: **this wave is goal 2, and it delivered a product change plus one clean lead**

- **P4 ships**, under a ship rule fixed and committed before the data: `G25` is not `G-DIRECTION-VIOLATED` and
  `N25` returned `N-PRESERVED`.
- **The mechanism is now named in the product, not in prose.** `MaxHalfWidthSampledHalfSpanMultiple` has been a
  **ceiling with no floor**; and the `half-width-unresolved` exit returned before the ceiling was consulted at
  all. Both are fixed, both are separately observable (`WasBandFloored` vs `WasCapped`), and the two are mutually
  exclusive by construction and in the data.
- **Efficacy on unseen data: `G25-P3c`, 3 of 8 gate landings widened, 0 narrowed, one of them a real dataset
  (`mccomiskey`).**
- **Efficacy on the motivating cells: 2 of 3 bridged**, reproducing a pre-registered per-round arithmetic
  prediction exactly, and the third's miss is diagnosed to a named line of code (§7.3).
- **Efficacy on the blind population: NONE MEASURED, because the mechanism was absent from it** (§4.3). This is
  the wave's principal limitation and it is stated rather than papered over.
- **No goal-2 *rate* is quoted.** `N25-E` did not run, so wave 23's `13 of 17` stays formally uncomparable and
  nothing replaces it. **The S1 re-measurement is discharged as an arm** — both sides ran, 18 of 20 cells, paired
  — **but not as a published accuracy number**, and it must not be quoted as one until `N25-E` says whether P2
  moved S1.

### Goal 1 — bridge detected gaps: **served secondarily, and it returned a null with content**

`W25` measured the bridge on a blind 13 and found the pathology absent. That is a measurement about the bank's
composition, not about P4. `lumos`'s zero-star frame was **declared dropped in advance** (design §1.5) rather
than under clock pressure — *a drop taken in advance is a decision, a drop taken at 04:40Z is an accident* — and
goes to wave 26's opener.

### Goal 3 — **NOTHING. And this wave says so, exactly as wave 24 did.**

No clause here touches optimizer behaviour, the flat-direction question, or `J`'s coordinate system. `G25-1`'s
bit-identical `FinalJ` on 8 of 8 is a *control* proving P4 did not touch goal-3 territory, not a goal-3 result.
**The P23 flat-direction perturbation arm remains the deepest open goal-3 question and remains unrun** — two
waves in a row.

---

## §9 — The controller's ten deviations

`/mnt/d/hf_w25/CONTROLLER_DEVIATIONS.md`, written during execution, not reconstructed at write-up.

| # | what it records | folded into |
|---|---|---|
| **D1** | the pre-flight blocked the wave's own instruments on first run: 21 findings, all real, one load-bearing (`G24_START`) | §2.2 |
| **D2** | **the pre-flight found `G24_START` BY ACCIDENT** — `\bG24\b` cannot match it; another clause fired on the same line | §2.3 |
| **D3** | the empty `exe/`/`gate/`/`before/`/`after/` directories at 00:52Z are `layout_w25.sh --self-test`'s doing, **not a measurement**. The `w25_code_backups/{pre_P4,post_P4_pre_P5}/` restore targets were pre-created — wave 24 §10.2's correction applied **in advance** | §1, §10 |
| **D4** | **P4a's blast radius is wider than the three named cells, recorded BEFORE any AFTER data existed**, with the `e ∈ (~2.135, 3.0)` derivation and the discontinuity at `e = 3` | §3.1 |
| **D5** | P4 touches a **fifth** file and P5 lands as **three** artefacts; both diverge from the design's file lists | §1, §11 |
| **D6** | the controller's brief to the code agent contained a false premise (*"the tree carries the uncommitted pre-registration"* — `HEAD` was `352cf26`, clean). The no-VCS-revert instruction was honoured regardless, so it cost nothing | §10 |
| **D7** | the controller re-verified the red tests with an **independent mutant**, not a replay: inverting `BandDemonstrablyUnsampled` gave `Failed: 11, Passed: 35` and killed `BandReached_LeavesTheRecommendationBitIdentical` — **which is what proves that companion is a control rather than decoration** | §10.4 |
| **D8** | **the pre-flight's second gap, same underscore, other affix** — it passed a file that could not run; plus the surviving **w23** label and the guard rewritten to assert positively | §2.3 |
| **D9** | the gate refused twice (`exit 3`) for **missing controls**, and both refusals were correct | §3.3 |
| **D10** | **why `D01` missed: the floor is not sticky across rounds, and the cap pulls it back down** | §7.3 |

**The pattern behind D1, D2 and D8 is one pattern and it is §2.3's**: three separate failures of `\b` against `_`
in two waves. **D3, D5 and D9 share a different one** — each is an artifact that *looks* alarming (empty
directories, a wider file list, an `exit 3`) and is in fact a guard or a pre-registered correction working. Both
patterns are worth the entries because a later reader will have the same first reaction.

---

## §10 — What SHIPPED, and under which rule

### 10.1 P4 — the product change

`StepSizeRecommender.cs`, `StarDetectionOptimizerWizardVM.cs`, `SynthValidationReport.cs`,
`SynthValidateRunner.cs`, `StepSizeRecommenderTests.cs`.

- **P4a — the missing lower bound.** After the existing cap block: when `BandDemonstrablyUnsampled(sampledHfrRange)
  && maxHalfWidth > 0.0 && halfWidth < maxHalfWidth`, take `halfWidth = maxHalfWidth` and set `wasBandFloored`.
- **P4b — the exit that skipped the bound.** At the `DegenerateReasonHalfWidthUnresolved` return, when the band is
  demonstrably unsampled, **do not take the degenerate exit**: floor, leave `DegenerateReason` null, and continue
  down the ordinary path (detectability bound, `ResolvePointsPerSide`, `ClampStep`).
- **The could-not-look guard**, which is the single most important line: `BandDemonstrablyUnsampled` is
  `double.IsFinite(sampledHfrRange) && sampledHfrRange < HfrThresholdMultiple`. **NaN means "could not look" and
  must not engage the floor.** `x < 3.0` is already false for NaN, but writing it that way would make the safety
  depend on IEEE-754 trivia — [F79](followups.md)'s defect shape is a check that fails **closed to a value**
  instead of reporting that it could not look, and this is its mirror.
- **Declared scope limit:** the `no-fit` and `non-finite-vertex` exits are **not** floored. Wave 24's `D24-B`
  measured **0** field occurrences of either, so the limit costs nothing ever observed. `T7` tests it.
- **The new observable** ([F76](followups.md)): `WasBandFloored` on `StepSizeRecommendation`,
  `StepSizeWasBandFloored` on the wizard's `OptimizationSummary`, `wasBandFloored` on the report snapshot.
  `WasCapped` is **not** overloaded, deliberately, so `N25-D`'s 100 % is provable from the code.
- **UI:** `StepSizeText`'s "partial step" wording is gated on `StepSizeWasCapped || StepSizeWasBandFloored`. The
  existing wording already describes a floored recommendation, and the sampled range it quotes is the very number
  that engaged the floor. **No new XAML row**, confirmed by `DataTemplates.xaml`'s absence from the delta.

**Ship rule, fixed before the data: `RULE G25` not `G-DIRECTION-VIOLATED` AND `RULE N25` = `N-PRESERVED`. Both
satisfied. P4 SHIPS.**

### 10.2 P5 — the harness message

`SynthValidateRunner.cs`'s stall message now reads `roundReport.StepRecommendation.DegenerateReason` and branches,
via a new pure `TestApp/SynthBank/StallReason.cs` (source-linked into the test project by one `<Compile Include>`
line). **Ship rule: UNCONDITIONAL** — it changes no number, it is [F34](followups.md)'s tail verbatim, and
`RULE M25` measures it with its FAIL end on real published artifacts. Shipped.

### 10.3 The unship boundary, named as a directory before any code landed

`/mnt/d/hf_w25/w25_code_backups/pre_P4/` and `post_P4_pre_P5/`, **pre-created** (D3). P4 and P5 share
`SynthValidateRunner.cs`, so the backup set was taken **twice**. To unship P4 while keeping P5: restore from
`pre_P4/`, then re-apply P5 from the `post_P4_pre_P5/` diff. **`cp` and byte backups, `cmp`-verified, never a VCS
revert.**

**D5's pattern is now twice-confirmed and belongs in the register:** *in this codebase a behaviour that must be
unit-testable cannot live in a TestApp runner* — `SynthValidateRunner.cs` renders frames and runs the optimizer
and the csproj says so in a comment — *so every "confine the change to one file" instruction in a design will be
violated by the implementation, and the revert boundary must be a NAMED SNAPSHOT DIRECTORY, never a file list.*
Wave 24 hit this identical wall with P2. Wave 25 took its backups twice for exactly this reason and was right to.

### 10.4 Tests — seven new, four shown RED by named mutants

Suite **3966 → 3973**, exactly the pre-registered `+7`.

| # | test | pre-change |
|---|---|---|
| **T1** | `UnderReachedFit_WithBandUnsampled_FloorsHalfWidthToTheSweepBound` | **RED** under **M1** (delete the P4a block) |
| **T2** | `HalfWidthUnresolved_WithBandUnsampled_RecommendsTheSweepBound_NotTheHeldStep` | **RED** under **M2** (restore the early `return Degenerate(...)`) |
| **T3** | `HalfWidthUnresolved_WithBandUnsampled_IsNotReportedAsDegenerate` | **RED** under **M2** |
| **T4** | `StallMessage_NamesTheNonDegenerateCause_WhenDegenerateReasonIsNull` | **RED** under **M3** (restore the unconditional string) |
| **T5** | `BandReached_LeavesTheRecommendationBitIdentical` | companion, passes pre-change |
| **T6** | `SampledHfrRangeNaN_DoesNotEngageTheFloor` — the F79 mirror | companion, passes pre-change |
| **T7** | `NoFitAndNonFiniteVertexExits_AreNotFloored` — the declared scope limit | companion, passes pre-change |

**The mutants are ORTHOGONAL, and the evidence file records which tests each did and did not kill** — M1 killed
**T1 only** (T2/T3 survive: they exercise the other branch), M2 killed **T2 and T3 only**, M3 killed **T4**. A
mutant that killed everything would prove nothing about which test guards what.

**Every mutant was restored by `cp` from a byte backup and the restore asserts `cmp -s` before reporting.** No
`git checkout --`, no VCS revert.

**D7 is the strongest single item in this section.** Rather than replaying the code agent's mutants, the
controller **inverted the new predicate itself** (`BandDemonstrablyUnsampled`: `<` → `>=`), engaging the floor on
exactly the complementary set: **`Failed: 11, Passed: 35`**, killing T2, T3 **and
`BandReached_LeavesTheRecommendationBitIdentical`** — the companion that asserts nothing moves when the band **is**
reached. **A companion failing under inversion is what proves it is a control rather than decoration.** Restored
`cmp`-identical, md5 `0cd77207` matching the code agent's reported final state.

**Full suite: `Failed: 0, Passed: 3973, Skipped: 0, Total: 3973`, `3 m 42 s`, `SUITE_EXIT=0`**, read by COUNT
([F37](followups.md)). The known-flaky `SendAsync_WritesOnABackgroundThread` did not fire. **No test was skipped,
marked expected-to-fail, or weakened.**

---

## §11 — Register entries this wave owes

Fixed in design §13 before the data; discharged here.

| entry | action |
|---|---|
| **[F34](followups.md)** | **CLOSED.** `RULE M25` = `M-CORRECTED`, with the FAIL end measured on the published artifact that motivated it (25 cells, 1 contradiction, `D01` named) and the AFTER arm returning 0 |
| **[F25](followups.md)** | **STATUS from `RULE W25`, in `W25`'s own words.** The narrow-end half of the directional gate is **shipped**; `W-UNEXERCISED` licenses saying the mechanism is **rarer on this population than the three published cells suggest** and nothing more. **The wide end remains unmeasured, not untriggered** |
| **[F21](followups.md)** | **EXTENDED** twice: a third population on which R² is no defence (`D01` stalls at `R² = 0.99999999999994` with `sampledHfrRange = 1.4628`), and **wave 25's measured S1 arm times**, which corroborate wave 24's `S1 234 s` mean within 6 % on two independent arms |
| **NEW — the missing lower bound** | **NEW ENTRY.** `MaxHalfWidthSampledHalfSpanMultiple` was a **ceiling with no floor**, and `half-width-unresolved` returned before the ceiling was consulted. Neither F25 nor F34 nor F26 owned it. **Shipped as P4** |
| **NEW — the floor is not sticky across rounds** | **NEW ENTRY**, with `D01`'s numbers and the `SearchSpan`-over-fitted-points mechanism. §7.3 |
| **NEW / [F80](followups.md) extension — `\b` cannot see `_`** | **EXTENDED** with `RULE V25`'s result **and its two own gaps**, both demonstrated |
| **NEW — the A4 truth-model gap** | **NEW ENTRY, wave-26 item.** The harness's `A4` computes the cap boundary from the **requested** sweep while the product computes it from the **fitted** span, and compares against `WasCapped` alone. Deliberately **not** fixed here |
| **[F76](followups.md)** | **CITED, not extended.** P4 ships with `WasBandFloored`; `W25-B` is what proves it engaged, and `G25-P3c` is what proves it engaged on unseen data |
| **[F66](followups.md)** | **EXTENDED** — an **eighth** apphost counter-example |
| **[F74](followups.md) / [F75](followups.md)** | **CITED.** Every marker carried its payload and every consumer parsed it out; no marker was written by a human. **But see §0.2** — the central scorer's own evidence reached the next stage only as console scrollback |
| **[F26](followups.md)** | **NOT DISTURBED.** Wave 23 §13's harness-not-product scope correction stands. P2 is not re-scored |
| **[F62](followups.md)** | **CITED.** P4 is **not** a rejection change — it moves no frame and no star into or out of any fit, reads `bestFit.Outputs`/`Inputs` already fixed, and changes only a scalar derived after fitting. `G25-1`'s bit-identical `FinalJ` on 8 of 8 is the out-of-sample check that the declaration is true |
| **[F22](followups.md)** | **NOT BUNDLED** |
| **the trap list** | **UNTOUCHED.** `lumos` is design §1.5's declared drop; `Panos`'s σ claim still costs one `af-fit` |

---

## §12 — Timing: what was priced, what it cost

| # | step | priced | **actual** | ratio |
|---|---|---|---|---|
| 0 | `RULE V25` pre-flight + layout + derivations + self-tests | ~15 m | **not separately timed**; it blocked twice and both blocks were correct | — |
| 1 | **S1 BEFORE arm on B14, 20 cells** | **~80 m** (measured; band 70–100) | **`01:13:17Z → 02:27:21Z` = 74 m 04 s** | **0.93×** |
| 2 | code (P4 + P5) + 7 tests + 4 mutant red demos, inside step 1's window | ~60 m | overlapped as planned | — |
| 3 | build B15 + freshness + two-binary provenance | ~10 m | provenance stamped `02:54:43Z` | ~in band |
| 4 | **RULE G25 gate, 8 runs** + P3c + P4 + interlock | **~42 m** (measured) | **`02:59:19Z → 03:42:02Z` = 42 m 43 s** | **1.02×** |
| 5 | **S1 AFTER arm on B15, 20 cells** | **~85 m** (*derived*; band 70–110) | **`03:44:22Z → 05:03:49Z` = 79 m 27 s** | **0.94×** |
| 6 | scoring + full suite by COUNT + register + hand-off | ~25 m | suite alone `3 m 42 s`; `N25-E` dropped | — |

**Both arms landed 18 of 20 rows**, and the two absentees are the same two on both sides: `D05_tec140_1000mm` and
`D19_cygnus_deep_shed`, each `exit=124` at `timeout 600` on **both** arms. **Named before the data** in design §6
and in both arm logs' headers.

**Per-cell rates, both measured on the same instrument and the same scenario** ([F21](followups.md)'s rule):

| arm | wall | scheduled | **per cell** |
|---|---|---|---|
| BEFORE (B14) | 4 444 s | 20 | **222 s** |
| AFTER (B15) | 4 767 s | 20 | **238 s** |

Both land within **6 %** of wave 24's recorded `S1 mean 234 s`, on 40 more cells — **F21's S1 row is now
corroborated by three independent arms and should be treated as settled.**

**The AFTER arm cost 323 s more than the BEFORE arm, and 266 s of that (82 %) is the three control cells**
(`D01` 97→210 s, `D02` 113→217 s, `D03` 69→118 s), which each took a second round where they previously stopped
after one. **The derived +5 m estimate for "floored cells using more rounds" was right, and it is right for the
right reason** — the extra time is localised to exactly the cells that floored.

**Design §1.1's correction was the wave's best piece of pricing work.** Wave 24 §14 priced wave 25 at ~3 h 15 m
by costing *one* S1 arm for a *paired* comparison. §1.1 re-priced it at **~3 h 27 m TestApp / ~4 h 15 m critical
path** before starting. Actual TestApp wall: **74 + 43 + 79 = 3 h 16 m**, inside the corrected band and **outside
the original estimate's arm accounting**. A wave that discovers at 04:30Z that it is 45 minutes over is a wave
that shrinks a denominator at 04:30Z; this one did not have to.

---

## §13 — What was NOT run, and priced

| item | price | why not |
|---|---|---|
| **`N25-E`** — the comparability clause | **~4 m of scoring, 0 TestApp** | **Drop-ladder rung D2.** Its 04:40Z trigger had already fired when the AFTER arm ended at 05:03:49Z. **The comparability question returns to wave 26 unanswered**, and wave 23's `13 of 17` stays formally uncomparable. **Its inputs are intact and fingerprinted** — wave 23's 38 reports are byte-identical BEFORE and AFTER (38 of 38, CNL 0) — so this is a **4-minute** item for wave 26, not a re-run |
| **the scorer's `--out` capture** | ~1 m | §0.2. Re-running `score_w25.py --score` over the two arm roots would produce the file; **the arms are complete and untouched**, so it costs nothing but a minute |
| **`lumos`'s zero-star frame** | ~10 m | Declared dropped **in advance** (design §1.5), not under pressure. Wave 26's opener |
| **the P23 flat-direction perturbation arm** | ~30 m | Untouched for a second wave. Still the deepest open goal-3 question |
| **the wide end of [F25](followups.md)** | an S2 arm | **Unmeasured, not untriggered.** No clause in this wave touches it |
| **`Panos`'s σ fit** | ~5 m of `af-fit` | The only part of that trap entry still folklore |
| **F67's residual** | ~30 m + a rule | An [F62](followups.md) landmine (no `BestJ` across factors) and it touches no named gap |
| **F73's code axis** | ~1 h 50 m + a ~42 m gate | 0 for 3 on the owner's goals |
| **F70(b′) / PR #192** | — | **REJECTED by the owner. Not re-proposed** |
| **RULE F14 / RULE S16 / RULE D20** | — | **Three permanent fences. Not re-scored, not converted, not harvested** |

---

## §14 — Wave 26, recommended — and it competes with the owner's final results table

**The owner has asked for a final results table** — precision / recall / optimization time / score / sigma /
exposure vs optimal / binning vs optimal / `BrightnessSensitivity`, per dataset — which the controller will build
from a `golden eval` arm. **That is the highest-value remaining item and it should be scheduled first**, because
it is the only deliverable the owner has asked for by name and because everything below is a follow-up to work
already banked.

**With the hours that remain after it, in priority order:**

| # | item | price | goal | why |
|---|---|---|---|---|
| **1** | **`N25-E`** — is wave 25's B14 BEFORE arm comparable to wave 23's `13 of 17`? | **~4 m scoring, 0 TestApp** | **2** | The cheapest unanswered question in the series. Inputs on disk and fingerprinted. Either P2 did not move S1 and the series **recovers a baseline it wrote off**, or it did and `13 of 17` is formally retired **with evidence**. Both outcomes are informative |
| **2** | **the floor-not-sticky fix** (§7.3) | ~45 m code + tests, **plus a 3-cell targeted re-run ~10 m** | **2** | `D01` is a *named, diagnosed, reproducible* miss with a bar already pre-registered against it. **It does NOT need a paired 20-cell arm**: the blind arm is `W-UNEXERCISED`, so the only cells that can move are the three controls plus whatever `G25-P3c` moves. **Re-run `D01`/`D02`/`D03` and the 8-run gate; that is the whole cost.** Pre-register which of the two candidate fixes (monotone floor vs requested-span bound) before looking |
| **3** | **the A4 truth-model gap** | ~20 m | 2 | The harness computes the cap boundary from the requested sweep and the product from the fitted span; they disagree on `D01` r1 by 2×. **Fix the assertion, not the product** — and keep it out of any arm that also carries a product change |
| **4** | **`lumos`'s zero-star frame** | ~10 m | 1 | Carried from wave 25's declared drop |
| **5** | **the P23 flat-direction arm** | ~30 m | **3** | **Goal 3 has now had two consecutive waves of nothing.** If the run has an hour spare, this is the only thing that changes that |

**What wave 26 must NOT do.** Re-run a full paired 20-cell S1 arm to chase `D01`. `W-UNEXERCISED` is the
measurement that makes that unnecessary: 13 blind cells moved by exactly zero across a real product change, so a
second full arm would buy a second `SAME 13` for ~2 h 40 m. **Spend it on the results table.**

**Is the dry-wave stop in view? No.** Wave 25 produced a shipped product change, a `M-CORRECTED` closure of F34,
two new register entries (the missing lower bound, the floor-not-sticky defect), an F80 extension with two
demonstrated checker gaps, an eighth F66 counter-example, and a corroborated F21 costing. **The counter stays at
zero.**

---

## §15 — What was OPEN at the write-up timestamp, and how it closed

Recorded here rather than by editing the box at the top, so the record of what was open when the analysis was
written stays intact.

| was open at `05:06:53Z` | closed | evidence |
|---|---|---|
| **the full suite by COUNT** | **`Failed: 0, Passed: 3973, Skipped: 0, Total: 3973`, `3 m 42 s`, `SUITE_EXIT=0`** | `/mnt/d/hf_w25/suite.log`, finished **`05:10:24Z`** — **three and a half minutes AFTER the timestamp**, which is precisely why it was marked open rather than anticipated |
| **`RULE N25-E`** | **STILL OPEN — not run.** Drop rung D2. §13 prices it at ~4 m for wave 26 | `n25e.txt` absent; `v23_report_fingerprint_BEFORE.json` + `fp_v23_AFTER.txt` (38 of 38) preserve its inputs |
| **the scorer's `--out` files** | **STILL OPEN.** `w25_score.txt`, `w25_scorer_selftest.txt`, `m25_failend.txt` absent. **Every number they would carry is verified in this document against the arms' own reports** | §0.2 |
| **the commit, the push, the PR #195 section** | see the commit this document lands in | — |

**The suite delta is exactly as pre-registered: `3966 + 7 = 3973`**, and all seven are named in §10.4 — four shown
red against pre-change behaviour by named mutants, three labelled companions that pass pre-change, one of which
(`T5`) was independently proven to be a control by the controller's own predicate inversion (D7).

**Waves 26+ baseline from 3973.** Every number below it in any document — 3922, 3933, 3958, 3960, **3966** — is
stale.

---

## §16 — `N25-E`, run by the controller AFTER the write-up timestamp, and the two artifact defects it sits beside

§0.2 records that the wave's scoring pass left **no `--out` artifact** and that `N25-E` was **never run**. Both
are resolved here rather than by editing the sections above, so the record of what was open at
`2026-08-13T05:06:53Z` stays intact.

### 16.1 `N25-E` — RUN, and it RETIRES a published number

```
python3 /mnt/d/hf_w25/score_w25.py --n25e /mnt/d/hf_w25/before /mnt/d/hf_w23/v23     -> rc 0
```

| | |
|---|---|
| paired cells | **18** |
| identical on all 17 terminal fields | **17 of 18 = 0.9444** |
| the one that DIFFERS | **`D08_c11_2800mm`**, on `converged`, `roundsUsed`, `finalStepSize`, `deltaStepVsExpected`, `overallVerdict` |

**`RULE V23`'s S1 result of `13 of 17` is formally RETIRED as uncomparable**, and the evidence is the named
cell: `D08` is precisely where wave 24's P2 (the binning-revisit bound) took effect, so a rate measured on B13
cannot be set beside one measured on B14 or B15. **This is the clause doing the job it was written for.** Wave
24 deliberately quoted no accuracy rate so that nothing would be contaminated; wave 25 now shows the number
*would* have been contaminated, on exactly one cell, and says which.

**The cost of not having run this at the time:** none to any verdict — `N25-E` carries **no bar** by
pre-registration. Its value is that a future wave quoting `13 of 17` as a baseline now has a written refusal
with a reason, instead of an unexamined inheritance.

### 16.2 The missing scorer artifact — an F74/F75-family defect, and it is the controller's

The wave's scoring pass was invoked with **stdout redirected** rather than with `--out`, so
`score_w25.py --score` produced no file in `/mnt/d/hf_w25/`. Wave 23 recorded the identical defect for its
interlock FAIL end; it is the second time this run.

The transcript has been preserved as **`w25_score_CONTROLLER_CAPTURED.txt`** with a `.README` stating its
provenance in the file itself: it is a controller-captured transcript, **not** the scorer's own output, and no
claim may be upgraded on the strength of it. **Every W25 / N25 / M25 / W25-K number in this document was
independently re-derived from the arms' own report JSONs** by the analysis agent, and all of them hold — which
is why the gap costs the wave nothing beyond the lesson.

**The lesson, now paid for twice:** *a scorer that accepts `--out` must be invoked with it.* A redirect puts the
evidence somewhere the artifact root does not know about, and a job-scoped temporary directory is deleted
without ceremony. **Wave 26 should make `--out` mandatory** — a scorer that can be run without leaving an
artifact will eventually be run that way.
