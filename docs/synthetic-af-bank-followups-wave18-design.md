# Synthetic AF bank — followups wave 18 (design / pre-registration)

Charter: [`docs/waves17-21-handoff-prompt.md`](waves17-21-handoff-prompt.md).
Wave 17: [`docs/synthetic-af-bank-followups-wave17-results.md`](synthetic-af-bank-followups-wave17-results.md).
Wave 16: [`docs/synthetic-af-bank-followups-wave16-results.md`](synthetic-af-bank-followups-wave16-results.md) — **and
its CORRECTION block.** Register: [`docs/followups.md`](followups.md).
Plan: [`plans/synthetic-af-bank-followups-wave18-plan.md`](../plans/synthetic-af-bank-followups-wave18-plan.md).

> ## PROVENANCE — what is fixed by this document, before any measurement
>
> | input | value |
> |---|---|
> | tree | branch `ghilios/synthetic-af-bank-followups-wave13`, PR #191. **This document and the plan are committed before either binary is built.** |
> | code shipped by this wave | **YES — item A, in two parts.** Part 1 is in **both** binaries; Part 2 is in the second only |
> | binaries — **TWO, planned up front (F53(c))** | **B1** `D:\hf_w18\exe_b1` = HEAD + Part 1 · **B2** `D:\hf_w18\exe_b2` = B1 + Part 2. Record both dll sha256 pairs and both `BuildId`s. Neither is ever rebuilt mid-wave |
> | settings **S0** | `D:\hf_w11\pinned_settings_w11.json` md5 **`a67ffc06164c81613aef5c4f8324b9b8`**. **Not re-pinned** ([F63](followups.md)(b) closed as DO-NOT-RE-PIN in wave 17) |
> | profile | `astrodet` `ce3f3e63-8fd3-4b72-a0ca-d90db9441382`, pinned on **every** arm |
> | banks | `D:\SyntheticAutofocusBank` (20 datasets, `renderRequest.OptimalFocuserPosition` = truth), `D:\Autofocus Bank` (19 runs) |
> | F15 controls | **both classes, before the gate and after every arm**: 42 bank landings (`bank_fingerprint_w15.py`) and 59 aux files (`aux_fingerprint_w17.py`). Every driver aborts without both BEFORE files |
> | suite | baseline **3824** (wave 17). Item A adds tests and changes two existing assertions; the final count is named in the results doc and verified by COUNT ([F37](followups.md)) |
>
> **No fan-out.** Every arm is `--profile-id`-pinned and a pinned arm cannot fan out at all (wave 11's K3);
> the authorisation is worth 1.33×, not 4× ([F60](followups.md)). Wave 5's φ table is quoted nowhere.
>
> **[RULE F14 is closed and is not re-opened here. RULE S16 is not re-scored here](waves17-21-handoff-prompt.md).**
> Nothing in this wave reads wave 14's `sem_audit.tsv`, wave 16's rungs, or any repaired form of `S16-A(b)`.

---

## §0 — Items

| item | what | rule | ships? | `TestApp` cost |
|---|---|---|---|---|
| the gate | eight values on **B1**, and the measurement that Part 1 is inert | **RULE G18** | — | ~42 m |
| **A** | **[F70](followups.md)** — `NoiseReductionRadius` has two shipped defaults, `ResetDefaults()` is *non-deterministic* between them, and the drift guard is green on a path no load takes. Plus [F69](followups.md)(a)'s four-line comment | **RULE N18** | Part 1 unconditionally; **Part 2 iff N18 returns `N-ALIGN`** | ~110 m |
| **B** | **NOTHING**, argued in §3 with all three candidates rejected in writing | — | — | 0 |
| **C** | the A1–A9 UI check | — | **BLOCKED, blocker named** (§4) | 0 |

**Estimated wave `TestApp` wall: ~2 h 32 m**, or ~3 h 14 m if the conditional `G18'` re-baseline fires
(§6), against a **6 h** ceiling. Code + tests ~2 h.

---

## §1 — RULE G18: the gate, and this wave's stopping gate

Eight runs, `optimize --per-run --max-evals 250`, both pins named in the driver header, sequential, one
`TestApp.exe`, no NINA. Driver `/mnt/d/hf_w18/gate_w18.sh`. **It runs on B1 and only on B1** — §2.6 is why.

### 1.1 Every clause, with its (P)opulation, (S)tatistic, (A)ggregation and (E)mpty answer

| clause | threshold, fixed here | P — artifact + field | S | A | E — what it returns on an empty set |
|---|---|---|---|---|---|
| **G18-1** | all eight `BestJ` reproduce the K8 table to **6 dp**. A partial reproduction is a FAILURE and stops the wave | `FinalJ` in the 8 `optimized_settings.json` under `/mnt/d/hf_w18/gate` | equality to 6 dp | all-of, printed `n of 8` | an unreadable landing is COULD-NOT-LOOK, **named**, and **G18-1 FAILS**. For a *stopping* gate, "could not look" is not "undecided" — it is "has not passed" |
| **G18-2** | `aggregate_summary.json produced: 8`, asserted **in the driver before any scorer runs** | `find` over the gate root | count | `== 8` | 0 found ⇒ FAIL |
| **G18-3a** | exactly **one** `BuildId`, **novel** against `5cb7e474`, `103d61c4`, `62334f10`, `084e3485`, `df3a867d`, `10bc1b47`, `932a1366` | `Provenance.BuildId` ×8 | set | cardinality 1 **and** disjointness | empty set ⇒ FAIL. **Two tables, never merged**: `KNOWN_DLL_SHA256` is separate and the scorer fails loudly if a `BuildId` starts with a known dll hash ([F66](followups.md)) |
| **G18-3b** | `DetectorVersion` **2**, read as the **FIELD** | `Provenance.DetectorVersion` ×8 | set | `== {"2"}` | empty ⇒ FAIL. `strings -el` appears only as a flag-presence probe, never as a detector check |
| **G18-3c** | `ProfileId` contains `ce3f3e63-…` **and** cardinality exactly 1 | `Provenance.ProfileId` ×8 | containment + cardinality | **both, evaluated separately** | `all()` over an empty list is vacuously true, so the **cardinality** clause carries an empty read; empty ⇒ FAIL |
| **G18-3d** | one distinct `FitInputs`, exactly `MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid` | `Provenance.FitInputs` ×8 | set | `== {expected}` | empty ⇒ FAIL |
| **G18-3e** | `ConcurrencyCheck == exclusive` on **all eight**, read **across the arm** | `Provenance.ConcurrencyCheck`, as a list carrying an explicit `None` per unreadable landing | equality | all-of over a length-8 list | a `None` is a FAIL, not a skip — the list is built so an empty read cannot shorten it into agreement |
| **G18-3f** | `BaselineJ` reproduces wave 11 ([F41](followups.md)) to 6 dp | `BaselineJ` ×8 | equality | `n of 8` | missing ⇒ FAIL |
| **G18-4** | `prov_w18.py --self-test` PASSES on the real arm and FAILS on a mutated copy, **with the mutation asserted by read-back**, before any G18 number is quoted | the arm and a `copytree` of it | — | both directions | a mutation that cannot be made prints **SELF-TEST COULD NOT RUN** — not a pass, not a fail |
| **G18-P1** | the gate's own `optimize/seed` block reads `NoiseReductionRadius=3` on **8 of 8** logs, asserted **in the driver** | the `PARAMS-DUMP optimize/seed` block in the 8 gate logs | field value | `== 8` | a missing block ⇒ the driver refuses to hand the arm on. **If this reads 4, Part 2 is in the gate's binary and the gate is not a gate** |

The eight values, exactly as they must reproduce:

```
toml999    0.9957838768299878   CWhiteFocus 0.9960675916058808   uneven 0.9963677194179505
muggsie    0.9971948738498605   mccomiskey  0.9767460801208465
D18        0.9998815090506263   D19         0.9994870586135448   D20    0.9997378027339423
```

### 1.2 What a PASS proves, stated narrowly

Two things and no more. **(a)** A ninth binary reproduces the coordinate system. **(b) Part 1 is inert on
every harness path** — that is a *measurement*, not an assumption, and it is the reason Part 1 is in the
gate's binary rather than beside it. `StarDetectionOptions.ResetDefaults()` is reachable from exactly one
call site in the whole product, `HocusFocusPlugin.cs:224`, the UI "restore defaults" `RelayCommand`; no
`TestApp` command reaches it, the rewritten drift guard is a test, and F69(a) is a comment.

**If G18-1 fails, Part 1 is the first suspect and the wave stops.** It is not reported as a machine problem.

### 1.3 What it does not prove

It says nothing about Part 2, which is deliberately absent from B1. It runs at
`MaxOutlierRejections = 0`, so it says nothing about the rejection path.

---

## §2 — Item A: F70, and the decision the register asked for

### 2.1 The facts, all read from source at pre-registration time

`NoiseReductionRadius` has **seven** places that assert a default. They do not agree.

| # | where | value | who reaches it |
|---|---|---|---|
| 1 | `StarDetectionOptions.cs:266` accessor fallback | 3 | the backing field, immediately overwritten |
| 2 | `StarDetectionOptions.cs:348` `ResetDefaultsImpl` | 3 | the UI "restore defaults" button — **or 4, see 2.2** |
| 3 | `StarDetectionOptions.cs:185 + 223` `DerivePresetSettings` | **4** | **every construction, every profile change, every preset edit — the value the product runs and persists** |
| 4 | `HocusFocusStarDetection.cs:425` `BuildDefaultStarDetectorParams` | 3 | the optimizer's seed: the wizard (`RunEvaluationLoader.cs:200`) and 11 `TestApp` call sites |
| 5 | `IStarDetector.cs:244` `StarDetectorParams` class default | 3 | `new StarDetectorParams()` in tests and tools |
| 6 | `documentation/docs/settings/preprocessing.md:23` | 3 | the user manual |
| 7 | `app.config:73-75` legacy `Properties.Settings` | 0 | dead — the plugin reads only `IPluginOptionsAccessor` |

The mechanism is `StarDetectionOptions.cs:221-224`, and it is **deliberate and correct**:

```csharp
if (HotpixelThresholdingEnabled && HotpixelFiltering) {
    // Without thresholding, hotpixel filtering does a blur. To compensate, we increase the noise reduction radius
    NoiseReductionRadius += 1;
}
```

Both operands default `true`, so on a fresh install `Typical ⇒ 3` then `+1 ⇒ **4**`, and the setter persists 4.
**The `+1` does not compound**: the switch assigns `NoiseReductionRadius` absolutely (`:175/180/185/190`) on
every invocation before the increment, and the enum is covered exhaustively, so `DerivePresetSettings` is
idempotent at 4 no matter how many times it re-enters. That hypothesis is ruled out, not left open.

**`ResetDefaultsImpl` is a hand transcription of `DerivePresetSettings`' output at Typical/Typical/Typical.**
The two methods share **20** properties. **Nineteen agree literal-for-literal.** The twentieth is
`NoiseReductionRadius`, and it is the only one of the twenty that the derivation *post-adjusts*. That is a
transcription that copied `:184-185` and not `:221-224`.

### 2.2 The finding F70 does not have: `ResetDefaults()` is not deterministic

`ResetDefaultsImpl`'s **last statement** is `UseOptimizedSettings = false` (`:394`). `UseOptimizedSettings` is
a member of `SimplePropertyNames` (`:108`), so its setter's `RaisePropertyChanged()` re-enters
`StarDetectionOptions_PropertyChanged` (`:240`) → `ConfigureSimpleSettings()` → `DerivePresetSettings()` →
**4**. But every setter in this class is change-guarded, so that re-entry happens **only when the flag was
actually on**.

> **`ResetDefaults()` returns `NoiseReductionRadius = 4` when "Use Optimized Settings" was on and `3` when it
> was off. The same button, two detectors, decided by a checkbox the button itself clears.**

This is not hypothetical. Measured at pre-registration time over the **9** `*.profile` files in
`%LOCALAPPDATA%\NINA\Profiles` (`score_n18_w18.py --reach`, and the numbers below are its real output):

| profile | UseAdvanced | UseOptimizedSettings | stored radius | effective | `ResetDefaults()` would leave |
|---|---|---|---|---|---|
| `Default-2026-08-05T10:57:42` | absent | absent | 4 | 4 | **3** |
| `Default-2026-08-03T08:53:37` | absent | absent | 4 | 4 | **3** |
| `Default-2026-08-05T10:54:36` | absent | absent | absent | 4 | **3** |
| `Default-2026-08-05T10:57:40` | absent | absent | absent | 4 | **3** |
| `astrodet` | false | **true** | 3 | 3 | **4** |
| `Default` | false | **true** | 3 | 3 | **4** |
| `40mm` | false | **true** | 3 | 3 | **4** |
| `AA1600MM` | false | **true** | 4 | 4 | **4** |
| `AA1600MM Copy` | false | **true** | 7 | 7 | **4** |

**`ResetDefaults()` lands on 4 on 5 of 9 and on 3 on 4 of 9.** And the drift guard's own fixture
(`InMemoryPluginOptionsAccessor`, virgin, `UseOptimizedSettings` absent) takes the 3-branch — the minority
branch of a non-deterministic method, on a path no load takes. *It is green twice over.*

A second free observation from the same table: **5 of 9 profiles carry an optimizer snapshot**, and
`ApplyOptimizedSnapshotToLiveProperties` (`:143`) applies the landing's radius *after* `DerivePresetSettings`,
so those five run whatever the optimizer last recommended — 3, 3, 3, 4 and 7. Which brings us to what the seed
literal actually decides.

### 2.3 What changing it does to users — quantified, on the real bank

`BuildDefaultStarDetectorParams()` is **the bundle the optimizer starts from** —
`OptimizationDiagnosticRunner.cs:348` says so in those words and `:373` does it; the shipped wizard's
equivalent is `RunEvaluationLoader.cs:200`. `NoiseReductionRadius` is a **curated search variable**
(`OptimizerVariable.cs:184`, integer 0–10 step 1, one of the 12 unconditional axes) and an **EARLY**
cache-key parameter (`StarDetector.cs:153`), so a candidate that moves it forces a full re-detect.

**Measured, at pre-registration time, on wave 15's `land_mor0` — the identical invocation on these exact 20
datasets:**

> **The landing's `NoiseReductionRadius` equals the seed's `3` on 18 of 20 synthetic datasets.** The two
> exceptions are `D01_ultrawide_40mm` and `D04_esprit_550mm`, both landing at 6 — and both are the datasets
> whose `BaselineJ` is exactly **0.0** ([F20](followups.md)).

At 250 evaluations the search barely moves this axis. **So the hardcoded seed literal is, in effect, the value
the wizard recommends on 90 % of this bank** — and the value it currently recommends is one no Simple-mode
default configuration ever holds.

What it costs at runtime, stated precisely — **and this corrects F70's own text.** F70 says the field "sets
the measurement-image smoothing kernel at `CvImageUtility.ConvolveGaussian(srcImage, srcImage, …)` — 7 px
against 9 px", citing `StarDetector.cs:535`. **That call is gated by `StarMeasurementNoiseReductionEnabled`,
which is `false` on every default path** (`ResetDefaultsImpl:347`, `BuildDefaultStarDetectorParams:421`, the
`Typical` preset `:184`), so at defaults it never runs. The call that *does* run is **`:556`**, on
`noiseReducedImage` — **the structure-map source**. The 7-vs-9 px difference is real; the image it acts on is
the one that decides **which stars are detected**, not the one that measures their HFR. It also moves the
`KappaSigmaNoiseEstimate` at `:562` that sets the binarization threshold. That is a stronger statement than
F70's, not a weaker one, and F70's line should be corrected when the register is updated.

> **The user manual already says this, two rows above the row that is wrong.**
> `documentation/docs/settings/preprocessing.md:22` describes `StarMeasurementNoiseReductionEnabled` as
> *"Also blur the **measurement** image, not just the structure-detection image"* — i.e. by default only the
> structure-detection image is blurred. Line 23 of the same table then gives the radius's default as **3**.
> One line of the manual corroborates the correction and the next line carries the bug.

### 2.4 The decision: **4 is the shipped default; 3 is the preset's pre-compensation base**

Four independent arguments, in order of strength:

1. **The transcription argument.** 19 of 20 shared literals agree; the one that differs is the one with a
   post-adjustment. That is an omission, not a decision.
2. **The internal-consistency argument.** `BuildDefaultStarDetectorParams()` sets `HotpixelFiltering = true`
   (`:423`) and `HotpixelThresholdingEnabled = true` (`:424`) — *exactly the configuration the `+1` exists
   for* — and does not carry the compensation. The bundle contradicts the rule that produced its neighbours.
3. **The "what does the product run" argument.** Every construction yields 4 and persists it. Two of the nine
   profiles on this machine have `4` literally written into their XML by that persist.
4. **The durability argument.** `ResetDefaults()`'s 3 does not survive a restart: the next construction
   re-derives 4 and writes it. *A value the next launch overwrites is not a default.*

The counter-evidence is stated rather than buried: the accessor fallback, the `StarDetectorParams` class
default, and the **user manual** all say 3. Those are three more places the decision has to reach, and the
manual line is a documentation bug under either answer.

**Rejected alternatives, in writing.** *(a) Move the `+1` out of `DerivePresetSettings` into a shared build
path so the stored value is 3 and the effective value is 4.* Rejected: an Advanced-mode user who explicitly
sets 3 would silently detect at 4, and `BuildDefaultStarDetectorParams` builds the *params* bundle, so it
would have to carry the compensation anyway — the seed moves regardless, and an extra class of user is
surprised. *(b) Remove the `+1`.* Rejected: it changes the effective detector for every Simple-mode user on
every detection, everywhere in the product, and it deletes a deliberate, reasoned compensation on no evidence.
*(c) Do nothing but fix the test.* Rejected as a whole, retained as a **branch** — that is `N-HOLD`.

### 2.5 What ships, in two parts

**Part 1 — unconditional, in BOTH binaries, inert on every harness path.**

- **(1a) `ResetDefaults()` becomes deterministic and equal to construction.** The contract, stated as the
  thing the tests assert: *after `ResetDefaults()`, the object's property state equals that of a freshly
  constructed `StarDetectionOptions` over a blank accessor — for every property, from every entry state.*
  The natural implementation is an unconditional `ConfigureSimpleSettings()` as the final statement of
  `ResetDefaultsImpl` (after `optimizedSettings = null` and `UseOptimizedSettings = false`), which also
  retires the whole class of transcription drift rather than this one instance. The code agent may choose
  another implementation that satisfies the contract; it may not satisfy it only for this field.
- **(1b) The drift guard is rewritten so it cannot be green on a path that does not exist.** Three tests,
  replacing the one:
  - **T1** `BuildDefaultStarDetectorParams()` vs `BuildStarDetectorParams(freshly constructed options)` over
    every option-derived field. *This is the assertion nothing in the repo currently makes.*
  - **T2** `ResetDefaults()` from **four named entry states** — virgin; Advanced mode with a non-default
    radius; `UseOptimizedSettings = true` with a snapshot applied; non-default `Simple_*` presets — all
    produce the same state, equal to a fresh construction. **T2's `UseOptimizedSettings` case fails against
    the pre-change source**, and that must be demonstrated.
  - **T3** the accessor-fallback / literal lockstep guard, modelled on the one that already exists for a
    different knob at `AutoFocusOptionsTests.cs:103-115`.
  Under `N-ALIGN`, T1 is a plain equality. Under `N-HOLD`, T1 carries **one named exception** —
  `NoiseReductionRadius`, asserted in **both** directions (3 on the seed bundle, 4 on the constructed one)
  with an F70 comment — so it still runs on the real path and any *new* divergence still fails loudly.
- **(1c) [F69](followups.md)(a)**: the four-line comment at `OptimizationDiagnosticRunner.cs:405-409`, every
  clause of which is backwards. ~5 m, no behaviour change, and it is the piece that cost RULE P16 its
  premise. The register asks for it "in the next wave that ships code".

**Part 2 — conditional on RULE N18, in B2 only.**

- `HocusFocusStarDetection.cs:425`, `NoiseReductionRadius = 3` → `4`, with a comment naming the compensation
  it is carrying.
- `documentation/docs/settings/preprocessing.md:23`, default 3 → 4.
- The two existing assertions that encode the old answer become 4:
  `StarDetectionOptionsTests.cs:513` (`ResetDefaults_RestoresDocumentedDefaults`) and
  `StarDetectionOptionsBufferedModeTests.cs:115` (`Suppressed_ResetDefaults_SkipsLegacyKeys`). **Both change
  meaning under Part 1 alone**, because Part 1 makes `ResetDefaults()` derive; they are named here so the
  change is disclosed rather than discovered in a diff.

### 2.6 Why the gate cannot run on B2 — and why this wave builds two binaries

**The controller's brief fixes the order as "code → ONE binary → gate → arms". That order is correct for a
change the gate cannot see, and this is not one.** `BuildDefaultStarDetectorParams()` is the optimizer's
starting point; the gate is `optimize --max-evals 250`; the landing follows the seed on 18 of 20. A gate run
on B2 would be **testing the change**, and its FAIL would be the *expected consequence of a deliberate edit*
rather than evidence of a broken build. That is not a gate — it is an arm with a gate's name on it.

So, planned up front and disclosed here rather than discovered mid-wave (F53(c), and wave 14's lesson about
disclosure):

- **B1 = HEAD + Part 1.** Runs **RULE G18**. Its PASS is the ninth reproduction *and* the measurement that
  Part 1 is inert.
- **B2 = B1 + Part 2.** Runs arm **A1**. The two binaries differ in **one literal**, so the A0/A1 comparison
  is an intervention rather than a correlation — wave 17's shape, applied to a change instead of a flag.

There is deliberately **no flag** for Part 2. A flag added so the arm could run on one binary would be code
that exists only for the measurement.

### 2.7 RULE N18

**The arms.** Both 20 synthetic datasets, `optimize --per-run --max-evals 250 --settings S0 --profile-id
astrodet`, sequential, arm-major (see the driver header for why not interleaved), pre-registered dataset
order D18, D19, D20 then D01…D17.

- **A0** on **B1** — seed radius **3** (status quo).
- **A1** on **B2** — seed radius **4** (treatment).

**Out of sample.** Each of the 40 landings is converted to a harness settings file by
`convert_landing_w15.py` (through the production Accept path) and re-fitted with `af-fit` at **budget 0 fixed
in both arms**, `--params-from '__PINNED_NO_SAVED_PARAMS__' --confidence 0.95 --weighted true`, on **B1 for
both arms** — `af-fit` never calls `BuildDefaultStarDetectorParams`, so one instrument reads two inputs.

#### The clauses. Every one names P / S / A / E.

**Validity gates** — all of V1, V2, V3, V6, V7 must pass or N18 is `N-UNEVALUATED` and issues no verdict.

| clause | threshold | P | S | A | E |
|---|---|---|---|---|---|
| **N18-V1** *population* | `optimized_settings.json produced: 20 / 20` per arm, asserted **in the driver** | the landings under each arm root | count | `== 20` | short ⇒ UNEVALUATED, datasets **named**; never "fewer landings moved" |
| **N18-V2** *the treatment took, and only the treatment* | the `optimize/seed` blocks differ between arms in **exactly one field** (`NoiseReductionRadius`, `3`→`4`) and the `optimize/baseline` blocks in **zero**, on **20 of 20** | the paired `PARAMS-DUMP` blocks in the 2 × 20 `run.log`, over the 55 parsed fields | per-field string inequality, same dataset | two rates, denominator 20, per-dataset field lists printed | a log missing either block is COULD-NOT-LOOK **by name**; if any is, **N18 is UNEVALUATED**. This is the clause that says the intervention *happened*; its absence is never "the change did nothing" |
| **N18-V3** *provenance* | one `BuildId` per arm, the two **differ**, both novel; `DetectorVersion` 2; profile pinned by containment **and** cardinality; one `FitInputs`; `ConcurrencyCheck` exclusive across each arm | `Provenance` in the 2 × 20 landings | equality / cardinality | per arm, and **across** arms | an unreadable landing is its own state, named, and the clause FAILS. **Two arms sharing a `BuildId` is an explicit FAIL** — it would mean one binary ran both arms |
| **N18-V4** *`J` is the same function in both arms* | `BaselineJ` **bit-identical** across arms on the common set | `BaselineJ` ×2×20 | exact 17-digit equality | rate, denominator printed | missing ⇒ COULD-NOT-LOOK, named. **If it fails, N18-J is UNEVALUATED.** It is the clause that *licenses* the cross-arm `FinalJ` comparison instead of assuming it ([F62](followups.md)) |
| **N18-V6** *the gate reproduces inside the arm* | arm A0's `D18/D19/D20` landings equal RULE G18's on `FinalJ` **and** `BaselineJ`, 3 of 3 | the gate's and A0's landings | exact equality | 3 of 3 | missing ⇒ COULD-NOT-LOOK, named, clause FAILS. Same binary, same command, same settings — wave 15's G-e measured 3 of 3 bit-identical |
| **N18-V7** *the conversion took* | the `af-fit/detector` dump carries the converted file's `NoiseReductionRadius`, 20 of 20 per arm | the 2 × 20 `af-fit` `run.log` dumps against the 2 × 20 converted settings files | field equality | rate per arm | missing ⇒ COULD-NOT-LOOK, dataset **named** and removed from **both** of N18-P's denominators, which are re-printed |

> **N18-V7 deliberately does not use wave 15's star-count equality (its clause `G-c`).** Wave 17 showed `G-c`
> is confounded: `optimize` applies the F39(b) per-run detection-binning factor and `af-fit` does not, so the
> two counts disagree **by construction** on `{D08, D09, D10, D12, D14, D15, D17}` — and `G-c` returned
> *could not look* on exactly those 7, which is what cost RULE L15 its verdict. A field-to-field check on the
> detector dump cannot be confounded by a resolution difference. **This is the repair wave 15 was owed, and
> it costs nothing.**

**Measurement clauses.**

| clause | what it measures | P | S | A | E |
|---|---|---|---|---|---|
| **N18-A** *the seed anchors the landing* | the rate at which the landing's radius equals its arm's seed | `NoiseReductionRadius` in each arm's 20 landings | per-dataset equality to 3 (A0) / 4 (A1) | one rate per arm over 20, **full per-dataset table printed** | missing ⇒ COULD-NOT-LOOK, named, out of the denominator, re-printed |
| **N18-M** *the recommendation moves* | **reported, never a bar** | the paired landings' curated knobs + `RecommendedStepSize` + `RecommendedOffsetSteps`, excluding a named bookkeeping set so a timestamp cannot manufacture a movement | per-dataset "any field differs" (wave 13 D5's own definition) and the per-field list | one rate over 20 + per-dataset field counts + step `old → new` | missing ⇒ COULD-NOT-LOOK, named |
| **N18-J** *the objective's own preference* — **decisive** | `FinalJ(A1) − FinalJ(A0)` | `FinalJ` in the paired landings | signed difference, **tie band `1e-9` fixed here** | three counts over the common set + min / median / max | missing ⇒ COULD-NOT-LOOK, named; **V4 failed ⇒ UNEVALUATED**, printed as such — not a tie and not a null |
| **N18-P** *out of sample* — **a VETO, not the decider** | `e = |minPos − truth| / step` at budget 0 | the `af-fit` budget-0 row on the converted landing settings, against `renderRequest.OptimalFocuserPosition` and the step read from `synthetic_meta.json` (cross-checked against the declared value) | `Δe = e(A1) − e(A0)` | wins / losses / ties + **median\|Δe\|** + max\|Δe\|, both denominators printed | a dataset failing conversion, `af-fit` or the truth read is COULD-NOT-LOOK, **named**, out of **both** denominators, which are re-printed |
| **N18-R** *reach on real configurations* | free, zero `TestApp` | the 9 `*.profile` files + `pinned_settings_w11.json` | the plugin's own load rule, replayed | two rates over the readable profiles | an unparseable profile is COULD-NOT-LOOK, named, out of the denominator, re-printed |

