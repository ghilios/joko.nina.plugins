# Synthetic AF bank — followups wave 15 (results)

Design: [`docs/synthetic-af-bank-followups-wave15-design.md`](synthetic-af-bank-followups-wave15-design.md).
Plan: [`plans/synthetic-af-bank-followups-wave15-plan.md`](../plans/synthetic-af-bank-followups-wave15-plan.md).
Wave 14: [`docs/synthetic-af-bank-followups-wave14-results.md`](synthetic-af-bank-followups-wave14-results.md).
Standing authorisation: [`docs/waves14-21-autonomous-prompt.md`](waves14-21-autonomous-prompt.md).
Register: [`docs/followups.md`](followups.md).

> ## PROVENANCE
>
> | input | value |
> |---|---|
> | tree | branch `ghilios/synthetic-af-bank-followups-wave13`. Pre-registration `8ac26ac`, checkpoint `dd9b0cd`. **Wave 15 ships NO code**; the tree did not move during the wave and `git status` is clean |
> | binary | `D:\hf_w15\exe` — **ONE binary for the whole wave**, never rebuilt. `TestApp.dll` sha256 `a2693968d584ecd9e2d11dbedb77b5c4ee70d3957ff114be3055c4e0bff4850f` · `NINA.Joko.Plugins.HocusFocus.dll` sha256 `4c32abe70deb5bd537884c7733773ad370c6b7525aa6518b840b999fef432912` · `BuildId` **`df3a867d6fc74204a0dbfc75060b9a96`**. **A SIXTH binary** |
> | detector | `DetectorVersion` **2**, read as a **FIELD** on all 8 gate landings and on all 40 item-B landings. No `strings` probe is quoted anywhere ([F66](followups.md)) |
> | settings **S0** | `D:\hf_w11\pinned_settings_w11.json` md5 **`a67ffc06164c81613aef5c4f8324b9b8`** — `MaxOutlierRejections` explicitly **0** |
> | settings **S1** | `D:\hf_w13\pinned_settings_w13_mor1.json` md5 **`89e14ef884dbb31d0502182bd89d584e`** — `MaxOutlierRejections` **1**. Driver-level one-key assertion ran before anything else and passed: *"top-level diffs none; Options diffs `['MaxOutlierRejections']`"* |
> | profile | `astrodet` `ce3f3e63-8fd3-4b72-a0ca-d90db9441382`, pinned on **every** arm, one distinct value across all 48 landings |
> | banks | `D:\SyntheticAutofocusBank` (20 datasets, `renderRequest.OptimalFocuserPosition` = **truth**), `D:\Autofocus Bank` (19 runs, gate only) |
> | F15 control | **42 of 42 landings BYTE-IDENTICAL** after the gate, both arms and both scoring passes. `--update-run-folder` passed **nowhere** |
> | suite | **3781** — see §5.1 |
>
> **ONE BINARY, AND IT HELD.** Wave 14 needed two because item 2's Stage B code did not exist when the gate binary
> was frozen. Wave 15 ships nothing, so a single build served RULE G15 **and** all four of item B's passes, and the
> gate is a control on the same binary every arm ran. That is the improvement wave 14's provenance block asked for.
>
> **No fan-out.** Every arm is `--profile-id`-pinned and a pinned arm cannot fan out at all (wave 11's K3); the
> authorisation is worth 1.33×, not 4× ([F60](followups.md)). Wave 5's φ table is quoted nowhere.

## Status of this document

| item | state |
|---|---|
| **RULE G15** — the gate | **PASS, 8 of 8 to 6 dp, bit-identical to all sixteen digits, on a SIXTH binary.** §1 |
| **item A** — F65/F66(a), repair V4 and re-score wave 14 | **V4′ PASSES, 585 of 585 triples, 0 violations, 2 NOVEL-CONSENSUS.** Published as a corrected diagnostic that **names no rung**. **RULE F14 stays at NO VERDICT permanently.** §2 |
| **item B** — F63(a) + F62 at the landing level | **RULE L15 returns NO VERDICT. The failing gates are G-c and G-d.** Both arms ran complete; every clause below is a diagnostic. §3 |
| **the G-c failure itself** | **INVESTIGATED, and it is not what the gate's name says.** G-c is confounded by a pipeline disagreement that predates the converter by two waves. §3.2 |
| **item 2 of waves 13/14** — the nine UI changes | **NOT ATTEMPTABLE.** Sixth wave. §4 |

---

## §1 — RULE G15: the coordinate system on a sixth binary

`/mnt/d/hf_w15/gate_w15.sh`, **01:52:26Z–02:33:37Z = 41 m 11 s**, sequential, `--per-run --max-evals 250`,
`--settings D:\hf_w11\pinned_settings_w11.json` **and** `--profile-id ce3f3e63-…` both named in the script
header, machine quiet. Population asserted **before** scoring: `aggregate_summary.json produced: 8 expected: 8`.

| run | `BestJ` (exact) | 6 dp | expected | | bit == K8 | `BaselineJ` | wave 11 |
|---|---|---|---|---|---|---|---|
| `toml999` | 0.9957838768299878 | 0.995784 | 0.995784 | **MATCH** | **YES** | 0.983477 | ok |
| `CWhiteFocus` | 0.9960675916058808 | 0.996068 | 0.996068 | **MATCH** | **YES** | 0.994320 | ok |
| `uneven` | 0.9963677194179505 | 0.996368 | 0.996368 | **MATCH** | **YES** | 0.991794 | ok |
| `muggsie` | 0.9971948738498605 | 0.997195 | 0.997195 | **MATCH** | **YES** | 0.993288 | ok |
| `mccomiskey` | 0.9767460801208465 | 0.976746 | 0.976746 | **MATCH** | **YES** | 0.846082 | ok |
| `D18_m24_deep_shed` | 0.9998815090506263 | 0.999882 | 0.999882 | **MATCH** | **YES** | 0.997405 | ok |
| `D19_cygnus_deep_shed` | 0.9994870586135448 | 0.999487 | 0.999487 | **MATCH** | **YES** | 0.999187 | ok |
| `D20_m24_bright_control` | 0.9997378027339423 | 0.999738 | 0.999738 | **MATCH** | **YES** | 0.999454 | ok |

> **RULE G15's one clause — *all eight reproduce to 6 dp; a partial reproduction is a FAILURE* — PASSES, 8 of 8,
> and every one is bit-identical to all sixteen digits.** Wave 11's eight values have now reproduced across **six**
> binaries and two settings files. [F41](followups.md)'s free control (`BaselineJ`) passes 8 of 8.

**The free controls, as FIELDS, diffed rather than asserted.** `BuildId` `df3a867d6fc74204a0dbfc75060b9a96` — one
value across all eight and **novel** against waves 11 (`5cb7e474…`), 12 (`103d61c4…`), 13 (`62334f10…`) and 14
(`084e3485…`), so a build did happen. `DetectorVersion` **2 ×8**. `ProfileId` `astrodet (ce3f3e63-…)` ×8, checked
by containment **and** by cardinality. `FitInputs` one distinct value,
`MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid`.
`ConcurrencyCheck` `exclusive` on all eight **read across the whole arm** — one landing's `exclusive` proves
nothing, because `WaitOne(0)` is won by exactly one of N contenders.

**Wave 14's Stage-B binary (`D:\hf_w14\exe_floor`) has no recorded `BuildId`** and could not be listed: it ran
`af-fit` only, and `af-fit` writes no landing, so nothing ever stamped it. Wave 15's id is recorded above so wave
16 can carry it.

### §1.1 The gate scorer was demonstrated in BOTH directions before being quoted — F66(a) as a wave rule

`prov_w15.py` is `prov_w14.py`'s repaired form carried forward verbatim, and the repair is the reason it is
carried. Its `--self-test` runs both directions on the wave's own arm:

| direction | input | result |
|---|---|---|
| 1 | the real 8-landing gate arm | **PASS** |
| 2 | a copy with **one** landing's `ProfileId` rewritten (`muggsie`: `astrodet (ce3f3e63-…)` → `Default (b10b1d6d-…)`) | **FAIL on BOTH clauses**, naming both values — and the mutation is asserted to have happened before the scorer is believed |

**And it refused to certify itself when pointed at the wrong arm.** Run against *wave 14's* gate it reports
`SELF-TEST **FAIL** — do not quote this check`, because that `BuildId` is on the prior-wave list and direction 1
therefore fails. *A self-test bound to the arm it certifies is the point; one that passes against any input
certifies nothing.*

### §1.2 The F15 control

All **42** bank `optimized_settings.json` files were fingerprinted by sha256 + size + mtime **before** any arm
ran, and re-checked after the gate, after both landing arms and after both scoring passes.

> **42 of 42 BYTE-IDENTICAL. 0 changed, 0 added, 0 removed, 0 could-not-look.**

Second wave running, second wave clean. `--update-run-folder` is passed nowhere in this wave.

Reproduce: `python3 /mnt/d/hf_w12/score_w12.py /mnt/d/hf_w15/gate --rule G15`;
`python3 /mnt/d/hf_w15/prov_w15.py --self-test /mnt/d/hf_w15/gate`;
`python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --check /mnt/d/hf_w15/bank_landing_fingerprint_BEFORE.json`.
All three take **WSL** paths; a Windows path yields eight `UNEVALUATED` and looks exactly like a failed arm.

---

## §2 — Item A: V4′, and RULE F14's CORRECTED DIAGNOSTIC

> ### THE HONESTY BANNER — read this before any number below is quoted anywhere
>
> **1. THIS IS A RE-STATEMENT UNDER A CORRECTED GATE, NOT A NEW TEST.** Every rung was measured in wave 14 and is
> on disk. Every decision clause (C1–C5) and every threshold is wave 14's, unchanged. **No `TestApp` time was
> spent and no new measurement was made.**
>
> **2. THE OUTCOME WAS ALREADY PUBLISHED**, in wave 14's results doc, before `score_f14_w15.py` existed. The
> design's DISCLOSURE section enumerates every number this re-score would produce. Nothing here can surprise its
> author, which is the definition of the thing a pre-registration exists to prevent.
>
> **3. THE REPAIR WAS FORCED BY A PROVED-VS-TESTED MISMATCH, NOT BY DISLIKE OF THE ANSWER** — checkable by a third
> party from wave 14's committed design text without re-running anything.
>
> **4. NO NEW EVIDENCE IS CREATED BY ITEM A. The register must never cite it as independent confirmation of
> anything.** Any register entry touched by item A carries the words *corrected diagnostic, wave 14 data*.

### §2.1 The defect, stated as proved-vs-tested

Wave 14 **proved** (design §2.6) that a uniform scale floor multiplies every `z_i` in a round by one constant, so
the argmax never moves and each candidate model's floored rejection set is a **prefix** of its unfloored set;
production removes the **intersection** over the four Hybrid models; hence `consensus^f_B ⊆ consensus_B`.
Wave 14 **tested** (V4) the strictly stronger *"Stage B's production row must equal one of that run's wave-13
printed rows at budget ≤ B."* **The intersection of prefixes is not a prefix of the intersection**
([F65](followups.md)), and `Panos_attempt01` refutes the tested form on 2 rows while the proved form holds on
585 of 585. V4 additionally compared rows containing `NaN` with plain equality, so a row could **fail to match
itself** ([F66](followups.md)) — 2 of its 4 reported failures were byte-identical to wave 13's.

### §2.2 V4′, self-tested before it touched wave-14 data

`score_f14_w15.py --self-test`, on constructed input, returns **every** outcome:

```
PASS  PASSES a suppressed-but-lawful rung
PASS  F65's novel-but-subset consensus is REPORTED, not failed
PASS  FAILS (i) a position wave 13 never rejected | (ii) more rejections than the budget
PASS  FAILS (iii) budget 1's rejection vanishes at budget 2 | (v) same set, different sigma
PASS  NaN-SAFE: a row containing NaN matches ITSELF (wave 14's V4 could not)
PASS  a missing summary is COULD NOT LOOK, not 'no violations'
```

### §2.3 The validity gates, each with its pre-registered threshold

| gate | pre-registered threshold | measured | verdict |
|---|---|---|---|
| **V1** — control rung `f = 0.00` | 0 field differences | **0** | **PASS** |
| **V2** — population by count | 39 of 39 readable on each of six rungs | **39 of 39 ×6** | **PASS** |
| **V3′** — Stage A inertness (reads `rounds.tsv`, the file its producer actually writes) | all rounds INERT at `f = 0`, 0 INCONSISTENT | **72 of 72 rounds, all INERT, 0 INCONSISTENT** | **PASS** |
| **V4′ (i)(ii)(iii)(v)** | 0 violations on **585** triples; population 585 / 195 or UNEVALUATED | **0 violations; 585 of 585 triples; 195 of 195 budget-0 rows empty both sides** | **PASS** |
| **V4′ NOVEL-CONSENSUS** | reported count, **never a bar**; expected **≥ 1**; zero would be a finding about the scorer | **2** | reported |

The 2 NOVEL-CONSENSUS rows are `Panos_attempt01` at `B1.00`, budgets 2 and 3 — `[40396]` matching none of
`[[], [], [38396, 40396]]`. **Named, never a bar**, because that is F65 and not a defect. Wave 14's V3 looked for
a `stageA_report.txt` its own producer never creates; V3′ reads `rounds.tsv`.

### §2.4 The decision clauses, re-run verbatim (wave 14's numbers, reproduced)

| rung | max ρ | C1 | `N_fire`@1 | @2 | @3 | blast@1 | C3 (`caboose` b3) |
|---|---|---|---|---|---|---|---|
| A0.00 | 45.1768 | fail | 7 | 13 | 13 | 0 | DOES NOT CONTAIN |
| A0.25 | 1.1304 | fail | 3 | 5 | 5 | 4 | CONTAINS-BLUNTLY |
| **A0.50** | **1.0000** | **PASS** | **2** | 2 | 2 | 5 | **CONTAINS-BLUNTLY** |
| A1.00 | 1.0000 | PASS | **0** | 0 | 0 | 7 | CONTAINS-BLUNTLY |
| B0.50 | 45.1768 | fail | 7 | 13 | 13 | 0 | DOES NOT CONTAIN |
| B1.00 | 45.1768 | fail | 7 | 11 | 11 | 0 | DOES NOT CONTAIN |

C4 vetoes nowhere: median `|Δe|` **0.00000** at every rung and budget, max **0.00993** against a **0.10** floor.
`Panos_attempt01` is UNEVALUATED **by name** on the ρ clause at `A0.00` and `B0.50` (degenerate σ fit), never
silently dropped.

### §2.5 RULE F14 STAYS AT NO VERDICT, PERMANENTLY

> **The controller accepted the pre-registration's recommendation over its own brief. This is not an override and
> it is not a re-decision: the rule's outcome is exactly what wave 14 published.**
>
> The corrected diagnostic **visibly points at `A0.50`** — the only rung satisfying C1 ∧ C2 ∧ C3 with no C4 veto —
> and **saying so is part of the honesty, not a breach of it.** What is refused is the step from *"the diagnostic
> points here"* to *"the rule recommends this"*. Three reasons, in increasing weight:
>
> 1. **RULE F14's own guard cannot bind a reader who has already seen wave 14 §3.7.** *"Never resolved by
>    preferring whichever rung agrees with something else"* only means something to a reader who does not yet know
>    which rung agrees with what. That condition is permanently gone.
> 2. **V4 was not the rule's only defect.** Wave 14 recorded that the branch built to consume its strongest
>    measurement — §3.9's SEM audit — is **unreachable**, gated on *"C1/C3 hold only at rungs failing C2"* which
>    `A0.50` falsifies. **Repairing one defect in a two-defect rule yields a rule that is still wrong**, and a rule
>    that cannot consume its own best evidence should not issue verdicts.
> 3. **The precedent costs more than the answer.** Wave 14's Lesson 3: *"the moment a pre-registration may be
>    re-decided after the data, it stops being one."* A wave that repairs a failed gate and immediately harvests a
>    verdict teaches the next wave to do the same.
>
> **V4′ is applied PROSPECTIVELY** by the wave that measures the SEM-unit floor (wave 14 §7.3, ~2 h), which needs
> exactly this clause on a **new** population. **What this costs:** F45(b) stays open with no named rung for at
> least one more wave. That is the correct price and it is small.

**Item A names no rung and recommends nothing.** Any register text drawn from it says *corrected diagnostic,
wave 14 data, post hoc*.

Reproduce: `python3 /mnt/d/hf_w15/score_f14_w15.py --self-test`, then
`python3 /mnt/d/hf_w15/score_f14_w15.py --root /mnt/d/hf_w14 --w13 /mnt/d/hf_w13 --stagea /mnt/d/hf_w14/stageA --out /mnt/d/hf_w15/f14_corrected`.
Zero `TestApp` time.

---

## §3 — Item B: does the landing move, and is either landing BETTER?

**The question.** Wave 13's D5 found the optimizer's **landing** moves on **6 of 8** runs between
`MaxOutlierRejections` 0 and 1 at `--max-evals 250`, and said, correctly, that it could not say whether either
landing was *better*: *"`BestJ` is computed by the fit under test."* That is [F62](followups.md) at the landing
level and it had never been resolved. Wave 14 made it a shipped question: the code default moved 1 → 0 and four
of this machine's nine profiles have the key absent and therefore move.

**Scope: the 20 synthetic datasets, because that is the only place truth exists.**
`renderRequest.OptimalFocuserPosition` is the generator's true focus, it is out of sample for both arms, and
neither arm can move it.

**The arms.** `land_w15.sh`, **LAND_START 02:35:29Z … LAND_DONE 04:17:54Z**, 20 datasets × 2 arms,
**paired-interleaved** in the pre-registered order `D18, D19, D20, D01…D17`, one `TestApp.exe` at a time,
`< /dev/null` on every invocation. Both arms complete: **20 of 20 landings each, 0 datasets dropped.** The
scoring passes ran at **04:18:45Z–04:22:30Z** (arm 0) and **04:22:32Z–04:26:14Z** (arm 1), 20 of 20 each.

### §3.0 RULE L15 clause by clause, each pre-registered threshold beside its measured value

| clause | pre-registered threshold | measured | outcome |
|---|---|---|---|
| **G-a — ONE KEY** | S0 and S1 differ in exactly `MaxOutlierRejections` and nothing else, **or in nothing**, which aborts | top-level diffs **none**; `Options` diffs **`['MaxOutlierRejections']`**, `'0'`→`'1'` | **PASS** |
| **G-b — POPULATION BY COUNT** | 20 of 20 landings per arm **and** 20 of 20 scored `af-fit` runs per arm, each with an `af_fit_points.csv`; `Settings:` line on the arm's own converted path | arm 0: landings **20/20**, summaries **20/20**, points **20/20**, converted path **20/20**. arm 1: identical | **PASS** |
| **G-c — THE CONVERSION TOOK** | **20 of 20 TOOK per arm**. Any `DID-NOT-TAKE`, `AMBIGUOUS` or `COULD-NOT-LOOK` fails the gate by name | arm 0: TOOK **13**, DID-NOT-TAKE **0**, AMBIGUOUS **4**, COULD-NOT-LOOK **3**. arm 1: TOOK **13**, DID-NOT-TAKE **0**, AMBIGUOUS **6**, COULD-NOT-LOOK **1** | **FAIL** |
| **G-d — CONVERTED FILES DIFFER IFF LANDINGS DIFFER**, and only in `Options.OptimizedSettingsJson` | consistent on 20 pairs | 19 consistent; **1 problem**: `D13_apo200_1800mm` — *"converted files differ but the landing did not move"*. No pair differs outside the snapshot key | **FAIL** |
| **G-e — GATE REPRODUCTION, FREE** | arm 0's `D18/D19/D20` landings reproduce RULE G15's on the curated knobs and the recommended step | **3 of 3 identical** — and stronger than the clause asks (§3.4) | **PASS** |
| **L15-S — DID THE LANDING MOVE?** | no threshold; reported as `M of 20` beside D5's 6 of 8 | **19 of 20** (1 unmoved, 0 UNEVALUATED by name) | diagnostic |
| **L15-P (a) DIRECTION** | `wins(arm) ≥ max(6, ⌈0.75·M⌉)` = **15 of 19**, **and** `wins(other) = 0` | arm 0 **7**, arm 1 **7**, exact ties **5** | not met, by either arm |
| **L15-P (b) MATERIALITY** | median over the movers of \|Δe\| **≥ 0.10 step** | median **0.00167** step, max **0.02584** step | not met |
| **L15-P verdict** | DOMINATES iff (a) and (b) for the **same** arm; otherwise NO DOMINANCE | — | **NO DOMINANCE** |
| **L15-N — each arm at its own budget** | REPORTED, never a verdict | over the movers: arm 0 **8**, arm 1 **7**, ties **4** | diagnostic |
| **L15-M — magnitude, refuses to vote** | median, max, per-dataset table | median **0.00167**, max **0.02584** step | diagnostic |
| **L15-B — the baseline clause** | reported; **an arm reaching `e` = 1.0 step is a new register finding** | max `e` anywhere in either arm = **0.02921** step. **The alarm did not fire, by a factor of 34** | diagnostic |

> ### **RULE L15: NO VERDICT.** The failing gates are **G-c** and **G-d**, named.
>
> Every clause below the gates is a **diagnostic**, not a result, and is written here as one.

### §3.1 The blocker item B had to route around, restated because it is the wave's most transferable engineering result

The scoring pass has to run the detector at a **landing's** settings, and `af-fit --settings <landing>` fails
**silently**. `HarnessSettingsStore.ResolveAt` deserialises to `HarnessSettingsFile` and reads `loaded.Options`;
a landing (`optimized_settings.json`) is a flat `OptimizedStarDetectionSettings` DTO with **no `Options` member at
all**, so it yields an empty option bag with `PixelSizeMicrons = 0`, `FocalLengthMm = 0`, and the run silently
uses code defaults. **And the field that should catch it cannot:** `HarnessSettingsStore.Fingerprint` appends
nothing when `Options` is absent, so **two different landings of one dataset hash identically**. `af-fit` does not
print a fingerprint at all. *An instrument that is not connected reports perfect agreement.*

Three proposed follow-ups were wrong and the pre-registration corrected all three in source rather than by
assumption: `--params-from` is a substring filter on `*_star_detection_result.json` paths **inside `--af-run`**
and cannot take a landing; a knob-lifting converter would drop exactly [F59](followups.md)'s six
validating-setter knobs *and* be defeated by `UseAdvanced=False`, under which `ConfigureSimpleSettings()` →
`DerivePresetSettings()` overwrites precisely the knobs the optimizer tunes — **both arms would collapse onto the
preset baseline and item B would measure a perfect null it manufactured itself.**

**The converter is two keys through the production Accept path:**

```
Options["OptimizedSettingsJson"] = <the landing file's RAW TEXT, byte for byte>
Options["UseOptimizedSettings"]  = "True"
Options["UseAdvanced"]             stays "False"   <-- load-bearing
```

`ConfigureSimpleSettings()` then takes the `UseOptimizedSettings && HasOptimizedSettings` branch →
`ApplyOptimizedSnapshotToLiveProperties()`, which derives the preset baseline and applies all 25 curated knobs on
top. Because the knobs ride inside the snapshot, F59's six dropped keys are carried and the converter never needs
a knob's option-key spelling. `convert_landing_w15.py --self-test` converts, round-trips, and **every refusal
refuses** — a landing passed as the base, `UseAdvanced=True`, an unrecognised key, a missing curated knob.

### §3.2 THE G-c FAILURE, INVESTIGATED — and it is not what the gate's name says

The star-count control was demonstrated in **both directions** on `D18_m24_deep_shed` for ~30 s before any arm
ran, and passed 9 of 9 each way (`good` → TOOK, `mutant` with `UseOptimizedSettings` flipped back → DID-NOT-TAKE).
It then returned **TOOK on 13 of 20** per arm. This section establishes what that means and what it does not.

#### (1) `COULD-NOT-LOOK` is the settings BITING, and the control cannot say so

All four `COULD-NOT-LOOK` reads are the same shape: *"focuser positions do not line up: af-fit 7, current 9,
optimized 9, common 7."* The af-fit logs name the reason on the line above the fit:

```
D12_c14_585_afbin2, arm 0:
  pos 20564: 0 stars (skipped - Y would be 0)
  pos 20423: 2 stars, medianHFR=12.0277, ...
  ...
  pos 19577: 2 stars, medianHFR=12.0092, ...
  pos 19436: 0 stars (skipped - Y would be 0)
```

`AfFitDiagnosticRunner` drops a position when `hfrs.Count <= 1` (`AfFitDiagnosticRunner.cs:175`). **In all four
cases the dropped positions are exactly the two sweep extremes** — `D12` arm 0 and arm 1 dropped `{19436, 20564}`
from a sweep `[19436..20564]`; `D14` arm 0 and `D17` arm 0 dropped `{11760, 12240}` from `[11760..12240]`. The
extremes are the most defocused frames and therefore the first to starve.

> **So on those datasets the landing's settings were aggressive enough to empty the sweep wings — which is the
> settings taking — and the control's response is to say it could not look. On exactly the datasets where the
> settings bite hardest, the control is anti-correlated with what it measures.**

That is a real defect in the clause and it is worth stating plainly: G-c's `COULD-NOT-LOOK` state was designed
for *"the artifact is missing or unreadable"* and it fires instead for *"the intervention worked."*

#### (2) `AMBIGUOUS` is TWO PIPELINES, and the decisive test needs no new run

`DID-NOT-TAKE` was **0 on every dataset in both arms**. Nothing matched `currentStarCount` either, so *"the file
was written and ignored"* is not what happened — that shape did not occur once in 40 runs.

The two counts come from **two different code paths**:

| | `optimize_result.csv` `optimizedStarCount` | `af_fit_points.csv` `Stars` |
|---|---|---|
| producer | `RunEvaluationData.EvaluateAndFitAsync(result.BestParams).Metrics.FrameStarCounts[i]` = `FrameDetectionResult.StarCount` (`RunEvaluationData.cs:675`) | `img.DetectAsync(detector, baseParams).DetectedStars.Count` (`AfFitDiagnosticRunner.cs:173-174`) |
| detector entry point | `HocusFocusStarDetection.BuildDetectionContext` + `GateAndMeasure` through `HocusFocusSplitFrameDetector` (`RunEvaluationLoader.cs:297`) | `StarDetector` directly, on an `IRenderedImage` from `DetectionSource.LoadAsync` |
| params object | `result.BestParams`, the `StarDetectorParams` the search produced | `HocusFocusStarDetection.BuildStarDetectorParams(options)`, rebuilt from `StarDetectionOptions` **after** the 25-knob DTO round-trip, then `ModelPSF = false` |

**They are not the same number, and the disagreement predates this wave by two waves.** Wave 13 ran `af-fit` on
all 20 synthetic datasets at exactly S0 — the same settings whose evaluation wave 15's `optimize` records as
`currentStarCount`. No converter is anywhere in that comparison. Scoring wave 13's `af_fit_points.csv` against
wave 15's `currentStarCount`:

| | datasets |
|---|---|
| wave-13 af-fit ≠ wave-15 `currentStarCount` at the **same pinned base**, no converter | `D08`, `D09`, `D10`, `D12`, `D14`, `D15`, `D17` |
| wave-15 G-c non-TOOK, **arm 0** | `D08`, `D09`, `D10`, `D12`, `D14`, `D15`, `D17` |
| wave-15 G-c non-TOOK, **arm 1** | `D08`, `D09`, `D10`, `D12`, `D14`, `D15`, `D17` |

> **The three sets are EQUAL.** G-c returns non-TOOK on exactly the datasets where the two pipelines already
> disagreed at the base, with no landing, no snapshot and no converter involved. **G-c is not measuring whether
> the conversion took. It is measuring whether `af-fit`'s detector and the optimizer's evaluator agree on a given
> dataset — and they disagree on 7 of the 20, as a fixed property of the dataset.**

The disagreement has the same signature at the base and after the conversion: `af-fit` finds systematically
**fewer** stars, worst at the sweep wings and near parity at focus.

```
D08_c11_2800mm, at the pinned base S0 (wave-13 af-fit vs wave-15 currentStarCount):
  13672   6 / 12  (0.50)    13918  50 / 69  (0.73)    14164  26 / 33  (0.79)
  13754  11 / 23  (0.48)    14000  68 / 83  (0.82)    14246  12 / 23  (0.52)
  13836  25 / 31  (0.81)    14082  52 / 69  (0.75)    14328   5 / 11  (0.46)
```

**This also explains why STEP 0 could not have caught it.** STEP 0 ran on `D18_m24_deep_shed` — a member of the
*agreeing* set. The demonstration was sound and it sampled the wrong axis: it varied the thing the control tests
(settings applied / not applied) and held constant the thing that actually decides the control's answer (which
dataset). *A control demonstrated on one member of a population is not demonstrated on the population.*

