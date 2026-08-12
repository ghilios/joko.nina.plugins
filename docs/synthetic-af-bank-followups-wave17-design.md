# Synthetic AF bank — followups wave 17 (design / PRE-REGISTRATION)

Charter: [`docs/waves17-21-handoff-prompt.md`](waves17-21-handoff-prompt.md).
Wave 16: [`docs/synthetic-af-bank-followups-wave16-results.md`](synthetic-af-bank-followups-wave16-results.md).
Register: [`docs/followups.md`](followups.md) — [F67](followups.md), [F68](followups.md), [F66](followups.md),
[F65](followups.md), [F63](followups.md), [F62](followups.md), [F45](followups.md), [F39](followups.md),
[F21](followups.md), [F59](followups.md).
Plan: [`plans/synthetic-af-bank-followups-wave17-plan.md`](../plans/synthetic-af-bank-followups-wave17-plan.md).
Drivers: `D:\hf_w17\` (`/mnt/d/hf_w17/`).

> ## PROVENANCE (fixed BEFORE any measurement)
>
> | input | value |
> |---|---|
> | tree | branch `ghilios/synthetic-af-bank-followups-wave13`, PR #191. This pre-registration is committed **before** the build |
> | code shipped by this wave | **NONE.** Item A uses an existing flag (`--no-run-detection-binning`); item B reads a printout wave 16 already shipped. **ONE binary, and the two-binary failure mode of wave 16 cannot occur** |
> | binary | `D:\hf_w17\exe`, built once. `TestApp.dll` sha256, `NINA.Joko.Plugins.HocusFocus.dll` sha256 and `BuildId` recorded in the results doc. `BuildId` must be **novel** against `5cb7e474` (w11), `103d61c4` (w12), `62334f10` (w13), `084e3485` (w14 gate), `df3a867d` (w15), `10bc1b47` (w16) |
> | detector | `DetectorVersion` **2**, read as a **FIELD** on all 8 gate landings. No `strings` probe is ever quoted as a detector check ([F66](followups.md)) |
> | settings **S0** | `D:\hf_w11\pinned_settings_w11.json` md5 **`a67ffc06164c81613aef5c4f8324b9b8`**. The only settings file this wave uses. **Not re-pinned** — see §3.4 |
> | profile | `astrodet` `ce3f3e63-8fd3-4b72-a0ca-d90db9441382`, pinned on **every** arm |
> | banks | `D:\SyntheticAutofocusBank` (20 datasets, `renderRequest.OptimalFocuserPosition` = truth), `D:\Autofocus Bank` (19 runs) |
> | F15 control | 42 bank landings fingerprinted **before the gate** and re-checked after every arm. **`--update-run-folder` is passed nowhere.** This wave adds a SECOND fingerprint set — §1.5 |
> | suite baseline | **3824**. This wave ships no code, so the expected count is **3824 unchanged**, verified by COUNT ([F37](followups.md)) |
>
> **No fan-out.** Every arm is `--profile-id`-pinned and a pinned arm cannot fan out at all (wave 11's K3);
> the authorisation is worth 1.33x, not 4x ([F60](followups.md)). Wave 5's phi table is quoted nowhere.
>
> **Everything in §2.2 and §5 marked *(measured at pre-registration time)* was computed from artifacts already on
> disk BEFORE this document was committed.** That is [F68](followups.md)(b) applied as written — *derive the
> denominator on the artifacts the clause will read, at pre-registration time* — and those numbers are **not
> results of this wave**. The wave's own measurement is the intervention in §2.5, whose outcome is unknown.

---

## §0 — Items and their state

| item | question | cost | ships? |
|---|---|---|---|
| **RULE G17** | the gate: eight values, two pins, an eighth binary | ~42 m | stopping gate |
| **item A / RULE C17** | **F67(c): decide the cause of the `af-fit` vs `optimize` star-count disagreement** | ~36 m + Python | no code; a register close |
| **item B / RULE D17** | **F63(b) detector half: how far is the pinned file's detector from the SHIPPED default?** | **~0 m of `TestApp`** | no code; a costed recommendation |
| **item C** | `query session` | < 1 m | — |

**Total `TestApp` wall estimate: ~78 m against a 6 h ceiling.** §6.

---

## §1 — RULE G17: the gate, and this wave's stopping gate

Identical instrument to G12–G16. Eight runs, `optimize --per-run --max-evals 250`, sequential, both pins named in
the driver header, machine quiet, one `TestApp.exe`.

```
toml999 0.995784 | CWhiteFocus 0.996068 | uneven 0.996368 | muggsie 0.997195
mccomiskey 0.976746 | D18 0.999882 | D19 0.999487 | D20 0.999738
```

Driver `/mnt/d/hf_w17/gate_w17.sh`. Scored with
`python3 /mnt/d/hf_w12/score_w12.py /mnt/d/hf_w17/gate --rule G17` and
`python3 /mnt/d/hf_w17/prov_w17.py --self-test /mnt/d/hf_w17/gate`. **WSL paths only** — a Windows path yields
eight `UNEVALUATED` and looks exactly like a failed arm.

### §1.1 The clauses, each with its F68 four-part statement

Every clause below names **(P)** the population as a concrete artifact and field, **(S)** the statistic,
**(A)** the aggregation, and **(E)** what it returns when its set is empty.

| clause | threshold | P — population (artifact + field) | S — statistic | A — aggregation | E — empty set |
|---|---|---|---|---|---|
| **G17-1** | all eight `BestJ` reproduce the table to **6 dp**; a partial reproduction is a FAILURE and stops the wave | the 8 files `/mnt/d/hf_w17/gate/<run>/aggregate_summary.json`, field `BestJ` (one row per run; `<run>` from the fixed list of 8) | `round(BestJ, 6)` compared to the K8 constant, and separately the full 17-significant-digit repr | **conjunction over exactly 8**; the reported number is `MATCH count / 8` | **FAIL, never vacuous PASS.** A missing/unreadable `aggregate_summary.json` is UNEVALUATED for that run and UNEVALUATED counts as a failure of G17-1. G17-2 fires first and the wave stops |
| **G17-2** | `aggregate_summary.json produced: 8, expected: 8` | `find /mnt/d/hf_w17/gate -mindepth 2 -name aggregate_summary.json` | count | equality to the literal 8 | 0 != 8 ⇒ FAIL. The count is asserted **in the driver, before any scorer runs** |
| **G17-3a** `BuildId` | exactly **one** distinct value, and **novel** against the six recorded ids | `Provenance.BuildId` in the 8 `optimized_settings.json` under `/mnt/d/hf_w17/gate/<run>/**/` | the string | set cardinality == 1 **and** set-disjointness from `PRIOR_BUILD_IDS` | a landing that cannot be read is UNEVALUATED **before any field is read** and is a FAIL; an empty id set is a FAIL, not "novel" |
| **G17-3b** `DetectorVersion` | **2** on all eight, read as the **FIELD** | `Provenance.DetectorVersion`, same 8 landings | the value as a string | set equality to `{"2"}` | empty set != `{"2"}` ⇒ FAIL |
| **G17-3c** `ProfileId` | contains `ce3f3e63-...` on all eight **and** cardinality exactly 1 | `Provenance.ProfileId`, same 8 landings | containment + the string | `all(...)` over 8 **and** cardinality == 1 | `all()` over an empty list is vacuously true, so the **cardinality clause carries it**: cardinality 0 != 1 ⇒ FAIL. Both clauses are evaluated, never one |
| **G17-3d** `FitInputs` | one distinct value, exactly `MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid` | `Provenance.FitInputs`, same 8 landings | the string | set equality to the singleton | empty set: the scorer reports `distinct FitInputs: 0` and FAILS on the UNEVALUATED clause above |
| **G17-3e** `ConcurrencyCheck` | `exclusive` on **all eight**, read **across the arm** | `Provenance.ConcurrencyCheck`, same 8 landings | the string | `all(c == "exclusive")` over a list that carries an explicit `None` for every unreadable landing | a `None` entry is `!= "exclusive"` ⇒ FAIL. The list is built with a `None` placeholder precisely so an empty read cannot shorten it into agreement |
| **G17-3f** `BaselineJ` | reproduces wave 11 ([F41](followups.md)) | `BaselineJ` in the 8 `aggregate_summary.json` | 6 dp | conjunction over 8 | same as G17-1 |
| **G17-4** | the scorer PASSES on the real arm and FAILS on a mutated copy, **with the mutation asserted by read-back** | `/mnt/d/hf_w17/gate` and a `copytree` of it with `muggsie`'s `Provenance.ProfileId` rewritten | the two verdicts | conjunction of (direction 1 PASS) and (direction 2 FAIL on **both** the containment and the cardinality clause) | if the victim landing does not exist, the self-test prints **SELF-TEST COULD NOT RUN** and returns neither PASS nor FAIL |

### §1.2 The two `strings` heaps, and the detector

`strings -el` (UTF-16, `#US`) is used **only** as a flag-presence probe. `--no-run-detection-binning` must appear
on the final `TestApp.dll`; plain `strings` will find 0 and that is expected, not a failure. **A `strings` result
is never quoted as a detector-version check** — the `DetectorVersion` FIELD is (F66).