**Why `FinalJ` is comparable here and was not in waves 13/15.** In wave 13's D5 and wave 15's L15-S the two
arms differed in `MaxOutlierRejections`, which changes **the fit** — so `J` was a different function on each
side and the register was right to refuse the comparison. Here the two arms share the settings file, the
profile, the `FitInputs`, the frames and the objective code, and differ only in **where the search starts**.
`J` is therefore one function evaluated at two landings, and the optimizer's own definition of better applies.
**N18-V4 checks that rather than asserting it.**

#### The branch table, fixed before the data

```
V1 and V2 and V3 and V6 and V7 all pass?           no  -> N-UNEVALUATED  (name the gate; Part 2 does NOT ship)
N18-P could not be evaluated?                      yes -> N-UNEVALUATED  (the only out-of-sample clause did not run)
N18-P veto fires (median|de| >= 0.10 step)?        yes -> N-HOLD
N18-J UNEVALUATED (because V4 failed)?             yes -> N-UNEVALUATED
N18-J: A0-better count > A1-better count?          yes -> N-HOLD
otherwise                                              -> N-ALIGN
```

- **`N-ALIGN`** — Part 2 ships. **The eight-value coordinate system is then invalid for wave 19.** If the
  wave's `TestApp` wall is ≤ **3 h 30 m** at that point, run **`G18'`** (the same eight runs on B2) and
  publish the new eight values beside the old, labelled as a new coordinate system. Above 3 h 30 m, do not
  run it and record that **wave 19's first act is a fresh eight-value baseline, priced at 42 m**. *The choice
  is fixed here so it is not made by fatigue.*
- **`N-HOLD`** — Part 2 is reverted. Part 1 still ships; the drift guard carries `NoiseReductionRadius` as
  one named exception asserted in both directions; F70(a) is recorded as a **costed recommendation with the
  measured price attached**. `N-HOLD` is not "the fix is wrong" — it is "the seed move costs measured
  objective value at 250 evaluations", which is also a finding about the search.
- **`N-UNEVALUATED`** — nothing of Part 2 ships and the failing gate is named. **An unsatisfiable rule is a
  finding and is not re-decided after the data.**

**If the change turns out to be inert** — `N18-A` on A1 near zero, every `FinalJ` tied, no landing moving —
that reaches `N-ALIGN` through the "otherwise" branch, and **the results doc must say so in those words: the
rule did not observe the anchoring it was built to measure, and `N-ALIGN` here rests on the source argument
of §2.4 and not on the arm.** Shipping on a null is a defensible thing to do and an indefensible thing to
describe as a pass.

---

## §3 — Item B: **nothing**, and the three candidates rejected in writing

The charter prices item A's code at "more than ~3 h with tests ⇒ take nothing else and say so". Item A is
**~110 m of arms on top of a 42 m gate, plus ~2 h of code and tests**. So: nothing. The rejections are
substantive, not budgetary alone.

**F45(b)'s production plumbing (~3–4 h + tests) — rejected.** Three reasons. (i) It alone exceeds the
remaining budget, and a wave that starts it would ship it untested or ship neither item. (ii) **There is
nothing for it to ride with.** RULE S16 reached outcome 4, NO RECOMMENDATION; §3a forbids repairing
`S16-A(b)` and re-scoring wave 16's rungs; so the plumbing would deliver a mechanism that no rule has
recommended. Production plumbing for an unrecommended criterion is a mechanism with no decision behind it.
(iii) Its reach is now small and contingent: after wave 14 flipped `MaxOutlierRejections`'s default to 0,
`RejectionTest` does not run at all on a default profile, so a SEM floor reaches only profiles that
explicitly store ≥ 1 — **3 of the 9 on this machine**. The correct home for it is a wave that first writes
the new rule §3 demands, on a population measured **after** that rule is fixed.

**F21 / the step-size family — rejected, and wave 17's own instruction is why.** Wave 17 §7 scoped it and
ended: *"wave 18 must measure one dataset first and price from that, because this series has never run
`synth-validate` as an arm."* That is correct and it is the reason not to schedule it as an item: **an
instrument with no measured rate cannot be planned inside a wave that is already at 2 h 32 m of measured
arms.** Two further reasons. Wave 10 refuted the entry's own hypothesis (`FindHalfWidth` is exact, returning
`√8·HFR_min/κ`) and *the entry's case did not reproduce*, so the population an F21 arm would characterise is,
as of wave 10, empty. And the proposed truncation arm needs either `af-fit --af-run` pointed at truncated
frame sets — an unverified capability — or a `synth-validate` arm at an unmeasured cost. **What wave 18 owes
wave 19 is the price, not the arm**: §7 lists a ≤ 20 m timeboxed, rule-free price probe of one
`synth-validate --scenarios S0 --datasets D17` run as an optional tail, droppable first, whose only
deliverable is a measured rate.

**F59's five knobs (~1 h) — rejected, and this wave adds a reason wave 17 did not have.** Wave 17's grounds
still hold: the knobs are absent from the pinned file so there is no printed value to diff, and nothing in
the register depends on the answer. The new ground is specific to wave 18: **this wave may already be moving
the coordinate system.** Stacking a second, unrelated coordinate-system move into the same wave makes the two
indistinguishable — a moved gate could never be attributed to either. *Two coordinate-system moves in one
wave is one move you cannot read.*

---

## §4 — Item C: BLOCKED, and the blocker has a name

### 4.1 What is now known

Eight waves recorded `Disc` and called it *"the machine has no attached display"*. Wave 17 read **`Active`**
at write-up time, and the check then ran further than it ever has: composited desktop confirmed, deploy
verified **byte-for-byte before launch**, NINA launched with a non-zero hwnd and a real window title, and it
**wrote a log** — where wave 13's attempt hung with 5 s of CPU and no log at all. Then NINA crashed during
startup:

```
CompositionRoot.cs|Compose|145  System.NullReferenceException
  NINA.Core.Utility.AsyncObservableCollection`1.RunOnSynchronizationContext (:44)
  NINA.Core.Utility.AsyncObservableCollection`1.InsertItem                   (:49)
  NINA.Utility.PluggableBehaviorSelector`2..ctor                             (:39)
```