#### (3) A second control, free and already printed, DOES transfer — 40 of 40

`HarnessSettingsStore.SimpleModePresetOverrides` hands the file's own option bag to a real
`StarDetectionOptions` instance and diffs the bag afterwards, so its WARNING line prints **the value the live
options object holds after `InitializeOptions`**, measured rather than listed. It is printed on every `af-fit`
run in this wave, unconditionally, and nobody had a reason to read it as a control before:

```
D18_m24_deep_shed, arm 0:
WARNING: ...has UseAdvanced=False... Overwritten by the presets:
  BrightnessSensitivity 10→14.666666666666664, MinHFR 1.2→0.7, NoiseReductionRadius 4→3,
  StarClippingMultiplier 2→0.25, StructureLayers 4→5.
```

Every "after" value is the landing snapshot's value for that knob.

> **Across all 40 runs (20 datasets × 2 arms): 205 of 205 overridden knobs equal the landing snapshot's value.
> Zero mismatches, zero unreadable, including on all seven datasets G-c could not certify.**

**And the DID-NOT-TAKE hypothesis is decisively falsified by the same line.** `DerivePresetSettings()`'s inputs
are `Simple_NoiseLevel`, `Simple_PixelScale`, `Simple_FocusRange` plus the pixel-scale pair — and **all 40
converted files carry one identical `Simple_*` tuple** (they are all built on S0; `distinct Simple_* tuples across
the 40 converted files: 1`). Under *"the file was written and ignored"*, all 40 runs would print the **same** set
of preset values. They print **39 distinct sets** — the only collision is `D13`'s two arms, whose landings are
identical — each equal to its own landing. That cannot happen if the snapshot did not reach the options object.