### §1.3 What a G17 PASS does and does not prove

The gate runs at `MaxOutlierRejections = 0`, so it says nothing about the rejection path. This wave ships **no
code**, so the gate's job here is narrower and should be stated as such: it establishes that **the eighth binary
of this series reproduces the coordinate system**, which is the precondition for reading item A's arm against
wave 15's and wave 16's numbers. It is a control on the *binary*, not on a change.

### §1.4 The gate carries item B at zero extra cost

Every one of the eight gate logs contains **both** `PARAMS-DUMP optimize/baseline` and
`PARAMS-DUMP optimize/seed`. RULE D17 (§3) is computed from those blocks. **Do not delete the gate logs.**

### §1.5 The F15 control, extended — and why it is extended this wave

The 42 bank `optimized_settings.json` are fingerprinted before the gate and re-checked after every arm
(`/mnt/d/hf_w15/bank_fingerprint_w15.py`). **This wave adds a second set**, because item A's answer *depends on
the contents of two other file classes inside the bank*:

- the 39 `harness_settings.json` (one per run folder) — `HarnessSettingsStore.ResolveForRun` **writes one when it
  is absent**, and reads it otherwise (`  settings: ... (existing, not re-derived)`);
- the 20 `synthetic_meta.json` — `expectedOptimal.detectionBinning` is the field that decides item A's answer.

`/mnt/d/hf_w17/aux_fingerprint_w17.py` covers both, with the same three states (unchanged / changed /
could-not-look) and a separate POPULATION clause. **A file class that becomes evidence must become a control in
the same wave**; wave 15 learned the mirror of this (a control whose premise was never tested across the
population).

---

## §2 — Item A: F67(c), and the two-candidate narrowing is WRONG

### §2.1 What wave 16 concluded, and the exact sentence that fails

RULE P16 returned **P-b**: 53 of 55 `StarDetectorParams` fields identical between `af-fit`'s bundle and
`optimize`'s baseline bundle, the other two inert by proof. From that, F67(c) was narrowed **"by construction and
not by hypothesis"** to two candidates: the frame loading, or the counting/gating stage.

**The narrowing rests on a premise that is false, and the pre-registration that produced it says so in its own
words.** `OptimizationDiagnosticRunner.cs:406-409`, the comment sitting immediately above the two `ParamsDump`
calls:

```
// Printed HERE, where the bundles are constructed. Two fields are re-derived per run afterwards and
// are logged where that happens: PixelScale (per-run, from the frame headers ...) and, only under
// --apply-run-detection-binning, DetectionBinning + PixelScale together via
// ApplyRunDetectionBinningIfRequested. Neither the gate nor the P16 probe passes that flag.
```

Twenty lines away, at `:216`:

```csharp
bool applyRunDetectionBinning = !DiagnosticUtil.HasFlag(args, "--no-run-detection-binning");
```

**The flag polarity is inverted. F39(b) was adopted as the DEFAULT in wave 8; `--apply-run-detection-binning` is
a retained no-op and `--no-run-detection-binning` is the opt-out.** So `ApplyRunDetectionBinningIfRequested` ran
on the gate and on the P16 probe, and it mutates `ctx.Baseline` **after** `ParamsDump.Write` has already printed
it:

```csharp
DetectionBinningResolver.ApplyFactor(ctx.Seed, factor);
DetectionBinningResolver.ApplyFactor(ctx.Baseline, factor);   // sets DetectionBinning AND PixelScale
```

> **P16 dumped the params at construction time and compared them to `af-fit`'s params at detection time.**
> The dump is not wrong; it is a snapshot taken one step before the mutation that matters. This is
> [F66](followups.md)'s shape in a new place — an instrument that cannot observe the thing it was built to
> observe — and it is [F68](followups.md)'s shape too, because *"53 of 55 fields identical"* is a count over a
> population (**the bundle as constructed**) that is not the population the claim is about (**the bundle as
> detected**).

