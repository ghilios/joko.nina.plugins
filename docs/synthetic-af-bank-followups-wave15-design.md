# Synthetic AF bank — followups wave 15 (design)

Plan: [`plans/synthetic-af-bank-followups-wave15-plan.md`](../plans/synthetic-af-bank-followups-wave15-plan.md).
Wave 14: [`docs/synthetic-af-bank-followups-wave14-results.md`](synthetic-af-bank-followups-wave14-results.md).
Standing authorisation: [`docs/waves14-21-autonomous-prompt.md`](waves14-21-autonomous-prompt.md).
Register: [`docs/followups.md`](followups.md).

> ## PROVENANCE — fixed before any arm runs
>
> | input | value |
> |---|---|
> | tree | branch `ghilios/synthetic-af-bank-followups-wave13`, at wave 14's HEAD. **Wave 15 ships NO code**, so the tree does not move during the wave |
> | binary | `D:\hf_w15\exe` — **ONE binary for the whole wave**, never rebuilt (F53(c)). `TestApp.dll` / `NINA.Joko.Plugins.HocusFocus.dll` sha256 and `BuildId` recorded at build time. **A SIXTH binary** for the gate table |
> | detector | `DetectorVersion` **2**, read as a **FIELD** on every landing. **Never from a `strings` probe** — see below |
> | settings **S0** | `D:\hf_w11\pinned_settings_w11.json` md5 **`a67ffc06164c81613aef5c4f8324b9b8`** — `MaxOutlierRejections` **explicitly 0** |
> | settings **S1** | `D:\hf_w13\pinned_settings_w13_mor1.json` md5 **`89e14ef884dbb31d0502182bd89d584e`** — identical to S0 except `MaxOutlierRejections` = **1**. Verified key by key: equal top-level fields, equal `Options` key sets, exactly one differing value |
> | profile | `astrodet` `ce3f3e63-8fd3-4b72-a0ca-d90db9441382`, pinned on **every** arm |
> | banks | `D:\SyntheticAutofocusBank` (20 datasets, `renderRequest.OptimalFocuserPosition` = **truth**), `D:\Autofocus Bank` (19 runs) |
> | F15 control | `/mnt/d/hf_w15/bank_landing_fingerprint_BEFORE.json`, **42** landings, **0 changed** since the wave-14 fingerprint. `--update-run-folder` is passed **nowhere in this wave** |
> | suite baseline | **3781** (wave 14's count on this branch). Wave 15 adds no tests because it adds no code |
>
> **ONE BINARY, AND THIS TIME THAT IS ACHIEVABLE.** Wave 14 needed two because item 2's Stage B code did not
> exist when the gate binary was frozen, and rebuilding `D:\hf_w14\exe` in place would have been F53(c) exactly.
> Wave 15 ships nothing, so a single build serves the gate **and** all four of item B's arms, and RULE G15 is a
> control on the same binary every arm runs. That is a real improvement over wave 14 and it is worth naming.
>
> **THE DETECTOR VERSION IS A FIELD. THE `strings` PROBES DO NOT DISCRIMINATE, IN EITHER DIRECTION**
> ([F66](followups.md)). `strings … | grep AtrousWaveletFast` returns **1 on every binary this project has ever
> produced** — it is a **type name**, and a type is compiled in wherever it exists in source, including a build
> that selects a different implementation at run time. `strings … | grep -- '--profile-id'` returns **0** on
> binaries that demonstrably accept the flag, because literals live in the `#US` heap as UTF-16 (`strings -el`
> returns 28). Two probes, two heaps, and the register read them as one instrument for four waves. This wave
> quotes the `DetectorVersion` **field** and nothing else.
>
> **No fan-out.** Every arm is `--profile-id`-pinned and a pinned arm cannot fan out at all (wave 11's K3); the
> authorisation is worth 1.33×, not 4× ([F60](followups.md)). Wave 5's φ table is invalid on three axes and is
> quoted nowhere.

---

## DISCLOSURE — what already existed when these rules were fixed

This series' rule is *fix the rule before the data*. **Item A does not satisfy it, cannot satisfy it, and the
design says so in the loudest terms it has.**

| item | did its data exist when the rule was fixed? |
|---|---|
| RULE G15 (the gate) | **no** — the binary does not exist |
| item B (the two landing arms and their scoring pass) | **no** — no wave-15 `optimize` run exists |
| **item A (V4′ and the re-score of RULE F14)** | **YES, AND ITS OUTCOME IS ALREADY PUBLISHED** |

Item A reads wave 14's six rungs, which have been on disk since 2026-08-09, and re-scores them under a
corrected validity clause. **Wave 14's results doc already published every number the re-score will produce.**
For the avoidance of any doubt, this is the complete list of what is already known:

- **C1** passes at `A0.50` and `A1.00` and fails at the other four rungs (max ρ 45.1768 / 1.1304 / 1.0000 /
  1.0000 / 45.1768 / 45.1768);
- **C2** passes everywhere except `A1.00`, where `N_fire` is **0 / 0 / 0**;
- **C3** is **CONTAINS-BLUNTLY** at every family-A rung ≥ 0.25 and **DOES NOT CONTAIN** for family B;
- **C4** never vetoes — median `|Δe|` **0.00000** at every rung and budget, max **0.00993** against a 0.10 floor;
- **C5** blast radius at budget 1 is 0 / 4 / 5 / 7 / 0 / 0;
- **V1** passed (39 × 4 × 7 fields, zero differences) and **V2** passed (39 of 39 × 6);
- the property V4′ tests — `consensus^f_B ⊆ consensus_B` — **already held on 585 of 585 triples**, checked by
  hand in wave 14 §3.5.

**So item A cannot produce evidence, and this document forbids it being read as though it had.** What it
produces is a *corrected instrument* plus a *corrected statement of what wave 14 measured*. §2 argues, at
length, that it should not be allowed to convert wave 14's NO VERDICT into a verdict at all.

**What was NOT computed before this design was written:** nothing about item B. No wave-15 `optimize` ran; the
only landings read while authoring were **wave 14's**, and only to (a) establish the file formats §3.2 depends
on, and (b) verify the converter's `KNOWN` key list against a real landing. The star counts quoted in §3.3 come
from wave 14's `gate/D18_m24_deep_shed` and wave 14's `affit_A0.00` rung, and they are quoted as a *format*
demonstration, not as a result.

**The scorers were self-tested during authoring, and that is disclosed too.** `convert_landing_w15.py`,
`prov_w15.py`, `score_land_w15.py` and `score_f14_w15.py` each carry a `--self-test` that was run; each was
shown to return **every one of its outcomes** on constructed input. `prov_w15.py --self-test` was additionally
run against **wave 14's** gate directory, where it correctly reports FAIL on direction 1 (that arm's `BuildId`
*is* wave 14's, which is what the novelty clause exists to catch) and FAIL-as-expected on direction 2. **No
scorer was run against wave-14 rung data or against any landing's numbers.**

---

## The items

| # | item | kind | decided by |
|---|---|---|---|
| **G15** | the gate reproduces the eight `BestJ` **bit-identically** on a sixth binary | **stopping gate** | RULE G15 |
| **A** | **F65/F66(a)** — repair V4, re-score wave 14's six rungs | **a CORRECTED INSTRUMENT + a corrected statement. Argued NOT to yield a verdict** | V4′ (§2) |
| **B** | **F63(a) + F62 at the landing level** — do the two `MaxOutlierRejections` values land differently on the synthetic bank, and is either landing better **out of sample**? | **measured; returns a finding or a measured null** | RULE L15 (§3) |

**Item 2 of waves 13/14 (the nine UI changes A1–A9) is not attempted and is not listed as an item.** Five waves
have run `query session` and found `Disc`. It costs one command; the plan runs it and records the answer in one
line. If it ever returns `Conn`, it becomes an item that wave's controller schedules.

---

## §1 — RULE G15: the gate, and it is this wave's STOPPING gate

Wave 11's eight values have reproduced bit-identically across **five** binaries and two settings files
(waves 11, 12, 13, 14) and — outside RULE W14-D, whose scorer refuses the comparison — across two wave-10
builds as well. This wave adds a **sixth** binary.

| run | expected `BestJ` (6 dp) | run | expected `BestJ` (6 dp) |
|---|---|---|---|
| `toml999` | 0.995784 | `mccomiskey` | 0.976746 |
| `CWhiteFocus` | 0.996068 | `D18_m24_deep_shed` | 0.999882 |
| `uneven` | 0.996368 | `D19_cygnus_deep_shed` | 0.999487 |
| `muggsie` | 0.997195 | `D20_m24_bright_control` | 0.999738 |

> ### RULE G15 — ONE PASS/FAIL CLAUSE
>
> **All eight `BestJ` reproduce the table to 6 dp. A partial reproduction is a FAILURE, not a warning** — the
> eight are one instrument. Bit-identity to all sixteen digits is *reported* as the stronger observation; 6 dp
> is the clause.
>
> **A FAIL STOPS THE WAVE**, before item B runs, because item B's two arms are compared inside this coordinate
> system and a moved coordinate system makes them incomparable to each other and to waves 5–14.

Sequential, `--per-run --max-evals 250`, `--settings D:\hf_w11\pinned_settings_w11.json` **and**
`--profile-id ce3f3e63-8fd3-4b72-a0ca-d90db9441382`, both named in `gate_w15.sh`'s header, machine quiet.
Scored by `python3 /mnt/d/hf_w12/score_w12.py /mnt/d/hf_w15/gate --rule G15` — **a WSL path, never a Windows
one** (a Windows path yields eight `UNEVALUATED` and looks exactly like a failed arm).

`score_w12.py` is reused **unmodified**: editing a scorer that produced earlier waves' numbers would
retroactively change how those numbers were produced.

### §1.1 The free controls, as FIELDS, diffed rather than asserted

| field | expected |
|---|---|
| `BuildId` | **must DIFFER from** wave 14's `084e3485f9f94260a1cfc2d9ac99f453`, wave 13's `62334f10…`, wave 12's `103d61c4…`, wave 11's `5cb7e474…`. A match means no build happened |
| `DetectorVersion` | **2 ×8, as a FIELD** |
| `ProfileId` | contains `ce3f3e63-…` on all eight **and** exactly one distinct value across the arm |
| `FitInputs` | `MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid` ×8 |
| `ConcurrencyCheck` | `exclusive` **read across the whole arm** — one landing's `exclusive` proves nothing (`WaitOne(0)` is won by exactly one of N contenders) |
| `BaselineJ` | reproduces wave 11 — [F41](followups.md)'s free control |

**Wave 14's Stage-B binary (`D:\hf_w14\exe_floor`) has no recorded `BuildId`** and cannot be listed above: it
ran `af-fit` only, and `af-fit` writes no landing, so nothing ever stamped it. The results doc must record wave
15's id so wave 16 can carry it.

### §1.2 EVERY GATE IN THIS WAVE MUST BE DEMONSTRATED TO PASS **AND** TO FAIL BEFORE IT IS QUOTED

This is [F66](followups.md)(a), promoted from a next-step to a wave rule, because wave 14 produced **three**
checks in one wave that could not return their own PASS: `prov_w14.py`'s profile clause compared a formatted
`ProfileId` to a bare GUID; `score_affit_w14.py`'s V3 read a report file its own producer never writes; V4
compared rows containing `NaN` with plain equality, so a row could fail to match itself. All three failed
**closed**, which is the safe direction — *and a control that always says FAIL trains its reader to skip it,
which is how a real failure gets through.*

Concretely, before any number from an instrument is quoted:

| instrument | known-good input | known-bad input |
|---|---|---|
| `prov_w15.py` | the real gate arm → **PASS** | a copy with one landing's `ProfileId` rewritten → **FAIL on both clauses**, and the mutation is asserted to have happened *before* the scorer is believed |
| `convert_landing_w15.py` | a real landing → converts and round-trips | a landing passed as the base, `UseAdvanced=True`, an unknown key, a missing knob → **each refuses** |
| the star-count control (`score_land_w15.py` G-c) | `affit_w15.sh probe`'s `good/` → **TOOK** | `probe`'s `mutant/` (`UseOptimizedSettings` flipped back to `False`) → **DID-NOT-TAKE** |
| `score_land_w15.py` | `--self-test` returns every classifier outcome | same |
| `score_f14_w15.py` (V4′) | a lawful suppressed rung → PASS | four constructed violation shapes → **each FAILS** |

`prov_w14.py`'s repaired form is carried forward verbatim as `prov_w15.py` — **the repair is the reason it is
carried, not an aside.**

---

## §2 — Item A: repair V4, re-score RULE F14, and the argument that this must NOT produce a verdict

### §2.1 What went wrong, stated as a proved-vs-tested mismatch and not as a disliked answer

Wave 14's design proved one statement and tested a strictly stronger one:

- **PROVED (§2.6).** A uniform scale floor replaces `scale` with `max(scale, f)`, which multiplies every `z_i`
  in a round by the same constant, so the **argmax never moves**. Each *candidate model's* floored rejection
  set is therefore a **prefix** of its unfloored set; production removes the **intersection** over the four
  Hybrid models; hence `consensus^f_B ⊆ consensus_B`.
- **TESTED (V4).** *"Stage B's production row must equal one of that run's wave-13 printed rows at budget ≤ B."*

The second does not follow from the first, and `Panos_attempt01` refutes it. Three models reject `38396` first;
`TiltedHyperbola` rejects `40396` first. **The intersection of prefixes is not a prefix of the intersection**
([F65](followups.md)). At `α = 1.00` the round-2 floor suppresses `TiltedHyperbola`'s second round only, the
intersection becomes `{40396}` — a subset of the budget-2 consensus and equal to no printed row. The proved
statement held on **585 of 585** triples; the tested one failed on **2 rows**.

V4 also compared rows containing `NaN` with plain tuple equality, so a row could **fail to match itself**: 2 of
its 4 reported failures were `Panos` rows that are byte-identical to wave 13's ([F66](followups.md)).

**Neither defect is an opinion about the answer.** Both are checkable from wave 14's own committed text: the
proof is in §2.6, the assertion is in V4, and the scorer's own `eq()` helper — which handles `NaN` correctly —
sits in the same file V4 declines to call it from.

### §2.2 V4′ — stated in full, before it is run

For each rung `T ∈ {A0.25, A0.50, A1.00, B0.50, B1.00}` (the control rung `A0.00` belongs to **V1**, not to
V4), each run `r` present in **both** wave 13 and the rung, and each budget `B`; writing `R_T(r,B)` for the
rung's printed rejected-position set and `R₁₃(r,B)` for wave 13's:

| clause | statement | why it is the right test |
|---|---|---|
| **(i) SUBSET** | `R_T(r,B) ⊆ R₁₃(r,B)` for `B ∈ {1,2,3}` | this **is** `consensus^f_B ⊆ consensus_B`, the statement the design proved |
| **(ii) CARDINALITY** | `|R_T(r,B)| ≤ |R₁₃(r,B)|` **and** `|R_T(r,B)| ≤ B` | a floor can only suppress; and no model may reject more than `B`, so the consensus cannot either |
| **(iii) MONOTONE** | `R_T(r,B) ⊆ R_T(r,B+1)` for `B ∈ {0,1,2}` | if `x` is in every model's `B`-prefix it is in every model's `(B+1)`-prefix. A structural property of the cascade that V4 never checked |
| **(iv) BUDGET 0** | `R_T(r,0) = R₁₃(r,0) = ∅` | reported as its own count (195 rows) |
| **(v) SUPPRESSION IDENTITY, NaN-SAFE** | where `R_T(r,B) = R₁₃(r,B′)` for exactly one `B′ ≤ B`, the rung's row fields must equal wave 13's budget-`B′` row **under `eq()`**, in which two `NaN`s are the same absence | this is the *useful* half of V4, with the defect removed |

**A row whose rejected set matches no printed row is `NOVEL-CONSENSUS`: counted, named, and reported. It is not
a failure.** That is exactly the F65 case, and treating it as a failure is the defect being repaired.

**Population, pre-registered: `5 × 39 × 3 = 585` triples**, plus `5 × 39 = 195` budget-0 rows. Fewer than 585
readable ⇒ **V4′ is UNEVALUATED**, never *"no violations found"*.

> **VERDICT.** **PASS** iff (i), (ii), (iii) and (v) hold on every readable triple **and** the population is
> 585 / 195. **FAIL** on any violation — and a FAIL refutes the **proved** lemma, so its consequence is
> unchanged from wave 14's: **Stage A is retracted in full.** The `NOVEL-CONSENSUS` count is a **reported
> quantity and never a bar**; the pre-registered expectation is **≥ 1** (the two `Panos` rows at `B1.00`), and
> **a count of zero is a finding about the scorer, not about the data.**

**V4′ IS WEAKER THAN V4 IN EXACTLY ONE WAY AND STRONGER IN THREE.** It drops the "equals a printed row"
requirement, which the design never proved. It **adds** the cardinality bound, the monotonicity clause and the
budget-0 clause, none of which V4 checked. **It must still be able to fail, and here is what would fail it:**

| data | clause |
|---|---|
| a rung row rejecting a position wave 13's same-budget row did not reject | (i) — and the proved lemma dies with it |
| a rung row with more rejections than wave 13's, or more than `B` | (ii) |
| a rung whose budget-1 rejection is absent from its own budget-2 row | (iii) |
| a rung row with wave 13's rejected set but a different `σ_focus` | (v) |

**Each of the four is demonstrated on a constructed input by `score_f14_w15.py --self-test`, together with a
lawful rung that passes, an F65-shaped novel consensus that is reported rather than failed, and a `NaN` row
that matches itself.** Run before the scorer touches wave-14 data; the run is in the plan as a gate.

### §2.3 THE HONESTY REQUIREMENT — read this before any re-scored number is quoted anywhere

> **1. THIS IS A RE-STATEMENT UNDER A CORRECTED GATE, NOT A NEW TEST.** Every rung was measured in wave 14 and
> is on disk. Every decision clause (C1–C5) and every threshold is wave 14's, unchanged. **No `TestApp` time is
> spent and no new measurement is made.**
>
> **2. THE OUTCOME IS ALREADY PUBLISHED.** The DISCLOSURE section above enumerates it. Nothing in item A can
> surprise its author, which is the definition of the thing a pre-registration exists to prevent.
>
> **3. THE REPAIR WAS FORCED BY A PROVED-VS-TESTED MISMATCH, NOT BY DISLIKE OF THE ANSWER** — and that is
> checkable by a third party from wave 14's committed design text without re-running anything.
>
> **4. NO NEW EVIDENCE IS CREATED BY ITEM A. The register must never cite it as independent confirmation of
> anything** — not of F45(b)'s containment, not of the ladder's shape, not of Stage A's 696-of-702 agreement.
> Any register entry touched by item A must carry the words *corrected diagnostic, wave 14 data*.

### §2.4 MY JUDGEMENT, WHICH THE CONTROLLER ASKED FOR AND WHICH DISAGREES WITH THE BRIEF IN PART

**The repair is legitimate. Letting it produce a RULE F14 verdict is not.** Those are separable, and this
design separates them.

**Why the repair is legitimate.** A published gate that is known to test a proposition its own design did not
prove is a defect in the register's instruments, and leaving it standing is worse than fixing it: the next wave
that wants a prefix-style clause would either inherit the broken one or invent a fourth variant. The repair is
also *forced* rather than *chosen* — V4′ is the unique clause that tests the proved statement — and it is
cheap, mechanical and demonstrable in both directions.

**Why the verdict is not.** Three reasons, in increasing order of weight:

1. **The branch table was written to be consumed by an author who had not seen §3.7. It would now be consumed
   by one who has.** RULE F14's own closing clause says the verdict is *"never resolved by preferring whichever
   rung agrees with something else"* — a sentence that only means anything if the reader does not yet know
   which rung agrees with what. That condition is permanently gone.
2. **V4 is not the only known defect in RULE F14, and repairing one defect in a rule with two produces a rule
   that is still wrong.** Wave 14's own §3.8 records that the branch which would have consumed the wave's
   strongest measurement — the SEM audit, §3.9 — is **unreachable**, because it is gated on *"C1/C3 hold only
   at rungs failing C2"* and `A0.50` falsifies the antecedent. A verdict emitted from a table with a known dead
   branch is not the verdict the pre-registration promised.
3. **The precedent is the expensive part.** Wave 14's Lesson 3 is *"the moment a pre-registration may be
   re-decided after the data, it stops being one, and every earlier wave's verdict becomes retroactively
   negotiable."* If a failed validity gate can be repaired by the very next wave and the rule then re-applied,
   that sentence is false in practice however carefully it is labelled. The register's single most valuable
   property is that its NO VERDICTs are real.

> ### THE ALTERNATIVE THIS DESIGN RECOMMENDS
>
> **RULE F14 stays at NO VERDICT permanently.** The verdict is a property of the wave that ran it.
>
> Item A publishes `score_f14_w15.py`'s output as **`RULE F14 — corrected diagnostic (wave-14 data, post hoc)`**,
> under the §2.3 banner, in the wave-15 results doc and as an addendum to F45/F65. It states which validity
> gates pass under V4′ and reprints C1–C5. **It names no rung and recommends nothing.**
>
> **V4′ is then applied PROSPECTIVELY**, by the next wave that measures a **new** population. The natural
> consumer already exists and is already named: wave 14 §7.3's **SEM-unit floor** (`z` on `r·√N*`), priced at
> ~2 h, which is the same instrument on a new ladder and needs exactly this clause. That wave gets a gate that
> was fixed before its data existed — which is what a pre-registration is.
>
> **What this costs:** F45(b) stays open with no named rung for at least one more wave. That is the correct
> price, and it is small: wave 14 already established that *nothing about F45(b) ships*, that the containment
> window is one rung wide, and that the sub-question is written in the wrong units.

**If the controller decides otherwise** — which is entirely within its authority, exactly as item 1 of wave 14
overrode RULE M13 — then the results doc must say, in the same paragraph as the numbers, that **the controller
overrode this design's recommendation and applied a repaired rule to data whose outcome was public**, with this
section cited. An override recorded that way is a decision; an override not recorded that way is a moved
goalpost, and after one wave nobody can tell them apart.

---

## §3 — Item B: does the landing move, and is either landing BETTER? (F63(a), F62 at the landing level)

### §3.1 The question, and why it is scoped to the 20 synthetic datasets

Wave 13's D5 found the optimizer's **landing** moves on **6 of 8** runs between `MaxOutlierRejections` 0 and 1
at `--max-evals 250`, including the recommended AF step size by up to **17 %** and the brightness sensitivity
by a **factor of two** — and said, correctly, that it could not say whether either landing was *better*:
*"`BestJ` is computed by the fit under test, so neither landing can be called better."* That is
[F62](followups.md) at the landing level and it has never been resolved.

**Wave 14 made it a shipped question.** The code default moved 1 → 0, four of this machine's nine profiles have
the key absent and therefore move, and *"every profile with the key absent silently changes what the wizard
recommends."* The register currently asserts that the recommendation changes and cannot say in which direction.

**Scope: the 20 synthetic datasets, because that is the only place truth exists.**
`renderRequest.OptimalFocuserPosition` is the generator's true focus, it is out of sample for both arms, and
neither arm can move it. The 19 real bank runs have no truth and are excluded; the arms are cheaper for it.

### §3.2 THE BLOCKER, AND WHAT IT COSTS — `af-fit --settings <landing>` FAILS SILENTLY

The scoring pass has to run the detector at a **landing's** settings. It cannot be done by pointing `af-fit` at
the landing, and the failure is silent:

- `HarnessSettingsStore.ResolveAt` does `JsonConvert.DeserializeObject<HarnessSettingsFile>(text)` and reads
  `loaded.Options`. **A landing (`optimized_settings.json`) is a flat `OptimizedStarDetectionSettings` DTO with
  no `Options` member at all** — 25 knob keys at top level plus bookkeeping. It deserializes to an **empty
  option bag** with `PixelSizeMicrons = 0` and `FocalLengthMm = 0`, and the run silently uses code defaults.
  **It does not error.** It produces a full set of plausible numbers about a detector nobody chose.
- **And the field that should catch it cannot.** `HarnessSettingsStore.Fingerprint` hashes
  `#pixelSizeMicrons` + `#focalLengthMm` + the parsed `Options` bag and appends **nothing** when `Options` is
  absent, so two *different* landings passed directly to `--settings` hash **identically**. `af-fit` does not
  print a fingerprint at all. *An instrument that is not connected reports perfect agreement.*
- **`--params-from` is not the instrument either, and this was checked in the source rather than assumed.**
  `AfFitDiagnosticRunner.LoadOriginalDetectorParams` enumerates `*_star_detection_result.json` **inside the
  `--af-run` folder** and uses `--params-from` as a **substring filter on those paths**, then reads a
  `DetectorParams` object out of the matched file. A landing is neither in the run folder nor of that shape.
  The flag cannot be pointed at one.

**A naive knob-lifting converter fails twice more, and both would be silent:**

1. **Six of the 25 curated knobs have no key in an exported settings bag at all** — `MaxDistortion`,
   `StarCenterTolerance`, `HotpixelThreshold`, `DefocusDistortionMinFactor`, `DonutMinAnnularityHoleFraction`,
   `DonutMaxStreakEccentricity`. That is [F59](followups.md) exactly: the export drops every knob whose setter
   validates. A lift-by-key converter would not carry them and nothing would say so.
2. **`pinned_settings_w11.json` has `UseAdvanced: "False"`.** `StarDetectionOptions.InitializeOptions` ends in
   `ConfigureSimpleSettings()`, which — absent an optimized snapshot — calls `DerivePresetSettings()` and
   **overwrites exactly the knobs the optimizer tunes**: `NoiseClippingMultiplier = 4`,
   `StarClippingMultiplier = 2`, `StructureLayers = 4`, `BrightnessSensitivity = 10`,
   `MinStarBoundingBoxSize = 5`, `StarPeakResponse = 0.75`, `MaxDistortion = 0.5`,
   `StarCenterTolerance = 0.3`, `MinHFR = 1.2`, `HotpixelThreshold = 0.001`, `NoiseReductionRadius`. **Both
   arms would collapse onto the preset baseline and item B would measure a perfect null it manufactured
   itself.** The warning is printed on every wave-11…14 `af-fit` run and is in every log on disk; nobody had a
   reason to care until a landing needed to take.

### §3.3 THE CONVERTER, AND THE CONTROL THAT PROVES IT TOOK

**`convert_landing_w15.py` uses the production path, and changes two keys:**

```
Options["OptimizedSettingsJson"] = <the landing file's RAW TEXT, byte for byte>
Options["UseOptimizedSettings"]  = "True"
Options["UseAdvanced"]             stays "False"   <-- load-bearing
```

`StarDetectionOptions.InitializeOptions` reads both keys, deserializes the snapshot, and
`ConfigureSimpleSettings()` takes the `UseOptimizedSettings && HasOptimizedSettings` branch →
`ApplyOptimizedSnapshotToLiveProperties()`, which derives the preset baseline and then applies **all 25 curated
knobs** on top. **That is exactly what a user gets when they press Accept in the wizard.** `UseAdvanced` must
stay `False` because `ConfigureSimpleSettings` **returns immediately** when it is true — the converter aborts
on an advanced base rather than writing a file that cannot take. Because the knobs ride inside the snapshot,
**F59's six dropped keys are carried and the converter never needs to know a knob's option-key spelling.**

**Unrecognised keys are an ERROR, not a drop** (the controller's instruction, and the right call): a knob the
converter does not know about is a knob that would silently not take. A missing curated knob is an error too —
a partial landing would deserialize the rest to CLR defaults.

> #### THE POSITIVE CONTROL, AND IT IS FREE, ALREADY PRINTED, AND CAN FAIL THREE WAYS
>
> `optimize --per-run` writes `optimize_result.csv` with **both** star counts per frame
> (`currentStarCount`, `optimizedStarCount`); `af-fit` writes `af_fit_points.csv` with its own `Stars` per
> focuser position. **They are the same number at the same settings** — checked on `D18_m24_deep_shed`, wave
> 14's gate `currentStarCount` against wave 14's `affit_A0.00` rung: `1266 / 2070 / 3789 / 5860 / 3553 / 5900 /
> 3782 / 2050 / 1282`, **exact on all nine frames**. So:
>
> | af-fit's per-frame `Stars` | verdict |
> |---|---|
> | equals `optimizedStarCount` on every frame | **TOOK** |
> | equals `currentStarCount` on every frame | **DID-NOT-TAKE** — the exact signature of a settings file that was written and never reached the detector |
> | neither | **AMBIGUOUS** — named, reported, never averaged |
> | any set empty or the positions not lining up | **COULD-NOT-LOOK** — its own state, and the guard runs **before** any field read |
>
> **This is stronger than any settings-fingerprint diff**, because it proves the *detector* ran at the
> landing's knobs rather than proving that two files differed.

**And it is demonstrated in both directions before the wave spends anything.** `affit_w15.sh probe` runs two
`af-fit` invocations on **wave 14's already-landed `D18`** — one properly converted, one with
`UseOptimizedSettings` flipped back to `False` — and requires **TOOK** and **DID-NOT-TAKE** respectively. Cost:
**~30 seconds**, no wave-15 arm, on data that already exists. If STEP 0 fails, item B's scoring pass is
UNEVALUATED and the arms are not run: *an arm scored through an unverified converter is worth less than no arm.*

**Both arms' converted files are built on S0**, deliberately. `af-fit` iterates budgets 0…3 explicitly and
never reads `MaxOutlierRejections` from the settings file, so the key is inert in the scoring pass; using one
base means the **only** difference between arm 0's and arm 1's converted files is the landing snapshot, and
clause **G-d** is then exact rather than approximate.

### §3.4 The arms

| arm | command | settings |
|---|---|---|
| **arm 0** | `optimize --per-run --max-evals 250 --profile-id ce3f3e63-…` | **S0**, `MaxOutlierRejections` = 0 |
| **arm 1** | identical | **S1**, `MaxOutlierRejections` = 1 |
| **score 0/1** | `af-fit --params-from __PINNED_NO_SAVED_PARAMS__ --confidence 0.95 --weighted true --step-size <meta>` | each dataset's **converted landing** |

**The one-key assertion is a driver-level abort, not a scorer-level note.** `land_w15.sh` re-derives it before
running anything and **aborts if the two files differ in anything other than `MaxOutlierRejections` — or in
nothing**. Differing in nothing is the worse failure: the arm would produce a perfect null of its own making.
(Verified while authoring: equal top-level fields, equal `Options` key sets, one differing value `'0'`→`'1'`.)

**Paired interleaving, and it is a deliberate departure from wave 13's arm-major order.** Each dataset gets arm
0 immediately followed by arm 1. Every clause in RULE L15 reads a **pair**, so at any stop point an interleaved
run leaves a complete paired population and an arm-major run leaves 20 halves. Nothing else changes: separate
`--out` roots, separate settings files, one `TestApp.exe` at a time.

**Pre-registered order, and the drop order is the tail:**

```
D18, D19, D20,  D01, D02, D03, D04, D05, D06, D07, D08, D09, D10, D11, D12, D13, D14, D15, D16, D17
```

`D18/D19/D20` run **first** because they are also RULE G15's synthetic gate runs — so arm 0's landings for them
must reproduce the gate's landings exactly (**clause G-e**, a free cross-arm control wave 14 had no analogue
of) — and because wave 13's D5 measured all three at these two values and found **all three moved**, so they
are simultaneously a reproduction of the finding this arm extends. **If the wave over-runs, drop from the tail
and record exactly which datasets were dropped.** A short population makes every population clause
**UNEVALUATED**; it does **not** mean *"fewer landings moved"*.

### §3.5 RULE L15 — fixed before any wave-15 data exists

Notation, on the 20 synthetic datasets:
`e_b(arm, d) = |minPos_b(arm, d) − OptimalFocuserPosition(d)| / step(d)` — **the same metric wave 13's D1 and
wave 14's C4 used, so the bar is the predecessors' bar.** `truth` and `step` are read from wave 13's own
`affit_syn_score.txt`, i.e. the same numbers D1 used, rather than re-derived.
`M` = the number of datasets whose **landing moved**.

#### Validity gates — any failure ⇒ NO VERDICT, and the wave says which gate failed

- **G-a — ONE KEY.** S0 and S1 differ in exactly `MaxOutlierRejections` and in nothing else, **or in nothing**,
  which aborts.
- **G-b — POPULATION BY COUNT.** **20 of 20** landings per arm and **20 of 20** scored `af-fit` runs per arm,
  each with an `af_fit_points.csv`. `verify_affit_w15.sh <dir> 20 <settings-prefix>` owns the `Settings:`
  positive-control line and the count. **COULD NOT LOOK is its own state at every level and the guard comes
  BEFORE any field read** — wave 14 broke this in its own W14-D scorer and printed empty control sets that read
  as *"all controls agree"*.
- **G-c — THE CONVERSION TOOK.** 20 of 20 **TOOK** per arm, on the star-count control of §3.3. Any
  `DID-NOT-TAKE`, `AMBIGUOUS` or `COULD-NOT-LOOK` fails the gate **by name**.
- **G-d — THE CONVERTED FILES DIFFER IFF THE LANDINGS DIFFER**, and differ **only** in
  `Options.OptimizedSettingsJson`.
- **G-e — GATE REPRODUCTION, FREE.** arm 0's landings for `D18/D19/D20` reproduce RULE G15's landings on the
  curated knobs and the recommended step. Same binary, same command, same settings file — so a difference here
  means the arm is not the gate's arm.

#### Decision clauses

- **L15-S — DID THE LANDING MOVE?** Count over the 20, on **wave 13 D5's own definition**: any curated knob, or
  the recommended step, differs. Reported as **M of 20** beside D5's **6 of 8**. **No threshold** — this is the
  clause that is directly comparable to the predecessor, and it is the clause most likely to carry the wave.
- **L15-P — THE PRIMARY, OUT OF SAMPLE, AT BUDGET 0.** Budget 0 is fixed for both arms so the comparison
  isolates the **landing**; the fit machinery is then identical and the detector is the only difference.
  Phrased over the **movers**, because a landing that did not move cannot produce a different fit and a clause
  over all 20 would be foreclosed by its own tie structure — wave 13's D1 needed ≥ 15 of 20 wins that 16 ties
  made unreachable, and the standing prompt calls that *"a badly-formed bar, not a passed test"*.
  - **(a) DIRECTION.** `wins(arm) ≥ max(6, ⌈0.75·M⌉)` **and** `wins(other) = 0`.
  - **(b) MATERIALITY.** median over the movers of `|Δe|` **≥ 0.10 step** — wave 13's D1 clause (b) floor,
    **unchanged**.
  - **DOMINATES** iff (a) and (b) hold **for the same arm**. Otherwise **NO DOMINANCE**. `M = 0` ⇒
    **UNEVALUATED**, never *"a tie"*.
- **L15-N — EACH ARM AT ITS OWN BUDGET** (arm 0 at budget 0, arm 1 at budget 1): what the user would actually
  get. **REPORTED, never a verdict** — it confounds the landing move with the fit budget, which is the whole
  reason L15-P is scored at a fixed budget. Printed from the same `af-fit` invocation at zero extra cost.
- **L15-M — MAGNITUDE, AND IT REFUSES TO VOTE** (wave 13's D4 pattern). Median, max and the full per-dataset
  table of `|Δe|`, plus the **reachable maximum computed from the data**, so the results doc can state whether
  clause (b) was ever attainable instead of leaving it implied.
- **L15-B — THE BASELINE CLAUSE, WHICH NO WAVE HAS EVER RUN.** Each arm's `e` against the **un-optimized**
  pinned base — wave 13's own `e0` column, already on disk. Reported. **If either arm's max `e` reaches
  1.0 step, that is a new register finding** — *the wizard's landing focuses further from truth than the
  settings it replaced* — and it is recorded as one. **It never votes on arm 0 against arm 1.**

> **VERDICT SHAPES, all four named in advance:**
>
> - **`<arm> DOMINATES`** — L15-P (a) and (b) both hold for that arm. The register then has, for the first
>   time, a *direction* for F63.
> - **`NO DOMINANCE`** — anything else, with L15-S's count and L15-M's magnitude reported. **This is the
>   pre-registered expectation, for the reason in §4.**
> - **`UNEVALUATED`** — a validity gate failed, or `M = 0`.
> - **`L15-B ALARM`** — orthogonal to the above and reportable alongside any of them.

### §3.6 What item B cannot say, said in advance

- **It cannot generalise past the synthetic bank.** 20 well-formed synthetic sweeps; the real bank has no truth
  and is excluded. D5's 6-of-8 was mostly real runs, so L15-S is a *comparison*, not a continuation.
- **It cannot separate "the landing moved" from "the search is noisy".** `optimize` at `--max-evals 250` is a
  stochastic search; two runs at the *same* settings are not guaranteed to land identically. **Clause G-e is
  the only handle on this** — arm 0 against the gate on three datasets, same everything — and three is a small
  control. If G-e shows drift at identical settings, **L15-S is reported as an upper bound on the knob's effect
  and the drift is the finding**, and the results doc must say so rather than attributing all of `M` to the
  knob.
- **It cannot decide the shipped default.** Wave 14 already shipped it as an owner's decision that overrode a
  pre-registration. Item B tells the register whether that decision has a measurable direction; it does not
  re-open it.
- **It says nothing about `--max-evals` other than 250.**

---

## §4 — SATISFIABILITY: every clause's maximum attainable value, with arithmetic, before the data

Wave 13's D1 clause (a) needed ≥ 15 of 20 wins, which 16 ties made unreachable in principle. Wave 14's C4 could
not return *"material"* in either direction, because the largest movement reachable anywhere in the bank is
**0.00993 step** against a 0.10 floor. **This section exists so wave 15 does not inherit the shape a third
time — and it concludes that one clause is very probably foreclosed, and says so now rather than afterwards.**

| clause | population | max attainable | reachable? |
|---|---|---|---|
| **RULE G15** | 8 runs | 8 of 8 to 6 dp | **yes**, five times already |
| **V4′ (i)(ii)(iii)(v)** | **585** triples + 195 budget-0 rows | no violations | **yes, and it CAN fail** — four violation shapes, each demonstrated on constructed input by `--self-test`. Note this is a **re-statement**, §2.3 |
| **V4′ NOVEL-CONSENSUS** | same | reported count | expected **≥ 1**; **zero would be a finding about the scorer** |
| **G-a** | 2 files | one differing key | **yes** — verified while authoring |
| **G-b** | 20 × 4 artifact sets | 20 of 20 | yes |
| **G-c** | 20 × 2 arms | 20 TOOK per arm | **yes, and all four outcomes are reachable** — demonstrated by STEP 0's `good/` and `mutant/` |
| **G-d** | 20 pairs | consistent | yes |
| **G-e** | 3 datasets | 3 of 3 identical | **yes, and a miss is the finding named in §3.6** |
| **L15-S** | 20 | `M ∈ [0, 20]` | **yes, in both directions.** D5 measured 3 of 3 on exactly these three synthetic datasets, so `M` is expected well above 0; `M = 0` would refute D5 on the synthetic bank and is a finding |
| **L15-P (a)** | `M` movers | `wins ≤ M` | **reachable iff `M ≥ 6`.** Below 6 the direction clause cannot fire *by construction* — 6 unanimous of 6 is `p = 2/2⁶ = 0.031` two-sided, and below that a sign test is noise. **If `M < 6`, L15-P returns NO DOMINANCE and the design says now that this was foreseeable, not a surprise** |
| **L15-P (b)** | `M` movers | see below | **PROBABLY FORECLOSED. Stated here, before the data** |
| **L15-N** | `M` movers | reported | yes |
| **L15-M** | `M` movers | magnitude | **always reachable — it is a magnitude and it refuses to vote** |
| **L15-B** | 20 × 2 arms | **unbounded above** | **yes.** This is the one clause in the wave with no ceiling |

### §4.1 Clause (b) is probably foreclosed, and here is the arithmetic

Wave 13's `affit_syn_score.txt` gives `e` at the **pinned base** for all 20 datasets:

```
min 0.00000 (D02)   median 0.00243   max 0.03000 (D17_cdk14_oiii5)
```

If the landings behave like the base, `|Δe| ≤ 2 × 0.03 = 0.06 step` and **the 0.10-step materiality floor
cannot be met.** For clause (b) to fire, a landing must move the vertex **more than 3.3× further from truth
than the worst un-optimized fit in the whole bank.** That is possible — the synthetic bank's known pathology is
that the optimizer drives the sensitivity gate to its floor ([F32](followups.md)/[F33](followups.md)), and a
starving landing can drop a focuser position out of the fit entirely through `JRun`'s hard `NHard` floor
(the wave-14 F62 audit's falsification of F62's own detector carve-out) — but **it is not the expected case,
and it is one-directional: (b) can only fire if a landing is materially WORSE, never if it is materially
better.**

> **So the pre-registered expectation for L15-P is NO DOMINANCE, and the wave is designed not to depend on it.**
> The clauses that carry weight are **L15-S** (a count over 20, fully satisfiable, directly comparable to D5's
> 6 of 8), **L15-M** (a magnitude that refuses to vote), and **L15-B** (unbounded, and the only clause that can
> produce a *new* register finding). This is wave 14's Lesson 4 applied at design time: *a pre-registration
> should ask what happens to each measurement under every branch, not only whether each branch is reachable.*

### §4.2 The one thing the brief asked about that turns out NOT to foreclose this clause

The brief asks whether L15-P is foreclosed by `MaxOutlierRejections` firing on only **4 of 20** synthetic
datasets through `af-fit` (wave 13's I1). **It is not, and the distinction matters.**

That 4-of-20 is the rate at which the rejection fires **inside a single fit at fixed detector settings**. Item
B's mechanism is different and is exactly what D5 discovered: **a search follows `J`, and `J` shifts wherever
the rejection fires anywhere in the explored space, not merely at the starting point.** Wave 13 measured the
seed evaluation moving on **8 of 39** and the landing on **6 of 8**. So `M` is governed by the *search's*
exposure to the knob, not by the seed's firing rate, and the 4-of-20 figure does not bound it.

**What the 4-of-20 does bound is L15-N**, where arm 1 is read at budget 1: on the ~16 datasets where the
rejection does not fire, arm 1's budget-1 row *is* its budget-0 row, so L15-N and L15-P coincide there. That is
why L15-N is reported and never votes.

---

## §5 — Budget

**Priced from measurement, not from similarity**, and the price is stated per population rather than per tool.

The only measured `optimize --per-run --max-evals 250` times on **synthetic** datasets are the three gate runs,
and they are stable across three waves and two settings files:

| | `D18` | `D19` | `D20` | total | mean |
|---|---|---|---|---|---|
| wave 13 gate (S0) | 6 m 13 s | 5 m 50 s | 2 m 05 s | **14 m 08 s** | 4.71 m |
| wave 14 gate (S0) | 6 m 17 s | 5 m 43 s | 2 m 07 s | **14 m 07 s** | 4.71 m |
| wave 13 `land_w13.sh` (S1) | 5 m 34 s | 5 m 44 s | 2 m 42 s | **14 m 00 s** | 4.67 m |

> **THE CONTROLLER'S BRIEF ASKED WHICH OF TWO RATES THIS WAVE USES — 5.2 m/run (wave 14's gate) or 2.6 m/run
> (`land_w13.sh`) — AND THE ANSWER IS THAT THE SECOND NUMBER IS A BOOKKEEPING ERROR.** Wave 13's results doc
> §7 records *"I5 — optimize ×8 at S1 | ~45 m | **21 m**"*. `D:\hf_w13\land_w13.log` records
> `I5_START 16:15:41Z` … `I5_DONE 16:52:55Z` = **37 m 14 s**. Per-run the three arms above agree to within 1 %.
> **There is no 2× rate difference to choose between; there is one rate, and a wrong total in a published
> budget table.** The 5.2 m/run figure is the *whole-gate* mean and is inflated by the real-bank runs
> (`mccomiskey` alone is 10 m 25 s); the synthetic-only mean is **4.71 m**. This wave uses **4.71 m per
> synthetic dataset**, and the correction belongs in the register.

| arm | population | estimate | basis |
|---|---|---|---|
| **RULE G15** — the gate | 8 runs | **~42 m** | wave 14 actual 41 m 48 s |
| **item A** — V4′ + re-score | 6 rungs on disk | **< 1 m**, zero `TestApp` | pure Python over existing artifacts |
| **item B / STEP 0** — the converter probe | 2 `af-fit` runs | **~1 m** | wave 13 measured `af-fit` on `D18` at 13 s |
| **item B / arm 0** | 20 datasets | **~94 m** | 20 × 4.71 m |
| **item B / arm 1** | 20 datasets | **~94 m** | 20 × 4.71 m |
| **item B / scoring** | 2 × 20 `af-fit` | **~8 m** | wave 13 measured 20 synthetic in **3 m 47 s** |
| **wave total** | | **~4 h 0 m** | against a **6 h** ceiling |

An `af-fit`-time-weighted extrapolation (the 20 datasets' `af-fit` total is 4.63× the three gate datasets')
gives **~65 m per arm** and a wave total of ~2 h 40 m. **The flat mean is used because it is the conservative
one**, and because per-dataset `af-fit` time is dominated by process startup and is a poor proxy for a
250-evaluation search. **Record the actual against both.**

### §5.1 Pre-registered drop order, if the wave over-runs

1. **Drop `D17_cdk14_oiii5` first.** It finds zero stars at short exposures — a starvation extreme, named in
   the standing prompt as useless as a measurement.
2. **Then drop from the tail of the pre-registered order** (`D16`, `D15`, `D14`, …), recording each by name.
3. **Never drop `D18/D19/D20`** — they carry clause G-e and the D5 reproduction.
4. **Never drop the scoring pass to buy another arm.** An unscored landing arm re-measures D5 and answers
   nothing new; the whole point of this wave is the out-of-sample half.
5. **Never drop STEP 0.** *An arm scored through an unverified converter is worth less than no arm.*

A dropped dataset makes **G-b fail** and every population clause **UNEVALUATED**. That is the honest outcome
and it must not be softened into *"fewer landings moved"*.

---

## §6 — What this wave will NOT run, and what it costs

- **The SEM-unit floor** (`z` on `r·√N*`) — wave 14 §3.9's finding and the named next step for F45(b).
  **~2 h**: thread `N*` from `MeasurePoint` construction through the fit to `RejectionTest`, plus tests.
  **This is the wave that should consume V4′ prospectively** (§2.4).
- **F63(a) on the 19 real bank runs.** ~90 m per arm at the measured 4.7 m/run, and **it buys no arbiter** —
  the real bank has no truth, so it would reproduce D5 at greater length and still be unable to say which
  landing is better. Deliberately excluded.
- **F63(b) — pinning the SHIPPED default rather than `astrodet`'s.** After wave 14's item 1 the shipped default
  **is** `astrodet`'s value, so this is now nearly free. It is not done here only because item B needs both
  values pinned explicitly and would be unaffected. **Re-price it at ~0 and close it next wave.**
- **F59's five knobs** stay at code defaults. Re-pinning moves the coordinate system RULE G15 has now
  reproduced a sixth time: ~42 m for a new baseline plus the re-derivation of every cross-wave comparison.
  **Note that §3.2 found F59's dropped knobs are exactly the six the converter has to route around** — the
  entry is now load-bearing for more than provenance.
- **The nine UI changes A1–A9.** ~30 m on a **connected** session. `query session` is run once and the answer
  recorded in one line; six waves have said `Disc`.
- **A repaired RULE F14 branch table.** Wave 14 recorded that the branch consuming §3.9 is unreachable. Fixing
  the branch table is a *design* job for the wave that applies V4′ prospectively, not a re-score.
- **F18/F21/F25/F26** — the step-size and sweep-width family, untouched for many waves. Still unpriced.
- **F61(b), F52(d), F46(b), F54, F50** — nothing depends on them.

---

## §7 — Traps, carried into the drivers rather than into the prose

Each of these is a line of code in `/mnt/d/hf_w15/`, not a reminder:

| trap | where it is enforced |
|---|---|
| WSL paths (`/mnt/d/...`) to every scorer; a Windows path yields UNEVALUATED and looks like a failed arm | every `*.py` aborts on a `\` in any path argument |
| `< /dev/null` on every `TestApp` invocation inside a loop — wave 13's I2 "completed" in 25 s having scored 1 of 19 runs | `gate_w15.sh`, `land_w15.sh`, `affit_w15.sh` |
| one bank path contains a SPACE (`timmer/5 AutoFocus_…`) | item B is synthetic-only; the gate quotes its paths |
| **assert the POPULATION SIZE** | `gate_w15.sh` (8), `verify_affit_w15.sh <dir> 20 <prefix>`, G-b, V4′'s 585 |
| "could not look" has its own state, and **the guard comes BEFORE any field read** | `classify_star_counts`, `landing_moved`, `v4prime_run`, `bank_fingerprint_w15.py` — each demonstrated by `--self-test` |
| only one `TestApp.exe` at a time; NINA must not be running during a pinned arm | `tasklist.exe` counts in all three drivers, in the refuse-to-guess form (`grep -c` exits 1 on no match) |
| never rebuild an arm's directory mid-wave (F53(c)) | every driver aborts on an existing output directory; `land_w15.sh` additionally aborts on a **half-written pair** |
| no fan-out (1.33×, F60; a pinned arm cannot fan out at all) | stated in every header; the drivers are strictly sequential |
| Newtonsoft writes NaN/Infinity as the **strings** `"NaN"`/`"Infinity"` | `num()` in both scorers returns NaN and never 0 |
| `D17_cdk14_oiii5` finds zero stars at short exposures | first in the drop order; UNEVALUATED by name if it produces no fit |
| `Panos` has a degenerate σ fit and must be UNEVALUATED **by name** | `score_f14_w15.py` names it in C1's UNEVALUATED list; item B is synthetic-only so it does not arise there |
| **F15 is fixed** — do not pass `--update-run-folder` | passed nowhere; 42 landings fingerprinted BEFORE, re-checked after every arm by `bank_fingerprint_w15.py --check` |
| a gate must be shown to PASS *and* to FAIL before it is quoted (F66(a)) | `--self-test` on every scorer, and STEP 0 for the star-count control |
| the `DetectorVersion` **FIELD**, never a `strings` probe | `prov_w15.py`; the probes are documented as non-discriminating in the header |
| do not pipe a verification command to `tail` — it masks the exit code | stated in the plan's verification steps |