#### (4) What this establishes, and what it does not

**Established:**

- The landing's knobs reached the live `StarDetectionOptions` on **40 of 40** runs, 205 of 205 differing knobs,
  read out of the production `InitializeOptions` path.
- `DID-NOT-TAKE` did not occur on any dataset in either arm, and is structurally impossible given (3).
- G-c's non-TOOK set is **exactly** the set of datasets on which the two star-counting pipelines already disagreed
  at the pinned base, two waves earlier, with no converter present.

**Not established, and this is the honest limit:**

- Nothing above proves that `HocusFocusStarDetection.BuildStarDetectorParams(options)` carried every one of the
  25 knobs into the `StarDetectorParams` the `af-fit` detector consumed. The options object holding the right
  values and the detector running at them are one function call apart, and that call is exactly what G-c existed
  to close.
- The *cause* of the pipeline disagreement is not identified here. The shape (defocus-graded deficit) is
  consistent with a sensitivity- or size-gate difference; that is a hypothesis, not a result.

> **The honest conclusion: the converter almost certainly worked on all 40 runs, and G-c cannot prove it because
> G-c is not a test of the converter.** The pre-registration's own words apply and are not softened: *an arm
> scored through an unverified converter is worth less than no arm.* **RULE L15 returns NO VERDICT and that
> stands.**

