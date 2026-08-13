# Wave 29 — results

## THE TIMESTAMP — the roll call, taken at an instant

```
$ date -u '+%Y-%m-%dT%H:%M:%SZ'; ls -la --time-style=full-iso /mnt/d/hf_w29/
2026-08-13T18:03:36Z
drwxrwxrwx  ...  2026-08-13 13:52:45.735921200 -0400 .
```

**One row was OPEN at that instant and is marked open in place.** `fp_AFTER.txt` had mtime
`13:52:45 → 14:03:36.461` (`= 18:03:36Z`) — **the same second as the timestamp**. Three `stat` calls two
seconds apart returned `373637 → 377044 → 380971` bytes: it was **mid-write**, the 42 GB sweep still running.
It closed at `14:04:27.954 -0400` (`= 18:04:27Z`), **51 seconds after the roll call**, at 398 023 bytes and
`POPULATION 2452`. It is reported below as a completed artifact **and** as having been open at the timestamp,
because wave 19's document was falsified by an artifact written 33 seconds before its own commit and the fix
for that is to say both things.

| row | state at **18:03:36Z** | artifact |
|---|---|---|
| `RULE W29-S` | **CLOSED** 17:36:08Z | `w29s_score.txt` |
| `RULE W29-R` | **CLOSED** 17:36:19Z | `w29r_score.txt` |
| `RULE W29-L` arm | **CLOSED** — `W29L_ARM_END 17:51:23Z`, 5 of 5 cells `exit=0`, interlock written by the driver | `w29l_arm.txt`, `w29l_manifest.tsv`, `W29L_MANIFEST_READY`, `l/out/L{0..4}.log` |
| `RULE W29-L` scoring | **CLOSED** 17:52:13Z | `w29l_score.txt` |
| `RULE V29` (pre-flight) | **CLOSED** 17:35:55Z, over a root of **7 files**; self-test 17:35:39Z `27 of 27`; four fixture probes 17:20:58–17:21:32Z | `vdrift_w29.txt`, `vdrift_selftest_w29.txt`, `vdrift_probe_hf_w2{4,6,7,8}.txt`, `vdrift_instrumentcheck_w29.txt` |
| `W29-FP` AFTER sweep | **OPEN AT THE TIMESTAMP** — mid-write, closed 18:04:27Z (11 m 42 s, 2 452 files) | `fp_AFTER.txt`, `fp_after_run.txt` |
| cross-wave integrity check | **NOT YET WRITTEN** at the timestamp — appeared 18:05:04Z | `fp_crosswave_check.txt` |
| `W29-FP` **BEFORE** sweep | **NEVER RUN** — controller deviation (i), §8 | — |
| the **closing** `V29` (`vdrift_w29_FINAL.txt`) | **NOT RUN** — design §11 called it the one thing not cut at any budget | — |
| Step 7, [F83](followups.md)'s `P-D08` at `n = 3` | **NOT RUN** — no artifact under the root | — |
| the three scorers' `--self-test` captures | **NO ARTIFACT** — `w29s_selftest.txt` / `w29r_selftest.txt` / `w29l_selftest.txt` are absent; the two drivers' self-tests **are** on disk (`27 of 27`, `33 of 33`) | — |
| `fp_w29.sh --self-test` capture, `T0.txt` | **NO ARTIFACT** — both named by the plan, neither on disk | — |

**Vessel.** Wave 29 ran on its own branch `ghilios/synthetic-af-bank-followups-wave29`, with commits `544f2b2`
(pre-registration) and `80e42c5` (the CORRECTION), on **PR #197**, verified `OPEN`, stacked on #196. **Design
§0 was executed rather than reversed** — wave 28's declared deviation was not repeated, and #196 does not carry
a third section.

**But the PR's own title still carries the withdrawn claim** — *"Wave 29: F82's fix has no product carrier, and
the step-size truth is produced by the code under test"*. The second half is `S-a` and holds. **The first half
is `S-b` and is FALSE** (§1). The title is editable where the pre-registration commit message is not, and
correcting it is owed before merge.

---

## §0 — What this document is allowed to say

Every number below is quoted from an artifact under `/mnt/d/hf_w29/`, from `/mnt/d/hf_w28/`, from
`/mnt/d/hf_w25/`, or from a file in the repository at `HEAD`, and each is cited. The pre-registered rules in
`docs/synthetic-af-bank-followups-wave29-design.md` are **applied as written**. Where a rule turned out to be
unsatisfiable, under-specified or directionally wrong, that is reported as a **finding** (§6, §7, §8, §9) — it
is not repaired, and no repaired version is scored.

**Everything below is synthetic-only.** The bank has no real under-sampled dataset
([F35](followups.md), [F43](followups.md)), and `RULE W29-L` is **one dataset, `D01_ultrawide_40mm`**, named as
one dataset everywhere it is quoted.

**Verdicts, in one table.**

| rule | verdict | population | artifact |
|---|---|---|---|
| **`RULE W29-S`** | **`S-CARRIER-EXISTS`** — `S-a` HOLDS, `S-c` HOLDS, **`S-b` FALSE** | 4 source files at `HEAD`, by sha256 | `w29s_score.txt` |
| **`RULE W29-R`** | **`R-STANDS`** — `k = 18`, **`c = 1`**, **`c_blind = 0`** | 18 addressable paired S1 cells under `/mnt/d/hf_w25/after` | `w29r_score.txt` |
| **`RULE W29-L`** | **`L-UNEVALUATED`** — `joint = −12162`, not positive; shares undefined | 5 cells × 9 frames on `D01` | `w29l_score.txt` |
| **`RULE V29`** | **`V-CLEAN`** over **7** instrument files; self-test **27 of 27**; tree **12 regions / 0 uncovered** | 7 files | `vdrift_w29.txt` |
| **`W29-FP`** | **NO WAVE-29 BEFORE.** Cross-wave overlap `PRESERVED`, 1 080 of 1 080, 0 moved; **four roots have no baseline in this run and are NOT claimed preserved** | 2 452 files AFTER | `fp_AFTER.txt`, `fp_crosswave_check.txt` |

---

## §1 — THE HEADLINE: the design's own headline was HALF WRONG, and the wave's own rule refuted it

**`S-b` is FALSE. A product carrier exists.**

Design §1(b)'s caller table named **two** of `BuildSummaryAsync`'s **five** callers. The scorer discovered the
full set rather than being handed it — `StartAsync` (`:3022`), `ReOptimizeWithLabelsAsync` (`:4741`),
`ContinueOptimizationAsync` (`:4853`), `OptimizeAgainAtRecommendedBinningAsync` (`:4946`), and the one that
matters, **`CaptureNewSweepAsync` (`StarDetectionOptimizerWizardVM.cs:5012`)**, which calls `BuildSummaryAsync`
at **`:5098`**. `w29s_score.txt` prints the discrimination directly:

```
      driver                                                         reloads  captures               prior-round state (REPORTED)
      StarDetectionOptimizerWizardVM.cs:CaptureNewSweepAsync         yes      RunLiveAttemptAsync    RecommendedStepSize,SelectedSummary
      StarDetectionOptimizerWizardVM.cs:ContinueOptimizationAsync    yes      NO                     -
      StarDetectionOptimizerWizardVM.cs:OptimizeAgainAtRecommendedBinningAsync yes      NO                     SelectedSummary
      StarDetectionOptimizerWizardVM.cs:ReOptimizeWithLabelsAsync    yes      NO                     -
      StarDetectionOptimizerWizardVM.cs:StartAsync                   NO       AcquireAsync           -
    S-b(i) -> FALSE
```

And the cross-round carrier is on the instance, verified at `HEAD`: `recaptureStepSize` is declared at
`:2608`, **assigned from the previous round's recommendation** at `:5037`
(`recaptureStepSize = geometry != null && geometry.RecommendedStepSize != …`), the fresh sweep is taken at
`:5071` (`RunLiveAttemptAsync`), and that sweep consumes it at `:3236`
(`ApplyRecaptureGeometry(options, recaptureStepSize)`).

**So design §1(b)'s sentence — *"No new sweep is taken between wizard rounds, so `SearchSpan` cannot change
between them and the defect cannot occur there at all"* — is FALSE**, and with it the two claims that hang off
it: that fix (1) *"reaches no user"*, and that F82 *"scores goal 2 zero"*. **F82 is a real product defect on a
real product path.** The controller repeated the false claim in the pre-registration commit message
(`544f2b2`), which cannot be edited; it is corrected in the design's CORRECTION block, in commit `80e42c5`, in
PR #197, and here.

