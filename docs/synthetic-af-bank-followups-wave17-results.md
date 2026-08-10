# Synthetic AF bank — followups wave 17 (results)

Design / pre-registration: [`docs/synthetic-af-bank-followups-wave17-design.md`](synthetic-af-bank-followups-wave17-design.md).
Plan: [`plans/synthetic-af-bank-followups-wave17-plan.md`](../plans/synthetic-af-bank-followups-wave17-plan.md).
Wave 16: [`docs/synthetic-af-bank-followups-wave16-results.md`](synthetic-af-bank-followups-wave16-results.md) —
**and its CORRECTION block, which this wave caused. It is not duplicated here; §3.1 cross-references it.**
Charter: [`docs/waves17-21-handoff-prompt.md`](waves17-21-handoff-prompt.md).
Register: [`docs/followups.md`](followups.md).

> ## PROVENANCE
>
> | input | value |
> |---|---|
> | tree | branch `ghilios/synthetic-af-bank-followups-wave13`, PR #191. Pre-registration `550e748`, committed **before** the build |
> | code shipped by this wave | **NONE.** One binary, and the two-binary failure mode of wave 16 could not occur |
> | binary — the only one | `D:\hf_w17\exe`. `TestApp.dll` sha256 `179db6027a41ad7114c28476bab516edbf52ac2ccf63732c6b5979786fb27538` · `NINA.Joko.Plugins.HocusFocus.dll` sha256 `974262575b67cdd33f850948eaf7f89ab5ebaf1a471a569f913433dd9b002724` · `BuildId` **`932a1366d2944f8a9da8e8787717b1ec`**. **AN EIGHTH BINARY**, novel against `5cb7e474` (w11), `103d61c4` (w12), `62334f10` (w13), `084e3485` (w14), `df3a867d` (w15), `10bc1b47` (w16) |
> | detector | `DetectorVersion` **2**, read as a **FIELD** on all 8 gate landings ([F66](followups.md)). The `strings` probe is quoted below **only** as a flag-presence probe |
> | settings **S0** | `D:\hf_w11\pinned_settings_w11.json` md5 **`a67ffc06164c81613aef5c4f8324b9b8`**, re-verified at write-up time. The only settings file this wave used. **Not re-pinned** — §4.5 |
> | profile | `astrodet` `ce3f3e63-8fd3-4b72-a0ca-d90db9441382`, pinned on **every** arm; one distinct value across all 8 gate landings and all 20 item-A runs |
> | banks | `D:\SyntheticAutofocusBank` (20 datasets, `renderRequest.OptimalFocuserPosition` = truth), `D:\Autofocus Bank` (19 runs) |
> | F15 control — landings | **42 of 42 bank landings BYTE-IDENTICAL**, re-verified after every arm and again at write-up. 0 changed, 0 added, 0 removed, 0 could-not-look. `--update-run-folder` passed **nowhere**. Fourth consecutive clean wave |
> | F15 control — **aux, NEW this wave** | **59 of 59 byte-identical** (`harness_settings.json` ×39 + `synthetic_meta.json` ×20), 0 changed / added / removed / could-not-look. §2.3 |
> | suite | **3824 passed, 0 failed, total 3824**, verified by COUNT ([F37](followups.md)). No code ships, and the count is unchanged from wave 16 |
>
> **No fan-out.** Every arm is `--profile-id`-pinned and a pinned arm cannot fan out at all (wave 11's K3); the
> authorisation is worth 1.33×, not 4× ([F60](followups.md)). Wave 5's φ table is quoted nowhere.
>
> **Every number in this document was re-derived from the artifacts at write-up time**, by re-running each scorer
> into a scratch path and diffing against the committed output. `score_c17_w17.py` and `score_d17_w17.py` both
> reproduce **byte-identically**. No arm directory was written to.

---

## §0 — Status

| item | rule | pre-registered expectation | outcome | register consequence |
|---|---|---|---|---|
| the gate | **RULE G17** | 8 of 8 `BestJ` to 6 dp | **PASS — 8 of 8, and bit-identical to all sixteen digits.** 41 m 29 s | the eighth binary reproduces the coordinate system |
| item A | **RULE C17** | `C-BINNING` | **C-BINNING**, by **intervention**: `C17-A` **63 of 63 = 1.0000** against a currently-measured **0.000 of 63** | **[F67](followups.md) CLOSES.** The cause is `DetectionBinning` — **neither** of wave 16's two candidates |
| item B | **RULE D17** | `D-SMALL` | **D-SMALL**, union size **1**: `NoiseReductionRadius`, shipped **3** vs pinned **4** | **[F63](followups.md)(b) closes as a costed recommendation. DO NOT RE-PIN** — and the offset turned out to be a **product** fact, §4.4 |
| item C | — | `Disc` — an eighth consecutive NOT ATTEMPTABLE | `Disc` at wave time; **`console ghili 1 Active` at write-up time. IT FLIPPED** | §6 — and it is a finding about the instrument, not about the machine |
| new | — | — | — | **[F69](followups.md)** (F39(b)'s self-description) and **[F70](followups.md)** (two shipped defaults for `NoiseReductionRadius`) |

**Wave `TestApp` wall: 80 m 22 s** against a 6 h ceiling, estimated at ~78 m (range 65–95). §5.

---

## §1 — RULE G17: the gate

Eight runs, `optimize --per-run --max-evals 250`, sequential, both pins named in the driver header, one
`TestApp.exe`, machine quiet. Driver `/mnt/d/hf_w17/gate_w17.sh`; window **20:27:54Z – 21:09:23Z**.

### §1.1 Every clause, its pre-registered threshold, and what it measured

| clause | pre-registered threshold | measured | verdict |
|---|---|---|---|
| **G17-1** | all eight `BestJ` reproduce the K8 table to **6 dp**; a partial reproduction is a FAILURE and stops the wave | **8 of 8 at 6 dp, and 8 of 8 bit-identical at all 17 significant digits** | **PASS** |
| **G17-2** | `aggregate_summary.json produced: 8, expected: 8`, asserted **in the driver before any scorer runs** | `aggregate_summary.json produced: 8   expected: 8` | **PASS** |
| **G17-3a** | exactly **one** distinct `BuildId`, **novel** against the six recorded ids | one value, `932a1366d2944f8a9da8e8787717b1ec`; `differs from waves 11/12/13/14/15/16: YES` | **PASS** |
| **G17-3b** | `DetectorVersion` **2** on all eight, read as the **FIELD** | `DetectorVersion(s): ['2']` | **PASS** |
| **G17-3c** | `ProfileId` contains `ce3f3e63-…` on all eight **and** cardinality exactly 1 | both clauses evaluated; containment 8 of 8, cardinality 1 | **PASS** |
| **G17-3d** | one distinct `FitInputs`, exactly `MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid` | `distinct FitInputs: 1`, the exact string | **PASS** |
| **G17-3e** | `ConcurrencyCheck` == `exclusive` on **all eight**, read **across the arm**, over a list carrying an explicit `None` for every unreadable landing | eight `'exclusive'`, list length 8, no `None` | **PASS** |
| **G17-3f** | `BaselineJ` reproduces wave 11 ([F41](followups.md)) | 8 of 8 at 6 dp | **PASS** |
| **G17-4** | the scorer PASSES on the real arm and FAILS on a mutated copy, **with the mutation asserted by read-back** | direction 1 PASS; direction 2 FAIL on **both** the containment and the cardinality clause, with the rewritten `ProfileId` echoed back | **SELF-TEST PASS** |

The eight values, exactly as pre-registered and exactly as measured:

```
toml999    0.9957838768299878   CWhiteFocus 0.9960675916058808   uneven 0.9963677194179505
muggsie    0.9971948738498605   mccomiskey  0.9767460801208465
D18        0.9998815090506263   D19         0.9994870586135448   D20    0.9997378027339423
```

### §1.2 The F68 four-part statement, per clause

The pre-registration names **(P)** population, **(S)** statistic, **(A)** aggregation and **(E)** the empty
answer for every clause. All ten held as written; the two that could have bitten:

- **G17-3c (E)**: `all()` over an empty list is vacuously true, so the **cardinality** clause carries it. Both
  were evaluated, and the self-test's direction 2 failed on **both**, which is the demonstration that the
  cardinality clause is not decoration.
- **G17-3e (E)**: the list is built with a `None` placeholder per unreadable landing precisely so an empty read
  cannot shorten it into agreement. It ran at length 8 with no placeholder, so the guard was not exercised on
  real data — recorded as **not exercised**, not as passed.

### §1.3 The flag-presence probe, with the correct instrument

`strings -el` (UTF-16, the `#US` heap) on the final `TestApp.dll` finds `--no-run-detection-binning` as an exact
line **1** time. Plain `strings` finds **0**, which is expected and is not a failure — that is
[F66](followups.md)'s two-heaps lesson applied as a habit. **This is quoted as a flag-presence probe only. The
detector version is the FIELD, above.**

### §1.4 What this PASS does and does not prove

The gate runs at `MaxOutlierRejections = 0`, so it says nothing about the rejection path, and **this wave ships
no code**, so it is not a control on a change. Its job here is narrower and was stated as such in advance: it
establishes that **the eighth binary reproduces the coordinate system**, which is the precondition for reading
item A's arms against wave 15's and wave 16's numbers. It is a control on the *binary*.

It also carried item B at zero extra cost: every one of the eight logs contains both `PARAMS-DUMP
optimize/baseline` and `PARAMS-DUMP optimize/seed`, and RULE D17 (§4) is computed entirely from those blocks.

---

## §2 — The controls

### §2.1 The landings

`python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --check /mnt/d/hf_w17/bank_landing_fingerprint_BEFORE.json`

> **42 of 42 BYTE-IDENTICAL. 0 changed, 0 added, 0 removed, 0 could-not-look.** Fourth consecutive clean wave.

### §2.2 The scorers, both directions, before either was quoted

| scorer | demonstrations | result |
|---|---|---|
| `prov_w17.py --self-test` | 2 directions, mutation asserted by read-back | **SELF-TEST PASS** |
| `score_c17_w17.py --self-test` | **13** — every branch of the C17 table (`C-BINNING`, `C-BOTH`, `C-LOAD`, `C-COUNT`, `C-THIRD`, `C-UNEVALUATED` ×2) plus the join/rate primitives, including *"a rate over an EMPTY set is UNEVALUATED, not PASS"* and *"a rate bar survives a denominator change"* | **13 of 13 ok** |
| `score_d17_w17.py --self-test` | **7** — `D-NOOP`, `D-SMALL`, `D-LARGE`, and **both** ways of producing an empty union distinguished (`seed block absent everywhere → D-UNEVALUATED, NOT D-NOOP`; `empty gate directory → D-UNEVALUATED, NOT D-NOOP`) | **7 of 7 ok** |

### §2.3 The aux fingerprint, and why it is new

`/mnt/d/hf_w17/aux_fingerprint_w17.py` covers the **39** `harness_settings.json` and the **20**
`synthetic_meta.json`, with the same three states as the landing control and a separate POPULATION clause:

> **59 of 59 byte-identical**, `{'harness_settings.json': 39, 'synthetic_meta.json': 20}` matching the expected
> counts, 0 changed / added / removed / could-not-look, checked after **both** arms.

It exists because **item A's answer reads those files**: `HarnessSettingsStore.ResolveForRun` *writes* a
`harness_settings.json` when one is absent, and `expectedOptimal.detectionBinning` in `synthetic_meta.json` is
the field that decides which datasets are in the treatment set. **A file class that becomes evidence must become
a control in the same wave.** Wave 15 learned the mirror of this (a control whose premise was never tested
across the population); this is the same lesson from the other side.

---

## §3 — Item A / RULE C17: F67(c) closes, by intervention

### §3.1 What this corrects, and where the correction lives

Wave 16's RULE P16 narrowed F67(c) *"by construction and not by hypothesis"* to two candidates. **The
construction was wrong**, and **wave 16's own results doc now carries the correction block** — it is not
restated here. The one-line version: `PARAMS-DUMP` prints where the bundles are **constructed**, one statement
before `ApplyRunDetectionBinningIfRequested` mutates `DetectionBinning` and `PixelScale`, and the C# comment
directly above the two dump calls reads the flag polarity **backwards**. So P16 compared `optimize`'s params *as
constructed* against `af-fit`'s *as detected*, and `DetectionBinning` is **F-live**.

`OptimizationDiagnosticRunner.cs:216`:

```csharp
bool applyRunDetectionBinning = !DiagnosticUtil.HasFlag(args, "--no-run-detection-binning");
```

**F39(b) is ON BY DEFAULT** (adopted in wave 8); `--apply-run-detection-binning` is a retained no-op. The
self-description defect this exposes is written up separately as [F69](followups.md), because F39's own entry is
correct about the *behaviour* and burying a live defect inside a mostly-closed entry hides it.

### §3.2 The instrument: two arms, one binary, and an intervention rather than a correlation

The correlational answer (7/7, 13/13, 188 of 188 positions at factor 1 and 0 of 63 at factor 2, across both
banks and three binaries) was computed **at pre-registration time from files already on disk**, and is quoted in
the design as the satisfiability analysis, not as this wave's result. What the wave bought for its 39 minutes is
the **intervention**: turn the suspected cause off with an existing flag and predict a number already on disk.

| arm | command | window | wall | landings |
|---|---|---|---|---|
| **X1** (status quo, F39(b) ON) | `optimize --per-run --max-evals 250 --settings S0 --profile-id astrodet` | 21:09:49Z–21:20:47Z | **10 m 58 s** | 10 of 10 |
| **X0** (treatment, F39(b) OFF) | the same **plus `--no-run-detection-binning`** | 21:20:56Z–21:48:51Z | **27 m 55 s** | 10 of 10 |

**"One binary" is measured, not asserted.** The arms' own landings carry `BuildId
932a1366d2944f8a9da8e8787717b1ec` — the gate's — with `DetectorVersion 2`, `ProfileId astrodet
(ce3f3e63-…)` and `ConcurrencyCheck exclusive`, and the recorded `CommandLine` shows the two arms differing in
exactly one token: `--no-run-detection-binning`.

Datasets: the **7** with a derived factor of 2 (`D08`, `D09`, `D10`, `D12`, `D14`, `D15`, `D17`) plus **3**
factor-1 controls (`D03_redcat_250mm`, `D07_rc10_2000mm`, `D11_rc10_585_afbin2`). `D11_rc10_585_afbin2` was
chosen deliberately: **its name says `afbin2` and its derived factor is 1.** A control picked by name rather
than by the field would have been the wrong control.

### §3.3 Every clause, its threshold and its measurement

| clause | pre-registered threshold | measured | verdict |
|---|---|---|---|
| **C17-V2** *population* | `optimize_result.csv produced: 10 / 10` in **each** arm, asserted in the driver before any scorer | X1 **10**, X0 **10** | **PASS** |
| **C17-V3** *the flag took* | `--no-run-detection-binning: F39(b) DISABLED` in **10 of 10** X0 logs and **0 of 10** X1 logs; `detection binning (F39b): <n> from` in **10 of 10** X1 logs | X1: DISABLED **0**, factor lines **10**. X0: DISABLED **10**, factor lines **0** | **PASS** |
| **C17-V1** *the status quo reproduces* | **rate 1.000 of 90** against wave 15's `land_mor0` | **90 of 90 = 1.0000**, 9 of 9 on every one of the 10 datasets | **PASS** |
| **C17-A** *decisive* | **rate 1.000 of 63** — X0's `currentStarCount` == wave 16's `af-fit` `Stars` on the 7 factor-2 datasets | **63 of 63 = 1.0000**; per dataset 9/9 on all seven | **PASS** |
| **C17-B** *control: the flag is a no-op at factor 1* | **27 of 27 on both equalities** | X0==X1 **27 of 27 = 1.0000**; X0==af-fit **27 of 27 = 1.0000** | **PASS** |
| **C17-C** *by-construction, recomputed* | reported; the factor-1 rate is a **precondition** for the branch table | factor 1: **188 of 188 = 1.0000**; factor 2: **0 of 63 = 0.0000**; could-not-look **0** | **reported, not decisive** |
| **C17-D** *the counting stage is inert here* | every dumped block reads a FULL `Region` with a null inner crop **and** `MeasurementAverage=Median`; total asserted **> 0** and printed | **95 of 95** blocks (16 gate + 40 arms + 39 wave-16 `af-fit`) | **ESTABLISHED** |

**The branch table, applied as written:**

```
C17-A rate 1.0000 | C17-B True | C17-C factor-1 clean True | C17-D established True | af-fit larger anywhere False
>>> RULE C17: **C-BINNING**
```

#### The F68 four-part statement, per clause

Each clause names its **(P)** population as a concrete artifact and field, **(S)** the statistic, **(A)** the
aggregation, and **(E)** the empty answer. The scorer prints all four beside every result, and they are
reproduced verbatim in `/mnt/d/hf_w17/c17_score.txt`. The three that carry weight:

- **C17-A** — (P) `currentStarCount` in `binX0/<D>/attempt01/optimize_result.csv` against `Stars` in
  `/mnt/d/hf_w16/affit_N/syn/<D>/af_fit_points.csv`, joined on focuser position, over the 7 factor-2 datasets;
  (S) per-position integer equality; (A) **one rate**, denominator printed and re-printed if a drop fires, plus
  a per-dataset breakdown so an effect confined to one dataset stays visible; (E) a missing CSV, a non-common
  position or an unparseable field is **COULD-NOT-LOOK**, counted in its own state, the dataset named, and the
  clause **UNEVALUATED** — and an empty common set routes to `C-UNEVALUATED`, **never to `C-THIRD`**.
- **C17-D** — (E) 0 blocks found ⇒ **UNEVALUATED, printed as NOT ESTABLISHED**, which routes the branch table
  *away* from `C-BINNING`. This is `P16-INSTRUMENT-vs-PRODUCT`'s empty-domain defect written out so it cannot
  recur, and it is why `C17-D` was measured rather than assumed.
- **C17-C** — (E) a run whose log carries no F39(b) line is named and **removed from both denominators, which
  are re-printed**. Zero such runs occurred, so the guard is recorded as **not exercised**.

**Both ends of C17-A's bar were observed, not merely attainable.** The status quo measures **0.000 of 63** on
this exact pair of artifacts (waves 15 and 16 both) and the treatment measures **1.0000 of 63**. That is the
check wave 16's S16-A(b) never made ([F68](followups.md)).

### §3.4 What the effect looks like, per position

X0 equals `af-fit` **exactly** on all 63 positions. X1 — the same binary, same settings, same profile, same
frames, one flag apart — is systematically larger, and the gap is **defocus-graded**:

| dataset | wing | wing | | focus | | wing | wing |
|---|---|---|---|---|---|---|---|
| `D08` X1 (factor 2) | 12 | 23 | … | **83** | … | 23 | 11 |
| `D08` X0 (factor 1) **== af-fit** | 6 | 11 | … | **68** | … | 12 | 5 |
| `D08` ratio, X0 / X1 | 0.500 | 0.478 | | **0.819** | | 0.522 | 0.455 |
| `D14` X1 (factor 2) | 51 | 78 | … | **370** | … | 74 | 55 |
| `D14` X0 (factor 1) **== af-fit** | 21 | 45 | … | **260** | … | 41 | 20 |

> **F67's own recorded numbers are reproduced to three decimals.** The entry says *"`D08` at the base runs
> 0.46–0.52 of `optimize`'s count at the extremes against 0.82 at focus."* Measured here: **0.455–0.522** at the
> extremes, **0.819** at focus. The disagreement F67 described is, digit for digit, the binning factor.

The mechanism is exact and was pre-registered: `StarDetector.cs:494` resamples the measurement image by
`p.DetectionBinning`, so at factor 2 the gates see 4× the flux per binned pixel and admit fainter stars —
which is why the effect is largest at the wings, where the star is faint and spread.

### §3.5 What C17 does NOT say

- **The pre/post-filter distinction wave 16 proved is real and unchanged.** It is simply **inert at S0**:
  `C17-D` measured a FULL `Region` and `MeasurementAverage=Median` on **95 of 95** dump blocks, the ROI crop
  needs `!Region.IsFull()`, the outlier trim needs `MeasurementAverage == MeanOutliers`, and the brightest-N trim
  runs *after* `result.DetectedStars` is assigned. **The two counts remain two different measurements with the
  same name, and the register keeps that.**
- **Fourteen waves were not quoting the wrong number.** On the 13 factor-1 synthetic datasets and on **all 5
  real gate runs** the two numbers **are the same number** — 188 of 188 positions, spanning both banks and three
  binaries. On the other 7 they are the same *definition* measured by **two different detectors**.
- **It does not claim the app is wrong.** F39(b) is a deliberate, argued, measured harness default (wave 7's
  recall +0.087…+0.146 on 7 of 7 at precision 1.000; σ_focus improving on 7 of 7 by 17–95 %).
- **It does not re-open RULE P16.** P16's 53-of-55 is true of the bundles it read. What is corrected is the
  inference drawn from it, and the correction names the line of code and the artifact line that show it.
- **No landing, `BestJ`, σ_focus or R² is scored anywhere in item A.** Nothing here is computed by a fit under
  test ([F62](followups.md)); the quantity is an integer star count read off two CSVs.

### §3.6 PRODUCT or INSTRUMENT — and the answer differs from wave 16's

Wave 16 recorded F67 as a **PRODUCT** finding because both surviving candidates were production code paths.
That reasoning does not transfer, because F39(b) is **harness-only**:

| entry point | applies a per-run detection-binning factor? |
|---|---|
| `TestApp optimize` | **YES, by default** (`OptimizationDiagnosticRunner.cs:216, 582`) |
| `TestApp golden eval` | YES, opt-in (`GoldenEvalRunner.cs:269`) |
| `TestApp af-fit` | **NO** — no such flag exists on the command |
| `TestApp bank-verify` | **NO** |
| the shipped wizard | uses the **profile's** `StarDetectionOptions.DetectionBinning`. No `synthetic_meta.json`, no per-run physics derivation in the app |

> **The honest reading: an INSTRUMENT fact with a real product consequence.** The instrument fact is that three
> harness commands disagree about what binning a bank run is detected at. The product consequence is the
> pre/post-filter distinction — real, unchanged, inert at S0, and *not* what caused this disagreement.

**The register consequence is larger than F67.** Any wave-to-wave comparison of `af-fit` against `optimize` on
`{D08, D09, D10, D12, D14, D15, D17}` has been comparing two detectors. Wave 15's RULE L15 lost its verdict to
exactly this — its gate G-c returned *could not look* on exactly those seven.

---

## §4 — Item B / RULE D17: F63(b)'s detector half, at zero `TestApp` cost

### §4.1 The clauses

Domain: the **50** of 55 readable `StarDetectorParams` properties that `ApplyAfContext` does **not** set.
`PixelScale`, `Region`, `ModelPSF`, `SaveIntermediateFilesPath` and `SuppressInfoLogging` are equal **by
construction** on both bundles, so including them would inflate the agreement with five fields that cannot
disagree — *a check that cannot fail*.

| clause | pre-registered threshold | measured | verdict |
|---|---|---|---|
| **D17-1** | report the **set** of differing field names with both values, as a **union across the 8 logs with the per-log count printed beside it** | union size **1**; **1 differing field on each of the 8 logs, the same field every time** | **reported** |
| **D17-2** *domain* | domain size **== 50** on every log, 55 fields parsed per block | 8 of 8 logs parsed, **0 COULD-NOT-LOOK** | **PASS** |
| **D17-3** *reproduction control* | the same union recomputed on the **8 wave-16 gate logs + 2 wave-16 probe logs** must equal wave 17's | 10 of 10 read, 0 could-not-look; wave 17 `['NoiseReductionRadius']`, wave 16 `['NoiseReductionRadius']` → **SAME SET** | **PASS, and labelled a control** |
| **D17-4** *the `UseAdvanced` trap* | the `UseAdvanced=False … Simple-mode presets override N recorded advanced knob(s)` warning present, and `N` reported | present on every log; **distinct N: [0]**, logs with no override line **0** | **reported** |

**Verdict: `readable logs 8, union size 1 → D-SMALL.`**

```
NoiseReductionRadius    shipped default = 3        pinned file = 4
```

### §4.2 The F68 four-part statement

- **(P)** the paired `optimize/seed` and `optimize/baseline` blocks in the 8 wave-17 gate logs, restricted to the
  50-field domain; **(S)** per-field string inequality *within one log*; **(A)** the **union** across logs **with
  the per-log list printed beside it**, so a field that differs on some runs and not others cannot hide inside a
  union — it did not, all 8 logs differ on the same single field; **(E)** a log missing either block is
  COULD-NOT-LOOK by name, and **if all 8 are, D17 is UNEVALUATED and issues no verdict.**
- The self-test demonstrates the distinction that matters: *"seed block absent everywhere → **D-UNEVALUATED, NOT
  D-NOOP**"* and *"empty gate directory → **D-UNEVALUATED, NOT D-NOOP**"*. **An empty union from an empty
  population is not a no-op.** That is the `P16-INSTRUMENT-vs-PRODUCT` empty-domain defect closed a second time
  in one wave, in a different rule.

### §4.3 D17-4 measured the trap, and the answer is *agreement*, not *bindingness*

The override line reads **N = 0 on every log**. The file's recorded advanced knobs *agree* with what the
Simple-mode presets compute, so nothing is overwritten — **but an edit to one of them still would not take**,
because the presets recompute them at every construction regardless. The instrument for this is
`HarnessSettingsStore.SimpleModePresetOverrides`, which **measures** the overridden keys (it hands the file's own
bag to a throwaway options instance and diffs it afterwards) rather than listing them as constants, so it cannot
drift from what the class does.

> **A reader will see `presets override 0 knobs` beside `the baseline differs from the shipped default on 1
> knob` and conclude one of them is wrong. Neither is.** §4.4 is why, and finding out cost nothing but source
> reading.

### §4.4 The offset is not the pinned file drifting — it is TWO SHIPPED DEFAULTS

This is the part D17 was not designed to find and found anyway, at zero compute.

| where | value | why |
|---|---|---|
| `HocusFocusStarDetection.BuildDefaultStarDetectorParams()` — the `optimize/seed` bundle **and the shipped Optimization Wizard's seed** (`HocusFocusStarDetection.cs:418, 542`) | **3** | a hardcoded literal, held to `ResetDefaults` by `StarDetectionOptionsTests.BuildDefaultStarDetectorParams_MatchesResetDefaultsBuild` |
| `StarDetectionOptions.ResetDefaultsImpl()` (`:348`) | **3** | the literal, assigned after the `Simple_*` properties, so it is the object's final state on that path |
| **any `StarDetectionOptions` constructed from an accessor** in Simple mode — the harness path, and the app's — | **4** | `InitializeOptions()` → `ConfigureSimpleSettings()` → `DerivePresetSettings()`: `Typical` ⇒ 3, then `StarDetectionOptions.cs:221-224` — *"Without thresholding, hotpixel filtering does a blur. To compensate, we increase the noise reduction radius"* — adds **+1** whenever `HotpixelThresholdingEnabled && HotpixelFiltering`, **both on by default** |

So the pinned file's `4` is exactly what the presets produce (hence D17-4's `N = 0`), and the seed's `3` is the
value one step **before** the compensation. **`ResetDefaults()` leaves the object in a state that constructing it
never produces**, and the drift-guard test asserts `NoiseReductionRadius` on the `ResetDefaults()` path only — so
it is green (3824 of 3824 pass) while the path every real load takes disagrees by one.

`NoiseReductionRadius` is **live**: it is in `StarDetector`'s early-key list (`:153`) and sets the smoothing
kernel at `CvImageUtility.ConvolveGaussian(srcImage, srcImage, p.NoiseReductionRadius * 2 + 1)` — **7 px against
9 px**. This is written up as [F70](followups.md); it is a product observation, not a harness one, and it makes
D17's one-field union considerably more interesting than "the pinned file is stale".

### §4.5 The recommendation, fixed before the data

**DO NOT RE-PIN.** The design fixed this in advance and it does not depend on which branch fired: the
eight-value coordinate system has now reproduced across **eight binaries** and two settings files and is the most
valuable instrument this series owns. Moving it costs a fresh **~42 m** baseline plus the re-derivation of every
cross-wave comparison, and buys an alignment no open question depends on.

**What is delivered instead: publish the offset.** It is one named field with both values, and it now has a
mechanism. `F63(b)` closes as a costed recommendation.

### §4.6 D17 is not a harvest of a previous wave's rule

RULE F14 is closed and nothing here re-opens it. D17 compares a **different pair of bundles** than P16 did
(`seed` vs `baseline`, not `af-fit` vs `baseline`), answers a **different register entry** (F63(b), which had no
prior rule and no prior verdict), and is computed on **wave 17's own gate logs**. Wave 16's ten logs appear only
as D17-3, labelled a reproduction control, and are quoted nowhere as new evidence.

---

## §5 — Budget: estimated against actual

| step | estimate | actual | derivation of the estimate, and how it held |
|---|---|---|---|
| **RULE G17** — 8 runs | **~42 m** | **41 m 29 s** | wave 16 measured 40 m 55 s and 41 m 36 s on the identical arm. **Inside the pair.** |
| **item A, arm X1** — 10 runs | **~10 m** | **10 m 58 s** | per-dataset wall from wave 15's `land_mor0`: 458 s (7 at factor 2) + 158 s (3 controls) = 616 s. Actual 491 s + 167 s = **658 s, +6.8 %** |
| **item A, arm X0** — the same 10 at factor 1 | **~26 m** (range 15–35) | **27 m 55 s** | the design's two independent derivations both gave 1403 s + 158 s. Actual **1510 s + 165 s** |
| **item A**, chain window incl. scoring + both fingerprint re-checks between arms | ~36 m | **39 m 11 s** | 21:09:46Z – 21:48:57Z |
| **item B** RULE D17 | **0 m of `TestApp`**, < 5 m of Python | **0 m of `TestApp`** | it reads a printout wave 16 already shipped |
| item C | < 1 m | < 1 m | one command |
| **wave total `TestApp` wall** | **~78 m** (range 65–95) | **80 m 22 s** | against a **6 h** ceiling |
| the suite | ~4 m | **3 m 38 s** | **3824 passed, 0 failed, total 3824** — unchanged, verified by COUNT |

**The scaling prediction was the sharp one.** The design priced X0 by scaling the factor-2 datasets by the
measured factor-1/factor-2 mean ratio of **3.06×**. Measured on the arms themselves: 1510 s / 491 s = **3.08×**.

**Two per-dataset facts the pooled numbers hide:**

- The three factor-1 controls took **167 s** in X1 and **165 s** in X0 — **1.2 % apart**. The flag is a no-op in
  wall time as well as in counts, which is `C17-B` from a third direction.
- `D12_c14_585_afbin2` took **14 s in both arms** — ratio **1.00×** — while its star counts changed on 9 of 9
  positions. **Wall time is not a proxy for the treatment having taken**; `C17-V3` is, and that is why the
  design made it a validity clause rather than an observation.

### The drop order was never invoked

`D1` (drop `D14`), `D2` (drop `D03`) and `D3` (drop arm X1 and use wave 15's `land_mor0` as the status-quo side)
were all pre-registered with their denominators. **None fired.** `C17-A` was scored at its full **63** and
`C17-B` at its full **27**, and no cross-wave substitution was made for X1. Recorded so a later reader does not
have to check.

---

## §6 — Item C: the answer flipped, and that is a finding about the instrument

The design's §4 recorded the pre-registration-time reading (`ghili` id 1 = `Disc`; `console` id 2 = `Conn` with
no username) and instructed the controller to re-run it at wave time and record whatever it said. At wave time
it read **`Disc`** — the eighth consecutive NOT ATTEMPTABLE.

Re-read at write-up time, **2026-08-10T21:54:16Z**, ~5 minutes after the arms finished:

```
 SESSIONNAME               USERNAME                 ID  STATE   TYPE        DEVICE
 services                                            0  Disc
>console                   ghili                     1  Active
 rdp-tcp                                         65537  Listen

 USERNAME              SESSIONNAME        ID  STATE   IDLE TIME  LOGON TIME
>ghili                 console             1  Active       2:09   7/18/2026 11:06 AM
```

**A connected console session exists. A1–A9 are now ATTEMPTABLE**, at the ~30 m wave 13 §3 prices.

> **Neither reading is wrong, and that is the point.** For eight waves a **single sample at a single instant**
> has been recorded as a durable property of the machine — *"the machine has no attached display"* — and a
> second sample forty minutes later disagrees. The instrument has **no `when` attached to it and no notion of a
> population**: it is an n = 1 measurement of a time-varying quantity, quoted as a standing state. That is
> [F68](followups.md)'s shape in the smallest possible clause, and it is recorded rather than repaired, because
> repairing it means sampling at the start and the end of an arm and reporting both.
>
> A second observation the same command yields, worth keeping: `IDLE TIME 2:09` puts the last user input at
> ~19:45Z, **before** the gate started at 20:27:54Z. So *"machine quiet"* held for the whole wave in the sense
> the design meant — no user input during any arm — even though a logged-in desktop session existed for part of
> it. **This wave did not attempt any UI capture and no display was touched.**

---

## §7 — Two controller errors, both recorded as findings

Both are the controller's, both are the same species, and the second is the one that matters.

### §7.1 An exit code was trusted over a log

The gate's first launch **aborted in one second** because a second fingerprint file was missing, and the driver
was right to refuse — it is a pre-registered precondition (§8 step 3 of the design writes *both* fingerprints
before the gate). The controller then read the background job's `exit=0` and reported the wave as *"running"*
without opening the log.

**`exit=0` means the wrapper exited, not that the arm ran.** The driver's refusal was a correct, loud,
one-second failure that was converted into a forty-minute-long false "in progress" by a reader who checked the
cheapest available signal. *A control that fires correctly still needs someone to read it.*

### §7.2 The progress counter had no "could not look" state — the exact defect this register has catalogued four times

The chain job's progress counter looked for `af_fit_summary.txt` under `c17_X1`. The arms write
`aggregate_summary.json` under **`binX1`**. So it printed:

```
########## C17 ARM X1 21:09:46Z ##########
  exit=0 summaries=0
########## C17 ARM X0 21:20:53Z ##########
  exit=0 summaries=0
```

— **`summaries=0` for two arms that produced 10 of 10 landings each**, and the wave was reported as failed
twice on the strength of it. **Wrong directory *and* wrong filename**, so the counter had two independent ways
to be looking at nothing, and **it had no way to say so**: it could return "0 found", and it could not return
"the path I looked at does not exist."

> **This is the defect the register has now catalogued in four scorers** — the three-state rule (unchanged /
> changed / **could-not-look**) that waves 12 and 13 built, that wave 15's G-c misapplied, and that this wave's
> own `aux_fingerprint_w17.py`, `score_c17_w17.py` and `score_d17_w17.py` all implement correctly. **The
> controller's own instrumentation did not have it.** It is recorded as a finding, not as a footnote: the
> discipline was applied everywhere it was pre-registered and nowhere it was improvised, which is the honest
> description of how a discipline actually fails.
>
> Nothing in the results moved. Both arms had in fact succeeded, the drivers' own population assertions
> (`optimize_result.csv produced: 10   expected: 10`, printed by the arm scripts themselves) said so at the
> time, and they were in the arm logs while the chain log was being read instead.

---

## §8 — What this wave did NOT run, and what it costs

- **Re-scoring the seven datasets' optimizer LANDINGS at factor 1.** Arm X0 produced them as a side effect and
  they are on disk in `/mnt/d/hf_w17/binX0/`. **No landing was compared and no `BestJ` is quoted across the two
  arms** — `BestJ` is computed by the fit under test at two different detector resolutions and is not comparable
  ([F62](followups.md)). Scoring them properly needs an out-of-sample arbiter at both factors: **~30 m** of
  additional `af-fit` plus a rule. *This is the single most interesting thing wave 17 leaves undone*, and it is
  sharper now than at pre-registration time: the seven datasets' published landings were produced at factor 2
  while thirteen waves of `af-fit` evidence about them was produced at factor 1, and §3.4 shows the two
  detectors differ by up to **2.2×** in star count at the wings.
- **The wizard-side reach of F39(b).** §3.6 establishes from code reading that `af-fit` and `bank-verify` apply
  no per-run factor and that the app derives binning from the profile. **Not confirmed in the running app.**
  ~30 m on a connected session — and §6 says a connected session now exists, so this is the natural companion to
  item C and is newly cheap.
- **The real bank at factor > 1.** All five real gate runs resolve to factor 1 from `harness_settings.json`
  (`DetectionBinningSource` = kept-from-base). Whether any of the other 14 real runs derives a factor > 1 from
  its own in-focus HFR is **unread**; reading it is ~0 (`ResolveRunDetectionBinningFactor` over 19 folders) but
  running `optimize` on them is ~90 m and buys no arbiter (the real bank has no truth).
- **Any repaired form of wave 16's S16-A(b).** Costs zero minutes and is **not done**, per [F68](followups.md)(c)
  and the charter's §3a. Not re-scored on a rate, not re-scored on the trace population, no repaired form
  evaluated anywhere in this wave.
- **[F59](followups.md)'s five knobs.** ~42 m for a fresh baseline plus re-derivation. Rejected in the design's
  §3.1 and still rejected: the knobs are absent from the pinned file, so there is no printed value to diff, and
  nothing in the register depends on the answer. **A decision with no consumer is not worth a wave.**
- **[F21](followups.md) / the step-size family.** Deliberately not this wave's item — wave 10 refuted the
  entry's own hypothesis and *the entry's case did not reproduce*, so an F21 arm would characterise a pathology
  whose population is empty. Scoped and priced for wave 18 in the design's §7: vary **sweep truncation**, not
  seed, re-fit each S0 sweep on progressively narrower sub-windows, and measure vertex displacement against the
  dataset's known optimum — an out-of-sample arbiter the F21 work has never had. **Wave 18 must measure one
  dataset first and price from that**, because this series has never run `synth-validate` as an arm and the
  `optimize` rate over-prices an `af-fit` rung ~18×.
- **[F45](followups.md)(b) production plumbing** — unchanged at ~3–4 h + tests. **[F61](followups.md)(b), F52(d),
  F46(b), F54, F50** — nothing depends on them.
- **Fixing anything found in §7, §4.4 or §3.1.** No C# changed in this wave. [F69](followups.md) and
  [F70](followups.md) are recorded with their prices and left for a wave that ships code.

---

## §9 — Lessons

1. **A narrowing "by construction" is only as good as the construction, and the cheapest way to check one is to
   read the code it names.** Wave 16 narrowed F67(c) to two candidates and both were dead before this wave ran a
   single command: candidate 1 named `RunEvaluationLoader`, the *wizard's* loader, which `TestApp optimize`
   never calls; candidate 2 was arithmetically impossible in the observed direction, because the post-filter set
   is a **subset** and the measurement had the pre-filter side smaller. *A subset cannot be larger than its
   superset* is a proof, and it cost nothing.

2. **A snapshot is only evidence for the moment it was taken.** P16's `PARAMS-DUMP` was correct, complete,
   reflective, shared by both runners — and printed **one statement before the mutation that mattered**. The
   instrument was excellent and the *placement* was wrong. When an instrument reports on state that later code
   mutates, the question "when was this printed, relative to everything that writes it?" is not pedantry; it is
   the whole validity of the reading. Item C (§6) is the same lesson at n = 1.

3. **Intervention beats correlation, and it is worth paying for even when the correlation is perfect.** 7/7 and
   13/13 on n = 20, 188 of 188 at factor 1 and 0 of 63 at factor 2, spanning both banks and three binaries, was
   already on disk and was *still* correlation. Thirty-nine minutes bought an exact-integer prediction with
   **both ends observed**: 0.000 of 63 at the status quo, 1.0000 of 63 under the treatment. That is what closes
   an entry.

4. **A file class that becomes evidence must become a control in the same wave.** Item A's answer reads
   `synthetic_meta.json` and `harness_settings.json`, so this wave fingerprinted all 59 of them before the gate
   and after every arm. The cost was one Python file; the alternative was a wave whose decisive field could have
   been rewritten underneath it by the very command measuring it (`ResolveForRun` *writes* a
   `harness_settings.json` when one is absent).

5. **The three-state rule is only as good as the places it is applied, and improvised instrumentation is where
   it is not.** Three scorers in this wave implement unchanged / changed / **could-not-look** correctly and
   demonstrate it in their self-tests. The controller's one-line progress counter did not, looked in the wrong
   directory for the wrong filename, and reported a fully successful arm as a failure **twice** (§7.2). The
   discipline held exactly where it was pre-registered and nowhere else.

6. **Two clauses of one rule can disagree in appearance and both be right.** D17-4 reports *"presets override 0
   recorded knobs"* while D17-1 reports *"the baseline differs from the shipped default on 1 knob"*. Resolving
   the apparent contradiction took one `grep` and found something better than either clause was designed to
   find: **the class has two shipped defaults for `NoiseReductionRadius`** and a green drift-guard test that
   asserts the pair on the one path where they agree (§4.4). *Reconciling two of your own measurements is
   cheaper than any new arm and occasionally more productive.*

7. **Publishing an offset is a legitimate deliverable; moving a coordinate system is not free.** F63(b) closes
   without a re-pin, with the exact field, both values, the mechanism, and the price of the move written down.
   Eight binaries of reproducibility is an asset, and spending it on a cosmetic alignment no open question
   depends on would have been the expensive kind of tidiness.

---

## §10 — Reproduce

All scorers take **WSL** paths. *A Windows path yields eight `UNEVALUATED` and looks exactly like a failed arm.*

```
python3 /mnt/d/hf_w12/score_w12.py /mnt/d/hf_w17/gate --rule G17
python3 /mnt/d/hf_w17/prov_w17.py --self-test /mnt/d/hf_w17/gate

python3 /mnt/d/hf_w17/score_c17_w17.py --self-test
python3 /mnt/d/hf_w17/score_c17_w17.py --x1 /mnt/d/hf_w17/binX1 --x0 /mnt/d/hf_w17/binX0 \
        --affit16 /mnt/d/hf_w16/affit_N --gate16 /mnt/d/hf_w16/gate \
        --w13 /mnt/d/hf_w13 --w15 /mnt/d/hf_w15 --gate /mnt/d/hf_w17/gate \
        --out /mnt/d/hf_w17/c17_score.txt

python3 /mnt/d/hf_w17/score_d17_w17.py --self-test
python3 /mnt/d/hf_w17/score_d17_w17.py --gate /mnt/d/hf_w17/gate --w16 /mnt/d/hf_w16 \
        --affit /mnt/d/hf_w16/affit_N --out /mnt/d/hf_w17/d17_score.txt

python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --check /mnt/d/hf_w17/bank_landing_fingerprint_BEFORE.json
python3 /mnt/d/hf_w17/aux_fingerprint_w17.py  --check /mnt/d/hf_w17/bank_aux_fingerprint_BEFORE.json
```

Drivers `/mnt/d/hf_w17/gate_w17.sh` and `/mnt/d/hf_w17/binning_w17.sh`; logs `gate_w17.log`, `binning_X1.log`,
`binning_X0.log`, `chain_w17.log`; committed scorer outputs `c17_score.txt`, `d17_score.txt`.