#### (5) What WOULD prove it, and what it costs

| check | what it closes | cost |
|---|---|---|
| **Print `EffectiveSensitivityGate(baseParams)` and the 25 knob values from `af-fit` itself**, beside the existing `Region:` line | closes the last gap in (4) outright: the values the **detector** received, from the detector's own params object, on every run | ~15 lines in `AfFitDiagnosticRunner`, one `TestApp` build, **~45 m** including a re-run of both scoring passes (~8 m of arm time). It is a *code* change, so it needs its own gate re-run |
| **Re-score G-c on the KNOB-DIFF control** (§3.2(3)) instead of on star counts | makes the gate transfer, at zero arm cost | pure Python, **< 5 m**, and it is already computed above |
| **Have `optimize` write the af-fit-path star count beside its own** for one dataset | identifies the pipeline disagreement's cause | ~1 h, and it is a diagnostic for a different wave |

**The second row is the recommendation.** The star-count control is strictly weaker than it looked: it can only
speak on the 13 of 20 datasets where two unrelated code paths happen to agree, and it is silent — in the wrong
direction — on the datasets where the intervention is strongest.

### §3.3 G-d and `D13_apo200_1800mm` — the converter is NOT injecting anything

G-d fired on one dataset: *"converted files differ but the landing did not move."* `D13` is also the one dataset
where L15-S says the landing did not move. Diffing the two landing files:

