# Wave 15 — controller notes (results in hand before item B finished)

Checkpoint so nothing is lost if the session ends mid-arm. The analysis agent should treat these as inputs and
**verify anything it re-states**.

---

## RULE G15 — PASS on a SIXTH binary

`/mnt/d/hf_w15/gate_w15.sh`, **01:52:26Z–02:33:xxZ**, sequential, `--per-run --max-evals 250`, both pins in the
script header. Binary `D:\hf_w15\exe`: `TestApp.dll` sha256 `a2693968d584ecd9…`,
`NINA.Joko.Plugins.HocusFocus.dll` sha256 `4c32abe70deb5bd5…`.

```
>>> RULE G15 A1: PASS, 8 of 8 to 6 dp.
>>> bit-identical to K8 on 8 of 8 (all 16 digits).
```

**Population asserted first**: `aggregate_summary.json produced: 8   expected: 8`. Free controls: one
`FitInputs`, `ConcurrencyCheck` exclusive ×8 read across the arm, `ProfileId` one distinct value.

### The gate scorer was demonstrated in BOTH directions before being quoted — F66(a) as a wave rule

`prov_w15.py --self-test /mnt/d/hf_w15/gate`:

```
direction 1: the real arm PASSES
direction 2: a copy with ONE landing's ProfileId rewritten -> FAIL on BOTH clauses
             (mutated muggsie: 'astrodet (ce3f3e63-…)' -> 'Default (b10b1d6d-0000-…)')
>>> SELF-TEST PASS -- this check has a demonstrated PASS and a demonstrated FAIL, so it may now be quoted.
```

**And it refused to certify itself when pointed at the wrong arm.** Run against *wave 14's* gate it reported
`SELF-TEST **FAIL** -- do not quote this check`, because that `BuildId` is on the prior-wave list and direction
1 therefore fails. *A self-test bound to the arm it certifies is the point; one that passes against any input
certifies nothing.*

---

## Item A — V4′ PASSES, and it is published as a CORRECTED DIAGNOSTIC that names no rung

`score_f14_w15.py --self-test` first, on constructed input:

```
PASS  PASSES a suppressed-but-lawful rung
PASS  F65's novel-but-subset consensus is REPORTED, not failed
PASS  FAILS (i) a position wave 13 never rejected | (ii) more rejections than the budget
PASS  FAILS (iii) budget 1's rejection vanishes at budget 2 | (v) same set, different sigma
PASS  NaN-SAFE: a row containing NaN matches ITSELF (wave 14's V4 could not)
PASS  a missing summary is COULD NOT LOOK, not 'no violations'
```

Then over wave 14's six rungs (`/mnt/d/hf_w14/affit_*`), **zero `TestApp` time**:

