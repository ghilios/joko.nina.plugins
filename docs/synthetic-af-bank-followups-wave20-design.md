# Synthetic AF bank — followups wave 20 (pre-registration)

Plan: [`plans/synthetic-af-bank-followups-wave20-plan.md`](../plans/synthetic-af-bank-followups-wave20-plan.md).
Charter: [`docs/waves17-21-handoff-prompt.md`](waves17-21-handoff-prompt.md).
Wave 19: [`docs/synthetic-af-bank-followups-wave19-results.md`](synthetic-af-bank-followups-wave19-results.md).
Wave 18: [`docs/synthetic-af-bank-followups-wave18-results.md`](synthetic-af-bank-followups-wave18-results.md).
Register: [`docs/followups.md`](followups.md).

> **This document is fixed before any measurement.** Every threshold, population, statistic, aggregation, empty-set
> answer and *which copy of the field is read* is written here and is not re-decided after the data. An
> unsatisfiable rule is a finding ([F68](followups.md)(c)).

---

## §0 — PROVENANCE, as of pre-registration time

| input | value |
|---|---|
| tree | branch `ghilios/synthetic-af-bank-followups-wave13`, PR #191. HEAD **`95753a9`** (wave 19's results commit). **`git status --short` is EMPTY and `git diff --stat` is EMPTY**, verified at pre-registration time |
| **the brief's `git status` is STALE for the THIRD consecutive wave** | It lists `HocusFocusStarDetection.cs`, `StarDetectionOptionsTests.cs` and `documentation/docs/settings/preprocessing.md` as modified. **They are not.** Wave 19 §10.4 recorded this twice; this is the third. `/mnt/d/hf_w18/part2_w18.patch` is still on disk and applying it silently converts this wave's binary into wave 18's B2 — which is why clause **G20-P1** is permanent |
| code this wave ships | **Item D, unconditionally: D1** ([F69](followups.md)(b), the `optimize/detected` dump), **D2** (F69(c), the no-op flag notice), **D3** ([F70](followups.md), the two manual lines). **D3 is committed BEFORE the build** so a cutoff cannot lose it |
| binaries | **ONE**, `D:\hf_w20\exe`, never rebuilt mid-wave ([F53](followups.md)(c)). Hash the **dlls**, not `TestApp.exe` — wave 18 established the apphost is byte-identical across genuinely different binaries |
| settings **S0** | `D:\hf_w11\pinned_settings_w11.json` md5 `a67ffc06164c81613aef5c4f8324b9b8`. **Not re-pinned** ([F63](followups.md)(b), DO-NOT-RE-PIN since wave 17; and see §3.4 for why F59 does not re-open it) |
| profile | `astrodet` `ce3f3e63-8fd3-4b72-a0ca-d90db9441382`, pinned on every arm alongside `--settings` |
| banks | `D:\SyntheticAutofocusBank` (20 datasets), `D:\Autofocus Bank` (19 runs) |
| suite baseline | **3831** (wave 18, verified by COUNT). **The charter's §6 step 8 says 3781 and is stale** — see §11.5 |
| artifacts read at pre-registration time | `/mnt/d/hf_w19/gate/*.log` (8), `/mnt/d/hf_w19/reA0/D*/run.log` (20), `/mnt/d/hf_w19/probe`, `/mnt/d/hf_w19/C19_PROBE_PASSED`. **Read-only.** Every denominator in §9 was counted on these files, and **fingerprint class 4 (§6.4) exists so the wave can prove they did not move between pre-registration and scoring** |

### §0.1 — A correction the wave inherits before it starts: **`C19-D2` RAN, and wave 19's results say it did not**

This is not a quibble and it is not wave 19's fault in the way its own Lesson 6 states it.

| | |
|---|---|
| what `docs/synthetic-af-bank-followups-wave19-results.md` §3 says | *"`C19-D2` … **NOT RUN.** `/mnt/d/hf_w19/probe` does not exist; `probe_w19.sh` was never invoked"* and *"**`/mnt/d/hf_w19/C19_PROBE_PASSED` does not exist.**"* |
| what is on disk | `/mnt/d/hf_w19/probe/good.json`, `probe/mutant.json`, `probe/good/`, `probe/mutant/` — **and `/mnt/d/hf_w19/C19_PROBE_PASSED`, 572 bytes, reading `C-DEMONSTRATED`** |
| when | `good.json` `01:42:53.966 -0400`; `C19_PROBE_PASSED` **`01:43:28.576 -0400`** = **`05:43:28Z`** |
| when the results were committed | `95753a9` at `01:44:01 -0400` = `05:44:01Z` — **33 seconds later** |

**So the one-minute item was run, and the document was written before it and never re-read.** Three consequences,
and the wave must carry all three rather than the comfortable one:

1. **Wave 19's Lesson 6 is false as written.** *"The cheapest thing left undone was the one-minute one"* — it was
   not left undone. What actually happened is worse and more general: **a document that claims *"every number in
   this document was re-derived at write-up time"* was falsified by an artifact created 33 seconds before its own
   commit.** The failure is not a skipped item; it is a **write-up that raced its own wave**.
2. **The register is wrong in two places.** [F71](followups.md)'s block says *"Still owed: the live direction,
   `C19-D2`, ~1 minute"*, and wave 19 §8's cheapest-first list opens with it. Both are corrected in §10.
3. **RULE C19's verdict is not this wave's to change.** `C19_PROBE_PASSED` exists and reads `C-DEMONSTRATED`, but
   **wave 20 does not re-issue wave 19's verdict** — that would be re-deciding a rule after the data. What wave 20
   records is the **artifact-level fact** (the marker exists, with its timestamp and content) and the
   **document-level error**. Whether C19 is `C-UNEVALUATED` or `C-DEMONSTRATED` belongs to whoever
   pre-registers the next rule that consumes it.

> **The generalisable lesson, and it is new:** *a wave's last action must precede its write-up, or the write-up's
> re-derivation claim is a claim about a tree that no longer exists.* Wave 20's plan therefore puts **every**
> measurement before the write-up and makes the write-up's first step a re-listing of the artifact directory.

---

## §1 — What this wave is, in one paragraph

Wave 19 pre-registered item D, claimed the gate measured its inertness, and **did not ship it** — `PARAMS-DUMP
optimize/detected` is on **0 of 8** gate logs and **0 of 20** arm logs, and `ParamsDump.cs` has no third constant.
Wave 20 ships it, plus F69(c)'s notice and F70's two manual lines, and — this is the part wave 19 could not do —
**puts a clause on the gate that reads the shipped code and can only pass if it is there.** The gate then measures
inertness rather than asserting it. One 3-minute probe on the single gate-excluded property (`DetectionBinning`
only moves where the resolved factor is not 1, and **it is 1 on all eight gate datasets**) demonstrates that the
new dump is genuinely *post*-mutation. Everything else this wave declines is declined in writing, on the merits.

---

## §2 — Items