```
land_mor0/D13_apo200_1800mm/attempt01/optimized_settings.json  vs  land_mor1/.../optimized_settings.json
 DIFF CreatedAtUtc :: 2026-08-10T04:00:36.769287Z || 2026-08-10T04:03:09.3468629Z
 DIFF Provenance   :: {... '--out D:\hf_w15\land_mor0\...', '--settings D:\hf_w11\pinned_settings_w11.json',
                       'SettingsFingerprint': 'c2206c6a03d1', 'FitInputs': 'MaxOutlierRejections=0;...'}
                   || {... '--out D:\hf_w15\land_mor1\...', '--settings D:\hf_w13\pinned_settings_w13_mor1.json',
                       'SettingsFingerprint': '9174c9c908c3', 'FitInputs': 'MaxOutlierRejections=1;...'}
```

**All 25 curated knobs are identical. So are `BaselineJ` (0.9935714032136365), `FinalJ` (0.9962004183074585),
`RecommendedStepSize` (129) and `LandingDetectionKeepFraction`.** The only differences are a wall-clock timestamp
and [F30](followups.md)'s provenance stamp, which records the arm's own `--out` path, its own settings file, that
file's fingerprint, and its `FitInputs` — all four of which *must* differ between arms and are there on purpose.

The converter embeds the landing's **raw text**, so those bookkeeping fields ride into
`Options.OptimizedSettingsJson` and the two converted files differ. Meanwhile `landing_moved()` compares only the
curated knobs and the recommended step. **G-d compares two different notions of "differ" and calls the mismatch a
failure.**

> **G-d is ill-posed, and its "identical" branch is unreachable.** Two landings of the same dataset can never
> produce byte-identical converted files, because `CreatedAtUtc` alone guarantees they differ. So G-d must fire on
> **every** dataset whose landing does not move — and the design's own L15-S expectation (`M` well above 0, not
> `M` = 20) made a non-mover likely. **The design's §4 satisfiability table lists G-d as *"20 pairs | consistent | yes"*; it
> checked that the PASS value was attainable and never checked that both branches were.** That is wave 14's
> Lesson 4 recurring inside the section written to prevent it.
>
> **Nothing about the converter is impugned.** The correct clause is *"the converted files' `OptimizedSettingsJson`
> differ outside `CreatedAtUtc` and `Provenance` iff the landings differ"*, and on that clause the wave passes
> 20 of 20 — the scorer already checks the other half (no pair differs in any `Options` key besides the snapshot)
> and it found **0 problems**.

**And `D13` says something small and real on its own:** identical `BaselineJ` **and** identical `FinalJ` across
the two arms means `MaxOutlierRejections` was inert end-to-end through a 250-evaluation search on that dataset —
the search never once visited a point where the rejection changed `J`.

### §3.4 G-e — the search is DETERMINISTIC at fixed settings, which is stronger than the clause asked