**The instrument was written to the pre-registration and was NOT tuned.** `S-b` was implemented exactly as
design §4 fixes it — enumerate every `Recommend(` site under the plugin assembly, resolve each site's enclosing
recommendation builder, resolve each builder's own callers, and ask of each whether it re-captures. It returned
**FALSE on its own terms**, against the document that commissioned it. **That is a pre-registration working**,
and it is the only mechanism in this series that can produce this outcome: a rule written to confirm a claim
and allowed to refute it instead.

**What survives, and it is the larger half.** `S-a` **HOLDS**, and it is the finding this wave was worth
running for:

```
ComputeStepBehavioral is DECLARED once, at source lines 1213 to 1270
StepSizeRecommender.Recommend( call sites INSIDE that body : 1 at lines [1262]
StepSizeRecommender.Recommend( call sites in the WHOLE file: 2 at lines [792, 1262]
writers of the value serialized as terminal.stepBehavioral, over 70 harness file(s): 1
S-a -> HOLDS
```

At `HEAD`, `SynthValidateRunner.ComputeStepBehavioral` is a `for (var iter = 0; iter < MaxIterations; iter++)`
loop that calls `StepSizeRecommender.Recommend` at `:1262` and returns when `rec.StepSize == step`. **The
harness scores the recommender against a fixed point of the recommender.** Every goal-2 conclusion in this
series — including F82's *"a truth of 9"* — is denominated in a bar that the code under test produces. That is
why `stepBehavioral` moved 8.0 → 9.0 across wave 25's two arms while it was identical on the other 17 cells:
wave 25 changed `Recommend`, so it moved the bar and the measurement together. F82 recorded the symptom and
called it *"an assumption this series can no longer make for free"*. **The cause is one line, it has been in
the file the whole time, and nobody had looked at it.**

`S-c` also **HOLDS**: `SweepDetectability` declares `FrameFocuserPositions`, reachable inside `Recommend` via
`Recommend → MeasureMaxUsefulHalfSpan`. So the third candidate fix registered in design §4.4 remains available
and remains **not chosen** — a wave that reads a diagnosis and picks the fix it just invented is the
[F14](followups.md) / `RULE D20` fence in a new costume.

---

## §2 — `RULE W29-R` = `R-STANDS`, on evidence and not on caution

Wave 26 wrote the escape clause — *"if a later wave measures that `SearchSpan` over `bestFit.Inputs`
**shrinks on more than a single cell** while the requested sweep widens, then the defect is in the quantity and
not in its persistence, and (2) becomes correct"* — and **three waves quoted it and none executed it.** Wave 29
executed it, at zero compute, on artifacts already on disk.

**Validity first, and it is the first column of its own tree.** The divisor was asserted at **both** trees, not
assumed: `MaxHalfWidthSampledHalfSpanMultiple` reads `1.5` at `HEAD` **and** at the B15 tree
`29665a62e4f8`, read out of `/mnt/d/hf_w25/binary_provenance_w25.txt` rather than typed. The population
asserted 20 dataset directories, **18** addressable, and the gap matched **by name** — `D05_tec140_1000mm`,
`D19_cygnus_deep_shed`. The applicability census, run before any statistic: `rounds seen 37;
capped-or-floored and not detect-bounded 37; detect-bounded 0; neither capped nor floored 0`. The
`wasDetectBounded` trap design §5.1 named never fired. And the reader asserted it consumed the **per-round**
`stepRecommendation`/`bootstrap` copies and read nothing from `terminal` ([F68](followups.md) part 5), proved
by inspecting its own source for the token.

**The result.**

```
k  = 18 addressable cells (bar: k >= 2)
c  = 1 cells with at least one qualifying transition, over the 18 addressable (bar: c >= 2)
c_blind = 0 over the 15 cells excluding the burned ['D01_ultrawide_40mm', 'D02_rich_135mm', 'D03_redcat_250mm']
qualifying cells, by name: ['D01_ultrawide_40mm']
```

**The one qualifying cell is `D01`, and `D01` is burned** — its rounds were published in F82's own table, and
design §5.2 predicted its contribution before the scorer ran. Over the **15 blind cells nobody had computed
this statistic on, `c_blind = 0`.** The identity reproduced F82's published table on the nose — `D01` r0
`halfWidth` 12.0 ⇒ implied 16 against requested 16; r1 `halfWidth` 9.0 ⇒ implied 12 against requested 24 — and
on all 17 other cells implied and requested moved **together, in the same direction, on every transition.**

**What that does to F82's apparent generality, said plainly.** The shrink-while-widening transition that
F82 diagnosed occurs on **exactly one cell out of 18 in the bank, and on zero of the 15 that were blind.**
F82 is real — §1 establishes it reaches a real product path — but it is **not general**. Wave 26 named `n = 1`
as insufficient to reverse its choice; wave 29 measured `n = 1` on a population 6× larger and blind on 15 of
it. **The defect is not being suppressed by a lack of looking. It is rare.** Anyone pricing a fix for it should
price it against one cell in eighteen, not against the 37-of-37 capped-or-floored population that design §1
quantified.

**Two `COULD-NOT-LOOK` cells, named, each having LEFT the denominator rather than scoring zero:**

```
  [CNL-NOREPORT] D05_tec140_1000mm      no synth_validate_report.json -- the cell produced no report
  [CNL-NOREPORT] D19_cygnus_deep_shed   no synth_validate_report.json -- the cell produced no report
```

These are [F74](followups.md)'s two structural timeouts. They left the denominator; `k = 18`, not 20, and the
gap was asserted by name before any statistic was taken.

**On this verdict, wave 26's pre-registered choice stands: fix (1) remains correct on wave 26's grounds.** And
§1's finding stands beside it, **changed**: it does now reach a user. Nothing ships (design §9).

---

## §3 — `RULE W29-L` = `L-UNEVALUATED`, and it is a control working

`L-0`, the control that **can** fail, **HELD**. `L0` reproduced wave 28's published `D01` `NC = 1.0` high-tier
attribution block **field for field, 8 fields compared, 0 disagreeing**, against
`/mnt/d/hf_w28/nc/D01_ultrawide_40mm_nc1.0/attempt01/golden_eval.txt`. All five cells `exit=0`, all five echoed
their intended knobs, per-cell wall times 177 / 198 / 152 / 168 / 145 s against wave 28's measured 179 s
reference, **874 s actual against a 900–1500 s pre-registered band.**

**And then opening gates made the counted misses go UP.**

| cell | gate | `FN_total` (high tier) | `exclusive` | `recall@high` |
|---|---|---|---|---|
| `L0` | (baseline) | **50 249** | — | 0.195 (12 162 / 62 411) |
| `L1` | `MinimumStarBoundingBoxSize` 6 → 3 | 50 246 | **3** | 0.195 (12 165 / 62 411) |
| `L2` | `MaxDistortion` 0.5 → 0.9 | **62 411** | **−12 162** | **0.000 (0 / 62 411)** |
| `L3` | `Sensitivity` 36.333 → 35.333 | 50 148 | **101** | 0.196 (12 263 / 62 411) |
| `L4` | all three | **62 411** | — | **0.000 (0 / 62 411)** |

```
joint (total recoverable across all three)      : -12162
sum of the exclusive shares                     : -12058
COULD-NOT-LOOK: joint is -12162, which is not positive; the shares are undefined and a
  ratio against it would be an artefact rather than a measurement.
```