| item | what | rule | ships? | cost |
|---|---|---|---|---|
| **D3** | [F70](followups.md) — `documentation/docs/settings/preprocessing.md` `:23` (table) **and** `:41` (prose) `3` -> `4`, plus one sentence of derivation | **D20-M** (a check, not a gate) | **unconditionally, and FIRST — committed before the build** | ~2 m |
| **D1** | [F69](followups.md)(b) — `ParamsDump.OptimizeDetected = "optimize/detected"` and a third `ParamsDump.Write` after `ApplyRunDetectionBinningIfRequested` | **G20-P2** (reach + inertness) and **RULE D20** (the post-mutation property) | **unconditionally** | ~20 m + tests |
| **D2** | F69(c) — a one-line notice that `--apply-run-detection-binning` is an accepted no-op and F39(b) is on by default | **D20-C** | **unconditionally** | ~5 m |
| **G** | the gate | **RULE G20** | — | ~42 m |
| **X** | the detected-probe: **one** dataset with resolved factor 2 (`D12_c14_585_afbin2`), `--apply-run-detection-binning` passed | **RULE D20** | — | ~3 m |
| **P** | [F21](followups.md)'s price probe — rule-free, hard-timeboxed | none, deliberately | — | <= 20 m |
| **R** | register + results corrections (§10) | none | **yes** | 0 m |

**Nothing in RULE D20 gates shipping.** D1/D2/D3 ship unconditionally, as wave 18's Part 1 did. D20 decides only
**what may be claimed** about the dump. That separation is deliberate: wave 19's failure was a *shipping* failure
dressed as a measurement, and tying the ship to the measurement would reward not shipping.

---

## §3 — What this wave does NOT do, and why — on the merits, not on budget alone

### §3.1 — Re-scoring wave 18's seed arms for a verdict: **FORBIDDEN, and honoured executably**

Wave 19 established that F70(a) is **not decidable by measurement on this bank** and escalated it to the owner.
Wave 20 builds **no `af-fit` driver**, `/mnt/d/hf_w18/seedA1` appears in **no clause, no driver and no scorer
path**, and fingerprint class 3 proves the 48 prior-wave landings did not move. *The surest way not to look is to
not build the thing that looks.*

Note the asymmetry that makes this cheap to honour: **`C19_PROBE_PASSED` now exists** (§0.1), which is precisely
the precondition an out-of-sample wave needs. Wave 20 declines to spend it anyway. Recorded so that the decision
reads as a choice rather than an inability.

### §3.2 — F70(b′), the 20 preset-owned dead literals: **NOT SHIPPED**