The clause required arm 0's `D18/D19/D20` landings to reproduce the gate's on the curated knobs and the
recommended step. They do — **3 of 3** — and the reproduction is total: the gate's landing and arm 0's landing for
each of the three differ in `CreatedAtUtc` and `Provenance` and **in nothing else**, including `BaselineJ` and
`FinalJ` to all sixteen digits.

> **The design's §3.6 pre-registered the worry that item B *"cannot separate 'the landing moved' from 'the search is noisy'"*,
> and said G-e was the only handle on it. G-e answers it: at `--max-evals 250` on this bank, two runs at identical
> settings land identically, bit for bit.** So L15-S's count is **not** an upper bound inflated by search noise —
> on the three datasets where it can be checked, search noise is exactly zero, and the movement is attributable to
> the knob. Three is still a small control and it is stated as three.

### §3.5 L15-S — the landing moved on 19 of 20 (F63(a) answered at population scale)

Counted on wave 13 D5's own definition: any curated knob, or the recommended step, differs.

> **`M` = 19 of 20.** One unmoved (`D13_apo200_1800mm`), 0 UNEVALUATED by name. Wave 13's D5 measured **6 of 8**
> on a different, mostly-real population.

Typical movements are large in knob terms: `D18` moves **11 fields** (`BrightnessSensitivity` 14.67 → 32.83,
`StarClippingMultiplier` 0.25 → 6.25); `D01` and `D14` move 10; `D07` moves `BrightnessSensitivity` 8.0 → 0.0 and
`StarClippingMultiplier` 2.0 → 7.125; `D16` moves `MinHFR` 1.1375 → 0.3. The smallest mover is `D05` at 3 fields.

> **This result does NOT depend on G-c.** It is computed from the landing files themselves. G-c gates the
> out-of-sample **scoring** pass — it says nothing about whether two `optimize` runs produced different landings,
> which is read directly off `optimized_settings.json`. G-e (§3.4) is its control, and it passed.

### §3.6 L15-P — a dead heat, and it is the pre-registered expectation

Out of sample against `renderRequest.OptimalFocuserPosition`, at **budget 0 fixed for both arms** so the
comparison isolates the landing. `e = |minPos − truth| / step`; `truth` and `step` are wave 13's own numbers,
read from `affit_syn_score.txt` rather than re-derived.

| dataset | moved | `e`(arm 0) | `e`(arm 1) | \|Δe\| | `e`(base, w13) | winner |
|---|---|---|---|---|---|---|
| `D01_ultrawide_40mm` | yes | 0.00111 | 0.00111 | 0.00000 | 0.00111 | = |
| `D02_rich_135mm` | yes | 0.00167 | 0.00000 | 0.00167 | 0.00000 | arm 1 |
| `D03_redcat_250mm` | yes | 0.00063 | 0.00125 | 0.00063 | 0.00063 | arm 0 |
| `D04_esprit_550mm` | yes | 0.00067 | 0.00000 | 0.00067 | 0.00133 | arm 1 |
| `D05_tec140_1000mm` | yes | 0.00286 | 0.00286 | 0.00000 | 0.00057 | = |
| `D06_sparse_1000mm` | yes | 0.00086 | 0.00857 | 0.00771 | 0.00571 | arm 0 |
| `D07_rc10_2000mm` | yes | 0.00545 | 0.01273 | 0.00727 | 0.00364 | arm 0 |
| `D08_c11_2800mm` | yes | 0.00366 | 0.02805 | 0.02439 | 0.01829 | arm 0 |
| `D09_c14_3800mm` | yes | 0.02203 | 0.00254 | 0.01949 | 0.02797 | arm 1 |
| `D10_rc16_3250mm_sparse` | yes | 0.00337 | 0.02921 | 0.02584 | 0.02247 | arm 0 |
| `D11_rc10_585_afbin2` | yes | 0.01091 | 0.01091 | 0.00000 | 0.01455 | = |
| `D12_c14_585_afbin2` | yes | 0.00213 | 0.00071 | 0.00142 | 0.00142 | arm 1 |
| `D13_apo200_1800mm` | **no** | 0.01024 | 0.01024 | 0.00000 | 0.00079 | = |
| `D14_cdk14_2563mm_e47` | yes | 0.00167 | 0.00167 | 0.00000 | 0.01333 | = |
| `D15_cdk20_3454mm_e47` | yes | 0.00156 | 0.01406 | 0.01250 | 0.01406 | arm 0 |
| `D16_esprit550_ha3` | yes | **0.02800** | 0.01667 | 0.01133 | 0.00067 | arm 1 |
| `D17_cdk14_oiii5` | yes | 0.00500 | 0.00333 | 0.00167 | 0.03000 | arm 1 |
| `D18_m24_deep_shed` | yes | 0.00133 | 0.00067 | 0.00067 | 0.00067 | arm 1 |
| `D19_cygnus_deep_shed` | yes | 0.00143 | 0.00429 | 0.00286 | 0.00286 | arm 0 |
| `D20_m24_bright_control` | yes | 0.00133 | 0.00133 | 0.00000 | 0.00200 | = |

> **Over the 19 movers: arm 0 wins 7, arm 1 wins 7, 5 exact ties.** Clause (a) needed **≥ 15** wins for one arm
> and **0** for the other. Clause (b) needed a median `|Δe|` **≥ 0.10 step** and measured **0.00167**; the largest
> `|Δe|` anywhere is **0.02584**, a quarter of the floor. **NO DOMINANCE.**

**§4.1 predicted this with arithmetic before the data existed**, and the prediction being right is itself a result
about the method. The design wrote: *"If the landings behave like the base, `|Δe| ≤ 2 × 0.03 = 0.06 step` and the
0.10-step materiality floor cannot be met… clause (b) can only fire if a landing is materially WORSE, never if it
is materially better."* The measured max is **0.02584**, comfortably inside the predicted 0.06 bound, and (b) did
not fire. **The pre-registration named its own expected null, in advance, with the arithmetic shown — and the
wave was designed not to depend on it.** That is what §4 exists for.

**L15-N**, each arm at its own budget (arm 0 at 0, arm 1 at 1) — what the user would actually get: over the
movers, **arm 0 8, arm 1 7, ties 4**. Reported, never a verdict: it confounds the landing move with the fit
budget, which is exactly why L15-P is scored at a fixed budget.

### §3.7 L15-B — the clause no wave has ever run, and it did not fire

Each arm's `e` against the **un-optimized** pinned base (wave 13's own `e0` column, already on disk). The
pre-registered trigger: *"if either arm's max `e` reaches 1.0 step, that is a new register finding."*

| dataset | arm | `e` | base `e0` | ratio |
|---|---|---|---|---|
| `D16_esprit550_ha3` | arm 0 | 0.02800 | 0.00067 | **41.8×** |
| `D16_esprit550_ha3` | arm 1 | 0.01667 | 0.00067 | **24.9×** |
| `D10_rc16_3250mm_sparse` | arm 1 | 0.02921 | 0.02247 | 1.3× |
| `D08_c11_2800mm` | arm 1 | 0.02805 | 0.01829 | 1.5× |
| `D09_c14_3800mm` | arm 0 | 0.02203 | 0.02797 | 0.8× (better) |