**`62 411` is `D01`'s entire high-tier golden population.** `L2` and `L4` returned `TP=0 FP=0 FN=110384
precision=NaN recall=0.000`. Every high-tier star became a false negative. The scorer refused rather than
divide by a negative denominator, and `L-1` / `L-2` are `UNEVALUATED`.

### 3.1 WHY — and it is a directional error in the pre-registration, verified from source

Design §6 headed its table *"one knob relaxed per cell"* and pre-registered `L2` as `MaxDistortion` **0.5 → 0.9**.
**That is not a relaxation. It is the strictest setting the axis can nearly express.**

`StarDetector.cs:1804`:

```csharp
var effectiveMaxDistortion = ComputeEffectiveMaxDistortion(p, d);
if (fillRatio < effectiveMaxDistortion) {   // ⇒ RecordRejection(..., RejectionGate.TooDistorted, ...)
```

`MaxDistortion` is compared as a **lower bound on the bounding-box fill ratio**. Raising it **tightens** the
gate. `DefocusAwareGates` is `false` in `D01`'s landed tree (`optimized_settings.json`), so
`ComputeEffectiveMaxDistortion` returns `p.MaxDistortion` verbatim and the effective threshold was exactly
`0.9`. And the file's own comment at `:1786` gives the ceiling: *"a perfect disk fills ~PI/4 ≈ 0.79."*

**`MaxDistortion = 0.9` is above the fill ratio of a perfect disk. `L2` and `L4` were guaranteed to return zero
detections before they were run.** The corroboration is in the wall clock: `L2` (152 s) and `L4` (145 s) were
the **two fastest** cells in the arm, because nothing survived the distortion gate to do downstream work.

**This is reported, not repaired.** No clause in design §6's tree reads the *direction* of a knob edit, so the
pre-registration could not catch it; the arm's own self-test checked that the edit was **surgical** (`33 of 33`,
including *"exactly 1 file changed"* and *"an unnamed field is UNCHANGED"*) but surgical is not the same as
correctly signed. The verdict stands at `L-UNEVALUATED`. The wave does not re-run `L2` at `0.1` and score that.

### 3.2 What the arm DID measure, which the pre-registered statistic threw away

The design's statistic is `FN_total` differences. The same artifacts carry the **per-gate** blocks, and those
answer the question the rule was written to ask. Differencing `L0` → `L1` gate by gate:

| gate | `L0` | `L1` | Δ |
|---|---|---|---|
| `REJECTED:TooSmall` | 11 372 | 1 835 | **−9 537** |
| `REJECTED:TooDistorted` | 14 876 | 20 573 | **+5 697** |
| `REJECTED:LowSensitivity` | 13 612 | 17 426 | **+3 814** |
| `REJECTED:NotCentered` | 147 | 154 | +7 |
| `REJECTED:OnBorder` | 77 | 87 | +10 |
| `REJECTED:Degenerate` / `TooFlat` | — | 5 / 1 | +6 |
| `NO CANDIDATE`, `Contaminated`, `ACCEPTED-elsewhere` | 8 931 / 533 / 701 | 8 931 / 533 / 701 | 0 |
| **net** | | | **−3** ✓ |

**Lowering the bounding-box floor from 6 to 3 released 9 537 high-tier candidates from `TooSmall`, and 99.97 %
of them were immediately re-caught by `TooDistorted` (+5 697) and `LowSensitivity` (+3 814). Three reached
acceptance.** That is a direct measurement of which gates bind, on real artifacts, and it is far stronger
evidence than the subtraction the rule pre-registered — which sees only the net `−3` and cannot tell a gate
that never fired from a gate whose entire yield was captured downstream.

The `L3` difference decomposes the same way and is quoted **only** as arithmetic, per design §6's fence:
`LowSensitivity` −122, `ACCEPTED-elsewhere` +5, `Contaminated` +4, `NotCentered` +12, net −101. **Nothing in
this document cites `L3` as evidence for or against a bound on the sensitivity axis**
([F84](followups.md), `RULE S16` is a permanent fence).

### 3.3 What is still unknown about `D01`, said plainly

**`D01` remains unlocalised.** Wave 28 converted 34 060 `NO CANDIDATE` into 33 298 gate rejections and said,
correctly, that it had not localised them. Wave 29 has now **measured** that:

- relaxing `MinimumStarBoundingBoxSize` 6 → 3 recovers **3 stars of 50 249** — **0.006 %**;
- relaxing `Sensitivity` by one notch recovers **101 of 50 249** — **0.20 %**;
- the third candidate gate was **never opened**, because the cell that was supposed to open it closed it
  instead.

So two of the three candidate gates move `D01` by a fifth of a percent between them, and the third is
unmeasured. **The binding constraint on `D01` has not been found, and the two cheapest candidates for it are
now excluded to within 0.2 %.** The gate-flow numbers in §3.2 say where to look next — `TooDistorted` and
`LowSensitivity` jointly absorb everything the bounding-box floor releases — but that is a hypothesis this wave
generated, not one it tested, and it is handed forward as such.

---

## §4 — `RULE V29` = `V-CLEAN`, and [F95](followups.md)'s remedy worked at its first half

```
=== RULE V29 -- --verify-derivation over /mnt/d/hf_w29 ===
  files checked: 7
  V29-A: sibling targets resolving on disk: 11 of 11 = 1.0000
  V29-B: unlicensed previous-wave tokens: 0
  V29-C: typed ordinals/counts in printed output: 0
regions enumerated: 12   uncovered: 0
>>> V-CLEAN
```

Self-test **`SELFTEST V29 27 of 27`**, clause groups `[1] [2] [3] [4] [5] [5b] [5c] [6]` all present, `[6]`
showing both ends. **The plan's Step 1 → Step 2 ordering was executed**: the last instrument was written at
`17:01:28Z` and the pre-flight ran at `17:35:55Z`, over a populated root of **7**. Wave 28 certified a root of
**one file**; wave 29 did not. **That is F95's remedy and it worked.**

**The remedy's second half did not run.** Design §11 named the closing `V29` as the one thing *not cut at any
budget* — *"the first run proves the checker works, the closing run proves the wave is clean, and this series
has been conflating them."* `vdrift_w29_FINAL.txt` is not on disk. `vdrift_instrumentcheck_w29.txt` (17:23:07Z)
and `vdrift_w29.txt` (17:35:55Z) are byte-identical in content and **both pre-date the arm**. The remedy is
therefore **half-executed, and the half that was cut is the half its own design said could not be cut.**
Price to close: **~2 m.**

---

## §5 — `W29-FP`: what is proved, and what is NOT

**There is no wave-29 BEFORE fingerprint**, because the controller ran `W29-S`, `W29-R` and the `W29-L` arm
before Step 3 (§8, deviation (i)). What exists is an AFTER sweep of the six declared read-only roots
(`POPULATION 2452`) and a cross-wave check against wave 28's `fp_BEFORE.txt` (mtime `2026-08-13 11:13:39 -0400`
= `15:13:39Z`; the check's own header rounds it to `~15:36Z`).

**PROVED:**

```
overlapping population, by root:
   /mnt/d/SyntheticAutofocusBank      640
   /mnt/d/hf_w25/table18              440
compared 1080   MOVED 0   in BEFORE but absent from AFTER 0
>>> CROSS-WAVE INTEGRITY = PRESERVED on the overlap (1080 files, 0 moved)
```

1 080 files across the two roots wave 28 also swept are **byte-identical from 15:13:39Z to 18:04:27Z**, i.e.
across the whole of waves 28 and 29. That is a real and useful integrity statement.

**NOT PROVED, and named as such by the artifact itself rather than left to a reader:**

```
roots in the AFTER sweep with NO baseline in this run (NOT claimed preserved):
    /mnt/d/hf_w25/after
    /mnt/d/hf_w25/before
    /mnt/d/hf_w25/exe
    /mnt/d/hf_w28/nc
```

**`/mnt/d/hf_w25/after` is the population `RULE W29-R` read, and `/mnt/d/hf_w28/nc` is the published baseline
`L-0` scored against.** Neither has an earlier fingerprint in this run. The wave cannot claim they were
unmodified; it can only claim that its own instruments opened them read-only, that `L-0` reproduced its
published block field-for-field (which is itself strong indirect evidence that `/mnt/d/hf_w28/nc` is intact),
and that the `/mnt/d/hf_w25/exe` dll sha256 matched `binary_provenance_w25.txt` before the arm's first cell.
**`W29-FP` is not `PRESERVED`, because the rule it was written for could not be evaluated. It is recorded as
what it is.**

---

## §6 — INSTRUMENT DEFECTS FOUND IN PREVIOUS WAVES' CHECKERS — report, do not repair

### 6.1 `historical_ranges` was STILL broken after wave 27 fixed it and wave 28 carried it forward — the THIRD generation

[F87](followups.md) is the record of this function eating its own file. Wave 27 repaired it; wave 28's design
§12 required the repair be carried forward and demonstrated, and wave 28's self-test clause `[6]` did
demonstrate it. **It was still broken, in a way clause `[6]` cannot see.**

Wave 28's function counts brackets on the **raw** line:

```python
opens = sum(line.count(c) for c in "[{(") - sum(line.count(c) for c in "]})")
```

An unbalanced `[` or `{` **inside a string literal** therefore opens a phantom block, and the depth never
returns to zero. Running wave 28's own function over wave 28's own file (599 lines) — measured, at analysis
time:

```
ranges computed by WAVE 28's own function: [(93, 120), (557, 565), (579, 598)]
  range 579 -> 598   opener line:     defonly = ['HISTORICAL_BLOCKS = ("PRIOR_BUILD_IDS", "ALLOW = [")',   # V28-OK: ...
   *** RUNS TO EOF ***
```

The opener at line 579 is a **test fixture string** containing the literal text `"ALLOW = ["`. The raw count
sees one unmatched `[`; the block runs to end of file. **Lines 579–598 of wave 28's own checker were exempt
from `V28-B` and `V28-C` for the whole of wave 28**, plus 557–565 by the same mechanism — 29 of 599 lines, and
the exempt span includes the file's tail.

**Whether it hid a live drift in wave 28: it did not, and that is luck rather than design.** Wave 29's probe
over `/mnt/d/hf_w28` reports lines 553, 558, 580 and 597, and every token in that span is **wave 28's own wave
id**, not a foreign residue. The finding is the defect, not a concealed drift.

**Wave 29's repair** blanks string literals length-preservingly before counting (`code_of`, `verify_derivation_w29.py:224`;
`historical_ranges`, `:229–238`), keeps `is_definition`, and keeps the `opens > 0` requirement. Its self-test
clause `[6]` now asserts all four ends, including *"a token INSIDE the declared historical literal is NOT
reported — the exemption survives the repair, which is the half a bare 'it reports findings now' would miss."*

**The register entry that matters is not the fix. It is that this function has now failed three times, each
time with a passing self-test, because each generation's self-test tested the ends the previous generation
broke.**

### 6.2 `V28-C`'s population was effectively EMPTY on every wave-28 scorer — measured

`V29-C` / `V28-C` is *"typed ordinals or counts in printed output"*. The clause finds a printing line with this
regex. Wave 28's:

```python
PRINTS = re.compile(r"\b(print|log|echo|printf|Prog|tee)\b")
```

Wave 28's three scorers emit through `out.write(`, which matches none of those tokens. Measured over
`score_w28b.py`, `score_w28n.py`, `score_w28s.py`:

```
out.write( lines total : 126        (36 + 52 + 38)
matched by w28 PRINTS  : 2
matched by w29 PRINTS  : 126
```

**124 of 126 printing lines in wave 28's three scorers were never scanned for a typed ordinal.** The two that
did match matched incidentally — the *string being written* happened to contain one of the keywords. **Wave
28's `V28-C: 0` was vacuous**: the clause returned zero because its population was almost empty, not because
the scorers were clean.

Wave 29's regex is widened to reach the method-call form:

```python
PRINTS = re.compile(r"\b(?:\w*[._])?(?:printf|print|echo|log|write|emit|say|tee|Prog)\b")
```

and the widening is **demonstrated**, not asserted — self-test clause `[3]` carries
*"ok: V29-C via out.write fired on its own mutant"* and *"ok: V29-C via an underscored helper fired on its own
mutant"*. On the wave-29 root, `V29-C: 0` over 7 files with the population now real.

**This is the same shape as §6.1 and it should be read as one finding in two places: a clause can pass for
three waves because its population is empty, and nothing in the verdict tree distinguishes "zero findings" from
"zero candidates".**

### 6.3 The per-clause fixture expiry REPEATED EXACTLY AS PREDICTED — the strongest evidence class this series has

Wave 28 pinned `/mnt/d/hf_w27` as its live `-B` fixture and wrote into its own instrument's header that such a
pin **goes silent exactly one wave later**, because `PREV` moves. Wave 29's plan §1a therefore forbade
inheriting the choice and required measuring it. The four probes, run 17:20:58–17:21:32Z:

| root | `V29-A` | `V29-B` | `V29-C` | role at wave 29 |
|---|---|---|---|---|
| `/mnt/d/hf_w24` | 1 of 1 | **0** | **2** | the durable **`-C`** fixture, third wave running |
| `/mnt/d/hf_w26` | 2 of 6 | **0** | 1 | expired at wave 28, **still asserted silent** |
| `/mnt/d/hf_w27` | 1 of 5 | **0** | 1 | **EXPIRED — wave 28's live `-B` fixture, now silent** |
| `/mnt/d/hf_w28` | 0 of 5 | **100** | 1 | the new live **`-B`** fixture |

Self-test `[5b]` records all four, including *"ok: `/mnt/d/hf_w27` yields ZERO `V29-B` findings — THE EXPIRY IS
DEMONSTRATED, which is why the `-B` fixture is pinned separately and re-measured every wave."*

**Why this is the strongest evidence class available here.** It is a **prediction written into an instrument's
own header one wave before the measurement**, with a stated mechanism (`PREV` advances, so last wave's tokens
stop being "previous-wave"), and it was confirmed by a measurement designed before the answer was known.
Everything else in this series is a rule scored against artifacts that already existed. This is the one place
where the series made a falsifiable forward claim about its own instruments and then checked it. **`hf_w27`
went from 100-class `-B` findings to zero in exactly one wave, as written.** Wave 30 must re-measure again;
`/mnt/d/hf_w28` will be the next to expire.

---

## §7 — A BLINDNESS BURN, caused by the instrument, recorded as a wave-29 deviation

Design §7's ledger says, verbatim: *"`halfWidth`, `bootstrap.offsetSteps` and `bootstrap.stepSize` have been
read on `D01` only (published), and the requested-vs-implied comparison has **never** been computed on any
other cell."*

**That claim is now false, and wave 29 made it false.** While validating `RULE W29-R`'s identity, the
instrument agent computed the requested-vs-implied comparison on **all 18 addressable cells, including the 15
blind ones**, and `w29r_score.txt` prints all 18 rows in full — `halfWidth`, implied span, requested span and
the qualification for every cell from `D01` to `D20`.

**The rule was NOT tuned.** `W29-R` was implemented exactly as design §5.3 writes it: `c` over 18, `c_blind`
over 15, the verdict taken on `c ≥ 2` as wave 26 wrote it, both printed so no reader has to do the arithmetic.
The burn is not a change to the statistic — it is that **computing the statistic and publishing the per-cell
table are the same act**, and design §7 wrote a blindness claim that the design's own §5.3 instrument was
guaranteed to violate.

**Scope of the burn, stated for whoever inherits it.** The 15 blind cells' per-round `halfWidth`,
`bootstrap.offsetSteps`, `bootstrap.stepSize`, implied span and requested span are **now published**. Any
future rule over `SearchSpan` persistence on `/mnt/d/hf_w25/after` has **no blind population left in this
bank.** `W29-R`'s own verdict is unaffected — it was taken on the first computation of the statistic, which is
what blindness protects — but it is the **last** verdict on this population that can claim it.

**The structural lesson, and it belongs in the register:** a blindness ledger that promises a quantity will
stay unread, while the same document pre-registers an instrument that must read it to produce its verdict, is
internally inconsistent at pre-registration time. Nothing checked the ledger against the instruments.

---

## §8 — CONTROLLER DEVIATIONS

**(i) The BEFORE fingerprint was never taken, and three measurements ran ahead of it.** The plan's Step 3
places `W29-FP` BEFORE ahead of Steps 4–6. The controller ran `RULE W29-S` (17:36:08Z), `RULE W29-R`
(17:36:19Z) and the whole `W29-L` arm (17:36:49–17:51:23Z) **first**, and only then swept
(17:52:45 → 18:04:27Z). `fp_BEFORE.txt` does not exist.

**What that costs, exactly.** `fp_AFTER.txt` is an integrity check with no wave-29 BEFORE. The two roots that
overlap wave 28's sweep — `/mnt/d/hf_w25/table18` (440) and `/mnt/d/SyntheticAutofocusBank` (640) — do have a
usable earlier baseline in wave 28's `fp_BEFORE.txt` (15:13:39Z), and across it **1 080 of 1 080 are
byte-identical, 0 moved**. The other four roots — `/mnt/d/hf_w25/{after,before,exe}` and `/mnt/d/hf_w28/nc` —
**have no baseline in this run at all**, and the cross-wave artifact names them rather than reporting them
preserved. `W29-FP` did not reach `PRESERVED` and is not claimed to have. §5 states what is and is not proved.

**(ii) The `--out` and `--rule` conventions in the plan's own command lines were wrong**, and the controller
used the instrument headers' forms instead. The instance verifiable from artifacts: `fp_w29.sh`'s own header
documents `bash fp_w29.sh before --out <file> [--rule W29-FP]` and `bash fp_w29.sh after --out <file>
[--rule W29-FP]`, while the plan's Steps 3 and 8 write `bash /mnt/d/hf_w29/fp_w29.sh after` with no `--out` —
against a standing constraint (design §12) that **`--out` is mandatory and every instrument refuses without
it**. The plan's Step 1a probe loop likewise omits `--rule`, which the three scorers require and the verifier
does not accept. **The plan's command lines were not checked against the instruments the plan itself
commissioned.**

**(iii) Steps 7 and 8's second half did not run.** [F83](followups.md)'s `P-D08` at `n = 3` (~2 m) has no
artifact, against design §11's explicit *"an item under two minutes is never dropped"* and its citation of
`RULE C19`. The closing `V29` has no artifact, against design §11's *"NOT cut, at any budget."* Neither is
laundered as done; both are priced in §11.

**(iv) No scorer `--self-test` capture is on disk.** The plan's Step 4 and Step 6 gates name
`w29s_selftest.txt` and `w29l_selftest.txt`; the arm driver's and the verifier's self-tests **are** captured
(`SELFTEST W29L-ARM 33 of 33`, `SELFTEST V29 27 of 27`), the three scorers' are not. Wave 28's §7.7 recorded
the identical class of omission for `fp_w28.sh`. **Named, not assumed.**

---

## §9 — UNDER-SPECIFIED IN THE PLAN, per the instrument agent

| the plan said | what was true |
|---|---|
| *"Five files under `/mnt/d/hf_w29/`"* (Step 1) | **Seven.** `V29` reports `files checked: 7` — `layout_w29.sh` (the shared layout function design §12 requires) and the split of the `W29-L` driver from its scorer were never counted |
| edit `MinimumStarBoundingBoxSize` and `Sensitivity` (design §6, `StarDetectorParams` spellings) | The file the arm edits is `optimized_settings.json`, whose keys are **`MinStarBoundingBoxSize`** and **`BrightnessSensitivity`**. The arm's self-test `[4]` asserts the landed FROM values against the tree by those names, and `[6]` proves *"editing a field the file does not declare REFUSES rather than inventing it"* — so the mismatch was caught by an instrument rather than by the plan |
| *"`Sensitivity` relaxed one notch"* (design §6, `L3`) | **"One notch" was undefined.** Read at run time from the product's own axis declaration — `OptimizerVariable.cs:168`, `Continuous(nameof(StarDetectorParams.Sensitivity), …, 1.0, …)` — giving `36.333 → 35.333`. That is a **2.75 %** relaxation, which is why `exclusive(Sensitivity)` is only **101** and is weak evidence for anything |
| *"pins `--settings`"* (plan Step 6) | Pinned to **the file wave 28's own log records** — `C:\Users\ghili\AppData\Local\HocusFocusHarness\harness_settings.json`, sha256 `b7e81813…` — recorded in the manifest, because the plan named no file |
| `binary_provenance_w25.txt` line `tree:` (plan Step 5) | **The `tree:` line holds a COMMIT.** `git cat-file -t 29665a62e4f8` returns `commit` (`29665a6 W25: floor the half-width when the sweep demonstrably never reached the band`). The assertion works, and the field name is wrong wherever it is quoted |
| the `MaxDistortion` direction (design §6) | Not specified at all, and wrong — §3.1 |

---

## §10 — [F21](followups.md): estimate vs actual, per step, and the budget error

| step | est | actual | note |
|---|---|---|---|
| 0 vessel + pre-registration | 10 m | — | two commits (`544f2b2`, `80e42c5`); `T0.txt` never written, so no in-band start stamp |
| 1 write **all** instruments | 60 m | **~37 m** | first write `16:41:51Z` (`layout_w29.sh`), last `17:18:45Z` (`verify_derivation_w29.py`) |
| 2 `RULE V29` pre-flight | 20 m | **~15 m** | probes `17:20:58Z` → verdict `17:35:55Z` |
| 3 `W29-FP` BEFORE | 8 m | **NOT RUN** | and mispriced — see below |
| 4 `RULE W29-S` | 25 m | **~13 s** at run time | zero compute; the cost was paid in Step 1 |
| 5 `RULE W29-R` | 25 m | **~11 s** at run time | zero compute; likewise |
| 6 `RULE W29-L` arm + scoring | 55 m / ~25 m compute | **15 m 32 s**, of which **14 m 34 s** compute | `17:36:41Z → 17:52:13Z`; arm 874 s against its own 900–1500 s band |
| 7 `P-D08` at `n = 3` | 2 m | **NOT RUN** | §8 (iii) |
| 8 fingerprint AFTER + closing `V29` | 12 m | **12 m 19 s for the AFTER half alone**; closing `V29` NOT RUN | `17:52:45Z → 18:05:04Z` |
| 9 results | 75 m | this document | |

**The budget error, and it is the one durable number here.** The six declared read-only roots measure
**~41.8 GB** (`hf_w25/after` 17 G, `hf_w25/before` 15 G, `SyntheticAutofocusBank` 8.7 G, `hf_w28/nc` 889 M,
`hf_w25/exe` 196 M, `hf_w25/table18` 14 M). The AFTER sweep hashed all 2 452 files in **11 m 42 s**
(`17:52:45Z → 18:04:27Z`) — **~61 MB/s**. The plan priced **Step 3 at 8 m** (a sweep alone is 1.5× that) and
**Step 8 at 12 m** to cover a second sweep *plus* the reversal check *plus* the closing `V29` (the sweep alone
consumed it, and the closing run is why Step 8 is ~2× under). **Steps 3 and 8 are both under-priced by ~1.5–2×,
and the omission in §8 (iii) is downstream of the mispricing, not independent of it.** A wave declaring these
six roots owes **~24 m of pure I/O** for a two-sided fingerprint and should price it from the measured
61 MB/s, not from instinct.

Design §11 predicted the analysis halves would again come in under (wave 28 ~59 % under, wave 27 ~36 %). They
did — Steps 1, 2, 4, 5 and 6 all landed under. **The compute step priced from a measured rate (Step 6) was the
most accurate one in the wave**, and the two steps priced from instinct (3 and 8) were the only ones that
overran. That is the fourth consecutive confirmation of the same lesson and it is now boring: *price compute
from the measured rate.*

---

## §11 — WHAT WAS NOT RUN, AND WHAT IT COSTS

| not run | price | why, and is it owed |
|---|---|---|
| **the 42 m `optimize` gate** | **42 m** measured (wave 25: 42 m 43 s, ~5.2 m/run, seven consecutive waves); predicted verdict `G-PASS`, 8 of 8 bit-identical to K8 | **NOT OWED this wave, and it is checked rather than asserted.** `git diff --name-only 6ffa498..HEAD -- '*.cs'` is **empty** — wave 29's two commits touch `docs/…wave29-design.md` and `plans/…wave29-plan.md` and nothing else — and no binary was built: the arm ran `/mnt/d/hf_w25/exe`, dll sha256 `2fb0c8fd…` asserted against `binary_provenance_w25.txt` before its first cell. **All three of design §8's reversal conditions are negative.** **It is owed, certainly and not probably, by the wave that ships any F82 fix** — `Q27-V1`'s own FAIL end names `StepSizeRecommender.cs` by path |
| **a wave-29 BEFORE fingerprint** | **~12 m** (measured: 11 m 42 s at 61 MB/s over 41.8 GB) | Owed by the plan and not taken (§8 (i)). Cannot be recovered retroactively — a BEFORE taken now is an AFTER. **The next wave must take it first, or declare fewer roots** |
| **the closing `V29`** | **~2 m** | Owed by design §11's *"NOT cut, at any budget"*. F95's remedy is half-executed (§4) |
| **`P-D08` at `n = 3`** | **~2 m** | Owed by charter §4 and design §11's cut-list, which explicitly refused to cut it |
| **the three scorers' self-test captures** | **~5 m** | The gates the plan wrote for Steps 4/5/6 (§8 (iv)) |
| **localising `D01`** | **~25 m** compute for a corrected 3-cell arm (`MaxDistortion` **lowered**, plus the two gates §3.2 identifies), on the binary already on disk | **The wave's largest open question and it is still open.** Two candidate gates now excluded to within 0.2 %; the third was never opened; §3.2 names `TooDistorted` + `LowSensitivity` as where the released candidates go |
| the unit suite | ~4 m | **Vacuously satisfied** — no C# changed. Baseline for the wave that does change it is **3997** (wave 28's measured close), not the charter's stale 3973 |
| a full paired 18-cell S1 arm | ~2 h 40 m | Unchanged: `W-UNEXERCISED` means a second arm buys a second `SAME 13` |
| `S27-1` and wave 27's three mutants | ~20 m | **STILL OPEN, THREE waves running.** Named again so it is not lost a fourth time |
| wave 28's `M1`–`M6` mutant records | ~10 m | Not applicable — wave 29 ships nothing and writes no test. Wave 28's debt stays wave 28's |
| new datasets between 1.4 and 19.4 ″/px | ≥ 1 h render | The only route to a **blind** wide-field dataset; the coverage hole is unchanged, and §7 has now spent the blindness that existed |
| the blur/layer decoupling build ([F92](followups.md)) | ~45 m + 10 m (+ 42 m to ship) | Unchanged |
| the `A4` truth-model gap | 20 m code **+ 42 m gate** | And §1 has now made it **more** urgent, not less: `S-a` shows the harness's own bar is produced by the code under test |
| anything on real frames | — | The bank has no real under-sampled dataset |

---

## §12 — REGISTER ENTRIES, READY TO PASTE

The register's highest existing entry is **F95**. These continue from it.

---

### F96 — The harness's `stepBehavioral` "truth" is a fixed point of the recommender it scores, so every goal-2 conclusion is denominated in a bar the code under test produces

**Status:** Open (diagnosed to a line; no fix priced) · found 2026-08-13, wave 29, `RULE W29-S` clause `S-a`
= HOLDS · `/mnt/d/hf_w29/w29s_score.txt`

`SynthValidateRunner.ComputeStepBehavioral` (`:1213–1270`) builds an analytic curve, fits it, calls
`StepSizeRecommender.Recommend` at **`:1262`**, and loops until `rec.StepSize == step`. `RULE W29-S` asserts
from source, at `HEAD`, by sha256, that the method is **declared once**, contains **exactly one** `Recommend(`
call site inside its body (whole-file count 2, at `:792` and `:1262`, printed so the scoping is visibly doing
work), and is the **only writer** over 70 harness files of the value serialized as `terminal.stepBehavioral`.

**Consequence.** A change to `Recommend` moves the bar **and** the measurement together. This is the mechanism
behind [F82](#f82)'s observation that `stepBehavioral` moved 8.0 → 9.0 across wave 25's two arms while it was
identical on the other 17 cells: wave 25 changed `Recommend`. **Assertion `A3`, and every goal-2 statement in
waves 20–29 that quotes `stepBehavioral` as truth, is a self-consistency check and not an accuracy measurement.**

**What it does NOT say.** It does not say the recommender is wrong; a fixed point can be correct. It says the
harness cannot tell. **The remedy is a spec-derived truth model** — backlog item 4's `A4` gap, priced at 20 m
of code **+ 42 m of gate** because it rebuilds `TestApp`.

**Note on scope.** Design §14 pre-registered F96 as a two-part entry — the truth-model coupling **and** "F82's
fix has no product carrier" — to be **withdrawn in place** on `S-CARRIER-EXISTS`. The verdict was
`S-CARRIER-EXISTS`, so **the carrier half is withdrawn and is not registered.** The coupling half is `S-a`,
which held, and stands.

---

### F97 — F82's shrink-while-widening transition occurs on exactly ONE cell in the bank, and on ZERO of the fifteen that were blind

**Status:** Open (measured; F82's fix choice unchanged) · found 2026-08-13, wave 29, `RULE W29-R` =
**`R-STANDS`** · `/mnt/d/hf_w29/w29r_score.txt`

Wave 26 pre-registered a reversal condition on its choice between F82's two fixes — *"if a later wave measures
that `SearchSpan` … shrinks on more than a single cell while the requested sweep widens, then … (2) becomes
correct."* **Three waves quoted it; none ran it.** Wave 29 ran it at zero compute over the 18 addressable
paired S1 cells under `/mnt/d/hf_w25/after`, via the exact inversion
`impliedSearchSpan = stepRecommendation.halfWidth / (1.5 × 0.5)`, with the multiple asserted `= 1.5` at **both**
the B15 tree `29665a62e4f8` and `HEAD`, and with all 37 rounds confirmed capped-or-floored and none
detect-bounded before any statistic was taken.

```
k = 18   c = 1   c_blind = 0   qualifying cells: ['D01_ultrawide_40mm']
```

**`c = 1`, and the one cell is `D01`, which was already burned.** `D05_tec140_1000mm` and
`D19_cygnus_deep_shed` produced no report and **left the denominator** as named `CNL-NOREPORT`, never as zeros.

**What this changes.** F82 is real and (per [F98's sibling finding in `RULE W29-S`](#f96)) reaches a real
product path — but it is **rare, not general**: one cell in eighteen, zero in fifteen blind. On all 17 other
cells the implied and requested spans move together on every transition. **Anyone pricing a fix should price it
against 1 of 18, not against the 37-of-37 capped-or-floored population.** Wave 26's choice of fix (1) stands on
wave 26's own grounds; the reversal condition is now **evaluated** rather than merely unexamined.

**Blindness spent.** Producing this verdict required publishing the per-cell table for all 18 cells, so the 15
formerly-blind cells' `halfWidth`/`bootstrap`/implied-span values are now read. See F100.

---

### F98 — `MaxDistortion` is a MINIMUM fill-ratio despite its name, and 0.9 is above the ~0.79 ceiling of a perfect disk, so a whole arm cell was dead before it ran

**Status:** Open — **candidate goal-3 product finding** · found 2026-08-13, wave 29, `RULE W29-L` =
**`L-UNEVALUATED`** · `/mnt/d/hf_w29/w29l_score.txt`, `/mnt/d/hf_w29/l/out/L{0,2,4}.log`

`StarDetector.cs:1804` rejects a candidate as `TooDistorted` when `fillRatio < effectiveMaxDistortion`. The
parameter is a **lower bound on bounding-box fill ratio**; **raising it tightens the gate.** The file's own
comment at `:1786` states the ceiling: *"a perfect disk fills ~PI/4 ≈ 0.79."* With `DefocusAwareGates: false`
in `D01`'s landed tree, `ComputeEffectiveMaxDistortion` returns `p.MaxDistortion` verbatim.

Wave 29's design §6 pre-registered `MaxDistortion` **0.5 → 0.9** under the heading *"one knob relaxed per
cell"*. Measured result on `D01`, 9 frames: **`TP=0 FP=0 FN=110384`, `recall@high = 0.000 (0/62 411)`** on
both `L2` and `L4` — total detection collapse. Corroborated by wall clock: `L2` (152 s) and `L4` (145 s) were
the two **fastest** cells in a five-cell arm, because nothing survived the gate to do downstream work.

**Two findings, and they are separable.**

1. **The instrument finding.** A pre-registration can specify a knob edit whose *direction* no clause reads.
   The arm's self-test proved the edit was **surgical** (`33 of 33`, *"exactly 1 file changed"*, *"an unnamed
   field is UNCHANGED"*) — surgical is not correctly signed. `RULE W29-L`'s verdict is
   **`L-UNEVALUATED`** and it stands; the cell was not re-run at a corrected value and scored.
2. **The product finding.** `OptimizerVariable.cs:176` declares the searchable axis
   `Continuous(nameof(StarDetectorParams.MaxDistortion), 0.1, 1.0, 0.1)`. **The upper ~21 % of that axis
   (> π/4 ≈ 0.785) is provably detection-killing for round stars** — any value above the perfect-disk fill
   ratio rejects every candidate. A searchable range whose top fifth returns zero detections is a parameter
   space that wastes optimizer evaluations, and the name `MaxDistortion` reads as the opposite of what it
   gates. **Worth ~20 m to bound the axis at π/4 and rename or document the field; owes the 42 m gate because
   it touches plugin code.**

---

### F99 — Relaxing the bounding-box floor on `D01` releases 9 537 candidates and three survive: the binding gates are downstream, and the pre-registered statistic could not see it

**Status:** Open — the hypothesis wave 30 should pre-register · found 2026-08-13, wave 29 ·
`/mnt/d/hf_w29/l/out/L{0,1}/attempt01/golden_eval.txt`

`RULE W29-L`'s pre-registered statistic is the difference in `FN_total`. On `D01`, lowering
`MinStarBoundingBoxSize` 6 → 3 gives `FN_total` 50 249 → 50 246, i.e. **−3**, which reads as "this gate does
almost nothing". The per-gate blocks in the same artifact say otherwise:

| gate | `L0` | `L1` (MinBox 6→3) | Δ |
|---|---|---|---|
| `REJECTED:TooSmall` | 11 372 | 1 835 | **−9 537** |
| `REJECTED:TooDistorted` | 14 876 | 20 573 | **+5 697** |
| `REJECTED:LowSensitivity` | 13 612 | 17 426 | **+3 814** |
| others | | | +23 |

**9 537 high-tier candidates were released from the bounding-box floor and 99.97 % of them were immediately
re-caught by `TooDistorted` and `LowSensitivity`. Three reached acceptance.** The gate is not inert; its entire
yield is absorbed one and two gates downstream.

**The lesson is about the statistic, not the detector.** A first-rejection-wins attribution differenced only at
the *total* cannot distinguish a gate that never fires from a gate whose output is fully captured downstream.
**Wave 30's rule over sequential gates must read the per-gate flow, not the net.** And the substantive
hypothesis this generates, stated so it can be wrong: **`D01`'s binding constraint is `TooDistorted`, jointly
with `LowSensitivity`, and no single-knob change recovers it** — which is `L-JOINTLY-BOUND`'s claim arrived at
by a different route than the rule that could not evaluate it.

---

### F100 — A blindness ledger that promises a quantity will stay unread, beside an instrument that must read it, burns the population at pre-registration time

**Status:** Recorded (the burn is spent; the structural fix is cheap) · found 2026-08-13, wave 29 ·
design §7 vs `/mnt/d/hf_w29/w29r_score.txt`

Wave 29's design §7 wrote: *"the requested-vs-implied comparison has **never** been computed on any other
cell"*, and named the 15 non-burned cells as **BLIND, and carrying `W29-R`'s verdict**. The same document's
§5.3 pre-registered a scorer that computes exactly that comparison over all 18 addressable cells and prints
`c` and `c_blind`. **`w29r_score.txt` publishes the full 18-row table.** The ledger's claim is now false, and
the instrument that falsified it was commissioned by the document that made the claim.

**The rule was not tuned** — `W29-R` is implemented exactly as §5.3 writes it, and its verdict was taken on the
first computation of the statistic, which is what blindness protects. **But it is the last verdict on this
population that can claim blindness.** Any future rule over `SearchSpan` persistence on `/mnt/d/hf_w25/after`
has no blind cells left.

**Fix, ~5 m per wave:** the design's blindness ledger must be checked against the design's own instrument
specifications before the pre-registration commit — every quantity the ledger promises stays unread must not
appear in any clause's per-member output. Nothing does this today.

---

### F101 — The derivation checker's `historical_ranges` has now failed THREE generations, each time with a passing self-test

**Status:** Repaired in wave 29's checker; the pattern is the entry · found 2026-08-13, wave 29 ·
`/mnt/d/hf_w28/verify_derivation_w28.py:579`, `/mnt/d/hf_w29/verify_derivation_w29.py:224–238`

[F87](followups.md) recorded this function eating its own file; wave 27 repaired it; wave 28's design required
the repair be carried forward and demonstrated, and wave 28's self-test clause `[6]` did demonstrate both ends
it knew about. **It was still broken.** Wave 28 counted brackets on the **raw** line, so an unbalanced `[` or
`{` inside a **string literal** opened a phantom block that ran to EOF. Measured on wave 28's own 599-line
file:

```
ranges computed by WAVE 28's own function: [(93, 120), (557, 565), (579, 598)]
  range 579 -> 598   opener: defonly = ['HISTORICAL_BLOCKS = ("PRIOR_BUILD_IDS", "ALLOW = [")',  ...
   *** RUNS TO EOF ***
```

**29 of 599 lines of wave 28's own checker, including its tail, were exempt from `V28-B` and `V28-C` for the
whole of wave 28.** It concealed nothing — every token in the exempt span is wave 28's own wave id — and that
is luck, not design. Wave 29 blanks string literals length-preservingly before counting and asserts all four
ends in clause `[6]`, including *"a token INSIDE the declared historical literal is NOT reported"*.

**The durable finding is the pattern:** three generations, three passing self-tests, because each generation's
self-test tested the ends the previous generation broke. **A self-test that only covers the last bug found is a
regression test, not a specification.**

---

### F102 — `V28-C` returned zero because its population was almost empty: 124 of 126 printing lines in wave 28's scorers were never scanned

**Status:** Repaired in wave 29's checker; the vacuity class is the entry · found 2026-08-13, wave 29 ·
`/mnt/d/hf_w28/verify_derivation_w28.py:170`, `/mnt/d/hf_w29/verify_derivation_w29.py:160`

`V*-C` forbids typed ordinals and counts in an instrument's printed output. Wave 28's line-selector was
`\b(print|log|echo|printf|Prog|tee)\b`; wave 28's three scorers emit through `out.write(`, which matches none
of it. Measured over `score_w28b.py` (36), `score_w28n.py` (52), `score_w28s.py` (38):

```
out.write( lines total : 126
matched by w28 PRINTS  : 2      (incidentally -- the string being WRITTEN contained a keyword)
matched by w29 PRINTS  : 126
```

**Wave 28's `V28-C: 0` was vacuous.** Wave 29 widened the selector to
`\b(?:\w*[._])?(?:printf|print|echo|log|write|emit|say|tee|Prog)\b` and **demonstrated** the widening rather
than asserting it — self-test `[3]` carries *"V29-C via `out.write` fired on its own mutant"* and *"V29-C via
an underscored helper fired on its own mutant"*.

**The class, which is the same as F101's and should be read with it:** a clause can pass for three waves
because it has no candidates, and **no verdict tree in this series distinguishes "zero findings" from "zero
candidates".** [F94](followups.md) made trees total over their *outcomes*; nothing makes a clause report its
*population*. **~10 m to make every clause print `candidates: N   findings: M` and refuse on `N = 0` where a
population is expected.**

---

### F103 — A pinned per-clause fixture goes silent exactly one wave later, as predicted in an instrument's own header and confirmed by measurement

**Status:** Closed as a mechanism; standing obligation to re-measure each wave · found 2026-08-13, wave 29 ·
`/mnt/d/hf_w29/vdrift_selftest_w29.txt` `[5b]`, `vdrift_probe_hf_w2{4,6,7,8}.txt`

Wave 28 pinned `/mnt/d/hf_w27` as its live `V28-B` fixture and wrote into its own instrument's header that such
a pin expires one wave later, because `PREV` advances and last wave's tokens stop being "previous-wave". Wave
29 measured all four candidate roots before pinning anything:

| root | `-A` resolving | `-B` findings | `-C` findings | role |
|---|---|---|---|---|
| `hf_w24` | 1 of 1 | 0 | **2** | durable `-C` fixture, third wave running |
| `hf_w26` | 2 of 6 | **0** | 1 | expired at wave 28; kept and **asserted silent** |
| `hf_w27` | 1 of 5 | **0** | 1 | **EXPIRED as predicted** — wave 28's live `-B` |
| `hf_w28` | 0 of 5 | **100** | 1 | the new live `-B` |

**This is the strongest evidence class this series has produced**, and the reason is structural: it is a
**falsifiable forward claim about the instruments, written before the measurement, with a stated mechanism, and
checked one wave later.** Everything else in the series is a rule scored against artifacts that already
existed. **`hf_w28` is next to expire; wave 30 must re-measure and must treat a clause with no live fixture as
a FAIL, not a pass.**

---

### AMENDMENTS TO EXISTING ENTRIES

**[F82](followups.md) — AMEND IN PLACE. Its fix choice stands; its scope, its generality and its truth
model are now measured.** Append:

> **Amended 2026-08-13, wave 29.** Three things about this entry were unmeasured when it was written.
>
> 1. **The "truth of 9" is a PRODUCT-DERIVED FIXED POINT, not a spec-derived truth.** `terminal.stepBehavioral`
>    is `StepSizeRecommender.Recommend` iterated to a fixed point by
>    `SynthValidateRunner.ComputeStepBehavioral:1262` — see **F96**. `D01`'s stall is measured against a bar the
>    recommender itself produces, and wave 25 moved both together.
> 2. **"Confined to the recommender's caller state" names a caller, and it is
>    `StarDetectionOptimizerWizardVM.CaptureNewSweepAsync` (`:5012`).** `RULE W29-S` clause `S-b` returned
>    **FALSE**: of `BuildSummaryAsync`'s five callers, `CaptureNewSweepAsync` takes a **fresh sweep**
>    (`RunLiveAttemptAsync`, `:5071`), calls `BuildSummaryAsync` at `:5098`, and carries the previous round's
>    recommended geometry forward through the instance field `recaptureStepSize` (`:2608`, assigned `:5037`,
>    consumed `:3236`). **F82 is a real product defect on a real product path**, and the wave-29
>    pre-registration's claim that it "reaches no user" and "scores goal 2 zero" is **withdrawn**.
> 3. **Its generality is one cell in eighteen.** `RULE W29-R` = `R-STANDS`: `k = 18`, `c = 1`, `c_blind = 0`.
>    The only qualifying cell is `D01` itself. See **F97**. Wave 26's choice of fix (1) stands, on wave 26's
>    own grounds, with the escape clause now **evaluated** rather than unexamined.
> 4. **A third candidate fix is registered and deliberately NOT chosen** (wave 29 design §4.4): bound
>    `maxHalfWidth` below by the span the sweep actually **requested**, guarded by
>    `BandDemonstrablyUnsampled(sampledHfrRange)`. `RULE W29-S` clause `S-c` **HOLDS** — the requested positions
>    are in `SweepDetectability.FrameFocuserPositions`, reachable inside `Recommend` via
>    `MeasureMaxUsefulHalfSpan`. It needs **no cross-round state and no `A3` re-derivation**, which makes it the
>    cheapest of the three (~45 m + a 3-cell re-run + the 42 m gate) and is precisely why the wave that thought
>    of it did not select it. **The next wave pre-registers the choice among three.**

**[F81](followups.md)** — append: the floor F81 shipped **is** reached in the product on a re-swept round, not
only at round 0 of a session. `CaptureNewSweepAsync` (`:5012`) re-captures and carries `recaptureStepSize`
forward, so the wizard's `Capture new sweep` path has genuine cross-round sweep state. The `Continue` and
`Re-optimize` paths do not — they call `LoadRunStampedAsync` and re-optimize already-captured frames — so on
those two the floor is inert after round 0, and that half of the original claim stands.

**[F94](followups.md)** — append: wave 29's design §10 is the first in the series to publish its own outcome
space as a table, and **every scorer re-derived it in code and agreed with the published counts**: `W29-S`
**8 / 0 uncovered**, `W29-R` **4 / 0**, `W29-L` **8 / 0** — matching design §10's three tables exactly — and
`V29`, which §10 declared as inheriting wave 28's tree without a count, re-derived **12 / 0** including the
`A=None` empty-population region. The two logically-unreachable regions (`R-INCOHERENT`, `L-INCOHERENT`) are
present, asserted empty, and checked against the measured numbers (`c = 1 ≤ k = 18 is True`; `L-1 ∧ L-2 =
False`). **The remedy works for outcome coverage.** It does **not** cover two adjacent failures found this wave: a clause whose *population* is
empty (**F102**) and a clause whose realized value is **neither `True` nor `False`** — `W29-L` ended at
`L-0 = True, L-1 = None, L-2 = None`, a state outside the enumerated boolean cube, yet the scorer still printed
`uncovered: 0` and appended a canned `READING:` line saying *"the control did not hold"* **when `L-0` in fact
HELD**. The tree was total over booleans and the run was not a boolean. **Trees must enumerate `None` as a
clause value, and a verdict's explanatory text must be derived from the clause values rather than templated per
verdict name.**

**[F95](followups.md)** — append: **the remedy's first half works and was proved this wave.** Wave 29's plan
ordered instruments (Step 1) before the blocking pre-flight (Step 2); the last instrument was written at
`17:01:28Z`, the pre-flight ran at `17:35:55Z` over a **populated root of 7 files**, and returned `V-CLEAN`
with `SELFTEST V29 27 of 27`. Wave 28's equivalent certified **one file**. **The second half did not run** —
the closing `V29` (`vdrift_w29_FINAL.txt`) has no artifact, against design §11's *"NOT cut, at any budget."*
Both halves are needed and the series has now demonstrated one of them.

**[F21](followups.md)** — append wave 29's per-step estimate vs actual (§10). Steps 1, 2, 4, 5 and 6 all landed
**under**; the only two that overran were the two priced from instinct rather than from a measured rate
(fingerprint sweeps, §10). Measured constant for future waves: **the six declared read-only roots are ~41.8 GB
/ 2 452 files and hash at ~61 MB/s, i.e. ~12 m per sweep, ~24 m for a two-sided fingerprint.**

**`docs/synthetic-af-bank-results-table.md`** — still owed from wave 28: the `K` column from `truthModel`
(`max(hfrMin, hfrMinEffective)`), the note that `D01`/`D02`/`D03` are the only three floored by the generator,
and **no 2–4 px band claim below the band** (`W28-B` = `B-SPLIT`). **Not paid this wave.** ~10 m.

---

## §13 — WAVE 30, priced and goal-scored

| # | item | price | compute | goals | why |
|---|---|---|---|---|---|
| **1** | **Pay wave 29's four small debts**: the closing `V29`, `P-D08` at `n = 3`, the three scorer self-test captures, and the results-table `K` column | **~20 m** | ~2 m | instrument, **3** ✓ | All four were owed, all four are cheap, and two of them (`P-D08`, the closing `V29`) were explicitly refused-to-cut by the design that then cut them. **Starting a wave by paying the previous wave's named debts is the only thing that stops the list growing** |
| **2** | **`D01` localised properly** — a 4-cell arm on the binary already on disk, with `MaxDistortion` **lowered** (0.5 → 0.3, 0.2), and a rule whose statistic is the **per-gate flow** (F99), not `FN_total` | **~50 m** | **~13 m** | **3** ✓✓ | The wave's largest open question, now with a corrected direction (F98) and a stated hypothesis that can be wrong (F99: `TooDistorted` + `LowSensitivity` bind jointly). `MinStarBoundingBoxSize` landed at **6** against a shipped default of **5** on the rig with the smallest stars — goal 3 verbatim |
| **3** | **Choose among F82's three fixes, pre-registered before reading**, and price the winner | **~40 m** | — | **2** ✓✓ | `RULE W29-S` and `W29-R` have delivered everything a choice needs: the carrier exists (`S-b` FALSE), the reversal condition is evaluated (`c = 1`), and `S-c` holds so candidate (3) is live. **The `RULE D20` fence forbade choosing in the wave that proposed (3); it does not forbid the next one.** Ship price is separate: fix (3) ~45 m + 3-cell re-run + **42 m gate** |
| **4** | **`MaxDistortion`'s axis and name** (F98 part 2) | ~20 m + **42 m gate** | — | **3** ✓✓ | The top ~21 % of a searchable axis returns zero detections for round stars. Touches plugin code, so the gate is owed — predict `G-PASS`, 8 of 8 bit-identical |
| **5** | **Clause populations** (F102) — every clause prints `candidates: N   findings: M` and refuses on an unexpected `N = 0`; and trees enumerate `None` (F94 append) | ~25 m | — | instrument | Two waves of vacuous clauses (F101, F102) and one verdict that landed outside its own tree. **This is the cheapest structural fix on the list** |
| **6** | `S27-1` and wave 27's three mutants | ~20 m | — | instrument | **Open three waves running.** Cut again and it should be formally withdrawn rather than re-listed a fifth time |

**Recommended wave 30 = items 1, 2 and 5** — ~1 h 35 m, ~15 m compute, no C# and therefore no gate. Item 3 is
the natural wave 31 and should be its own pre-registration; item 4 is a genuine ship and owes the 42 m gate.

**Run hard stop: `2026-08-14T00:45:17Z`.**

### Is the register thinning? Yes, and it should be said

**Of the eight entries this wave produces, five are about the instruments and three are about the product** —
and of those three, one (**F97**) is a *reduction* in an existing finding's scope, one (**F98** part 2) is a
20-minute parameter-range fix, and one (**F96**) says the series' own measuring stick is self-referential.
**Wave 29 found no new product defect.** It found that a known one is rarer than believed, that its fix does
reach a user after all, and that three of the checkers have been passing vacuously.

That is a real and useful wave, and it is also a pattern worth naming: **the marginal finding in this series is
increasingly about the apparatus rather than about HocusFocus.** The bank's coverage hole (no real
under-sampled dataset, [F35](followups.md)/[F43](followups.md)) is unchanged after ten waves, §7 has now spent
the blindness that remained on `/mnt/d/hf_w25/after`, and `RULE W-UNEXERCISED` has twice shown that a full
paired arm buys a second `SAME 13`.

**The charter's stop condition is *"two consecutive waves producing no finding worth a register entry."* That
condition is NOT met** — waves 28 and 29 both produced product findings (`B-SPLIT`/`N-RECOVERS`; F97/F98).
**But the honest forward-looking statement is that items 2 and 4 above are the last two product questions this
bank can answer without new data**, and once they are closed, the next wave that cannot name a product question
should stop rather than audit its own instruments a fourth time.

---

## §14 — HANDOFF (design §11's cut 4: folded here rather than written separately)

**Branch** `ghilios/synthetic-af-bank-followups-wave29`, **PR #197**, stacked on #196. Commits: `544f2b2`
(pre-registration — **its message contains the false §1(b) claim and cannot be edited**), `80e42c5`
(the CORRECTION), plus this results commit.

**Read first, in this order:** the design's CORRECTION block (above its §1) · §1 and §2 of this document ·
F96, F97, F82's amendment.

**Do not repeat:** (a) taking the fingerprint after the measurements — it cost `W29-FP` its verdict on four of
six roots; (b) pricing a sweep from instinct — the constant is ~61 MB/s, ~12 m per sweep; (c) writing a
blindness ledger without checking it against your own instruments (F100); (d) inheriting a fixture pin
(F103) — `hf_w28` is next to expire.

**Standing:** never push `develop`; commit with `322725+ghilios@users.noreply.github.com` as author **and**
committer; pass scorers WSL paths; `--out` mandatory; `--rule` asserted against the file's own declared name;
one `TestApp.exe` at a time; no directory rebuilt mid-wave.