Not budget. **It needs a mutant-demonstrated parametrized test whose property set is derived from source**
(F70's own next step names mutant **M-R1**), and the derivation-from-source is the expensive half — guessing the
set is exactly the failure mode the test exists to prevent. It does not fit beside a 42 m gate under a 12:00Z
cutoff, and a half-done version is worse than none because it would delete literals no test covers.
**Cost of not doing it: the hazard stands exactly as wave 18 left it — editing one of the 20 is a silent no-op.**

### §3.3 — F45(b)'s production plumbing (~3–4 h): **REJECTED on substance**

Unchanged from wave 19 and restated because it is a standing rejection, not a deferral:

1. **RULE S16 returned NO RECOMMENDATION and charter §3a forbids repairing `S16-A(b)`.** The plumbing would
   deliver a mechanism **no rule has recommended.** This is the decisive reason and it is not about time.
2. After wave 14 flipped `MaxOutlierRejections` to 0, `RejectionTest` does not run on a default profile, so a SEM
   floor reaches **3 of 9** profiles on this machine.
3. `N*` is not reachable in `AutoFocusEngine` (`:901` drops the count into `MeasureAndError`, a NuGet struct of two
   doubles) — it needs a parallel count map, a pooling rule that does not double-divide against the existing
   `/sqrt(frames)`, the same again in the optimizer's path, and a choice between `DetectedStars` and
   `hfrStars.Count`.

And a fourth that is about time and is stated as such: **3–4 h + tests does not fit in a window that ends at
12:00Z and already contains a 42 m gate.** Even if (1) were answered, this wave could not ship it.

### §3.4 — [F59](followups.md)'s five knobs (~1 h): **REJECTED, and on a NEW ground measured today**

The brief is right that wave 18's "two coordinate-system moves in one wave" objection has lapsed, and right that
**refuting an argument is not refuting its conclusion.** So here is the merits argument, derived from source at
pre-registration time rather than inherited.

F59's five missing knobs are `MaxDistortion`, `StarCenterTolerance`, `SaturationThreshold`, `HotpixelThreshold`,
`Sensitivity`. The pinned file carries **`UseAdvanced=False`**, so on load `ConfigureSimpleSettings()` runs
`DerivePresetSettings()`. Read directly from `StarDetectionOptions.cs`:

| knob | assigned by `DerivePresetSettings`? | consequence of adding it to the pinned file |
|---|---|---|
| `MaxDistortion` | **YES** — `:227`, `= 0.5` | overwritten on load. **No effect** |
| `StarCenterTolerance` | **YES** — `:228`, `= 0.3` | overwritten on load. **No effect** |
| `HotpixelThreshold` | **YES** — `:237`, `= 0.001d` | overwritten on load. **No effect** |
| `Sensitivity` (= `BrightnessSensitivity`) | **YES** — `:198`, `= 10.0 * sensitivityScale`, plus the WideRange/LongFocalLength deltas | overwritten on load. **No effect** |
| `SaturationThreshold` | **NO** — absent from `DerivePresetSettings`; falls back to the accessor default at `:308` | **this one WOULD bind** |

> **So F59 splits 4/1, and neither half earns an hour.** Four of the five are **provably inert under the current
> pin** — repairing the exporter and re-pinning changes the detector by exactly nothing for them, because
> Simple-mode presets own them. The fifth, `SaturationThreshold`, **would** change the detector — which makes it a
> coordinate-system move that invalidates K8 and owes a fresh 42 m baseline this wave cannot afford before 12:00Z.
> *The cheap half buys no change and the changing half is not cheap.*

**This does not close F59** and it is not offered as a verdict. It is a costing, and it belongs in the register so
that the next wave to consider F59 starts from *"4 of 5 are preset-owned"* rather than from *"~1 h"*.

### §3.5 — Fan-out, NINA, and re-runs

No fan-out (every arm is `--profile-id`-pinned and a pinned arm cannot fan out at all; the authorisation is worth
1.33x, [F60](followups.md)). **No NINA during any arm** — the drivers abort if a NINA process is running. **No arm
directory is rebuilt mid-wave** ([F53](followups.md)(c)) and every driver aborts on a populated output root.
Wave 5's phi table is quoted nowhere.

---

## §4 — RULE G20 — the gate, **and it is this wave's stopping gate**

Eight values, both pins named in the driver header, `optimize --per-run --max-evals 250`, sequential, one
`TestApp.exe`, no NINA. Driver `/mnt/d/hf_w20/gate_w20.sh`; scorers `score_w12.py --rule G20` and
`prov_w20.py`.

```
toml999 0.995784 | CWhiteFocus 0.996068 | uneven 0.996368 | muggsie 0.997195
mccomiskey 0.976746 | D18 0.999882 | D19 0.999487 | D20 0.999738
```

`BuildId` must be **novel** against the ten recorded landing ids:
`5cb7e474, 103d61c4, 62334f10, 084e3485, df3a867d, 10bc1b47, 932a1366, e745c958, 7a3a03ba, 763d1476`.
**dll sha256 values live in a separate, labelled table** and the scorer **fails loudly** if a `BuildId` field ever
begins with a known dll hash ([F66](followups.md) — a novelty list that mixes a file hash with build ids can never
fire on the entry it believes it guards).

### §4.1 — Every clause, with the **five-part** statement ([F68](followups.md), plus wave 18's fifth column)

**P** = population (a concrete artifact **and** the field), **S** = statistic, **A** = aggregation, **E** = the
answer on an empty set, **W** = *which of the possibly-several copies of the field is read, and is it the live
one*.

| clause | threshold, fixed here | P | W — which copy, and is it live | S | A | E |
|---|---|---|---|---|---|---|
| **G20-1** | all eight `BestJ` reproduce K8 to **6 dp**. A partial reproduction is a **FAILURE that stops the wave** | the 8 `optimized_settings.json` under `/mnt/d/hf_w20/gate` | `FinalJ`, top level. **A landing carries each name exactly once** — the two-copy hazard ([F71](followups.md)) is a property of converted *settings files*, not landings | equality to 6 dp | all-of, printed `n of 8` | unreadable landing => COULD-NOT-LOOK, **named**, **and G20-1 FAILS**. For a stopping gate "could not look" is "has not passed" |
| **G20-2** | `aggregate_summary.json produced: 8`, asserted **in the driver, before any scorer** | `find` over the gate root | file existence | count | `== 8` | 0 found => FAIL |
| **G20-3a** | exactly **one** `BuildId`, **novel** against the ten | `Provenance.BuildId` x8 | one copy per landing | set | cardinality **and** disjointness | empty => FAIL. `KNOWN_DLL_SHA256` is a separate table; a `BuildId` matching one is a **loud** failure |
| **G20-3b** | `DetectorVersion` **2**, read as the **FIELD** | `Provenance.DetectorVersion` x8 | one copy per landing | set | `== {"2"}` | empty => FAIL. **`strings`/`strings -el` appears nowhere as a detector check** |
| **G20-3c** | `ProfileId` contains `ce3f3e63-...` **and** cardinality exactly 1 | `Provenance.ProfileId` x8 | one copy | containment + cardinality | **both, evaluated separately** | `all()` over an empty list is vacuously true, so the **cardinality** clause carries the empty read |
| **G20-3d** | one distinct `FitInputs`, exactly `MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid` | `Provenance.FitInputs` x8 | one copy | set | `== {expected}` | empty => FAIL |
| **G20-3e** | `ConcurrencyCheck == exclusive` on all eight, read **across the arm** | `Provenance.ConcurrencyCheck` | one copy, collected as a length-8 list carrying an explicit `None` per unreadable landing | equality | all-of over a **length-8** list | a `None` is a **FAIL, not a skip** — the list cannot be shortened into agreement |
| **G20-3f** | `BaselineJ` reproduces wave 11 ([F41](followups.md)) to 6 dp | `BaselineJ` x8 | one copy | equality | `n of 8` | missing => FAIL |
| **G20-4** | `prov_w20.py --self-test` PASSES on the real arm and FAILS on a mutated copy, **the mutation asserted by read-back**, before any G20 number is quoted | the arm and a `copytree` of it | `Provenance.ProfileId` on one named landing | — | both directions | a mutation that cannot be made prints **SELF-TEST COULD NOT RUN** — neither a pass nor a fail |
| **G20-P1** | the `PARAMS-DUMP optimize/seed` block reads `NoiseReductionRadius=3` on **8 of 8**, asserted **in the driver** | the 8 gate logs | **`NoiseReductionRadius` inside the `optimize/seed` BEGIN..END range** — *not* `optimize/baseline`, *not* `optimize/detected`, *not* the landing's copy. **Three blocks now carry this name** | field value | `== 8` | a missing block => the driver refuses to hand the arm on |

### §4.2 — **G20-P2**: the clause that reaches this wave's shipped code

Wave 19's gate had **ten** thresholded clauses and every one of them read identically with item D present or
absent. The only thing that caught the non-delivery was an unthresholded count. **G20-P2 gives that count a
threshold**, in five parts.

| clause | threshold, fixed here | P | W — which copy | S | A | E |
|---|---|---|---|---|---|---|
| **G20-P2a** *reach* | `PARAMS-DUMP optimize/detected BEGIN` appears **exactly once** in each of the 8 gate logs | the 8 gate logs | the whole block, matched as a BEGIN..END range keyed on the exact tag | count of BEGIN markers per log | `== 1` on **8 of 8** | a log that cannot be read is COULD-NOT-LOOK, **named**, and **G20-P2a FAILS** |
| **G20-P2b** *shape* | the block carries **55** field lines and **55 unique** names, on 8 of 8 | the `optimize/detected` range | its own field lines | count, and count-of-distinct | both `== 55`, on 8 of 8 | empty block => FAIL, **not** "0 fields differ" |
| **G20-P2c** *it is POST-assignment* | `optimize/detected.PixelScale` is **finite** on 8 of 8 **and** `optimize/baseline.PixelScale` is the token `NaN` on 8 of 8 | the two ranges in each of the 8 logs | `PixelScale`, **named per block** | value class (finite vs the literal token `NaN`) | two rates, both denominators printed, both must be `8 of 8` | a missing block on either side => COULD-NOT-LOOK, named, clause FAILS |
| **G20-P2d** *the gate CANNOT see the binning half, and says so* | `optimize/detected.DetectionBinning == 1` on 8 of 8, **equal to** `optimize/baseline`'s | the two ranges | `DetectionBinning`, named per block | equality to 1 | `== 8` | missing => FAIL |
| **G20-P2e** *no substring collision* | `PARAMS-DUMP optimize/baseline BEGIN` still appears **exactly once** per log and `PARAMS-DUMP optimize/seed BEGIN` **exactly once** per log, on 8 of 8 | the 8 gate logs | the BEGIN markers, counted by exact string | count per tag per log | `== 1` for both tags on 8 of 8 | any log with a count != 1 => FAIL, and the tag is the suspect |

> **G20-P2d is deliberately an equality, not a difference, and this is the honest half.** On all eight gate
> datasets the resolved detection-binning factor is **1** — measured at pre-registration time on wave 19's own
> eight gate logs (`toml999`, `CWhiteFocus`, `uneven`, `muggsie`, `mccomiskey` from `harness_settings.json`;
> `D18`, `D19`, `D20` from `synthetic_meta.json`; **1 on every one**). So the gate population **cannot** exhibit
> `DetectionBinning` moving, and a clause demanding it would be unsatisfiable — the exact defect
> [F68](followups.md) catalogues. The gate asserts what it *can* see (the block exists, has the right shape,
> carries the run's real `PixelScale`, and did not disturb the other two tags) and **RULE D20's probe** carries the
> half it cannot.

### §4.3 — **What a G20 PASS proves about this wave's code — clause by clause, including what it does NOT reach**

Wave 19's claim (b) was void because the code was absent. Wave 18's was sound because the code was present and the
gate ran it. Here is the accounting, in advance:

| shipped piece | reached by the gate? | the clause that reaches it | what a PASS therefore proves |
|---|---|---|---|
| **D1** — the `optimize/detected` dump | **YES.** `optimize --per-run` executes `RunPerRun`, which is where the new `ParamsDump.Write` sits, once per dataset | **G20-P2a/b/c/d/e** | the code is **in this binary and executed on 8 of 8 gate runs**; and because **G20-1** reproduces K8 bit-identically with it present, **its inertness on the search is a MEASUREMENT, not an assumption** |
| **D2** — the F69(c) no-op notice | **NO.** The notice fires only when `--apply-run-detection-binning` is present, and **the gate does not pass it** | **D20-C**, in `/mnt/d/hf_w20/detected_probe_w20.sh`, which passes the flag on one dataset | nothing. A G20 PASS says **nothing whatever** about D2, and this row exists so no later reader infers otherwise |
| **D3** — the two manual lines | **NO. It is documentation and is in no binary** | **D20-M**, a textual assertion over the file, corroborated by `optimize/baseline.NoiseReductionRadius == 4` on 8 of 8 gate logs | nothing about the file. The corroboration establishes the *value the option actually has*, which is the fact the manual is wrong about |

> **Stated once, plainly:** a G20 PASS proves the coordinate system is unmoved on an eleventh binary, **and** that
> D1 is present, executed and inert. It proves **nothing** about D2 or D3, and the two clauses that do reach them
> are named above rather than left to be inferred. *If an item's code cannot be reached by the gate, say so and
> name the clause that does reach it.*

### §4.4 — What G20 does not prove, in general

Nothing about the landing beyond `FinalJ` and `BaselineJ` (RULE R19 owns that, and its population is 20 synthetic
datasets on **C#-identical builds** — see [F73](followups.md)). It runs at `MaxOutlierRejections = 0`, so it says
nothing about the rejection path. It is a control on the **binary**, not on the change — except where G20-P2
explicitly reads the change.

---

## §5 — RULE D20 — the dump is genuinely **post**-mutation

**The question.** F69(b)'s whole point is that `DetectionBinning` and `PixelScale` are mutated *after* the two
existing dumps, so the printed bundle is not the one that detected. A third block placed at the wrong site would
be indistinguishable from `optimize/baseline` — and, in F69's own words, *"a dump identical to the one above it is
a dump that has not been demonstrated."*

**The probe.** ONE dataset, `D12_c14_585_afbin2`, chosen because its resolved factor is **2** (measured on
`/mnt/d/hf_w19/reA0/D12_c14_585_afbin2/run.log` at pre-registration time), run with
`--per-run --max-evals 250 --apply-run-detection-binning`, both pins, into `/mnt/d/hf_w20/dprobe`. ~3 m.

> **Why `D12` and not the gate.** The seven factor-2 synthetic datasets are **`{D08, D09, D10, D12, D14, D15,
> D17}`** — *exactly* the seven on which `af-fit` and `optimize` disagreed under F67, which is the finding wave 17
> closed by intervention and named `DetectionBinning` as the cause. The coincidence is not used as evidence; it is
> named because it makes the choice of probe dataset auditable rather than arbitrary.

### §5.1 — Clauses, with the five-part statement

| clause | threshold, fixed here | P | W — which copy | S | A | E |
|---|---|---|---|---|---|---|
| **D20-V1** *validity: the probe really is a factor-2 run* | the probe log carries `detection binning (F39b): 2 from ` exactly once | `/mnt/d/hf_w20/dprobe/D12_c14_585_afbin2/run.log` | the per-run notice line, **not** any dump field | the integer captured by the regex | `== 2`, count `== 1` | line absent or factor `!= 2` => **COULD-NOT-LOOK, named** => **`D-UNEVALUATED`**. Never "0 of 1", never a drop |
| **D20-V2** *validity: one binary, the pins held* | `BuildId` on the probe landing equals the gate's, cardinality 1; `ProfileId` by containment **and** cardinality; `DetectorVersion == 2`; `ConcurrencyCheck == exclusive` | the probe's `optimized_settings.json` | `Provenance.*`, one copy each | equality / cardinality | all-of | any unreadable => COULD-NOT-LOOK, named, `D-UNEVALUATED` |
| **D20-A** *the decisive one* | `optimize/detected.DetectionBinning == 2` **and** `optimize/baseline.DetectionBinning == 1` in the same log | the two BEGIN..END ranges in the probe log | `DetectionBinning`, **named per block** | the two values | both must hold; rate **1 of 1**, denominator printed | either block missing => COULD-NOT-LOOK, named, `D-UNEVALUATED` |
| **D20-B** *the scale followed* | `optimize/detected.PixelScale` is finite **and** equals, to 6 dp, the value in the probe log's `detecting at PixelScale X arcsec/binned-px` line; `optimize/baseline.PixelScale` is the token `NaN` | the two ranges + the notice line | `PixelScale`, named per block, **and** the console line as the independent second source | equality to 6 dp; value class | all-of | any side missing => COULD-NOT-LOOK, named |
| **D20-C** *F69(c) fired* | the no-op notice appears **exactly once** in the probe log, and its text names both `--apply-run-detection-binning` and the fact that F39(b) is the default | the probe log | the notice line | count and containment | `== 1` and both substrings present | absent => **`D20-C` FAILS** (this is a FAIL, not a could-not-look: the flag was passed and the probe ran) |
| **D20-D** *corroboration — the whole difference set, not one field* | the set of the 55 field names on which `optimize/detected` differs from `optimize/baseline` in the probe log is **exactly** `{DetectionBinning, PixelScale}` | the two ranges | all 55 names, **per block**, no fallback across blocks | set difference | set equality, **the set printed in full either way** | either block missing => COULD-NOT-LOOK, named |
| **D20-E** *the factor-1 counterpart* | on the 8 gate logs the same difference set is **exactly** `{PixelScale}`, on 8 of 8 | the 8 gate logs' two ranges | as D20-D | set difference | set equality on **8 of 8**, rate printed | as D20-D, per log, named |

> **D20-D and D20-E exist because of [F71](followups.md)'s lesson**, and they are the cheapest possible form of it.
> Wave 18's control reduced a property to **one field** and was inverted; seven other fields in the same dump
> would have said so at zero cost. Here the "second field" is **all fifty-four others**, and the clause is stated
> as a *set equality* rather than a *containment*, so an unexpected field moving is a FAIL rather than a silent
> pass. **A two-directional demonstration proves the branches are distinguishable, not that they are correctly
> assigned** — the two directions here are factor 2 (D20-D) and factor 1 (D20-E), scored by the same code, and the
> thing that makes them non-circular is that the *expected sets differ* and both are fixed in advance.

### §5.2 — The branch table, fixed before the data

```
D20-V1 or D20-V2 does not pass                    -> D-UNEVALUATED  (name the gate; D1/D2/D3 STILL SHIPPED)
D20-A and D20-B and D20-C and D20-D and D20-E     -> D-DEMONSTRATED
G20-P2a passed but any of A..E failed             -> D-PRESENT-ONLY (name every failing clause; the block
                                                     ships and is reached, the post-mutation property is
                                                     NOT demonstrated)
G20-P2a failed                                    -> the GATE has failed; the wave stops at §4 and D20 is
                                                     never reached
```

**`D-PRESENT-ONLY` is a real, reachable outcome and it is not a failure of the ship.** It is what the register gets
if the dump is emitted at the wrong site. Naming it in advance is what stops a wave from quietly reporting
`D-DEMONSTRATED` on a block that happens to exist.

### §5.3 — What a `D-DEMONSTRATED` licenses, and what it does not

**Licenses:** that `optimize`'s post-mutation detector bundle is readable from the log, on a run whose resolved
factor is not 1, with the whole 55-field difference set accounted for. That is F69(b) discharged, and it is what a
future wave needs in order to read the params `optimize` actually detected with **without running an intervention
arm** — the instrument wave 16's RULE P16 lacked.

**Does not license:** anything about F67, which wave 17 **already closed by intervention** (`RULE C17`) — F69(b)
is not needed to close it and this wave does not re-open it. Nothing about `af-fit`. Nothing at other factors than
{1, 2}. Nothing about the joint (non-`--per-run`) path, where `ApplyRunDetectionBinningIfRequested` is **not
called at all** — see §9.3, which is a finding the probe cannot make and the source can.

---

## §6 — Controls

### §6.1 — [F15](followups.md): fingerprint class 1, the **42** bank landings
`python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --write|--check /mnt/d/hf_w20/bank_landing_fingerprint_BEFORE.json`

### §6.2 — F15: fingerprint class 2, the **59** aux files (`harness_settings.json` x39 + `synthetic_meta.json` x20)
`python3 /mnt/d/hf_w17/aux_fingerprint_w17.py --write|--check /mnt/d/hf_w20/bank_aux_fingerprint_BEFORE.json`

### §6.3 — Fingerprint class 3, the **48** prior-wave arm landings — **and its job in this wave is DIFFERENT**
`python3 /mnt/d/hf_w19/arm_fingerprint_w19.py --write|--check /mnt/d/hf_w20/w18_arm_fingerprint_BEFORE.json`
over `/mnt/d/hf_w18/seedA0` (20), `/mnt/d/hf_w18/seedA1` (20), `/mnt/d/hf_w18/gate` (8).

> In wave 19 this class was a control **on evidence** — R19 read `seedA0`. **Wave 20 reads none of it.** Here it is
> a **preservation** control: it proves the population §3.1 promises not to spend is provably unspent. Same file,
> different job, and the difference is stated rather than left for a reader to assume continuity.

### §6.4 — Fingerprint class 4 — **NEW: the wave-19 logs this pre-registration's denominators were counted on**
`python3 /mnt/d/hf_w20/log_fingerprint_w20.py --write|--check /mnt/d/hf_w20/w19_log_fingerprint_BEFORE.json`
over `/mnt/d/hf_w19/gate/*.log` (8) and `/mnt/d/hf_w19/reA0/D*/run.log` (20) — **28 files.**

> **Why it is new and why it is not decoration.** Every reachable-range number in §9 — *55 fields per block*,
> *`PixelScale=NaN` on 8 of 8 baselines*, *factor 1 on all 8 gate datasets*, *factor 2 on `D12`*, *`optimize/detected`
> on 0 of 28* — was counted **today, on those 28 files**. If one of them moves, the satisfiability analysis is
> about a population that no longer exists. Wave 17's generalization was *"a file class that becomes evidence must
> become a control in the same wave"*; **a file class that becomes a DENOMINATOR is evidence.**

**`gate_w20.sh` aborts unless all four `_BEFORE.json` files exist**, printing the four exact commands. Wave 19's
driver aborted on exactly this and the abort cost one minute; a guess costs a wave.

### §6.5 — Self-tests, all demonstrated **before** any number is quoted

| instrument | demonstrations required | notes |
|---|---|---|
| `prov_w20.py --self-test <arm>` | 2 directions on a `copytree`, **mutation asserted by read-back** | carried forward from `prov_w19.py`; the mutation rewrites one landing's `ProfileId` and must produce **>= 2** failures (the pin clause **and** the cardinality clause) |
| `score_d20_w20.py --self-test` | **>= 14** branches, listed in §7 | includes the two known-bad logs that `prov_w20.py` cannot construct |
| `log_fingerprint_w20.py --self-test` | **7** branches: population-assertion abort, write, clean check, changed, added, removed-as-could-not-look, missing snapshot | **demonstrated 7 of 7 at pre-registration time**, and the collector verified against the real 28 files (**28 of 28 byte-identical**) |
| `f21_probe_w20.sh --self-test`, `gate_w20.sh --self-test`, `detected_probe_w20.sh --self-test` | argument, path-direction and timebox handling with **no `TestApp.exe` launched**; `gate_w20.sh` additionally asserts the tag-collision property in both directions over all four tags | all three **demonstrated PASS at pre-registration time** |

> **One expected result the controller must not mistake for a broken instrument.** Running
> `prov_w20.py --self-test /mnt/d/hf_w19/gate` (i.e. against a **prior** wave's arm, as a smoke test) correctly
> returns **SELF-TEST FAIL**, because wave 19's `BuildId` `763d1476...` is now *in* `PRIOR_BUILD_IDS` and the
> novelty clause fires: *"BuildId matches a PRIOR wave -- NO BUILD HAPPENED"*. **That is the control working**, and
> it was verified at pre-registration time. Direction 2 was verified in the same run and produced **3** failures,
> including **both** `ProfileId` clauses. Against wave 20's own gate, direction 1 will pass on a novel id.

> **`prov_w19.py` carried forward is NECESSARY AND NOT SUFFICIENT.** Its self-test mutates `ProfileId`; it does not
> and cannot exercise **G20-P2**, whose failure mode is a *missing or mis-sited log block*. `score_d20_w20.py`'s
> self-test therefore constructs both known-bad logs explicitly (§7). A gate whose newest clause has no
> known-bad demonstration is wave 14's defect wearing this wave's clothes.

---

## §7 — `score_d20_w20.py --self-test`: the branches, fixed here

Each is a synthetic log built in a temp dir; none touches an arm.

| # | input | required output |
|---|---|---|
| 1 | all three blocks, factor 2, difference set exactly `{DetectionBinning, PixelScale}` | `D-DEMONSTRATED` |
| 2 | `optimize/detected` block **absent** | `G20-P2a` FAIL reported; `D20-A` COULD-NOT-LOOK; **`D-UNEVALUATED`** |
| 3 | `optimize/detected` present but **byte-identical to `optimize/baseline`** (the wrong-site mutant) | difference set `{}` != `{DetectionBinning, PixelScale}` => **`D-PRESENT-ONLY`**, naming `D20-A`, `D20-B`, `D20-D` |
| 4 | `optimize/detected.PixelScale` finite, `DetectionBinning` still 1, on a factor-2 run | **`D-PRESENT-ONLY`**, naming `D20-A` |
| 5 | notice line reads factor **1** | **`D-UNEVALUATED`**, naming `D20-V1` |
| 6 | notice line **absent** | **`D-UNEVALUATED`**, naming `D20-V1` as COULD-NOT-LOOK — *distinct from #5* |
| 7 | F69(c) notice absent, everything else good | **`D-PRESENT-ONLY`**, naming `D20-C` — and it is a **FAIL**, not a could-not-look |
| 8 | a **third** field also differs (e.g. `MinHFR`) | set equality fails => **`D-PRESENT-ONLY`**, naming `D20-D`, **with the unexpected field printed by name** |
| 9 | `optimize/detected` block present **twice** in one log | `G20-P2a` FAIL (`== 1` per log, not `>= 1`) |
| 10 | block with **54** field lines | `G20-P2b` FAIL, count printed |
| 11 | block with 55 lines but a **duplicate** name | `G20-P2b` FAIL on the distinct-count |
| 12 | log unreadable / absent | **COULD-NOT-LOOK, named**, never `0 of 1`, never silently dropped |
| 13 | a Windows path (`D:\...`) passed as an argument | **abort**, non-zero exit, before any field read |
| 14 | `PixelScale` value is the literal string `NaN` in the **detected** block | `G20-P2c` / `D20-B` FAIL — **`"NaN"` is a token, not a number**, and the comparison is on the token |
| 15 | an empty gate directory | rates are **UNEVALUATED**, never `1.000`, and never `8 of 8` |

**Every one of these is a branch the scorer must be shown to take before any real number is quoted.** #3 and #4
are the two known-bads that matter: they are what a dump written at the wrong call site actually produces.

---

## §8 — The interlock markers

The pattern has stopped five rungs and four arms across four waves from producing output nobody could quote.
Wave 20 uses three:

| marker | written by | required by | contents |
|---|---|---|---|
| `/mnt/d/hf_w20/G20_PASSED` | `score_w12.py` + `prov_w20.py` + the driver's own G20-2/P1/P2 assertions, **all of them** | `detected_probe_w20.sh` refuses to launch without it | the eight values, the `BuildId`, and the G20-P2 counts |
| `/mnt/d/hf_w20/D20_PROBE_READY` | `detected_probe_w20.sh` after asserting `D20-V1` on its own log | `score_d20_w20.py` refuses to issue a verdict without it | the resolved factor and the probe log path |
| `/mnt/d/hf_w20/SHIPPED_D1` | the build step, after asserting `strings`-free evidence: the built binary's own gate log carries the block | — reported, and it is the arm-level twin of wave 19's *"0 of 28"* | the per-log block counts |

**A scorer that cannot find its marker prints the exact command that writes it and exits non-zero.** *An abort
that prints the fix costs one minute; a guess costs a wave.*

---

## §9 — Satisfiability, **in two parts**

### §9.1 — Part A: every clause's reachable range, computed on the real artifacts **today**

Counted on the 28 wave-19 logs and 8 wave-19 gate landings named in §0, fingerprinted as class 4 (§6.4).

| clause | the **0 end** — observed or constructed? | the **1 end** — observed or constructed? | verdict |
|---|---|---|---|
| **G20-1** | never observed in 10 waves; **reachable** — a moved seed produces it, which is why G20-P1 exists | **OBSERVED** on ten binaries, bit-identical at 16 digits | satisfiable, both ends |
| **G20-3a** novelty | **reachable** — a rebuilt-without-changes binary still gets a new MVID, so the 0 end needs a *reused* directory; the driver's populated-root abort is what makes it unreachable by accident | **OBSERVED** ten times | satisfiable |
| **G20-P2a** | **OBSERVED TODAY: 0 of 8 gate logs and 0 of 20 arm logs carry the block.** This is the *current* state of the tree | requires **exactly the code this wave ships**, and nothing else in the wave can produce it | **This is the ideal shape: the FAIL end is measured on real artifacts and the PASS end is reachable only via the shipped code** |
| **G20-P2b** (55 / 55) | constructible (self-test #10, #11) | **OBSERVED: `optimize/baseline` and `optimize/seed` each carry exactly 55 fields, 55 unique, on 8 of 8 gate logs.** The new block is the same reflective printer over the same type, so 55 is arithmetic, not hope | satisfiable |
| **G20-P2c** (`detected.PixelScale` finite; `baseline.PixelScale` == `NaN`) | constructible (self-test #14) | **OBSERVED for the `NaN` half: `optimize/baseline.PixelScale` is the token `NaN` on 8 of 8.** The finite half is guaranteed by the call order — `ctx.Baseline.PixelScale = loaded.PixelScale` executes before `ApplyRunDetectionBinningIfRequested`, and the per-run scale is finite on 8 of 8 (`1.32574, 1.4101, 0.775556, 1.4101, 0.288525, 1.03356, 0.73944, 0.711519`) | satisfiable, and **this is the clause that makes the gate's own copy of the block non-vacuous** |
| **G20-P2d** (`DetectionBinning == 1` on 8 of 8) | constructible | **OBSERVED: factor 1 on all 8 gate datasets** | satisfiable — **and see the warning below** |
| **G20-P2e** (no collision) | constructible | **OBSERVED: exactly 1 `optimize/baseline BEGIN` and 1 `optimize/seed BEGIN` per log, on 8 of 8, today** | satisfiable |
| **D20-V1** (factor 2) | **OBSERVED** on 13 of the 20 synthetic datasets (factor 1) | **OBSERVED on `D12_c14_585_afbin2`: `detection binning (F39b): 2 from synthetic_meta.json`** | satisfiable, and the probe dataset is chosen *because* the 1 end is what every other candidate gives |
| **D20-A** | **OBSERVED** as the current state (no block at all) | **arithmetically forced** once the block ships at the right site: `DetectionBinningResolver.ApplyFactor(ctx.Baseline, 2)` runs between the two dumps | satisfiable **on the probe and ONLY on the probe** |
| **D20-D** (set == `{DetectionBinning, PixelScale}`) | reachable in three ways (self-tests #3, #4, #8) | requires the mutation to touch exactly those two — which is what `ApplyFactor` does by construction (`DetectionBinning` and `PixelScale` together) plus the per-run scale assignment | satisfiable |
| **D20-E** (set == `{PixelScale}` on the gate) | reachable | requires factor 1, which is **observed on 8 of 8** | satisfiable |
| **D20-C** | reachable (self-test #7) | requires D2, which the probe passes the flag for | satisfiable |

> ### The one clause that is UNSATISFIABLE, named here rather than discovered later
>
> **A clause of the form *"`optimize/detected.DetectionBinning` DIFFERS from `optimize/baseline`'s"* is
> UNSATISFIABLE ON THE GATE POPULATION**, because the resolved factor is 1 on all eight gate datasets. Wave 19's
> own gate driver header contemplated exactly such a check. It is **not** written into G20; **G20-P2d asserts
> equality to 1 instead**, and the difference is carried by RULE D20's probe on a dataset chosen for the purpose.
> *This is the section that, in three of the last six waves, contained the defect it exists to prevent
> ([F68](followups.md)) — so it is stated as a positive finding, not as an absence.*

### §9.2 — Part B: both branches of every clause are reachable, **and where each one routes on an empty domain**

| clause | PASS reachable | FAIL reachable | **empty domain routes to** |
|---|---|---|---|
| G20-1 | yes (observed x10) | yes | **FAIL** — a stopping gate treats could-not-look as has-not-passed |
| G20-3c | yes | yes, on **both** the containment and the cardinality sub-clause (the self-test's direction 2 must produce **>= 2** failures) | the **cardinality** clause; `all()` over empty is vacuously true and must not carry it |
| G20-P2a | yes (with the code) | yes (observed today) | **FAIL**, named |
| G20-P2b/c/d/e | yes | yes (self-tests #10/#11/#14, and #9) | **FAIL**, named |
| D20-V1 | yes (`D12`) | yes (#5) | **`D-UNEVALUATED`**, named — *away from* `D-DEMONSTRATED` |
| D20-A/B/D/E | yes | yes (#3, #4, #8) | **`D-UNEVALUATED`**, named — *away from* the verdict the wave wants |
| D20-C | yes | yes (#7) | **FAIL**, because the flag was passed and the run completed |
| every rate | yes | yes | **UNEVALUATED, never `1.000`, never the literal count** (#15) |

> **Every empty domain in RULE D20 routes to `D-UNEVALUATED`, i.e. AWAY from the outcome the wave is hoping for.**
> That is wave 17's `C17-D`/`D17-1` construction reused, and it is the direct answer to
> `P16-INSTRUMENT-vs-PRODUCT`'s defect: *a quantifier whose domain can be empty is a denominator wearing different
> clothes.* No clause in this wave is a universal quantifier over a set that could be empty without that being a
> named, verdict-changing state.

### §9.3 — One thing the source says that no clause here measures, recorded as a source observation

`ApplyRunDetectionBinningIfRequested` has **exactly one call site**, at `OptimizationDiagnosticRunner.cs:660`,
inside `RunPerRun`. **The joint (non-`--per-run`) path never calls it.** So on the joint path the two existing
dumps are already the detecting bundle, and the new `optimize/detected` block will not be emitted there.

This is stated as a **source counting argument, not a measurement**, and no clause consumes it — every arm in this
series is `--per-run`. It is recorded because F69(b)'s next reader will ask, and because the alternative — a
future wave discovering the joint path silently lacks the block and reading it as a regression — costs more than
this paragraph.

### §9.4 — The self-audit of this section, **checked against its own clauses**

Wave 17 wrote a self-audit it had not checked and was wrong; wave 19 caught its own design claiming an unshipped
item. So, mechanically:

- **Does every clause in §4.1, §4.2 and §5.1 have all five columns filled?** Yes — P, W, S, A, E on each of the
  10 G20 clauses, the 5 G20-P2 clauses, and the 7 D20 clauses. **22 clauses, 110 cells.**
- **Is any threshold stated as an absolute count whose denominator was borrowed from another instrument?**
  The counts are `8` (gate landings/logs), `55` (fields per block), `1` (per-log block count), `2` (the resolved
  factor). **Every one was counted today on the artifact the clause reads** (§6.4), not carried from a different
  view of the same runs — which is exactly `S16-A(b)`'s failure.
- **Does any clause read a field that exists in more than one copy without naming the copy?** No. The **W** column
  is mandatory and this is the wave that **adds a third copy of every field name to every log**, so the hazard is
  at its maximum here. Every read of `NoiseReductionRadius`, `PixelScale` and `DetectionBinning` is scoped to a
  named BEGIN..END range keyed on the exact tag.
- **Is any clause's PASS branch unreachable, or its FAIL branch?** §9.2 lists both for all of them, and §9.1 names
  the one clause that *would* have been unreachable and was therefore not written.
- **Does the wave claim a gate measures code that is not in its binary?** §4.3 is the whole answer, including two
  rows that say *"reached: NO"*.
- **Is any item shipped conditional on a rule whose failure would be convenient?** No — D1/D2/D3 ship
  unconditionally and RULE D20 governs only the claim.

---

## §10 — Register and results corrections this wave owes

Fixed here, before the data, so they are not "found" later:

1. **[F71](followups.md): `C19-D2` is NOT still owed.** It ran at `05:43:28Z` on 2026-08-11 and
   `/mnt/d/hf_w19/C19_PROBE_PASSED` exists reading `C-DEMONSTRATED`. The entry's *"Still owed: the live direction,
   `C19-D2`, ~1 minute"* is wrong. **Wave 20 does not re-issue RULE C19's verdict** (§0.1).
2. **`docs/synthetic-af-bank-followups-wave19-results.md` §3, §8 and §10.6 are wrong about the same thing**, and
   §10.6's lesson ("the cheapest thing left undone was the one-minute one") is **false as stated**. The true
   lesson is §0.1's: **a write-up that races its own wave falsifies its own re-derivation claim.**
3. **[F69](followups.md)(b)/(c) shipped in wave 20** — recorded with the arm-level evidence
   (`optimize/detected` on n of 8 gate logs), so the pattern of a design being read as evidence of shipping is
   broken in the other direction too.
4. **[F70](followups.md): the manual is fixed** at both `:23` and `:41`, with the derivation named
   (`Typical => 3`, plus the `+1` hotpixel compensation, `= 4`). **Two waves overdue.**
5. **[F59](followups.md): 4 of the 5 missing knobs are preset-owned** (§3.4) and repairing the exporter changes
   nothing for them under the current pin. New costing, not a verdict.
6. **[F21](followups.md): priced or dropped a fourth time** — whichever happens, it is recorded with the reason
   and the clock time at which the drop-order fired.
7. **The charter's suite baseline (§6 step 8, "3781") is stale.** It is **3831** (wave 18, verified by COUNT).

---

## §11 — Where the controller's brief is wrong or incomplete

Stated plainly, as the charter §6 step 1 requires.

1. **The `git status` in the brief is stale for the third consecutive wave** (§0). HEAD is `95753a9`, the tree is
   clean. This is no longer a slip; it is a property of how the brief is assembled, and `G20-P1` is the permanent
   guard.
2. **The brief inherits wave 19's Lesson 6, which is false.** `C19-D2` was run (§0.1). The brief's framing —
   *"Wave 19 left a one-minute item nearly undone"* — is close to right for the wrong reason: the item was
   **completed and then mis-reported**, which needs a different fix (measure before you write) than a drop-order
   floor. **Both fixes are adopted**; only one of them was the actual defect.
3. **"Shipping it makes `optimize`'s post-mutation params readable — which is what F67 needed"** overstates it.
   **F67 was closed in wave 17 by intervention** (`RULE C17`), and F69(b) is not needed to close it. What F69(b)
   buys is that the *next* such question can be answered by reading a log instead of running an intervention arm.
   The value is real and it is prospective.
4. **"Three fingerprint classes must be written before the gate"** is a floor, not a ceiling, and in wave 20 the
   third class does a **different job** than in wave 19 (§6.3) — it guards a population this wave never reads. A
   **fourth** class is added (§6.4) over the 28 wave-19 logs, because **this pre-registration's denominators were
   counted on them** and a denominator is evidence.
5. **The charter's stated suite baseline of 3781 (§6 step 8) is stale**; it is 3831.
6. **The brief's F59 framing is right that wave 18's objection lapsed, and the conclusion still stands** — but not
   for wave 19's reason. §3.4 gives the merits ground, measured from source today: **4 of the 5 knobs are
   preset-owned and would be overwritten on load**, so the cheap half of F59 changes nothing at all.
7. **One thing the brief gets exactly right and it deserves saying**: *"if an item's code cannot be reached by the
   gate, say so and name the clause that does reach it."* §4.3 is that table, and **two of its three rows say
   "reached: NO"**. A wave that could not write those two rows honestly should not ship those two items.

---

## §12 — Budget, with the **12:00Z cutoff explicit**

Rates, never crossed ([F21](followups.md)'s own warning): `optimize` mixed **~5.2 m/run** (five waves), `optimize`
all-synthetic **~2.64–2.73 m/run** (measured twice), `af-fit` **~11 s/dataset**. **`synth-validate` has NO measured
rate at all** — which is item P's entire point.

| # | step | instrument | estimate | cumulative from **07:15Z** |
|---|---|---|---|---|
| 0 | commit the pre-registration | git | 2 m | 07:17Z |
| 1 | **D3 (the manual) committed ON ITS OWN, before anything else** | text | **2 m** | **07:19Z** |
| 2 | D1 + D2 + tests | code | 35 m | 07:54Z |
| 3 | suite, verified by COUNT | `dotnet test` | 4 m | 07:58Z |
| 4 | build into `D:\hf_w20\exe`, record dll sha256 + `BuildId` | `dotnet build -c Release` | 1 m | 07:59Z |
| 5 | four fingerprint classes, `--write` | Python | 2 m | 08:01Z |
| 6 | **RULE G20 — the gate** | `optimize --per-run --max-evals 250`, 8 mixed | **42 m** | **08:43Z** |
| 7 | score G20 (`score_w12.py`, `prov_w20.py` both directions) | Python | 8 m | 08:51Z |
| 8 | **the detected probe** (`D12`, 1 run, all-synthetic) | `optimize`, 1 run | **3 m** | 08:54Z |
| 9 | score RULE D20 (self-test 15 branches, then the verdict) | Python | 10 m | 09:04Z |
| 10 | **item P — the F21 price probe** | `synth-validate`, hard `timeout 1200` | **<= 20 m** | 09:24Z |
| 11 | re-check all four fingerprint classes | Python | 3 m | 09:27Z |
| 12 | analysis + results document | writing | 60 m | 10:27Z |
| 13 | final suite by COUNT + commit + push + PR section + CI by COUNT | git / CI | 45 m | **11:12Z** |
| | **slack to the 12:00Z cutoff** | | | **48 m** |
| | **`TestApp` wall** | | **~65 m** | against a 6 h ceiling and a **~5 h** clock |

**The controller will not start a new arm after 12:00Z.** The only arms are steps 6, 8 and 10; the last of them is
scheduled to *finish* at 09:24Z, **2 h 36 m before the cutoff**. Every remaining minute is Python, writing and git.

---

## §13 — Drop order — **with a FLOOR, and cheapest-decisive first**

Wave 19's Lesson 6 asked for a floor. Here it is, and it binds before anything else:

> ### THE FLOOR: **an item that costs under two minutes is NEVER dropped, at any clock time, for any reason.**
> That is **D3** (the manual, ~2 m, zero compute, two waves overdue) and **item R** (the register corrections,
> 0 m). D3 is scheduled at **step 1, before the build**, precisely so a cutoff cannot reach it.

**Execution order is cheapest-decisive-first**, so what a cutoff removes is always the most expensive and least
decisive thing remaining:

| trigger | drop | what it costs | what survives |
|---|---|---|---|
| **D0** — *never* | D3, item R | — | the floor |
| **D1** at **09:30Z** | **item P**, the F21 price probe (20 m) | F21 stays unpriced a **fourth** consecutive wave. Rule-free, so **no verdict moves**. Recorded in the register with the clock time | everything else |
| **D2** at **10:00Z** | **the detected probe** (step 8, 3 m) and RULE D20's verdict | **`D-UNEVALUATED`**, naming `D20-V1` as could-not-look. **D1/D2/D3 still shipped**, and **G20-P2 still proves the block is present, correctly shaped, post-`PixelScale`-assignment and non-colliding on 8 of 8** — the `DetectionBinning` half alone is lost | the gate, the ship, G20-P2 |
| **D3** at **11:00Z** | the results document's **prose**; it becomes a STATUS STUB | the PROVENANCE table, the Status table, **every clause's threshold beside its measured value**, §"what was not run" and §"corrections" are **all retained**. Only narrative is cut | every number |
| **D4** at **11:30Z** | the PR-body section is reduced to a link + the Status table | reviewers read the doc instead | the commit and the push |

> **What is deliberately NOT in the drop order:** the gate (a stopping gate cannot be dropped — if it does not run,
> nothing may be quoted), the four fingerprint classes (~2 m, and they are the only thing standing between this
> wave and an unfalsifiable claim), and the scorer self-tests (a gate that has not been shown to PASS and to FAIL
> is not a gate, [F66](followups.md)(a)).

---

## §14 — What this wave will NOT run, and what it costs

In cost order, cheapest first — the ordering wave 19 introduced and which is what surfaced its own one-minute gap.

- **Re-issuing RULE C19's verdict — 0 m, deliberately.** The marker exists and reads `C-DEMONSTRATED` (§0.1).
  Re-issuing a prior wave's verdict from a later wave is re-deciding a rule after the data. **Cost: RULE C19 stays
  `C-UNEVALUATED` in the record until a wave pre-registers a rule that consumes it.**
- **The out-of-sample pass over wave 18's 40 arm landings — ~8 m, and no driver exists that could.** §3.1.
  **Cost: F70(a) stays with the owner.** The population is preserved and provably untouched (class 3).
- **F70(b′), the 20 dead literals — ~20 m + a mutant-demonstrated parametrized test.** §3.2. **Cost: editing one of
  the 20 remains a silent no-op.**
- **The other 11 `BuildDefaultStarDetectorParams` call sites — ~30 m each.** Moot while the seed literal does not
  ship. **Cost: none this wave.**
- **A `DetectionBinning`-differs clause on the gate — 0 m, because it is UNSATISFIABLE there.** §9.1. **Cost: none —
  the probe carries it.** Recorded because writing it would have looked like rigour.
- **The joint-path (`--per-run` absent) behaviour of the new dump — unmeasured.** §9.3 settles it in source: the
  block is not emitted there, because `ApplyRunDetectionBinningIfRequested` has one call site and it is in
  `RunPerRun`. **Cost: a source argument where a measurement would be nicer; ~5 m of `optimize` to measure, and
  nothing in the register depends on it.**
- **[F59](followups.md)'s five knobs — ~1 h. Rejected on the merits** (§3.4), with a new costing that makes the
  next consideration cheaper. **Cost: `pinned_settings_w11.json` still does not describe `SaturationThreshold`.**
- **[F45](followups.md)(b)'s production plumbing — ~3–4 h + tests. Rejected on substance** (§3.3). **Cost: the SEM
  criterion stays unshipped, which is what NO RECOMMENDATION means.**
- **Any repaired form of `N18-V7`, `S16-A(b)`, wave 16's rungs, or wave 14's `V4`/`V4'`; any re-opening of RULE F14
  or RULE S16 — 0 m, deliberately**, per [F68](followups.md)(c) and charter §3 and §3a.
- **Re-pinning the settings file — 0 m.** [F63](followups.md)(b), DO-NOT-RE-PIN.
- **A fresh eight-value baseline — not owed.** Nothing this wave ships can move the search, and G20-1 measures that
  rather than assuming it.
- **Item C / the NINA UI items A1, A2, A4–A8 — ~30 m** on a connected session with NINA not competing with a pinned
  arm. **Not attempted: this wave runs pinned arms and the charter forbids NINA during them.** The blocker is gone
  ([F72](followups.md)); only the work and the scheduling remain.