Two version facts sit beside it. The crashing process is NINA **3.3.0.1048** — the only installed
application — while every other NINA-libraries log that day is **3.2.0.2001**, which is this harness's own
`TestApp` runs. The plugin's `Joko.NINA.Plugins.HocusFocus.csproj:52` reads
`<PackageReference Include="NINA.Plugin" Version="3.2.0.2001-beta" />`, and it is deployed into that 3.3
install's `Plugins\3.0.0\`.

### 4.2 The brief's reading of the stack is not sound, and that matters

The brief records *"Every frame is NINA core or Microsoft DI; zero occurrences of 'Hocus' in the log"* as
though it exculpates the plugin. **It does not.** `PluggableBehaviorSelector<,>`'s constructor inserting into
an `AsyncObservableCollection` is **precisely** where MEF-imported plugin behaviours are consumed, and
HocusFocus exports two of them — `IStarDetection` and `IStarAnnotator`
(`AutoFocusEngineFactory.cs:31-32`, `InspectorVM.cs:101-102`). A behaviour imported from an assembly built
against a different NINA contract can NRE inside the selector's own frames without the plugin's name
appearing anywhere in the trace. **The absence of "Hocus" is consistent with the plugin being the cause**, so
the decisive control is decisive rather than optional.

### 4.3 The blocker, named