**The ratio is real and the magnitude is not.** `D16`'s step is **15 focuser counts**, so the 41.8× is a vertex
moving from **0.010 counts** off truth to **0.420 counts** off truth. The largest vertex error anywhere in either
arm is `D09`/`D10` at **2.6 focuser counts**; the un-optimized base's largest is **3.3 counts** on the same
dataset. **The maximum `e` over all 40 arm-datasets is 0.02921 step against the clause's own 1.0-step alarm
threshold — the alarm did not fire, by a factor of 34.**

> **This does NOT earn a register finding, and the reason is worth recording.** *"The optimizer's landing is 41.8×
> further from truth than not optimizing"* is a true sentence about two numbers that are both far below one
> focuser count, produced by the scoring pass whose validity gate failed. Quoting it would put a headline ratio
> into the register that a later wave would have to spend an arm to walk back. **What is recorded instead is that
> L15-B ran for the first time in the series, that a landing has now been scored against truth at all, and that
> its pre-registered alarm did not fire on 40 of 40 arm-datasets.**
>
> **The instrument lesson does earn a line:** L15-B's alarm is an absolute floor (1.0 step) but its *table* is
> printed as a ratio, and a ratio of two sub-count numbers is not a magnitude. The clause needs an absolute
> secondary floor before the ratio column is printed at all.

### §3.8 WHICH RESULTS SURVIVE G-c's FAILURE, AND WHICH DO NOT

This is the most useful distinction in the document, and G-c's own scope makes it clean: **G-c gates the
out-of-sample scoring pass, and nothing else.** It says whether `af-fit` ran the detector at the landing's knobs.
Anything read off the landing files themselves never passes through it.

| result | depends on G-c? | status |
|---|---|---|
| **RULE G15** — 8 of 8 bit-identical, sixth binary | no — different arm entirely | **stands** |
| **F15** — 42 of 42 landings byte-identical | no | **stands** |
| **item A / V4′** — 585 of 585, 0 violations, 2 novel | no — wave 14 data, no wave-15 arm | **stands**, as a corrected diagnostic |
| **G-a** — the two settings files differ in one key | no | **stands** |
| **G-b** — 20/20 landings and 20/20 scored runs per arm | no | **stands** |
| **G-e** — arm 0 reproduces the gate on `D18/D19/D20`, bit for bit | no — landing vs landing | **stands**, and §3.4 makes it stronger than the clause asked |
| **L15-S** — the landing moved on **19 of 20** | **no** — computed from `optimized_settings.json` directly | **stands.** This is F63(a) at population scale |
| **`D13`** — one dataset where the knob is inert end-to-end (`BaselineJ` and `FinalJ` identical) | no | **stands** |
| **The converter reached the live options on 40 of 40** (§3.2(3)) | no — read from the `af-fit` log's own WARNING line, which is printed before any detection | **stands** |
| **L15-P** — 7/7/5, NO DOMINANCE | **YES** | **gated.** Reported as a diagnostic only |
| **L15-N**, **L15-M** | **YES** | **gated** |
| **L15-B** — the baseline clause and its ratios | **YES** | **gated**, and its own alarm did not fire regardless |
| **G-d** — the converted-file identity clause | n/a | **FAILED, and the clause is ill-posed** (§3.3). The converter is not implicated |

> **So the wave's headline — the optimizer's landing moves on 19 of 20 synthetic datasets between
> `MaxOutlierRejections` 0 and 1, with search noise measured at zero on 3 of 3 controls — is untouched by the gate
> failure. What the gate failure costs is the answer to *which landing is better*, which is precisely the half
> item B was built for.**

### §3.9 What item B cannot say, said in advance and still true

- **It cannot generalise past the synthetic bank.** 20 well-formed synthetic sweeps; the real bank has no truth
  and was excluded. D5's 6-of-8 was mostly real runs, so L15-S is a *comparison*, not a continuation.
- **It cannot decide the shipped default.** Wave 14 already shipped it as an owner's decision that overrode a
  pre-registration. Item B tells the register whether that decision has a measurable direction; the answer, gated,
  is that it does not have one at this population and this materiality floor.
- **It says nothing about `--max-evals` other than 250.**

---

## §4 — Item 2 of waves 13/14: the nine UI changes A1–A9

`cmd.exe /c "query session"`, run once:

```
 SESSIONNAME               USERNAME                 ID  STATE
 services                                            0  Disc
>                          ghili                     1  Disc
 console                                             2  Conn
 rdp-tcp                                         65537  Listen
```

The user session (`ghili`, id 1) is **`Disc`** — the `console` row carries no username and is the
no-user-logged-in state. **NOT ATTEMPTABLE, sixth wave**, ~30 m on a connected session. One command, one line,
recorded.

---

## §5 — The suite, the budget, and what was not run

### §5.1 The suite

**Wave 15 ships no code.** The tree is at wave 14's HEAD, `git status` is clean, and no C# source was modified at
any point in the wave. The suite was re-run at the end of the wave as a gate rather than as a claim about a
change:

> **3781 passed, 0 failed, 0 skipped, in 3 m 33 s** — exactly wave 14's count, on an unchanged tree.

Verified by **COUNT**, not by tick ([F37](followups.md)), and not piped to `tail`, which would mask the exit code.
`dotnet.exe test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`, exit 0.

### §5.2 The budget, against the estimate

| arm | population | estimate | actual |
|---|---|---|---|
| **RULE G15** — the gate | 8 runs | ~42 m | **41 m 11 s** (01:52:26Z → 02:33:37Z) |
| **item A** — V4′ + the re-score | 6 rungs on disk | < 1 m, zero `TestApp` | **< 1 m**, zero `TestApp` |
| **item B / STEP 0** — the converter probe | 2 `af-fit` runs | ~1 m | **~30 s** |
| **item B / arms 0 + 1** | 20 datasets × 2, interleaved | **~3 h 8 m** (20 × 2 × 4.71 m) | **1 h 42 m 25 s** (02:35:29Z → 04:17:54Z) |
| **item B / scoring** | 2 × 20 `af-fit` | ~8 m | **7 m 29 s** (arm 0 3 m 45 s, arm 1 3 m 42 s) |
| **wave total `TestApp` wall** | | **~4 h 0 m** | **2 h 33 m 48 s**, against a 6 h ceiling |

> **THE LANDING ARMS CAME IN AT 54 % OF ESTIMATE, AND THE REASON IS A POPULATION MISMATCH IN THE RATE, NOT A
> WRONG MEASUREMENT.** §5's rate of **4.71 m per synthetic dataset** was derived correctly — it is the mean of
> `D18`/`D19`/`D20` across three wave-13/14 gate arms, stable to 1 %. But those three are the gate's *synthetic*
> runs, and the gate's synthetic runs are its three **largest**: `D18` alone is 6 m 15 s here, against `D12` at
> **15 s**, `D17` at **24 s** and `D09` at **27 s**. Priced over the whole 20-dataset population the mean is
> **2 m 34 s**, because the population is dominated by small, sparse datasets that a 250-evaluation search
> finishes in well under a minute. The estimate was priced from a **mixed real+synthetic** gate's synthetic
> subset; the arm is **all-synthetic and mostly small**, which is roughly 2× cheaper per run.
>
> *Wave 15's §5 corrected wave 13's budget error by insisting there is one rate, and it was right about that —
> there is no second rate. What there is, is a second **population**, and a per-run rate measured on three
> datasets does not transfer to twenty without checking that the three are representative. The design's own
> `af-fit`-time-weighted extrapolation — which it computed and then discarded as "the aggressive one" — gave
> ~65 m per arm and would have been the better estimate.* Record both, again, next wave.