The probe log says it out loud, and it was in the artifact all along
(`/mnt/d/hf_w16/probe/D12_c14_585_afbin2.log:135`):

```
detection binning (F39b): 2 from synthetic_meta.json expectedOptimal.detectionBinning (physics-derived);
                          detecting at PixelScale 0.62965 arcsec/binned-px
```

### §2.2 The free evidence, computed at pre-registration time

*(All four tables below were computed from artifacts already on disk, before this document was committed. They
are the satisfiability analysis, not the wave's result.)*

**(a) `expectedOptimal.detectionBinning` over the 20 synthetic datasets** — read from
`/mnt/d/SyntheticAutofocusBank/<D>/synthetic_meta.json`:

| factor | datasets |
|---|---|
| **2** | `D08`, `D09`, `D10`, `D12`, `D14`, `D15`, `D17` — **7** |
| 1 | the other **13** |

**F67's disagreeing set is `{D08, D09, D10, D12, D14, D15, D17}`. The two sets are EQUAL.**

**(b) The factor actually applied**, read from the `detection binning (F39b): <n> from <source>` line in each of
the 20 `/mnt/d/hf_w15/land_mor0/<D>/run.log` — the very arm F67 was measured on: **2 on those 7, 1 on the other
13, 0 could-not-look.**

**(c) Per-position star-count equality**, `af-fit`'s `Stars` against `optimize`'s `currentStarCount`:

| population | positions | exactly equal |
|---|---|---|
| 13 synthetic datasets at factor **1** (w13 `affit_syn` vs w15 `land_mor0`) | 115 | **115** |
| 7 synthetic datasets at factor **2** (same pair) | 63 | **0** |
| the 8 wave-16 gate runs at factor **1** — **5 of them REAL bank runs** (w16 `gate` vs w16 `affit_N`) | 73 | **73** |

**188 of 188 at factor 1. 0 of 63 at factor 2.** The factor-1 half spans both banks and both waves.

**(d) Two free stability controls, both of which had to hold before (c) means anything:**

- `af-fit`'s `Stars` is **identical between wave 13 and wave 16** on all 20 datasets, three binaries apart.
- `optimize`'s `currentStarCount` is **identical between wave 15 and wave 16** on `D12` and `D17`, two binaries
  apart. So neither side of (c) is binary-dependent.

### §2.3 Why the two surviving candidates cannot be the cause

**Candidate 1 — the frame loading — is excluded by code reading, and the exclusion is exact.**
`AfFitDiagnosticRunner` loads through `DetectionSource.LoadAsync` → `DiagnosticUtil.LoadRenderedImage(path,
profileService)`. `OptimizationDiagnosticRunner.PrepareRunAsync` loads through
`DiagnosticUtil.LoadRenderedImage(frame.Path, ctx.ProfileService)`. **It is the same static method with the same
two arguments on the same file.** `RunEvaluationLoader.LoadRenderedImageAsync` — the *wizard's* loader, the one
wave 16 named — is **not on the `TestApp optimize` path at all**; `optimize` reuses only
`RunEvaluationLoader.HocusFocusSplitFrameDetector`. Wave 16 named a loader the measured instrument does not call.

**Candidate 2 — the counting stage — cannot produce the observed sign.**
`HocusFocusStarDetection.BuildStarDetectionResult` starts from `starDetectorResult.DetectedStars` (the same list
`af-fit` counts) and can only ever **remove** from it before `result.DetectedStars = starList.Count`. So
post-filter <= pre-filter whenever both sides run the same detector on the same pixels. The measurement is
`af-fit` (pre-filter) **strictly below** `optimize` (post-filter) on 63 of 63 positions. *A subset cannot be
larger than its superset.*
And on this population the filter is a **no-op** anyway: the ROI crop needs `!Region.IsFull()`, the outlier trim
needs `MeasurementAverage == MeanOutliers`, and every params dump on both sides reads
`Region={OuterBoundary={StartX=0, StartY=0, Height=1, Width=1}, InnerCropBoundary=}` and
`MeasurementAverage=Median`. The brightest-N trim runs **after** `result.DetectedStars` is assigned and cannot
move it.

**What remains is `DetectionBinning`.** `StarDetector` resamples the measurement image by
`p.DetectionBinning` (`StarDetector.cs:494`), so at factor 2 the gates see 4x the flux per pixel and admit
fainter stars — which predicts the observed defocus grading (`D08` ratios 0.45-0.52 at the wings against 0.82 at
focus: binning helps most where the star is faint and spread).

> **F67(c)'s answer is therefore expected to be NEITHER of wave 16's two candidates.** The pre-registered
> expectation is stated so it can be refuted, and RULE C17 has a branch for every other outcome.

### §2.4 And this is a PRODUCT-vs-INSTRUMENT question with a different answer than wave 16 gave

Wave 16 recorded F67 as a **PRODUCT** finding on the reasoning that both surviving candidates are production code
paths. If the cause is F39(b), that reasoning does not transfer, because F39(b) is **harness-only**:

| entry point | applies a per-run detection-binning factor? |
|---|---|
| `TestApp optimize` | **YES, by default** (`OptimizationDiagnosticRunner.cs:216, 582`) |
| `TestApp golden eval` | YES (`GoldenEvalRunner.cs:269`) |
| `TestApp af-fit` | **NO** — no such flag exists on the command |
| `TestApp bank-verify` | **NO** |
| the shipped wizard (`StarDetectionOptimizerWizardVM`) | it uses the **profile's** `StarDetectionOptions.DetectionBinning`, plus an explicit user-driven re-optimize-at-a-different-factor path. There is no `synthetic_meta.json` and no per-run physics derivation in the app |

So the honest reading, if C17 returns C-BINNING, is: **an INSTRUMENT fact with a real product consequence.**
The instrument fact is that three harness commands disagree about what binning a bank run is detected at. The
product consequence is that **the two star counts are still two different measurements with the same name** —
wave 16's proof of that stands, it is just not what caused this disagreement, and it is *inert at the pinned
settings* (Region Full, `MeasurementAverage=Median`). RULE C17 records both, separately, and never lets one
stand in for the other.

**The register consequence is larger than F67.** Any wave-to-wave comparison of `af-fit` against `optimize` on
`{D08, D09, D10, D12, D14, D15, D17}` has been comparing two detectors. Wave 15's RULE L15 lost its verdict to
exactly this (G-c returned *could not look* on exactly those seven).

### §2.5 RULE C17 — the instrument, and it is an INTERVENTION

Correlation of 7/7 and 13/13 on n = 20 is strong and is still correlation. The register's own standing
discipline is **intervention beats correlation**, so the decisive clause turns the suspected cause **off** with
an existing flag and predicts an exact number that is already on disk.

**Two arms, same binary, same settings, same profile, sequential, one `TestApp.exe`.**

| arm | command | datasets |
|---|---|---|
| **X1** (status quo) | `optimize --per-run --max-evals 250 --settings S0 --profile-id astrodet` | the **7** factor-2 datasets + **3** factor-1 controls (`D03_redcat_250mm`, `D07_rc10_2000mm`, `D11_rc10_585_afbin2`) |
| **X0** (treatment) | the same **plus `--no-run-detection-binning`** | the same 10 |

`D11_rc10_585_afbin2` is chosen deliberately: its name says `afbin2` and its derived factor is **1**. A control
picked by name rather than by the field would have been the wrong control.

`--max-evals 250` is kept although the search result is discarded, so the arm is the **same invocation shape** as
the gate and can be priced from it. `currentStarCount` is the baseline evaluation and is computed before the
search either way.

#### The clauses

Every clause carries its F68 four-part statement. `<pos>` always means a focuser position present on **both**
sides; the common set is computed first, its size printed, and any non-common position is named.

| clause | threshold | P — population | S — statistic | A — aggregation | E — empty set |
|---|---|---|---|---|---|
| **C17-V1** *validity: the status quo reproduces* | **rate 1.000 of 90** | for each of the 10 datasets, `currentStarCount` in `/mnt/d/hf_w17/binX1/<D>/attempt01/optimize_result.csv` against the same column in `/mnt/d/hf_w15/land_mor0/<D>/attempt01/optimize_result.csv`, joined on `focuserPosition`. 7x9 + 3x9 = **90** positions, asserted before scoring | per-position integer equality | `equal / common`, one rate with the denominator printed; **not** a per-dataset median | any missing CSV, any non-common position, any unparseable field ⇒ **COULD-NOT-LOOK**, counted in its own state, that dataset named, and C17-V1 is **UNEVALUATED** (never PASS). `common == 0` ⇒ UNEVALUATED |
| **C17-V2** *validity: population* | `optimize_result.csv produced: 10 / 10` in **each** arm; 90 common positions in each | `find /mnt/d/hf_w17/binX{0,1} -name optimize_result.csv` | count | equality to 10, twice | 0 != 10 ⇒ FAIL, asserted **in the driver** before any scorer runs |
| **C17-V3** *validity: the flag took* | the string `--no-run-detection-binning: F39(b) DISABLED` present in **10 of 10** X0 logs and **0 of 10** X1 logs; and `detection binning (F39b): <n> from` present in 10 of 10 X1 logs | the 20 `run.log` files | per-log boolean | two counts, both exact | a log that cannot be read is COULD-NOT-LOOK and C17-V3 is UNEVALUATED. **A flag that cannot be shown to have taken is not a treatment** |
| **C17-A** *decisive* | **rate 1.000 of 63** | for each of the 7 factor-2 datasets, X0's `currentStarCount` against `Stars` in `/mnt/d/hf_w16/affit_N/syn/<D>/af_fit_points.csv`, joined on focuser position. **63** positions (7 x 9, verified on disk at pre-registration) | per-position integer equality | `equal / common`, **a rate**, denominator printed and re-printed if the drop order fires | as C17-V1. `common == 0` ⇒ UNEVALUATED, and the branch table routes UNEVALUATED to its own outcome, not to C-THIRD |
| **C17-B** *control: the flag is a no-op where the factor is already 1* | **27 of 27 on both equalities** | the 3 control datasets: X0 vs X1 `currentStarCount`, and X0 vs `af-fit`'s `Stars`. 3 x 9 = **27** positions | per-position integer equality | two rates over the same 27 | as C17-V1 |
| **C17-C** *the by-construction exclusions, free* | reported; and the factor-1 rate is a **precondition** for reading the branch table (see below) | §2.2(c)'s three sets, recomputed by the scorer rather than quoted from this document | per-position equality, grouped by the F39(b) factor parsed out of each `optimize` log | two rates: `equal/positions` within factor 1 and within factor 2 | a run whose log has no F39(b) line is COULD-NOT-LOOK, named, and **removed from both denominators, which are re-printed** |
| **C17-D** *the counting stage is inert here* | every dumped block reads `Region=...Height=1, Width=1...` **and** `MeasurementAverage=Median` | every `PARAMS-DUMP` block in `/mnt/d/hf_w17/gate/*.log` (16 blocks), `/mnt/d/hf_w17/binX{0,1}/*/run.log` (40 blocks) and `/mnt/d/hf_w16/affit_N/*/*/run.log` (39 blocks) | per-block boolean conjunction of the two field tests | `satisfying / total`, total asserted **> 0** and printed | **0 blocks found ⇒ UNEVALUATED, explicitly, and the branch table treats C17-D as NOT ESTABLISHED.** This is `P16-INSTRUMENT-vs-PRODUCT`'s empty-domain defect, written out so it cannot recur |

#### The branch table — a verdict per candidate, plus both and neither

Read only after **C17-V1, C17-V2 and C17-V3 all PASS**; if any is UNEVALUATED or FAIL, **RULE C17 returns
UNEVALUATED and names the failing gate.** The rule is applied as written; a clause that cannot fire is a finding.

| outcome | reached when | means | reachable by |
|---|---|---|---|
| **C-BINNING** | `C17-A == 1.000` **and** `C17-B == 27/27 both ways` **and** `C17-C` factor-1 rate `== 1.000` **and** `C17-D` established | the cause is `DetectionBinning`. **NEITHER of wave 16's two candidates.** F67(c) closes | the pre-registered expectation |
| **C-BOTH** | `0 < C17-A < 1.000` **and** `C17-B == 27/27` | binning explains part of the disagreement and something else explains the rest; the residual positions are listed by name with their two counts | any partial convergence |
| **C-LOAD** | `C17-A == 0` (or near it) **and** `C17-B == 27/27` **and** `C17-D` established | the remaining difference is upstream of the detector with identical params, i.e. the pixels. Candidate 1 survives after all and the §2.3 code reading is wrong | a genuine difference inside `DiagnosticUtil.LoadRenderedImage` between the two call sites (e.g. profile state mutated between them) |
| **C-COUNT** | `C17-D` **not** established anywhere in the population, **and** the af-fit side is the LARGER on at least one position | candidate 2 survives | a run whose `Region` is non-Full or whose `MeasurementAverage` is `MeanOutliers`. **Neither occurs at S0**, so on this population C-COUNT is reachable only through a C17-D failure — which is exactly why C17-D is measured rather than assumed |
| **C-THIRD** | `C17-A` fails **and** `C17-B` also fails | the flag perturbed something global; the two-candidate narrowing AND the binning hypothesis are all wrong. The wave reports the mechanism it can see and closes nothing | any global perturbation by the flag |
| **C-UNEVALUATED** | any validity clause UNEVALUATED, or `C17-A`'s common set empty | said **by name**, per dataset | a missing arm |

> **`C17-D` is reported, and it is never a bar on its own.** It establishes that the *pre-filter vs post-filter*
> distinction wave 16 proved is **inert at S0**. That is a statement about this population, not a retraction:
> the two counts remain different measurements with the same name, and the register keeps that.

### §2.6 What item A does NOT claim

- It does not claim the app is wrong. F39(b) is a deliberate harness default, argued and measured in wave 7.
- It does not re-open RULE P16. P16's 53-of-55 is **true of the bundles it read**; what is corrected is the
  inference drawn from it, and the correction names the line of code and the artifact line that show it.
- It does not score any landing, any `BestJ`, any sigma_focus or any R^2. Nothing here is computed by a fit under
  test ([F62](followups.md)); the quantity is an integer star count read off two CSVs.

---

## §3 — Item B: RULE D17, F63(b)'s detector half, at zero `TestApp` cost

### §3.1 Why this item, and why not the other two

**Chosen: F63(b)'s detector half.** The handoff prices it at ~1 h and calls it *"a coordinate-system move."* It
is only a move **after** somebody measures how big the move is, and nobody has. **The measurement is already
printed**: since wave 16, every `optimize` log carries `PARAMS-DUMP optimize/seed` — which is
`ApplyAfContext(HocusFocusStarDetection.BuildDefaultStarDetectorParams())`, the **shipped code defaults**, held
to `StarDetectionOptions.ResetDefaults` by an existing test — beside `PARAMS-DUMP optimize/baseline`, the pinned
file's preset-derived bundle. **Decision value per hour of compute is unbounded, because the compute is zero**,
and it closes a register entry that has been carried since wave 13.

**Rejected: [F59](followups.md)'s five knobs (~1 h + re-derivation).** Same shape as F63(b) but strictly worse.
F59 is marked *Fixed (wave 11)*; what is left is a deliberate re-pin. Its five knobs are **absent** from the
pinned file, so there is no printed value to diff — measuring the offset needs a fresh export **and** a fresh
42-minute baseline, and then every cross-wave comparison in the register has to be re-derived. Nothing in the
register depends on the answer. **A decision with no consumer is not worth a wave.**

**Rejected: [F21](followups.md) / the step-size family.** The most interesting entry on the backlog and the wrong
wave for it, for a reason that is exactly this wave's discipline. Wave 10 already discharged F21's own next step
and **refuted** its hypothesis: `FindHalfWidth` is exact for a hyperbola (`sqrt(8)*HFR_min/kappa`), the walk is
not what moves, the recommender is bit-reproducible at a fixed seed, and **the entry's own case did not
reproduce** — round 1 never occurred. So a wave-17 F21 arm would be characterising a pathology **whose
population is empty**: no reproducible instance exists on disk, and the instrument (`synth-validate`) has never
been run in this series' arms and is unpriced. *Pre-registering a clause over an empty population is the F68
defect this wave is being judged on.* What F21 needs is a designed experiment on vertex identifiability, not a
re-run — scoped and priced in §7 as a recommendation for wave 18.

### §3.2 RULE D17 — the clauses

`optimize/seed` and `optimize/baseline` both pass through `ApplyAfContext`, which sets `PixelScale`, `Region`,
`ModelPSF`, `SaveIntermediateFilesPath` and `SuppressInfoLogging` identically on both. **Those five fields are
equal by construction and are therefore OUTSIDE D17's domain** — including them would inflate the agreement with
five fields that cannot disagree, which is a check that cannot fail. The domain is the remaining **50** of the 55
readable properties, and the scorer asserts the domain size before comparing.

| clause | threshold | P — population | S — statistic | A — aggregation | E — empty set |
|---|---|---|---|---|---|
| **D17-1** | report the **set** of differing field names with both values | the paired `PARAMS-DUMP optimize/seed` and `PARAMS-DUMP optimize/baseline` blocks in the **8 wave-17 gate logs**, restricted to the 50 fields `ApplyAfContext` does not set | per-field string inequality within one log | the **union** of differing names across the 8 logs, **and** the per-log count printed beside it, so a field that differs on some runs and not others cannot hide inside a union | a log missing either block ⇒ COULD-NOT-LOOK, named. **If all 8 are COULD-NOT-LOOK, D17 is UNEVALUATED and issues no verdict** |
| **D17-2** *domain* | domain size **== 50** on every log, and 55 fields parsed per block | the parsed blocks | field count | equality, per log | a block that parses to != 55 fields is COULD-NOT-LOOK for that log |
| **D17-3** *reproduction control* | the same union, recomputed on the **8 wave-16 gate logs + 2 wave-16 probe logs**, must equal wave 17's | `/mnt/d/hf_w16/gate/*.log`, `/mnt/d/hf_w16/probe/*.log` | as D17-1 | set equality between the two unions | a wave-16 log that cannot be read is named; if fewer than 8 are readable the control is UNEVALUATED and **D17's verdict still stands on wave 17's own logs**, with the control reported as not run |
| **D17-4** *the `UseAdvanced` trap* | the `UseAdvanced=False ... Simple-mode presets override N recorded advanced knob(s)` warning must be present and `N` must be reported | the `WarnIfSimpleModeOverridesTheFile` line in each `af-fit`/`optimize` log that reads S0 | the integer `N` | reported per log; the set of distinct `N` printed | absent warning ⇒ COULD-NOT-LOOK, named. **Never report the file's recorded advanced knobs as the ones in force without this line** |

#### The verdict table

| outcome | reached when | consequence |
|---|---|---|
| **D-NOOP** | `|union| == 0` | pinning the shipped default is a **no-op for the detector too**, and F63(b) closes outright with no coordinate-system move |
| **D-SMALL** | `1 <= |union| <= 3` | F63(b) closes as a **costed recommendation**: the exact fields, their two values, and the price of the move (a fresh ~42 m baseline plus the re-derivation of every cross-wave comparison). The recommendation this design pre-registers is **DO NOT MOVE IT** unless a later wave has a consumer, and the reason is named in §3.4 |
| **D-LARGE** | `|union| >= 4` | the harness has been describing a materially different detector from the shipped one for twelve waves. **That is a register finding in its own right** and the wave says so; still no move this wave |
| **D-UNEVALUATED** | all 8 logs COULD-NOT-LOOK | said by name |

*(Reachable range on the real artifacts, measured at pre-registration time: the same computation over the eight
**wave-16** gate logs yields a union of size **1**. So D-SMALL is the expected branch; D-NOOP and D-LARGE both
remain reachable on wave 17's own logs, and D17-3 is the clause that would catch a wave-17 union that disagrees
with wave 16's.)*

### §3.3 D17 is not a harvest of a previous wave's rule

RULE F14 is closed and nothing here re-opens it. D17 compares a **different pair of bundles** than P16 did
(`seed` vs `baseline`, not `af-fit` vs `baseline`), answers a **different register entry** (F63(b), which has no
prior rule and no prior verdict), and is computed on **wave 17's own gate logs**, with wave 16's used only as a
reproduction control that is explicitly labelled as such and can never be quoted as new evidence.

### §3.4 The recommendation this design fixes in advance

**Do not re-pin.** The eight-value coordinate system has now reproduced across seven binaries and two settings
files and is the most valuable instrument this series owns. Moving it costs a fresh 42-minute baseline plus the
re-derivation of every cross-wave comparison, and buys a cosmetic alignment that no open question depends on.
**Fixed before the data**: whatever D17 measures, this wave issues a costed recommendation and does not move the
pin. If D17 returns **D-LARGE**, the recommendation changes to *"the offset must be published beside every
future arm's provenance"* — still not a move.

---

## §4 — Item C: the nine UI changes A1-A9

One command, one line, recorded. `cmd.exe /c "query session"`.
*(Run at pre-registration time: `ghili` id 1 is **`Disc`**; `console` id 2 is `Conn` with no username — the
no-user-logged-in state. **Eighth consecutive wave. NOT ATTEMPTABLE.** The controller re-runs it at wave time and
records whatever it says then; if it reads `Conn`, wave 13 §3's procedure is correct and costs ~30 m.)*

---

## §5 — Satisfiability, in two parts

### §5.1 Part 1 — maxima and reachable range, computed on the real artifacts

Not on constructed input. *Constructed populations cannot detect a denominator error, because the constructor
chooses the denominator* (F68).

| clause | bar | max attainable, ON THE ARTIFACT THE CLAUSE READS | min attainable | both reachable? | how the denominator was derived |
|---|---|---|---|---|---|
| G17-1 | 8 of 8 at 6 dp | 8 — the driver asserts 8 landings before scoring | 0 | yes; a mutated `BestJ` in the self-test copy demonstrates the FAIL direction | fixed list of 8 run names |
| G17-2 | count == 8 | 8 | 0 | yes | `find` over the arm |
| G17-3a-f | see §1.1 | each is a set-equality or a cardinality; all attain both directions in `prov_w17.py --self-test` | | yes, demonstrated in both directions before being quoted | the 8 landings |
| **C17-V1** | rate 1.000 over **90** | 90/90 = 1.000 | 0.000 | yes | counted on disk: 7 datasets x 9 positions + 3 x 9 = 90; every one of the 10 has exactly 9 rows in both `optimize_result.csv` files |
| **C17-A** | rate 1.000 over **63** | 63/63 = 1.000 | **0.000, and 0.000 is the CURRENTLY MEASURED value** (wave 15 and wave 16 both) | **yes — both ends are not merely attainable, both have been observed on this exact pair of artifacts** | counted on disk: for each of `D08 D09 D10 D12 D14 D15 D17`, `af_fit_points.csv` has 9 rows and `optimize_result.csv` has 9 rows and the position sets are equal ⇒ common = 9 each ⇒ 63. **This is the check S16-A(b) never made** |
| **C17-B** | 27 of 27, twice | 27 | 0 | yes | `D03`, `D07`, `D11` have 9 positions each on both sides (verified on w13 `affit_syn` and w15 `land_mor0`) |
| **C17-C** | reported; factor-1 rate is a precondition | factor-1: 188; factor-2: 63 | 0 | yes | counted on disk, §2.2(c) |
| **C17-D** | all blocks satisfy | 16 + 40 + 39 = **95** blocks | 0 | yes — a block with `MeasurementAverage=MeanOutliers` fails it, and the scorer's self-test constructs one | counted on disk: 2 blocks per `optimize` log, 1 per `af-fit` log |
| **D17-1** | report the union | **50** (the whole domain) | 0 | yes | 55 parsed fields minus the 5 `ApplyAfContext` sets; asserted per log |
| D17-3 | set equality | — | — | yes | 10 wave-16 logs |

**No clause in this wave is stated as an absolute count except the population assertions, and every population
assertion's number is derived from a `find`/row count on disk rather than carried across from another
instrument.** Where a count would have done, a **rate with a named denominator** is used, so a drop-order cut
(§6) cannot silently change a bar.

### §5.2 Part 2 — measurement to consuming-branch reachability

*"Check that BOTH branches of a clause are reachable, not just that the PASS value is attainable"* (wave 15's
G-d), plus *"what does it return when its set is empty"* (F68).

| branch | is there an input that reaches it? | is that input possible on this wave's population? |
|---|---|---|
| **C-BINNING** | `C17-A == 1.000`: X0's counts equal `af-fit`'s | yes — this is the pre-registered expectation, and the mechanism (§2.3) predicts exact integer equality, not approximate |
| **C-BOTH** | any X0 rate strictly between 0 and 1 | yes — e.g. binning explains 6 of 7 datasets and one has a second cause |
| **C-LOAD** | `C17-A == 0` with `C17-B` passing | yes — if the flag takes and nothing converges, the loader is back in play. **This branch would refute §2.3's code reading, which is why it is retained rather than argued away** |
| **C-COUNT** | `C17-D` fails somewhere and af-fit is the larger side on some position | **reachable only through a C17-D failure at S0.** Recorded explicitly: at S0 the ROI crop and the outlier trim are both no-ops, so C-COUNT's ordinary route is closed and the design says so instead of listing an outcome nothing can produce |
| **C-THIRD** | `C17-A` and `C17-B` both fail | yes — a flag that perturbs the pipeline globally |
| **C-UNEVALUATED** | a missing arm, an empty common set, a flag that cannot be shown to have taken | yes; and **each of C17-V1/V2/V3/A/B/D has an explicit empty-set answer in §2.5, none of which is "PASS"** |
| **D-NOOP** | union size 0 | reachable — it is what a fully-aligned pinned file would produce |
| **D-SMALL** | union size 1..3 | reachable; measured as 1 on wave 16's logs |
| **D-LARGE** | union size >= 4 | reachable — the domain is 50 |
| **D-UNEVALUATED** | all 8 logs missing a block | reachable (it is what wave 16's build-1 gate produced for the *af-fit* side of P16) |
| **G17 FAIL** | any of the eight `BestJ` moving, or a repeated `BuildId`, or a non-`exclusive` `ConcurrencyCheck` | reachable; all demonstrated by `prov_w17.py --self-test` before the check is quoted |

**Three clauses are deliberately NOT decisive and are labelled so everywhere they appear:** C17-C (correlational,
computed before the wave), C17-D (an inertness precondition), D17-3 (a reproduction control). None of them can
carry a verdict on its own, and none of them appears in a conjunction that produces one.

### §5.3 The F68 self-audit of this design

Three consecutive waves shipped a satisfiability defect of their own class: a denominator, an aggregation, an
empty domain. The three specific traps, and where this design closes each:

1. **A denominator carried across instruments.** Every denominator in §5.1 was counted **on the artifact the
   clause reads**, at pre-registration time, and the counting command is in the plan. No number is inherited from
   another wave's view of the same runs.
2. **An aggregation that cannot see the effect.** There is **no median anywhere in this design.** Every
   aggregation is a rate over a named denominator or a set operation, and the two rates that matter (C17-A,
   C17-C) are reported per dataset as well as pooled, so an effect confined to one dataset is visible.
3. **A quantifier over a possibly-empty domain.** Every clause in §1.1, §2.5 and §3.2 has an explicit **E**
   column, and in **every** case the empty answer is UNEVALUATED or FAIL — never a vacuous PASS. The one clause
   in this wave that most resembles `P16-INSTRUMENT-vs-PRODUCT` is **C17-D**, and its empty answer ("0 blocks
   found") is written as **NOT ESTABLISHED**, which routes the branch table away from C-BINNING rather than
   toward it.

A fourth trap this wave adds for itself: **a drop-order cut must not change a bar.** Because C17-A and C17-V1 are
rates, dropping a dataset changes the denominator and not the threshold, and the scorer re-prints both.

---

## §6 — Budget, priced from the same instrument, with the drop order

| step | population | instrument | estimate | how it was derived |
|---|---|---|---|---|
| **RULE G17** — the gate | 8 runs | `optimize --per-run --max-evals 250`, mixed real+synthetic | **~42 m** | wave 16 measured 40 m 55 s and 41 m 36 s on the identical arm |
| **item A, arm X1** | 10 runs (7 at factor 2, 3 at factor 1) | the same | **~10 m** | per-dataset wall times measured from wave 15's `land_mor0` logs: the 7 factor-2 datasets sum to **458 s**, the 3 controls to **158 s** |
| **item A, arm X0** | the same 10, at factor 1 | the same | **~26 m** (range 15-35 m) | two independent derivations that agree: (a) the factor-2 datasets scale by the measured factor-1/factor-2 mean ratio **200.4 s / 65.5 s = 3.06x** ⇒ 458 x 3.06 = 1403 s; (b) the factor-1 population mean 200.4 s x 7 = 1403 s. Plus the 3 controls at 158 s (unchanged — the flag is a no-op for them) |
| **item A** scoring | Python over CSVs | — | < 5 m | |
| **item B** RULE D17 | Python over the gate logs | — | **0 m of `TestApp`**, < 5 m of Python | |
| **item C** | one command | — | < 1 m | |
| **wave total `TestApp` wall** | | | **~78 m** (range 65-95 m) against a **6 h** ceiling | |
| the suite | `dotnet.exe test` | — | ~4 m | no code ships; expected **3824 unchanged** |

**The optimize rate is NOT applied to anything `af-fit` does, and no `af-fit` runs at all this wave.** Wave 16's
lesson stands: applying an `optimize` rate to an `af-fit` rung over-prices it ~18x. Item A reads `af-fit` numbers
**off disk** (wave 16's `affit_N`, which is byte-identical to wave 14's control rung) rather than re-running
them, which is the cheapest instrument being the one already printed.

### The drop order, fixed in advance

| # | cut | saves | what it costs | what it does to a bar |
|---|---|---|---|---|
| **D1** | drop `D14_cdk14_2563mm_e47` from **both** arms | ~15 m (it is the most expensive factor-2 dataset: 228 s at factor 2, ~700 s at factor 1) | one dataset of the 7 | C17-A's denominator 63 -> 54; **the threshold stays at rate 1.000**; the scorer re-prints 54 |
| **D2** | drop the third control `D03_redcat_250mm` from both arms | ~3 m | one of three controls | C17-B's denominator 27 -> 18; threshold stays 1.000 |
| **D3** | drop arm **X1** entirely and use wave 15's `land_mor0` as the status-quo side | ~10 m | the single-binary property; C17-V1 becomes a cross-wave comparison rather than a within-wave one | must be **disclosed in the results doc by name**. Only permissible after G17 has passed |
| **never** | the gate; C17-A's remaining datasets; the first two controls; any validity clause | | | |

---

## §7 — What this wave will NOT run, and what it costs

- **The wizard-side reach of F39(b).** §2.4 establishes from code reading that `af-fit` and `bank-verify` do not
  apply a per-run binning factor and the app derives binning from the profile. **Confirming that in the running
  app is not attempted** and needs a live NINA session, which item C says does not exist. **~30 m on a connected
  session**, and it is the natural companion to item C.
- **Re-scoring the seven datasets' optimizer LANDINGS at factor 1.** Arm X0 produces them as a side effect and
  they will be on disk, but **no landing is compared, and no `BestJ` is quoted across the two arms** — `BestJ` is
  computed by the fit under test at two different detector resolutions and is not comparable
  ([F62](followups.md)). Scoring them properly needs an out-of-sample arbiter at both factors: **~30 m** of
  additional `af-fit` plus a rule. *This is the single most interesting thing wave 17 leaves undone*, because the
  seven datasets' published landings were produced at factor 2 while thirteen waves of `af-fit` evidence about
  them was produced at factor 1.
- **The real bank at factor > 1.** All five real gate runs resolve to factor 1 from `harness_settings.json`
  (`DetectionBinningSource` = kept-from-base). Whether any of the other 14 real runs derives a factor > 1 from
  its own in-focus HFR is **unread**; reading it is ~0 (it is `ResolveRunDetectionBinningFactor` over 19 folders)
  but running `optimize` on them is ~90 m and buys no arbiter (the real bank has no truth).
- **[F45](followups.md)(b) production plumbing.** Unchanged at **~3-4 h + tests**, and wave 16's outcome 4 means
  there is no recommendation for it to ride with.
- **Any repaired form of wave 16's S16-A(b).** Costs zero minutes and is **not done**, per F68(c) and the
  charter's §3a. It is not re-scored on a rate, not re-scored on the trace population, and no repaired form is
  evaluated anywhere in this wave.
- **[F59](followups.md)'s five knobs.** ~42 m for a fresh baseline plus re-derivation. Rejected in §3.1.
- **[F21](followups.md) / F18 / F25 / F26.** Scoped and priced here for **wave 18**, since the charter asked:
  - *The question left owed*: why does the FITTED VERTEX move between two noise realisations of the same sweep,
    when `FindHalfWidth` is exact and the recommender is bit-reproducible at a fixed seed?
  - *The population that exists*: wave 7's F18 control arm and wave 10's `D:\hf_w10\step_probe.sh` output — six
    S0 datasets over uncapped rounds, already on disk, with `halfWidth`, `vertexY` and the resulting step.
  - *The measurement*: `halfWidth / vertexY` is stable to x1.03-x1.14 within a dataset while `halfWidth` spans
    x1.69, so the arm must vary **sweep truncation**, not seed: re-fit each S0 sweep on progressively narrower
    sub-windows and measure vertex displacement against the dataset's known optimum. **That is an out-of-sample
    arbiter the F21 work has never had.**
  - *The price*: ~0 `TestApp` time for the re-fits if `af-fit --af-run` can be pointed at truncated frame sets,
    otherwise one `synth-validate --scenarios S0` arm at an **unmeasured** cost — and *unmeasured* is the honest
    word, because this series has never run `synth-validate` as an arm. **Wave 18 must measure one dataset
    first and price from that**, not from the `optimize` rate.
- **[F61](followups.md)(b), F52(d), F46(b), F54, F50** — nothing depends on them.

---

## §8 — Step order, fixed and not negotiable

**This wave ships no code, so the ordering hazard that cost wave 16 forty-two minutes cannot arise — and the
order is still written down, because that is what wave 16 did not do.**

1. Commit this pre-registration and the plan. **Before any measurement.**
2. Build **ONE** binary into `D:\hf_w17\exe`. Record both dll sha256 values and the `BuildId`.
3. Write the two BEFORE fingerprints (42 landings; 39 `harness_settings.json` + 20 `synthetic_meta.json`).
4. Run **RULE G17**. Score it. Self-test `prov_w17.py` in **both** directions and assert the mutation. **A
   partial reproduction stops the wave.**
5. Score **RULE D17** off the gate logs (no `TestApp` time). It is not allowed to gate anything else.
6. Run item A **arm X1**, then **arm X0**, sequentially, in the background with an `until`-loop wait. One
   `TestApp.exe`, no NINA.
7. Score **RULE C17**. Check both fingerprints again.
8. Run the suite. Verify by **COUNT**: expected **3824**, unchanged.
9. Commit, push, append the wave's section to PR #191, verify CI by COUNT read out of the log.

**If a code change becomes necessary at any point after step 2, it is a FINDING and it is disclosed in the
results doc in wave 14's own words — it is not quietly done, and the gate is re-run on the final binary.**

---

## §9 — Where the controller's brief is wrong

Recorded here because every pre-registration in this series has been asked to say so.

1. **"Narrowed BY CONSTRUCTION to two candidates" is not sound, and the brief repeats it as settled.** The
   brief's own hedge — *"a construction can be wrong"* — is correct, but the failure is not subtle and it is not
   in the reasoning: it is that **wave 16's params dump was taken one statement before the mutation that
   matters**, and the C# comment beside it asserts the mutation cannot happen because it reads the flag polarity
   backwards. §2.1. Both named candidates are excluded, one by code reading and one by arithmetic on the sign of
   the observed difference (a subset cannot be larger than its superset — §2.3).
2. **The brief's candidate 1 names a loader the measured instrument never calls.** `RunEvaluationLoader`'s
   `LoadRenderedImageAsync` is the *wizard's*; `TestApp optimize` calls `DiagnosticUtil.LoadRenderedImage`, the
   same static method `af-fit` calls. The register inherited this from F67's original table, which is describing
   the **wizard** and not the harness command whose CSV the disagreement was measured in.
3. **"Emitting BOTH counts from the `optimize` path separates candidate 2 outright" would have cost code and
   answered a question already closed by arithmetic.** On this population the post-filter count is *provably*
   the pre-filter count (Region Full, `MeasurementAverage=Median`, and the brightest-N trim runs after the
   assignment), and the observed direction is the wrong way round for a filter. The brief's other suggestion —
   *"a checksum of the loaded pixel data on both paths"* — separates candidate 1 but is unnecessary once the two
   call sites are seen to be the same method; and it would have cost a binary, which would have re-created wave
   16's ordering hazard for no information.
4. **Item A is priced at ~1 h in the brief; the decisive part of it is free and the intervention is ~36 m.**
   The correlational answer (7/7 and 13/13, 188 of 188 positions at factor 1 and 0 of 63 at factor 2, spanning
   both banks and three binaries) was computed from files already on disk while writing this document. What the
   wave buys for its 36 minutes is the **intervention**, and that is the right thing to buy.
5. **"The answer says which number fourteen waves of this register have been quoting" is the wrong emphasis.**
   The answer is that on 13 of 20 synthetic datasets and on all 5 real gate runs **the two numbers are the same
   number**, and on the other 7 they are the same *definition* measured by **two different detectors**. The
   pre/post-filter distinction wave 16 proved is real, is unchanged, and is **inert at the pinned settings** —
   so it is not what fourteen waves were tripping over.
6. **F63(b) is described in the backlog as costing "~1 h + re-derivation" for the detector half. The
   measurement costs zero**, because wave 16's own `PARAMS-DUMP optimize/seed` block is the shipped code default
   bundle and it is printed in every gate log this series will ever run again. Only the *move* costs.
7. **A caution on the charter's own instruction, offered rather than acted on.** §3a says *"do not restate
   S16-A(b) as a rate and re-score wave 16's rungs"*, and this wave does not. But this design **does** state its
   own bars as rates, on the same page. Those are not in tension and the difference should be recorded so no
   later wave collapses them: **fixing a bar before the data is method; fixing it after seeing the data is
   harvesting.** The prohibition is about *when*, not about *rates*.