> **The decisive test — one launch with the plugin folder moved aside — was refused by the permission
> classifier. It is not worked around, and this wave designs no arm that needs it.**

That is the blocker: **a permission the agent does not hold and must not route around.** It is not a machine
state, not a display, and not a NINA bug that has been diagnosed. Nine waves of "no display" were, at least
in part, masking it.

### 4.4 The disposition, pre-registered

| | |
|---|---|
| **what would settle it** | One launch of NINA 3.3.0.1048 with `…\Plugins\3.0.0\` (or just the HocusFocus assemblies) moved aside. **Crashes anyway** ⇒ the fault is NINA's install and A1–A9 are blocked on that. **Starts clean** ⇒ the plugin is implicated, and the leading hypothesis is a 3.2-built plugin loaded into a 3.3 host |
| **what it costs** | ~3 m of wall time. The cost is **not** compute — it is a permission grant, which is the user's to give |
| **what the register says if the answer is "the plugin has not been rebuilt against 3.3"** | **Every UI item in this series' backlog is blocked behind a NINA 3.3 retarget** — a `NINA.Plugin` package bump, whatever API drift it brings, and a full regression pass. That is a project-level decision with its own cost, it is not a wave item, and A1–A9 stay NOT ATTEMPTABLE until it is taken. The register must also record that the eight-wave `Disc` reading was never the whole blocker |
| **what this wave does** | records the above, spends **zero minutes**, touches no display, and launches no NINA. `gate_w18.sh` and `seed_w18.sh` both abort if any NINA process is running |

---

## §5 — Satisfiability, in two parts

### 5.1 Part 1 — maxima and reachable range, computed **on the real artifacts**

Not on constructed input: *a constructed population cannot detect a denominator error, because the
constructor chooses the denominator* ([F68](followups.md)).

| clause | bar | max attainable **on the artifact the clause reads** | min | both reachable? | how the denominator was derived |
|---|---|---|---|---|---|
| G18-1 | 8 of 8 at 6 dp | 8 — the driver asserts 8 landings before scoring | 0 | yes; the FAIL direction is demonstrated by `prov_w18.py --self-test` on a mutated copy | fixed list of 8 run names |
| G18-2 | count == 8 | 8 | 0 | yes | `find` over the arm |
| G18-3a–f | see §1.1 | set equalities and cardinalities | | yes, both directions demonstrated before the check is quoted | the 8 landings |
| G18-P1 | 8 of 8 seed blocks read `3` | 8 | 0 | **yes, and the 0 end is what a mis-built B1 produces** — this clause exists because that mistake is the one this wave's two-binary structure makes possible | `grep` over the 8 gate logs |
| **N18-V1** | 20 per arm | 20 | 0 | yes | `find` over each arm root |
| **N18-V2** | seed rate 1.000 of 20; baseline rate 1.000 of 20 | 20 each | 0 | **yes, and both ends are structurally observed already**: on wave 17's gate logs the *same-binary* seed/baseline pair differs in exactly one field (`NoiseReductionRadius`, RULE D17's union size 1 on 8 of 8 logs and again on wave 16's 10), so the parser and the one-field shape are measured, not hoped for | 20 datasets × one paired log each, counted on disk |
| **N18-V3** | cardinality / equality | — | — | yes; the "two arms share a BuildId" branch is exactly what a mis-sequenced run produces | the 2 × 20 landings |
| **N18-V4** | rate 1.000 of 20 | 20 | 0 | **yes.** `BaselineJ` is computed from the *baseline* bundle, which Part 2 does not touch, so 1.000 is expected — **and 0 is reachable**, because if `J` were normalised against the seed's evaluation the whole column would move. That is precisely the failure this clause exists to catch, and it cannot be established by reading | the 2 × 20 landings |
| **N18-V6** | 3 of 3 | 3 | 0 | yes; wave 15's G-e measured 3 of 3 on the identical construction | the 3 synthetic gate runs |
| **N18-V7** | rate 1.000 of 20 per arm | 20 | 0 | **yes, both ends demonstrated before either arm is quoted** — `affit_w18.sh probe` builds a good file and a mutant (`UseOptimizedSettings` flipped to `False`) and the mutation is asserted by read-back | 20 landings per arm |
| **N18-A** | reported rate | 20 | 0 | **yes, and one end is OBSERVED**: on `/mnt/d/hf_w15/land_mor0`, A0's own statistic is **18 of 20 = 0.900** today, with the two exceptions named | counted on disk: 20 landings, each with a `NoiseReductionRadius` field |
| **N18-M** | reported rate | 20 | 0 | yes; L15-S measured 19 of 20 for a different knob on this same population | the 2 × 20 landings |
| **N18-J** | A0-better count > A1-better count | 20 | 0 | **yes by construction, and NEITHER end is observed** — no prior wave has run two arms differing only in the seed, so this is the one bar in the wave whose ends are argued rather than measured. It is argued from two facts that *are* measured: the landing follows the seed on 18 of 20 (so the two arms land at genuinely different points) and the search is stochastic in its trajectory (waves 13 and 15 both measured landings moving in both directions on this bank). **Stated as an argument, not as an observation** | the 2 × 20 landings |
| **N18-P** *veto* | median\|Δe\| ≥ 0.10 step | see below | 0 | **NO — very likely foreclosed, and the foreclosure is computed here** | the common set after V7 |
| **N18-P** *alarm* | any `e` ≥ 1.0 step | — | — | yes; wave 16 measured a max `e` of 0.13150 under family R, so an alarm has never fired and could | the same |
| **N18-R** | two rates over 9 | 9 | 0 | the "effective radius changed" rate is **0 of 9 by construction** and is printed as such rather than implied; the "ResetDefaults lands on 4" rate is **5 of 9, measured** | counted on disk: 9 `*.profile` files |

**The N18-P veto's foreclosure, computed before the data.** The largest landing-driven out-of-sample movement
ever measured on this bank is **0.02584 step** (wave 15's L15-P — the same instrument, the same 20 datasets,
a detector-only difference), and the largest movement of any kind anywhere in the bank is **0.00993 step**
(waves 14 and 16). Moving a **median** of 20 requires **≥ 11** datasets to move by ≥ 0.10 step, i.e. eleven
simultaneous movements each ~4× larger than any ever recorded. **The veto is therefore very likely
unreachable, it is labelled a VETO everywhere it appears, and it is deliberately not the deciding clause.**
It is retained because a structure-map kernel change is a larger lever than an outlier-rejection change and
*could* in principle move one dataset far, and because deleting it would leave the out-of-sample instrument
with no consequence at all. **This is `S16-E(i)`'s defect written out rather than repeated: the reachable
range is computed for the AGGREGATION, not only for the value.**

**Every denominator above was counted on the artifact the clause reads, at pre-registration time.** No number
is inherited from another wave's view of the same runs. Where a count would have done, a rate with a named
denominator is used, so a drop-order cut (§6) changes a denominator and never a threshold — and the scorer
re-prints both.

**Absolute counts in this design, and their reachable ranges** — stated because wave 17 claimed to use none
while `C17-B`'s bar was literally "27 of 27":

| bar stated as a count | reachable range | why a count and not a rate |
|---|---|---|
| G18-1 "8 of 8" | 0–8 | the gate is one instrument of eight fixed runs; a rate here would invite a partial reproduction to read as 0.875 rather than as a failure |
| G18-2 "== 8" | 0–8 | a population assertion, derived by `find` |
| G18-P1 "8 of 8" | 0–8 | a population assertion over the same 8 logs |
| N18-V1 "20 per arm" | 0–20 | a population assertion, derived by `find` |
| N18-V6 "3 of 3" | 0–3 | the population *is* three; it is stated as three, as wave 15 stated its own |
| N18-J "A0-better > A1-better" | both counts 0–20, and they share a denominator | it is a comparison of two counts over one denominator, which a rate cannot express |

Everything else is a rate with a printed denominator.

### 5.2 Part 2 — measurement → consuming-branch reachability, **both branches checked**

*"Check that BOTH branches of a clause are reachable, not just that the PASS value is attainable"* (wave 15's
G-d), plus *"what does it return when its set is empty"* (F68).

| branch of the N18 table | is there an input that reaches it? | is that input possible on this wave's population? |
|---|---|---|
| **N-UNEVALUATED** via a validity gate | any of V1/V2/V3/V6/V7 failing | yes — V2 fails if the wrong binary runs an arm (the exact mistake the two-binary structure creates), V6 fails if the gate and A0 disagree, V7 fails if a conversion silently does not bind |
| **N-UNEVALUATED** via N18-P | the af-fit pass not run, or its common set empty | yes — and it is listed **last** in the drop order precisely so this branch is not reached by budget |
| **N-UNEVALUATED** via N18-J | V4 failing ⇒ J UNEVALUATED | yes — see the V4 row above; a seed-normalised objective would produce it |
| **N-HOLD** via the veto | median\|Δe\| ≥ 0.10 | **very likely foreclosed** — stated in §5.1 rather than left for a reader to discover |
| **N-HOLD** via N18-J | A0 better on strictly more datasets than A1 | yes — the search maximises `J` from a starting point, and at 250 evaluations on an EARLY axis a worse start is not necessarily escaped |
| **N-ALIGN** on a real effect | gates pass, veto silent, A1 ≥ A0 on `J` | yes — the pre-registered expectation |
| **N-ALIGN** on a null | every `FinalJ` tied and no landing moving | yes, and it is **called out in the branch table as "not a pass on merit"** so the results doc cannot describe an inert change as a vindication |
| **G18 FAIL** | any `BestJ` moving, a repeated `BuildId`, a non-`exclusive` `ConcurrencyCheck`, or `G18-P1` reading 4 | yes; all demonstrated by `prov_w18.py --self-test` and by the driver's own assertion before the check is quoted |
| **`N18-A` = 0.000 on A1** | the search abandoning the seed's radius on every dataset | yes — it happened on 2 of 20 at the status quo, so it is not hypothetical, merely unlikely at scale |

**Three clauses are deliberately NOT decisive and are labelled so everywhere they appear**: `N18-A`
(magnitude), `N18-M` (magnitude), `N18-R` (reach). None can carry a verdict and none appears in a conjunction
that produces one.

### 5.3 The F68 self-audit of this design — checked against the clauses above, not asserted

1. **A denominator carried across instruments.** Every denominator in §5.1 was counted on the artifact the
   clause reads. The one number imported from another wave — `18 of 20` — is imported as an **observed value
   of A0's own statistic on the identical artifact type**, not as a bar, and A0 re-measures it this wave.
2. **An aggregation that cannot see the effect.** There is exactly **one median in this design**, `N18-P`'s,
   and §5.1 computes what it would take to move it and says out loud that the answer is probably "more than
   can happen". `N18-J` uses counts over a shared denominator rather than a median, for that reason.
3. **A quantifier over a possibly-empty domain.** Every clause has an explicit **E** column and in every case
   the empty answer is UNEVALUATED or FAIL — never a vacuous PASS. `N18-V2`'s empty answer routes to
   UNEVALUATED, i.e. **away** from shipping. The scorer's self-test demonstrates *"a rate over an EMPTY set is
   UNEVALUATED, not PASS"* and *"an EMPTY dump block is COULD-NOT-LOOK, not 'zero fields differ'"*.
4. **A self-audit that has not been checked against its own clauses.** The table of absolute counts in §5.1
   was produced by re-reading every threshold in §1.1 and §2.7 and listing each one that is a count. Six
   are. Their ranges are printed. *Wave 17's §5.1 claimed it used no absolute counts while `C17-B`'s bar was
   "27 of 27"; this design does not make that claim.*
5. **Improvised instrumentation.** Wave 17's two escapes were both outside the pre-registered clauses. So:
   the controller's progress counter for this wave must have a **could-not-look** state — the plan specifies
   the exact `find` it runs and the directory it runs in — and `query session` is **not** used at all, because
   item C is blocked on a permission rather than on a session state.

---

## §6 — Budget, priced from the same instrument, with the drop order

| step | population | instrument | estimate | derivation |
|---|---|---|---|---|
| build B1 and B2 | — | `dotnet build -c Release` | ~6 m | two Release builds |
| **RULE G18** | 8 runs | `optimize --per-run --max-evals 250`, mixed real+synthetic | **~42 m** | waves 16 and 17 measured 40 m 55 s, 41 m 36 s and 41 m 29 s on the identical arm |
| the suite, twice | — | `dotnet.exe test` | ~8 m | wave 17 measured 3 m 38 s |
| **arm A0** | 20 synthetic | the same, all-synthetic | **~51 m** | wave 15's 40-run all-synthetic pair (`land_w15.sh`, identical shape, these exact 20 datasets) was **1 h 42 m** ⇒ 51 m per 20-run arm; the charter's 2.6 m/run gives 52 m independently |
| **arm A1** | the same 20 | the same | **~51 m** | as A0. A 9 px Gaussian is not a 7 px one, so a small difference is possible; it is **reported and is never evidence** (wave 17: `D12` took 14 s in both arms while its counts changed on 9 of 9 positions) |
| **N18-P** conversions + `af-fit` | 2 × 20 | `af-fit`, per dataset | **~8 m** | **measured**: wave 15's `affit_mor0` covered these exact 20 datasets in **3 m 45 s** (04:18:45Z → 04:22:30Z). *Do not price this from the `optimize` rate — doing so over-prices an `af-fit` pass by roughly an order of magnitude* |
| the probe | 2 `af-fit` runs | the same | ~1 m | wave 15's equivalent was ~30 s |
| scoring | Python | — | < 10 m | |
| **subtotal** | | | **~2 h 32 m of `TestApp`** | |
| **`G18'`**, conditional on `N-ALIGN` **and** on the wall being ≤ 3 h 30 m | 8 runs | as G18 | **+42 m** | as G18 |
| **wave total** | | | **~2 h 32 m to ~3 h 14 m** against a **6 h** ceiling | plus ~2 h of code and tests |