Wave 13's published `I5 = 21 m` was corrected in place to its own log's **37 m 14 s** (`I5_START 16:15:41Z` …
`I5_DONE 16:52:55Z`) during this wave; the error had been quoted as a second, 2×-faster per-run rate.

### §5.3 What was NOT run, and what it would cost

- **The `af-fit` params read-out** (§3.2(5)) — print the 25 knob values and `EffectiveSensitivityGate` from
  `af-fit`'s own `StarDetectorParams`. **~45 m** including a rebuild, a fresh gate and a re-run of both scoring
  passes. It is the only thing that closes G-c's remaining gap outright, and it is a code change, so it belongs to
  a wave that is shipping code anyway.
- **Re-scoring G-c on the knob-diff control instead of star counts** — **< 5 m** of Python, already computed in
  §3.2(3). This is the cheap recommendation and the next wave should take it before it runs another landing arm.
- **Identifying the `af-fit` / `optimize` star-count disagreement's cause** — ~1 h. It affects nothing that ships;
  it affects every future control built on either count.
- **The SEM-unit floor** (`z` on `r·√N*`) — **~2 h**: thread `N*` from `MeasurePoint` construction through the fit
  to `RejectionTest`, plus tests. **This is the wave that should consume V4′ prospectively** (§2.5).
- **F63(a) on the 19 real bank runs.** ~90 m per arm, and **it buys no arbiter** — the real bank has no truth, so
  it would reproduce D5 at greater length and still be unable to say which landing is better.
- **F63(b) — pinning the SHIPPED default rather than `astrodet`'s.** After wave 14's item 1 the shipped default
  **is** `astrodet`'s value. **Re-priced at ~0 and it should close next wave.**
- **F59's five knobs** stay at code defaults. Re-pinning moves the coordinate system RULE G15 has now reproduced a
  sixth time: ~42 m for a new baseline plus the re-derivation of every cross-wave comparison. §3.1 found F59's
  dropped knobs are exactly the six the converter had to route around, so the entry is now load-bearing for more
  than provenance.
- **A repaired RULE F14 branch table.** Wave 14 recorded that the branch consuming §3.9 is unreachable. Fixing the
  branch table is a *design* job for the wave that applies V4′ prospectively, not a re-score.
- **The nine UI changes A1–A9**: ~30 m on a **connected** session (§4).
- **F18/F21/F25/F26** — the step-size and sweep-width family, untouched for many waves. Still unpriced.
- **F61(b), F52(d), F46(b), F54, F50** — nothing depends on them.

---

## Lessons

**1. A control demonstrated on one member of a population is not demonstrated on the population.** STEP 0 ran the
star-count control in both directions on `D18_m24_deep_shed`, got TOOK and DID-NOT-TAKE exactly as designed, and
proved nothing about the other nineteen datasets — because the axis it varied (settings applied / not applied) is
not the axis that decides the control's answer (which dataset). G-c then returned non-TOOK on **exactly** the
seven datasets where `af-fit` and `optimize` already disagreed at the pinned base two waves earlier, with no
converter anywhere in that comparison. *F66(a) says demonstrate a gate in both directions before quoting it. That
is necessary and it is not sufficient: demonstrate it in both directions **on a sample of the population it will
be applied to**, and if that is one dataset out of twenty, say so in the design.*

**2. "Could not look" must not fire for "the intervention worked."** All four `COULD-NOT-LOOK` reads are a sweep
whose two extreme frames starved — `pos 20564: 0 stars (skipped - Y would be 0)` — which is the landing's settings
biting hardest. The wave-13 "could not look" discipline exists so an empty thing never reads as agreement, and it
worked: nothing was misread as a pass. **But the state was designed for *"the artifact is missing"* and it fired
for *"the artifact changed in the way the arm was testing for."*** *A missing-data state needs to distinguish
absence-of-evidence from evidence-of-effect, or it becomes anti-correlated with the thing it guards on exactly the
cases that matter most.*

**3. Two numbers with the same name are not the same measurement, and the check costs nothing.** `af-fit`'s
per-position `Stars` and `optimize_result.csv`'s `optimizedStarCount` were declared *"the same number at the same
settings"* on the strength of one exact 9-of-9 match. They come from different detector entry points
(`StarDetector.Detect` vs `HocusFocusStarDetection.BuildDetectionContext`+`GateAndMeasure`) fed by differently
constructed `StarDetectorParams`, and they disagree on 7 of 20 datasets by up to 2× at the sweep wings. **The test
that settles it was free and already on disk**: wave 13 ran `af-fit` on all 20 at the same pinned base whose
evaluation wave 15's `optimize` records, and the two disagree on precisely those 7. *Before building a control on
an equality between two subsystems, check the equality on the population, not on an example.*

**4. Satisfiability analysis must check both branches, not only the one you expect.** §4's table was written
specifically to stop wave 13's and wave 14's foreclosed-clause failures from recurring — and it did its best work
of the wave, predicting L15-P's null with arithmetic before the data. It then listed G-d as *"20 pairs |
consistent | yes"*, having checked that the PASS value was attainable and never that the *"converted files
identical"* branch was reachable at all. It is not: every landing carries `CreatedAtUtc` and F30's provenance
stamp, so two converted files always differ, so G-d must fire on every dataset whose landing does not move. **The
section written to catch unreachable branches contained one.** *Ask of every clause: what input makes this return
each of its outcomes? — for each outcome, not for the expected one.*

**5. A ratio of two numbers that are both negligible is not a magnitude.** L15-B's headline read *"`D16` at arm 0
is 41.8× worse than not optimizing at all."* Both numbers are vertex errors below half a focuser count — 0.010 →
0.420 on a 15-count step — and the clause's own alarm threshold (1.0 step) was missed by a factor of 34. The
clause was right to state an absolute floor and wrong to print a ratio column beside it without one. *When a
clause reports both a ratio and an absolute threshold, the absolute one decides and the ratio does not get a
sentence of its own.*

**6. The wave's best control was already in every log and nobody had read it as one.**
`HarnessSettingsStore.SimpleModePresetOverrides` measures the knobs by constructing a real `StarDetectionOptions`
over the file's own bag and diffing it, and prints the result on every `af-fit` run — 205 of 205 overridden knobs
equal to their landing across all 40 runs, including all seven G-c could not certify. It also falsifies
DID-NOT-TAKE decisively, because all 40 converted files share one `Simple_*` tuple and a file that did not take
would print the *same* preset values on all 40. **It was written as a warning about a footgun and it is a stronger
positive control than the one the wave designed.** *Before building an instrument, grep the logs you already have
for the quantity you are about to measure.*

**7. NO VERDICT held under pressure, twice in one wave, and that is the point.** Item A had a repaired gate, a
passing population and a diagnostic that visibly points at one rung — and RULE F14 stays at NO VERDICT
permanently, on the pre-registration's own recommendation, accepted by the controller over its own brief. Item B
had a gate that failed for a reason this document shows is not the converter's — and RULE L15 stays at NO VERDICT,
with the surviving results separated from the gated ones in §3.8 rather than quietly promoted. *A register whose
NO VERDICTs are real is the only kind whose verdicts mean anything, and the price is paid in waves where the
answer was probably fine.*