| gate | result |
|---|---|
| **V1** | PASS — 0 field differences at f = 0.00 |
| **V2** | PASS — 39 of 39 readable on all six rungs |
| **V3′** | PASS — 72 of 72 rounds at f = 0, all INERT, 0 INCONSISTENT *(reads `rounds.tsv`, the file its producer actually writes — wave 14's V3 looked for a `stageA_report.txt` that is never created)* |
| **V4′** | **PASS — 585 of 585 triples, 0 violations; 195 of 195 budget-0 rows empty both sides** |

**The 2 NOVEL-CONSENSUS rows are `Panos` at `B1.00`, budgets 2 and 3** — `[40396]` matching none of
`[[], [], [38396, 40396]]`. **Reported and named, never a bar**, because that is [F65] and not a defect.

Decision clauses re-run verbatim (wave 14's numbers, reproduced):

| rung | max ρ | C1 | N_fire@1 | @2 | @3 | blast@1 | C3 (`caboose` b3) |
|---|---|---|---|---|---|---|---|
| A0.00 | 45.1768 | fail | 7 | 13 | 13 | 0 | DOES NOT CONTAIN |
| A0.25 | 1.1304 | fail | 3 | 5 | 5 | 4 | CONTAINS-BLUNTLY |
| **A0.50** | **1.0000** | **PASS** | **2** | 2 | 2 | 5 | **CONTAINS-BLUNTLY** |
| A1.00 | 1.0000 | PASS | **0** | 0 | 0 | 7 | CONTAINS-BLUNTLY |
| B0.50 | 45.1768 | fail | 7 | 13 | 13 | 0 | DOES NOT CONTAIN |
| B1.00 | 45.1768 | fail | 7 | 11 | 11 | 0 | DOES NOT CONTAIN |

C4 vetoes nowhere: median `|Δe|` **0.00000** on every rung, max **0.00993** against a 0.10 floor.
`Panos` is UNEVALUATED **by name** on the ρ clause (degenerate σ), never silently dropped.

> ### **RULE F14 STAYS AT NO VERDICT, PERMANENTLY. The controller accepts the pre-registration's recommendation over its own brief.**
>
> The corrected diagnostic **visibly points at `A0.50`** — the only rung satisfying C1 ∧ C2 ∧ C3 with no C4
> veto — and **saying so is part of the honesty, not a breach of it.** What is refused is the step from *"the
> diagnostic points here"* to *"the rule recommends this"*. Three reasons:
>
> 1. RULE F14's own guard — *"never resolved by preferring whichever rung agrees with something else"* — cannot
>    bind a reader who has already seen wave 14 §3.7. That condition is permanently gone.
> 2. **V4 was not the rule's only defect.** Wave 14 recorded that the branch built to consume its best
>    measurement (§3.9, the SEM audit) is unreachable. **Repairing one defect in a two-defect rule yields a rule
>    that is still wrong**, and a rule that cannot consume its own strongest evidence should not issue verdicts.
> 3. The precedent costs more than the answer: a wave that repairs a failed gate and immediately harvests a
>    verdict teaches the next wave to do the same.
>
> **V4′ is applied PROSPECTIVELY** by the wave that measures the SEM-unit floor, which needs exactly this clause.

---

## Item B — STEP 0 PASSED, both directions, before any arm ran

**The blocker, restated because it is the wave's most transferable engineering result:** a landing is **not** a
harness settings file. It is flat; `HarnessSettingsStore` reads `parsed["Options"]`. `--settings <landing>`
yields an **empty option bag** and silently uses code defaults, and `SettingsFingerprint` appends nothing when
`Options` is absent — so **two different landings of one dataset produce an IDENTICAL fingerprint.** Three of
the controller's proposed follow-ups were wrong and the pre-registration corrected all three: `--params-from`
is a substring filter on paths inside `--af-run` and cannot take a landing; a knob-lifting converter would
*also* be defeated by `UseAdvanced=False`, under which Simple-mode presets overwrite exactly the knobs the
optimizer tunes (**both arms would collapse onto the preset baseline — a perfect null the arm manufactured
itself**); and the right converter is **two keys** via the production Accept path.

```
Options["OptimizedSettingsJson"] = <the landing's RAW TEXT>
Options["UseOptimizedSettings"]  = "True"
Options["UseAdvanced"]             stays "False"    <-- load-bearing
```

`convert_landing_w15.py --self-test`: converts, round-trips, and **every refusal refuses** — a landing passed
as the base, `UseAdvanced=True`, an unrecognised key, a missing curated knob.

**The positive control is free, already printed, and three-way falsifiable** — and it is stronger than the
settings-fingerprint diff the controller proposed, because it proves the **detector** ran at the landing's
knobs rather than proving two files differed:

```
good     TOOK           9 of 9 frames equal optimizedStarCount
mutant   DID-NOT-TAKE   9 of 9 frames equal currentStarCount
         (the mutant differs from good in ONE key: UseOptimizedSettings True -> False)
>>> STEP 0 PASS. The star-count control may now be quoted.
```

The converted `D18` landing moves **5 knobs** off the preset base — `BrightnessSensitivity` 10.0 → 14.667,
`StarClippingMultiplier` 2.0 → 0.25, `MinHFR` 1.2 → 0.7, `StructureLayers` 4 → 5,
`NoiseReductionRadius` 4 → 3 — so the control had something to detect.

### The arms
`land_w15.sh`, **LAND_START 02:35:29Z**, 20 datasets × 2 arms, **paired-interleaved**, order
`D18, D19, D20, D01…D17`. The one-key assertion ran first and passed:
`MaxOutlierRejections '0' -> '1', and nothing else differs`. Priced at **4.71 m/dataset ⇒ ~3 h 8 m**.

---

## Corrections this wave has already made to the record

- **Wave 13's budget table said I5 took 21 m; its own log says 37 m 14 s** (`I5_START 16:15:41Z` …
  `I5_DONE 16:52:55Z`). Corrected in place in the wave-13 results doc. The error was not harmless: the
  controller quoted it as a **second, 2×-faster per-run rate** and tried to price a 40-run arm from the gap.
  There is no second rate — the three wave-13 arms agree within **1 %** per dataset.
- **The controller's "in landing but NOT in pinned: []" was an artifact** of diffing the landing's *empty*
  `Options` sub-object against the pinned bag. Compared properly, **12 landing keys have no counterpart**,
  including exactly F59's six validating-setter knobs. *A null result from the wrong comparison looks the same
  as a null result.*