**Three instruments, three rates, never crossed**: `optimize` mixed ≈ 5.2 m/run, `optimize` all-synthetic
≈ 2.6 m/run, `af-fit` ≈ 11 s/dataset. The `af-fit` pass is 8 m, not 90.

### The drop order, fixed in advance

| # | cut | saves | what it costs | what it does to a bar |
|---|---|---|---|---|
| **D1** | do not run `G18'` | 42 m | wave 19 must run it as its first act | nothing — it is already conditional |
| **D2** | drop datasets from the **tail** of the pre-registered order (D17 backwards) from **both** arms | ~5.2 m per dataset | population | every N18 denominator is re-printed; **no threshold moves**, because every one is a rate or a comparison of counts over a shared denominator. A short population makes N18-V1 UNEVALUATED — it does **not** mean "fewer landings moved" |
| **D3** | drop item A's arms entirely and record F70 as a costed recommendation with the source argument only | 110 m | the whole decision | `N-UNEVALUATED`, disclosed by name |
| **never** | the gate; N18-V2; N18-V4; the `af-fit` conversion pass (it is **8 minutes** and it is the wave's only out-of-sample instrument); one half of any A0/A1 pair | | | |

---

## §7 — What this wave will NOT run, and what it costs

- **A full eight-value re-baseline on B2 (`G18'`).** Conditional; see §6. **~42 m.** If it is not run and
  `N-ALIGN` shipped, wave 19 **cannot** use the K8 table and must produce a new one first. That is the single
  largest debt this wave can create and it is created deliberately, with the price attached.
- **An `af-fit` inertness control between B1 and B2.** ~6 m, **and it is not run because it is a check that
  cannot fail**: `af-fit` builds its detector from `--settings` via `BuildStarDetectorParams` and never calls
  `BuildDefaultStarDetectorParams`, so B2 would produce byte-identical output by construction. *A control
  that cannot fail is not a control.* The work it would have done is done by `N18-V2`'s baseline half, which
  **can** fail.
- **The other 11 `BuildDefaultStarDetectorParams` call sites.** Part 2 also moves the default bundle for
  `bank-verify` (`:63, :348, :618`), `golden eval` (`:413`), `synth-validate` (`:716`), `bank-donut-meta`
  (`:88`), `inspect-align` (`:113`) and `tilt-calibration` (`:414, :424, :505`). **None is measured.** Reach
  is stated from code reading and the register must carry it: any future arm on those commands that compares
  across the wave-18 boundary is comparing two default bundles. Measuring even one of them is ~30 m and buys
  no decision.
- **The wizard-side confirmation of anything in this wave.** Blocked with item C (§4). ~30 m *if the
  permission is granted*, which is not this agent's to grant.
- **F21's price probe.** One `synth-validate --scenarios S0 --datasets D17_cdk14_oiii5` run, hard-timeboxed
  at **20 m**, no rule attached, whose only deliverable is a measured rate for wave 19 — discharging wave
  17 §7's instruction. **Optional tail item, first thing dropped**, run only if the wave is under 4 h 30 m
  when the arms finish. If it is not run, wave 19 inherits the same unmeasured instrument and the same
  instruction.
- **Any repaired form of `S16-A(b)`, and any re-scoring of wave 16's rungs or wave 14's V4/V4′.** Zero
  minutes, deliberately not done, per [F68](followups.md)(c) and the charter's §3 and §3a.
- **[F59](followups.md), F61(b), F52(d), F46(b), F54, F50.** §3 for F59; nothing depends on the rest.
- **Re-pinning the settings file.** F63(b) closed as DO-NOT-RE-PIN in wave 17 and nothing here re-opens it.

---

## §8 — Step order, fixed and not negotiable

**This wave ships code that the gate cannot see and code that the gate would see. The order exists to keep
those two apart.**

1. Commit this pre-registration and the plan. **Before any build and any measurement.**
2. **Item A Part 1** lands in the tree. Run the suite. Every new test must be shown to **fail against the
   pre-change source** — `T2`'s `UseOptimizedSettings` case is the one that does, and the demonstration is
   verified by the controller, not asserted by the code agent.
3. Build **B1** into `D:\hf_w18\exe_b1`. Record both dll sha256 values and the `BuildId`.
4. Write **both** BEFORE fingerprints (42 landings; 39 + 20 aux files).
5. Run **RULE G18** on B1. Score it: `score_w12.py --rule G18`, `prov_w18.py --self-test` in **both**
   directions with the mutation asserted, both fingerprints. **A partial reproduction stops the wave, and
   Part 1 is the first suspect.** Then arm the interlock with
   `score_n18_w18.py --arm-g18-passed`, which re-reads the gate rather than trusting a claim — including that
   all 8 seed dumps read `NoiseReductionRadius=3`.
6. Run `affit_w18.sh probe` and score it. **Both directions, mutation asserted by read-back**, before any
   arm's conversion is quoted. It writes `PROBE_PASSED`; `affit_w18.sh 0|1` refuses to run without it.
7. Run **arm A0** on B1. Both fingerprints.
8. **Item A Part 2** lands in the tree. Build **B2** into `D:\hf_w18\exe_b2`. Record sha256 + `BuildId`.
   **Do not touch B1 or any existing arm directory (F53(c)).**
9. Run **arm A1** on B2. Both fingerprints.
10. Run `affit_w18.sh 0` then `affit_w18.sh 1`. Both fingerprints.
11. Score **RULE N18**: `--self-test` first, then the full scorer. **Apply the branch table as written. Do
    not re-decide a clause — an unsatisfiable rule is a finding.**
12. If `N-ALIGN` **and** the wall is ≤ 3 h 30 m: run **`G18'`** on B2 and publish the new eight values.
    If `N-HOLD` or `N-UNEVALUATED`: **revert Part 2** (the literal, the manual line, and the two assertions),
    and switch the drift guard to its one-named-exception form.
13. Run the suite on the **final** tree. Verify by **COUNT**. Name every added test and every changed
    assertion.
14. Commit, push, append the wave's section to PR #191, verify CI by COUNT read out of the log.

**If a third binary becomes necessary at any point, it is a FINDING and it is disclosed in the results doc in
wave 14's own words — it is not quietly done, and the gate is re-run.**

---

## §9 — Where the controller's brief is wrong

Recorded because every pre-registration in this series has been asked to say so, and because three of these
change what the wave does.

1. **"The order is code → ONE binary → gate → arms" is wrong for this item, and following it would have
   destroyed the gate.** `BuildDefaultStarDetectorParams()` is the optimizer's seed
   (`OptimizationDiagnosticRunner.cs:348, :373`); `NoiseReductionRadius` is a curated search variable
   (`OptimizerVariable.cs:184`); and the landing follows the seed on **18 of 20** datasets. A single binary
   carrying Part 2 would have made RULE G18 a test of the change, and its near-certain FAIL would have
   stopped the wave in its own §1 for the most predictable reason available. **This wave builds two binaries,
   planned up front and disclosed, and the gate runs on the pre-change one.** §2.6.
2. **F70's own mechanism line is wrong about which image the kernel touches.** The register cites
   `StarDetector.cs:535` and calls it the measurement image; that call is gated by
   `StarMeasurementNoiseReductionEnabled`, which is `false` on every default path. The live call at defaults
   is `:556`, on the **structure-map source**, and it also moves the σ estimate at `:562` that sets the
   binarization threshold. The finding is stronger than F70 states, not weaker. §2.3.
3. **The brief's framing of the item-C stack trace is not sound.** *"Every frame is NINA core or Microsoft
   DI; zero occurrences of 'Hocus'"* reads as exculpatory and is not: `PluggableBehaviorSelector<,>`'s
   constructor is exactly where MEF-imported plugin behaviours are inserted, and HocusFocus exports two.
   The absence of the plugin's name is fully consistent with the plugin being the cause. §4.2.
4. **"Is 3 or 4 the intended default" has a decisive, free answer, and the brief treats it as open.** It is
   settled by counting: `ResetDefaultsImpl` and `DerivePresetSettings` share 20 properties and agree on 19,
   and the twentieth is the only one with a post-adjustment. That took one careful read and no compute. §2.1.
5. **F70 understates itself: `ResetDefaults()` is not merely inconsistent with construction, it is
   non-deterministic.** Its last statement re-derives the presets *only when `UseOptimizedSettings` was on*,
   so the same button yields 4 on **5 of the 9 profiles on this machine** and 3 on the other 4. The brief
   (and the register) describe a two-valued constant; it is a state-dependent function. §2.2.
6. **The brief's out-of-sample arbiter cannot be the decider, and it says so itself while still calling it
   the arbiter.** With the largest reachable movement at 0.026 step against a 0.10 floor, `e` can only ever
   be a veto here. The wave's decisive clause is instead **`FinalJ` compared across the arms** — which is
   legitimate *for this treatment only*, because the two arms share the fit and differ only in where the
   search starts, and which the wave **checks** with `N18-V4` rather than assuming. That is a strictly better
   instrument than the one the brief asked for, and it costs nothing extra. §2.7.
7. **"Item B — ONE of these" cannot be satisfied honestly, and the brief's own escape clause is the right
   one.** Item A is ~110 m of arms plus ~2 h of code. §3 takes nothing and rejects all three on substance as
   well as on budget.
