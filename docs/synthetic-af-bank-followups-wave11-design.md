# Synthetic AF bank — followups wave 11 (design)

Plan: [`plans/synthetic-af-bank-followups-wave11-plan.md`](../plans/synthetic-af-bank-followups-wave11-plan.md).
Wave 10: [`docs/synthetic-af-bank-followups-wave10-results.md`](synthetic-af-bank-followups-wave10-results.md) ·
[design](synthetic-af-bank-followups-wave10-design.md). Register: [`docs/followups.md`](followups.md).

Wave 10 shipped as [PR #188](https://github.com/ghilios/hocus-focus/pull/188), **merged 2026-08-08** at `a54c48b`,
which is `develop`'s head. CI was green at 3722/3722 against `develop`'s 3708 (+14, all accounted for).

---

## §0 — What this wave inherits, and the one thing that changed before it started

Wave 10's own headline was that a `--settings`-pinned arm **is not pinned**: the harness loads whichever NINA
profile is ACTIVE, and that moves `BaselineJ` — one evaluation of a fixed seed on fixed frames, no search — by
**0.0144** ([F57](followups.md#f57--a---settings-pinned-arm-is-not-pinned-the-active-nina-profile-moves-baselinej-by-0014-and-every-cross-wave-comparison-inherits-it)).
It left three things owed: F57(c) (*which* profile-sourced quantity), F55(b) (the nondeterminism's trigger rate),
and F19's pre-registered successor.

**Before this wave ran a single optimization, two of those three collapsed into one answer, at zero compute, out
of logs wave 9 left on disk.** That is §0.1, and it re-shapes what items 1 and 2 have to do. It does not re-order
them.

### §0.1 — F55 and F57 are the SAME defect, and it is profile acquisition

**This section is measured, not conjectured**, and every input is on disk or in NINA's source. It does not depend
on wave 11's gate, because it is internal to one session, one binary and one probe.

**The mechanism, read out of `NINA.Profile`:**

1. `Profile.Load(path)` opens the `.profile` with `FileAccess.ReadWrite, FileShare.Read` and **keeps the stream
   for the profile's lifetime**. A second process's `Load` on the same file throws `IOException`
   (`ERROR_SHARING_VIOLATION`), and `Load` **rethrows it** — the journal/backup recovery path is explicitly
   skipped for the in-use case.
2. `ProfileService.SelectProfile` catches, logs, and returns **false**.
3. `ProfileService.TryLoad(id)` builds the candidate list `OrderByDescending(LastUsed)` and then
   `.SkipWhile(p => !SelectProfile(p)).FirstOrDefault()` — **so a locked profile is silently skipped and the NEXT
   profile by `LastUsed` is loaded instead.**
4. `Profile.Load` also sets `LastUsed = DateTime.Now` **and saves**. Loading a profile rewrites the ordering that
   decides which profile the *next* unpinned run gets.

**The consequence, observed in wave 9's own probe logs** (`D:\hf_w9\det\{S,C}_{1..5}.log`, the 40-second
reproducer, `D16_esprit550_ha3`, `--max-evals 1` so `BaselineJ` is the whole measurement):

| repeat | `Profile:` line printed by the run | `BaselineJ` |
|---|---|---|
| S_1 … S_5 (**sequential**) | `AA1600MM Copy (1bf0efaf-…)` — **all five the same** | `0.979173` ×5 |
| C_1 (**concurrent**) | `AA1600MM (4cf31cda-…)` | **0.988555** |
| C_2 | `Default (b10b1d6d-…)` | **0.988555** |
| C_3 | `astrodet (ce3f3e63-…)` | `0.979173` |
| C_4 | `Default-2026-08-05T10:57:42 (1a120eb1-…)` | **0.988555** |
| C_5 | `AA1600MM Copy (1bf0efaf-…)` | `0.979173` |

**Five concurrent processes loaded five DIFFERENT profiles**, exactly as the fall-through predicts, and one of
them (C_5) got the same profile the sequential phase got and returned the sequential phase's value.

**And J partitions on one field.** The optimize path feeds exactly four profile-sourced values into the fit
(`OptimizationDiagnosticRunner.cs:673–679`). Across those five profiles, three of the four are constant:

| profile | `BaselineJ` | `MaxOutlierRejections` | `OutlierRejectionConfidence` | `WeightedHyperbolicFitEnabled` | `HyperbolicFitModel` |
|---|---|---|---|---|---|
| `AA1600MM Copy` | `0.979173` | **0** | *(absent ⇒ 0.95)* | *(absent ⇒ true)* | Hybrid |
| `astrodet` | `0.979173` | **0** | *(absent ⇒ 0.95)* | true | *(absent ⇒ Hybrid)* |
| `AA1600MM` | **0.988555** | **1** | *(absent ⇒ 0.95)* | true | *(absent ⇒ Hybrid)* |
| `Default` | **0.988555** | **1** | **0.99** | true | *(absent ⇒ Hybrid)* |
| `Default-2026-08-05T10:57:42` | **0.988555** | **1** *(absent ⇒ 1)* | *(absent ⇒ 0.95)* | *(absent ⇒ true)* | *(absent ⇒ Hybrid)* |

> **`MaxOutlierRejections` = 0 ⇒ 0.979173. `MaxOutlierRejections` = 1 ⇒ 0.988555. Five of five.**
> `OutlierRejectionConfidence` varies *within* the firing group (0.99 vs 0.95) and does not move J, which is what
> a rejection budget of one predicts and a coincidence does not.

**What this explains, and the fit is total.** Every elimination waves 9 and 10 paid for is consistent with it and
none of them could have caught it:

- **the plugin's `Parallel.For` degree (1/2/4/8/48), swept and inert** — because this is not a thread race;
- **the BUILD (15 runs, 3 binaries, identical to 10 dp)** — because those fifteen ran in ONE session under ONE
  pinned profile;
- **folder state, `optimized_settings.json`, the detection cache, OpenCL, `Merge`, rented sorts, timeouts** — all
  irrelevant to which file the process opened at startup;
- **wave 9's sharpest unexplained clue** — *"whatever selects the attractor is stable within a process/session
  and variable across them … it does not fit a per-iteration data race and does fit some process-level state …
  acquired once"*. **A profile is acquired once, at startup.** The clue named the shape of the answer and the
  search kept looking below the fit.
- **the bimodality**, which was the one property that never fit floating-point summation order: the attractors are
  discrete because `MaxOutlierRejections` is a small integer that takes two values in the wild.
- **wave 9's fan-out arm** (`D16`: arm A 0.988555, arms B/C 0.979173) — arm A's process got a
  `MaxOutlierRejections = 1` profile and B/C's got a 0. The arm was voided for the right reason by the wrong
  mechanism.

**And it explains F57 without a second cause.** Wave 9's gate ran under `Default` (`MaxOutlierRejections = 1`),
wave 10's under `astrodet` (`0`); `toml999`'s `BaselineJ` went 0.997840 → 0.983477, and allowing one Grubbs
rejection improves a fit, which is the observed direction. **F57(c)'s answer is a named field**, and it is
[F45](followups.md#f45--the-grubbs-test-rejects-the-in-focus-point-of-a-near-perfect-curve-and-the-blind-walk-then-buys-an-extra-exposure)'s
outlier rejection deciding the objective from machine state nothing records. Item 2 still runs — a correlation
over five profiles is not an intervention — but it now runs as a **confirmation with a negative control**, not as
a search.

**Two further consequences that are not obvious and that change how arms must be run:**

- **A pinned run rewrites the default for the next unpinned run.** `LastUsed` is stamped by the act of loading,
  so `--profile-id X` today makes X the active profile tomorrow. Wave 10's own F57 probe pinned `Default` last,
  so **an unpinned run today should load `Default`, not the `astrodet` the wave measured under.** That is a free,
  decisive check of this whole model and §1.2 runs it before the gate.
- **`--profile-id` and fan-out are mutually exclusive as things stand.** With the id filter the candidate list has
  one entry, so the second concurrent process gets `SkipWhile` over an empty remainder, `TryLoad` returns false,
  and `optimize` throws *"No active NINA profile could be loaded"*. **Pinning turns a silent wrong answer into a
  loud failure**, which is the right trade and not a fix. The fix is §2.3.

**Filed as [F58](followups.md), cross-linked from F55 and F57.** F55's entry keeps its measurements and loses its
named mechanism; the `KappaSigmaNoiseEstimate` amplifier is **not withdrawn as a fact** — wave 10 measured its
gain and a unit test pins it — but it is withdrawn as *this* defect's explanation, and §2.4 is the residual check
that says whether anything is left for it to explain.

---

## §1 — The gate: RULE G11, and the profile is pinned and NAMED in the script header

### §1.1 Why the gate is not optional and not a formality

Wave 5's φ table is invalid on **two** axes now — `StarDetectorVersion` (1 → 2) and the active profile — and this
document does not quote it. Wave 11's coordinate system is **wave 10's eight values**, and they are only readable
if they reproduce on a fresh binary under the profile they were measured on.

The gate runs `D:\hf_w11\gate_repro_w11.sh`: wave 10's invocation, on a **fresh** `D:\hf_w11\exe`, sequentially,
`--per-run --max-evals 250`, `--settings D:\hf_w11\pinned_settings.json` (a byte-copy of the file that is
identical across waves 5–10, `md5 df7c7cd1…`, [F42](followups.md#f42--every-build-directory-silently-gets-its-own-detector-settings-and-the-run-instructions-require-a-new-one-per-arm)),
**and `--profile-id ce3f3e63-8fd3-4b72-a0ca-d90db9441382` (`astrodet`), stated in the script header as a first-class
input beside the settings file** — which is F57(b), applied to this wave's own first action.

### §1.2 The pre-gate probe, which tests the §0.1 model for 40 seconds

Run once, **before** the gate, on a scratch copy: `optimize --per-run --max-evals 1` with **no** `--profile-id`.

> **Prediction, fixed here: it prints `Profile: Default (b10b1d6d-…)`.**
>
> `LastUsed` ordering currently puts `Default` (2026-08-08T12:54:20-04:00) ahead of `astrodet`
> (2026-08-08T09:59:35-04:00), because wave 10's F57 probe loaded `Default` last.
>
> **If it prints anything else, §0.1's model of profile selection is wrong**, F58 must be re-derived before it is
> filed, and items 1 and 2 revert to wave 10's framing. This is the clause that can kill the wave's headline
> cheaply, and it runs first for that reason.

### §1.3 RULE G11 — ONE pass/fail clause, fixed before the gate runs

> **RULE G11 (pass/fail).** All eight runs must return wave 10's `BestJ` **to 6 dp**:
>
> | `toml999` | `CWhiteFocus` | `uneven` | `muggsie` | `mccomiskey` | `D18` | `D19` | `D20` |
> |---|---|---|---|---|---|---|---|
> | 0.995784 | 0.996068 | 0.996368 | 0.997195 | 0.976746 | 0.999882 | 0.999487 | 0.999738 |
>
> **A partial reproduction is a failure, not a warning — the eight are one instrument** (RULE G as originally
> written). **If it fires, STOP.** Wave 11 has no coordinate system and nothing downstream is readable.

Everything else the gate produces is a **measurement**, recorded and reported, never a gate:

| free control | expectation | what a violation means |
|---|---|---|
| `Profile:` / landing `ProfileId` | `astrodet (ce3f3e63-…)` on all eight | the pin did not take; every value is void |
| `ConcurrencyCheck` | `exclusive` on all eight | F55(c): something else was running; the pass is void |
| `DetectorVersion` | 2 | wrong binary |
| `BuildId` | **must DIFFER from wave 10's** | an MVID that matches means the build did not happen and this is wave 10's exe |
| `strings exe/NINA.Joko.Plugins.HocusFocus.dll \| grep AtrousWaveletFast` | **hit** | a v1 build, retroactively (wave 10's instrument) |
| `BaselineJ` for `toml999` | 0.983477 (wave 10 §1.1, under `astrodet`) | [F41](followups.md#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)'s free control |

**`BaselineJ` agreeing is not automatically good news.** If every `BaselineJ` reproduces while landings move, the
difference is in the search and not in the seed, which is [F8](followups.md#f8--optimizer-landings-are-not-reproducible-across-invocations)
and must be said rather than absorbed. *A number that agrees too well is also an instrument failure.*

**`uneven` emits no `Optimization complete` console line** (wave 10 §1.4). The scorer reads
`aggregate_summary.json`; **any script that greps the console line reports a failure on a successful run.**

---

## §2 — Item 1: F55(b), and it is now confirm → fix → re-measure

### §2.1 What is owed, restated after §0.1

Wave 10 owed a **trigger rate** for a mechanism it had named and half-measured. §0.1 supplies a different
mechanism with a complete partition over five observations, so what is owed changes shape:

1. **Confirm by intervention**, not only by correlation over profiles that already existed.
2. **Fix it**, so that a pinned arm is genuinely pinned and fan-out becomes safe.
3. **Re-measure the phenomenon with the fix in place** — the strongest available evidence is a fix that removes
   it — and run the **residual** check that says whether anything is left over for the kappa-sigma amplifier or
   anything else to explain.

### §2.2 RULE K — fixed before the probe runs

The probe is `D:\hf_w11\det\determinism_probe_w11.sh`: wave 9's 40-second reproducer (`D16_esprit550_ha3`,
`--max-evals 1`, 5 sequential then 5 concurrent, each repeat on its own pristine copy, **the banks are never
touched**) on the wave-11 binary, with each repeat's printed `Profile:` line and landing `ProfileId` captured.

> **K1 — the mechanism reproduces.** In phase C, the number of DISTINCT `BaselineJ` values must equal the number
> of distinct `MaxOutlierRejections` values across the profiles the five processes actually loaded, and repeats
> sharing a profile must share a value **to full double precision**. Anything else means the partition is not the
> whole story.
>
> **K2 — phase S is single-profile and single-valued.** All five sequential repeats must load one profile and
> return one value. If phase S splits, a second mechanism exists and F55 is not closed by F58.
>
> **K3 — pinning fails loudly under fan-out.** Phase C repeated with `--profile-id` must produce exactly one
> success and four hard failures (`No active NINA profile could be loaded`, non-zero exit). If a pinned
> concurrent process *succeeds* on a profile it was not given, §0.1's reading of `TryLoad` is wrong.
>
> **K4 — `ConcurrencyCheck` is exercised in both directions.** Phase S landings must stamp `exclusive`; phase C
> landings must stamp `concurrent`. This is wave 10's shipped guard meeting its **positive control** for the
> first time. If phase C stamps `exclusive`, the guard does not work and that is a finding of its own — *an
> absent check is more dangerous than a red one.*
>
> **K5 — THE FIX REMOVES IT.** With §2.3 in place and the fit inputs pinned by `--settings`, phase C must return
> **one** `BaselineJ`, identical to phase S to full double precision, **while still loading five different
> profiles** (the `Profile:` lines must still differ — otherwise the test has merely stopped exercising the
> hazard). **K5 is the clause this item is judged on.**
>
> **K6 — the residual.** If K5 fails and phase C still splits with the fit inputs pinned, then something below the
> fit is still moving, and §2.4's σ instrument runs to say whether it is `KappaSigmaNoiseEstimate`. If K5 passes,
> §2.4 runs **anyway, once**, as the check that the fix did not merely mask a second mechanism.
>
> **K7 — cross-profile identity, and it is the crispest statement of "pinned".** On the fixed binary with the
> extended settings file, `toml999` at `--max-evals 1` under `--profile-id astrodet` and under `--profile-id
> Default` must return **identical `BaselineJ` to full double precision**. Before the fix these differ by
> **0.0144**. Two runs, ~1 minute.
>
> **K8 — the coordinate system survives its own fix.** The eight gate runs, on the fixed binary with the extended
> file, must return RULE G11's eight values. Otherwise every future wave inherits an unverified discontinuity at
> the exact commit that claimed to remove one. **It costs nothing**: §4's population pass is the shipped-default
> invocation over both banks and the eight are among its 39, exactly as wave 10's pass was RULE G10-A at 8-fold
> redundancy.

**K5 is designed so that it can fail.** A fix that pinned the profile itself — one profile, one answer — would
pass a weaker version of this clause while proving nothing about the fit inputs. Requiring the five processes to
**still load five different profiles and still agree** is what makes the clause discriminating.

### §2.3 The fix: pin the fit inputs the same way the detector knobs are pinned

**The asymmetry is the bug.** `HarnessSettingsStore` exists precisely so that "a profile-sourced seed is mutable
machine state nothing records" cannot reach a run — its `FileOptionsAccessor` reads the pinned file and falls
back to **code defaults**, never to the profile. `StarDetectionOptions` is built on it. **`AutoFocusOptions` is
not**: `new AutoFocusOptions(profileService)` constructs a `PluginOptionsAccessor` bound to the ACTIVE PROFILE.
So the detector is pinned and the fit is not, and `--settings` has been read as pinning the arm for six waves.

The seam already exists — `AutoFocusOptions` has an `internal AutoFocusOptions(IProfileService, IPluginOptionsAccessor)`
constructor, and TestApp already uses the equivalent one on `StarDetectionOptions`. The change is:

1. Build the harness's `AutoFocusOptions` on `harnessSettings.Accessor` instead of the profile.
2. Carry the four fit inputs in the harness settings file, exported from the profile on bootstrap exactly as the
   detector knobs are, so an existing pinned file keeps behaving as its own code defaults describe **and says so**.
3. Record them in provenance as **values, not a hash**: `ProfileInputs =
   "MaxOutlierRejections=1;OutlierRejectionConfidence=0.99;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid"`.
   A hash tells a reader that something moved; the values tell them **which** — and this register's whole
   direction of travel is *a reader diffs a field* (`BuildId` over a version string, `DetectionBinningSource` over
   a sentence, `ConcurrencyCheck` over a rule).

**What this deliberately does NOT do.** It does not change the live plugin's behaviour: the wizard and the AF
engine keep reading `AutoFocusOptions` from the profile, which is correct — those *are* the user's settings. It
changes what the **harness** treats as pinned. And it does not remove the profile from the run: the image loader
still needs one (§3.1), so `--profile-id` remains mandatory on every arm.

**The pinned file's own hazard is inherited and must be checked.** `UseAdvanced = False` in
`pinned_settings.json` means `DerivePresetSettings()` overwrites ~16 advanced knobs from the `Simple_*` presets
(F42(3)); the store already warns and names them. The four new keys are **not** in that override set, and a test
must assert it rather than a comment claiming it.

### §2.4 The residual instrument, and why it is not the primary one

`CvImageUtility.KappaSigmaNoiseEstimate` already returns `NumIterations`, and `StarDetector` already emits
`Structure Map K-Sigma Noise Estimate: <σ>, Background Mean: <μ>, NumIterations=<n>` per detection at TRACE.
`optimize --verbose` already sets TRACE. **The instrument exists; nothing needs to be written.**

Two limits, stated because they bound what may be claimed:

- **The trace line carries no frame identifier**, and frames are detected in parallel, so a run yields a
  **multiset** of (σ, iterations), not a per-frame series. Multiset difference is enough for the question asked —
  *does σ move between two runs of identical inputs, and does the iteration count move with it* — and the count of
  differing entries is a per-detection rate. It is not enough to say *which frame*, and the write-up must not.
- **`--verbose` is documented as "slower: serializes per-detection stage timings to the NINA log"** — a timing
  perturbation, aimed at a load-sensitive phenomenon. So it carries its own control: **phase C must still split
  with `--verbose` on** (compared against a `--verbose`-off phase C in the same session). If it does not, the
  instrument destroys what it measures, and that is reported as the result rather than worked around.

**Readings, fixed in advance:**

- σ multiset **identical** across two runs that differ in landing ⇒ **`KappaSigmaNoiseEstimate` is exonerated**,
  and F55's named mechanism is withdrawn as an explanation (its measured gain stays as a fact about the function).
- σ differs **and** `NumIterations` differs ⇒ a **direct observation** of the named mechanism, not an inference.
- σ differs and `NumIterations` is **identical** ⇒ the flip is inside `Cv2.MeanStdDev` itself, which is a
  *different* mechanism from the one F55 names, and the entry must be rewritten to say so.

**`--cv-threads <n>` (new, harness only)** calls `Cv2.SetNumThreads(n)` and `optimize` prints
`Cv2.GetNumThreads()` in its provenance line. This replaces wave 9's `HF_STAREVAL_PAR` environment-variable route,
which silently measured the same configuration five times because **WSL environment variables do not reach a
Windows process without `WSLENV`** — *an instrument that is not connected reports perfect agreement*. A flag
cannot be disconnected by a missing `WSLENV`, and the printed `GetNumThreads()` is its positive control. It runs
only if σ is shown to move.

### §2.5 Explicitly not re-run

Every candidate waves 9 and 10 eliminated by measurement: the plugin's `Parallel.For` degree (1/2/4/8/48), the
per-run `optimized_settings.json`, the disk detection cache, OpenCL/`UMat` (none exists), the frame-level fan-out,
`StarDetectorMetrics.Merge`, rented-array sorts, `MedianInPlace`, load-sensitive timeouts, the build (15 runs, 3
binaries), folder state one artifact at a time, and `MedianFloat`'s `Random.Shared` quickselect (F55-shaped and
provably inert — rank *n* is rank *n*, and `Mat.GetArray` returns a copy).

---

## §3 — Item 2: F57(c), which profile-sourced quantity moves `J`

### §3.1 The read-level audit, done first because it is free

| profile-sourced input | reaches the objective? | status |
|---|---|---|
| **`AutoFocusOptions`** → `UseWeights`, `MaxOutlierRejections`, `RejectionConfidence`, `PreferredModel` | **YES** — `OptimizationDiagnosticRunner.cs:673–679` | **the only unpinned options surface**; 2 of the 4 differ between the two profiles |
| detector knobs via `StarDetectionOptions` | no | `FileOptionsAccessor` falls back to **code defaults**, never the profile — so `SaturationThreshold` (0.99 vs 0.9) and `DetectionBinning` (`Bin1` vs absent) are inert |
| `PixelScale` | no | resolved per run from the frame header; both runs printed `0.73944` |
| image loading (`RenderedImageLoading.ForDetection`) | conditionally | **`ImageSettings` is byte-identical between the two profiles**, and `toml999` is mono, so the debayer decision is inert twice over |
| `AutoFocusBinningConflict.Detect` | no | reports a conflict for a user prompt; does not resolve binning |
| `FocuserSettings.AutoFocusStepSize` (479 vs 188) | no | the harness infers the step from the frames' focuser positions |

**Two fields survive**, and one of them makes the other unreachable:

| field | `Default` | `astrodet` |
|---|---|---|
| `MaxOutlierRejections` | **1** | **0** |
| `OutlierRejectionConfidence` | **0.99** | *(absent ⇒ 0.95)* |

### §3.2 RULE C — the bisect, with the clause that can break it

`toml999`, `--max-evals 1`, on **copies** (F15: `optimize --per-run` rewrites the run folder, so the bank is not
the test bed). The bisect uses **synthetic profiles**: a copy of `astrodet` with a fresh GUID and Name and one
field changed, dropped into the profile folder — `TryLoad` enumerates `*.profile` and selects by Id, so the
user's two real profiles are **never edited**. Both reference points are re-measured in this session; wave 10's
0.983477 / 0.997840 are not quoted into the arithmetic.

> **C0 — the reference points hold.** `astrodet ⇒ J_astro`, `Default ⇒ J_def`, measured today. If either differs
> from wave 10's value, the bisect is void before it starts and *that* is the finding.
>
> **C1 — both fields together account for all of it.** `astrodet` + `MaxOutlierRejections=1` +
> `OutlierRejectionConfidence=0.99` must equal `J_def` **to full double precision**. A residue means an
> unenumerated profile read exists; report the residue, do not explain it away.
>
> **C2 — the single-field attribution.** `astrodet` + `MaxOutlierRejections=1` alone: equal to `J_def` ⇒
> `MaxOutlierRejections` is the whole cause and the confidence is inert. Moves but short of `J_def` ⇒ both are
> load-bearing, and C1 says by how much.
>
> **C3 — THE NEGATIVE CONTROL.** `astrodet` + `OutlierRejectionConfidence=0.99` alone **must return exactly
> `J_astro`**, because a rejection budget of zero makes the confidence unreachable. **Any movement here means the
> harness is not doing what it claims** — the synthetic profile is not being loaded, or the edit did not take, or
> something else moved between runs — and **C1 and C2 must be discarded.** This is the clause that makes the
> bisect an instrument rather than a demonstration.
>
> **C4 — connectedness.** Every bisect run must print its synthetic profile's own name and id in `Profile:` and
> stamp it into the landing's `ProfileId`. A run that silently fell back to the active profile would otherwise
> read as *"the field did nothing"* — which is wave 10's RULE-P-scorer failure in a new place.

### §3.3 What ships from item 2

- **(a)** already shipped in wave 10 (`ProfileId` in `OptimizerProvenance`).
- **(b)** the run-instruction change: **every arm passes `--profile-id` explicitly**, beside F42's `--settings`
  rule, recorded in `.claude/docs/testapp-cli.md` so it is not only in a wave document. §0.1 sharpens *why*: the
  default is LRU-by-last-load, so an unpinned arm is seeded by **whatever the previous arm pinned**.
- **(c)** answered: `MaxOutlierRejections`, with `OutlierRejectionConfidence` behind it — subject to RULE C.
- **(d)** the standing instrument: `ProfileInputs` in provenance (§2.3), so the next wave diffs a field instead of
  repeating §3.1.

---

## §4 — Item 3: F19's successor, and it is expected to be refuted

### §4.1 The rule is already fixed and is NOT renegotiated here

`WingRejectedRatio` = wing-third ÷ inner-third. Wave 10 fixed its acceptance rule **while the pass that refuted
its predecessor was still running**, and this document restates it verbatim rather than improving it:

- **W1–W4 unchanged** (wave 9's ladder, `D:\hf_w7\armE\t{0.5,1,2,4,8}`, still on disk, **no re-render**):
  `D02`@0.5 s ≥ 2× · `D16`@2 s < 1.25× · converges · `D16`@0.5 s asks.
- **W5** — its threshold must sit **above** the bank's median wing/inner ratio (**1.39** over 39 runs), or it is
  the same defect in a new coordinate.
- **W6** — it may **not** be validated on the population that refuted its predecessor; it needs its own arm.

**The verdict rule is RULE P, unchanged** — the population rule wave 10 already wrote and already applied. No new
pass/fail clause is invented for the successor, because inventing one after seeing wave 10's data is the exact
move W6 exists to prevent. P2 (*fires on > 50 % of the 39 runs ⇒ it is a constant, not a diagnosis*) is what
killed the predecessor and is what the successor must survive.

### §4.2 How W6 is honoured, and where the honouring is imperfect

The threshold is **chosen and written into the script header before the population pass runs**, from W1–W4 on the
ladder and W5's floor. The 39-run pass is then the **test set**: it supplies the fire rate, which is the clause
that decides.

**The imperfection, stated rather than smoothed over:** W5's floor (1.39) is itself a statistic *of* that
population. So the threshold is partly derived from the data it is tested on. What the population is not allowed
to do — and does not do — is supply the threshold's *value*; it supplies a lower bound published in wave 10,
before wave 11 existed. The fire rate remains an honest test. **This is weaker than a fresh population, and the
results document must say so in those words.**

### §4.3 The two known hazards, named before the pass

Neither is a new clause. Both are pre-stated expectations, so that meeting them cannot be presented as a surprise
or as a success:

- **`D17_cdk14_oiii5` fires at a ratio of 0.59** — its wings reject *less* than its core (0.297 vs 0.507) — and
  `D17` is one of P1's two pre-registered fires. **A ratio ≥ threshold cannot fire on a ratio of 0.59.** So the
  ratio form is expected to *lose* a dataset the absolute form got right, by a route that was already wrong.
- **`D20_m24_bright_control` has an inner fraction of exactly 0.000**, so its ratio is not finite. A statistic
  that asks the bank's **bright control** for more exposure is wrong on the one dataset whose name says it needs
  none.

**The NaN contract is inherited and extended, because two different things divide by zero:**

| case | value | meaning |
|---|---|---|
| the run cannot be placed on the wing axis | **NaN** | *"we could not look"* — never 0, wave 9's rule |
| the inner third is populated and rejects **nothing**, wings do | **+∞** | *"the wings reject and the cores do not"* — the strongest real signal |

`Newtonsoft` writes both as the **strings** `"NaN"` and `"Infinity"`. **A scorer that coerces either to a number
disables its own falsification rule** — wave 10's scorer read an unmeasured dataset as *"did not fire"* and
reported a partial run as a population verdict. The wave-11 scorer must have `UNEVALUATED`, `NaN`, `+∞` and a
number as four distinguishable states, and must be shown a positive control for each.

### §4.4 The successor-of-the-successor, named now and barred from this data

If the ratio is refuted, the next candidate is **`WingRejectedExcess` = wing − inner**: bounded in [−1, 1],
defined when the inner third rejects nothing, and unable to fire when wings and cores reject at the same rate —
which is the failure the ratio inherits from `D17` and the absolute fraction. **It is named here so that it
cannot be adopted on the data that refutes the ratio**, and its rule when the time comes is W1–W4 plus a
separating clause against the excess's own bank median. *A statistic tuned on the data that killed the last one
has been fitted, not tested* — the third time this register has had to write that sentence.

### §4.5 The pass's operating constraints

- **F15 is UNFIXED**: `optimize --per-run` still rewrites run folders and there is no suppress flag, so the
  population pass **runs last and alone**. Nothing else may be in flight.
- The concurrency guard in `D:\hf_w10\wing_pop.sh` is **validated** (against a deliberately-started `optimize`)
  and is reused rather than rewritten. `ConcurrencyCheck` is checked in every landing afterwards regardless —
  *a rule that depends on the operator noticing a violation is not a control, it is a hope.*
- `lumos` exits rc=3 reproducibly and reports `WingRejectedFraction` NaN; `astrodet` (the dataset) is frameless
  (F14) and excluded; `Panos` has a degenerate sigma fit. All three are **named in the score sheet as excluded or
  NaN**, never silently dropped.
- The banks currently hold **wave 10's shipped-default landing, measured under profile `astrodet` on
  `StarDetectorVersion` 2**. Any arm read against them states both.

---

## §5 — Deferred, with reasons

- **The landing-level wavelet bisect** (`exe_v1wav`, built in wave 10 and unused). The **seed-level answer is
  already in and is exact** — 15 runs, identical to 10 dp — so the eight-run bisect measures only whether the
  pattern search amplifies an identical seed into different landings. That is
  [F8](followups.md#f8--optimizer-landings-are-not-reproducible-across-invocations), not a new question, and it
  costs ~80 minutes. The invocation stays written down in F57(d).
- **F52(d)** (a cost term in `J`): the motivation is measurably gone — PR #187 made per-layer wavelet cost nearly
  flat, so a cost term now would penalise an artifact.
- **F46(b), F54, F50**: nothing depends on them.

---

## §6 — Traps this wave carries

- **`D:\hf_w10\exe` was never rebuilt** (F53(c)) and is the reference for wave 10's eight values. `exe3` and
  `exe_v1wav` are later builds. **Never rebuild an arm's directory mid-wave** — wave 11 builds to `D:\hf_w11\exe`
  and leaves it alone.
- **`strings <dll> | grep AtrousWaveletFast`** distinguishes a v1 from a v2 build **retroactively**. Use it on any
  `D:\hf_w*\exe` before trusting a `Reproduce:` line.
- **The console `Optimization complete` line is not reliably emitted** (`uneven` has none). `aggregate_summary.json`
  is the instrument.
- **`optimize --per-run` rewrites run folders** (F15). Probes use copies; the population pass runs alone.
- **The csproj PostBuild xcopy fails SILENTLY when NINA is running**, so **wave 8's and wave 9's AF/wizard changes
  and wave 10's capped-step copy are STILL UNCONFIRMED IN THE APP.** Not this wave's item, but not to be claimed
  as confirmed either.
- **CI: verify the test COUNT, not the tick** (F37). `develop` is at **3722**. An absent check is more dangerous
  than a red one.
- **WSL environment variables do not reach a Windows process without `WSLENV`.** This wave uses flags instead.

---

## §7 — Order of work, and the discipline it is run under

1. Pre-gate probe (§1.2) — 40 s, and it can kill the wave's headline.
2. Build `D:\hf_w11\exe`; gate (§1.3) — RULE G11, the only pass/fail clause in the wave.
3. Item 1 (§2) — confirm (K1–K4), fix (§2.3), re-measure (K5), residual (K6/§2.4).
4. Item 2 (§3) — RULE C, including C3.
5. Item 3 (§4) — threshold fixed in the header, then the population pass, alone and last.
6. Full suite, results document, F58 filed, PR.

**Every clause above was written before its measurement.** The rules this wave is run under, each of which has
already cost this project real time:

- **Design the control to EXCLUDE, not to confirm.** Wave 10's gate came back at its pre-registered top tier and
  the finding was still wrong; a three-way probe built to exclude killed it. §1.2, K5 and C3 are this wave's
  versions.
- **Ask every instrument what it would say if the thing it checks were completely broken, and validate it against
  a positive control.** K4 is `ConcurrencyCheck`'s first positive control; C4 is the bisect's; `--cv-threads`
  prints `GetNumThreads()` for the same reason.
- **A rule that depends on the operator noticing a violation is not a control, it is a hope.** Check
  `ConcurrencyCheck` in the landing before reading any arm.
- **Check whether a cheap instrument already exists, including one a previous wave left on disk.** §0.1 is the
  whole wave's headline and it came out of `D:\hf_w9\det\C_*.log`, at zero compute, from a `Profile:` line that
  had been printed and ignored for two waves.
- **The population that did not motivate the hypothesis is the one that tests it**, and three two-dataset
  conclusions have now reversed at full scale.
- **Name the successor before you need it and bar it from the data that killed its predecessor** (§4.4).
- **Pin `--settings` AND `--profile-id` on every arm.** `BaselineJ` is a free control; a number agreeing too well
  is also an instrument failure.
