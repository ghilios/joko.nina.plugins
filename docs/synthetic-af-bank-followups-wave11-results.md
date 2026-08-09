# Synthetic AF bank — followups wave 11 (results)

Design: [`docs/synthetic-af-bank-followups-wave11-design.md`](synthetic-af-bank-followups-wave11-design.md).
Plan: [`plans/synthetic-af-bank-followups-wave11-plan.md`](../plans/synthetic-af-bank-followups-wave11-plan.md).
Wave 10: [`docs/synthetic-af-bank-followups-wave10-results.md`](synthetic-af-bank-followups-wave10-results.md).
Register: [`docs/followups.md`](followups.md).

> ## PROVENANCE — and this is the last wave that needs to say most of it in prose
>
> Every measurement below came from one of three binaries, all built from `a54c48b` (PR #188's merge commit,
> `develop`'s head) plus this branch, all **`StarDetectorVersion` 2**, and — except where a probe deliberately
> runs unpinned — all **under `astrodet (ce3f3e63-8fd3-4b72-a0ca-d90db9441382)`, PINNED WITH `--profile-id`.**
>
> | directory | what it is | `TestApp.dll` sha256 | `BuildId` | used for |
> |---|---|---|---|---|
> | `exe` | **pre-fix** | `71ce1cd8…` | `40f08e36…` | the gate (§2), the probe's phases S/C/P (§3.1), the bisect (§4) |
> | `exe_fixed` | first post-fix build | `b0a81968…` | `5cb7e474…` | superseded by `exe_fix2` when F59 landed |
> | `exe_fix2` | **post-fix (F58 + F59)** | `857c5982…` | `5cb7e474…` | K5/K7 (§3.3), the ladder (§5.2), K8 (§6.1) |
>
> | settings file | md5 | contents |
> |---|---|---|
> | `pinned_settings.json` | `df7c7cd1…` | byte-identical to waves 5–10 (F42) |
> | `pinned_settings_w11.json` | `a67ffc06…` | the same **plus** the four fit inputs at `astrodet`'s effective values |
>
> **`--profile-id` is in this banner because of what the wave found**, and it is not hygiene: measured before the
> gate ran, an **unpinned** `optimize` on this machine today loads `Default` and returns `toml999`
> `currentJ = 0.99784` — **wave 9's value, not the wave 10 the banks were measured under**. An unpinned wave-11
> gate would have reproduced the wrong wave and called it a pass.
>
> **No directory was ever rebuilt** (F53(c)). `exe_fix2` exists precisely *because* F59 landed after `exe_fixed`
> was built — a new directory rather than a rebuilt one — and K5/K7 were re-run on it and reproduced exactly.
>
> **This banner is now largely redundant, which is the point.** `BuildId`, `DetectorVersion`, `ProfileId`,
> `ConcurrencyCheck` and — new this wave — `FitInputs` are all **fields on every landing**. A reader diffs a
> field; nobody diffs a banner.

## Status of this document

| item | state |
|---|---|
| **F58** — new, and it is the wave's headline | **F55 and F57 are ONE defect: concurrent `optimize` processes each acquire a DIFFERENT NINA profile, and the two "attractors" are two values of `MaxOutlierRejections`.** Found at **zero compute**, out of logs wave 9 left on disk. See §1 |
| **RULE G11** — the gate, the wave's only pass/fail clause | **PASS, 8 of 8 to 6 dp**, and all eight `BaselineJ` reproduce wave 10 exactly. See §2 |
| **item 1** — F55(b) | **K1–K5, K7, K8 ALL PASS.** The mechanism is confirmed by intervention, fixed, and the fix removes the phenomenon while the hazard stays present. `KappaSigmaNoiseEstimate` is WITHDRAWN as the explanation. See §3 |
| **item 2** — F57(c) | **ANSWERED: `AutoFocusOptions.MaxOutlierRejections`, one integer, 100 % of the 0.0144, residue exactly `0.0`** — with the negative control passing. See §4 |
| **item 3** — F19's successor | **REFUTED BEFORE IMPLEMENTATION.** RULE W2 is unsatisfiable at any finite threshold; the pinned ladder reproduced all ten rungs. The MEASUREMENT ships, no verdict does. See §5 |
| **F59** — new | **The settings export dropped every VALIDATING knob**: `pinned_settings.json` has been missing five detector knobs since wave 5. Found by a test written for density. Fixed. See §6.2 |
| **K8** — the coordinate system survives its own fix | **PASS, 8 of 8, BIT-IDENTICAL to all 16 digits.** See §6.1 |
| **the full suite** | **3740 passed, 0 failed** (`develop` was 3722 → **+18**, every one named) |

---

## §1 — F58: F55 and F57 are the same defect, and it was already on disk

**Nothing was run to find this.** The evidence was printed in a `Profile:` line that two waves had scrolled past,
and the reading that explains it is four lines of `NINA.Profile`.

### §1.1 The mechanism

1. **`Profile.Load(path)`** opens the `.profile` with `FileAccess.ReadWrite, FileShare.Read` and **holds the
   stream for the profile's lifetime**. A second process's `Load` throws `IOException`
   (`ERROR_SHARING_VIOLATION`), and `Load` **rethrows** — the journal/backup recovery path is explicitly skipped
   for the in-use case.
2. **`ProfileService.SelectProfile`** catches it and returns **`false`**.
3. **`TryLoad(id)`** orders candidates `OrderByDescending(LastUsed)` and then
   `.SkipWhile(p => !SelectProfile(p)).FirstOrDefault()` — **a locked profile is silently SKIPPED and the next by
   `LastUsed` is loaded instead.**
4. **`Profile.Load` also sets `LastUsed = DateTime.Now` and SAVES.**

*(Checked rather than assumed: `Profile.Peek`, which builds the candidate list, opens with
`FileAccess.Read, FileShare.ReadWrite` and therefore **succeeds** on a locked profile. So the list is complete and
the skip happens exactly at `SelectProfile`, not earlier — which is what makes the fall-through silent instead of
simply omitting the profile.)*

### §1.2 The observation — wave 9's 40-second reproducer, re-read

`D:\hf_w9\det\{S,C}_{1..5}.log`, `D16_esprit550_ha3`, `--max-evals 1`:

| repeat | `Profile:` printed by the run | `BaselineJ` |
|---|---|---|
| S_1 … S_5 (**sequential**) | `AA1600MM Copy (1bf0efaf-…)` — **all five the same** | `0.979173` ×5 |
| C_1 (**concurrent**) | `AA1600MM (4cf31cda-…)` | **0.988555** |
| C_2 | `Default (b10b1d6d-…)` | **0.988555** |
| C_3 | `astrodet (ce3f3e63-…)` | `0.979173` |
| C_4 | `Default-2026-08-05T10:57:42 (1a120eb1-…)` | **0.988555** |
| C_5 | `AA1600MM Copy (1bf0efaf-…)` | `0.979173` |

**Five concurrent processes, five different profiles.** C_5 drew the profile the sequential phase drew and
returned the sequential phase's value.

### §1.3 And `J` partitions on ONE field

The optimize path feeds exactly four profile-sourced values into the fit
(`OptimizationDiagnosticRunner.cs:673–679`). Across those five profiles, three of the four are constant:

| profile | `BaselineJ` | `MaxOutlierRejections` | `OutlierRejectionConfidence` | `Weighted…` | `HyperbolicFitModel` |
|---|---|---|---|---|---|
| `AA1600MM Copy` | `0.979173` | **0** | *(absent ⇒ 0.95)* | *(absent ⇒ true)* | Hybrid |
| `astrodet` | `0.979173` | **0** | *(absent ⇒ 0.95)* | true | *(absent ⇒ Hybrid)* |
| `AA1600MM` | **0.988555** | **1** | *(absent ⇒ 0.95)* | true | *(absent ⇒ Hybrid)* |
| `Default` | **0.988555** | **1** | **0.99** | true | *(absent ⇒ Hybrid)* |
| `Default-2026-08-05T10:57:42` | **0.988555** | **1** *(absent ⇒ 1)* | *(absent ⇒ 0.95)* | *(absent ⇒ true)* | *(absent ⇒ Hybrid)* |

> **`MaxOutlierRejections` = 0 ⇒ 0.979173. = 1 ⇒ 0.988555. Five of five.**
> `OutlierRejectionConfidence` varies *within* the firing group (0.99 vs 0.95) and does not move `J`, which is
> what a rejection budget of one predicts and a coincidence does not.

**The machine holds 9 profiles and they partition 2 / 7 on this field** (`D:\hf_w11\profiles_before.txt`,
snapshotted before anything in this wave ran). **That is the bimodality** — two discrete attractors because a
small integer takes two values in the wild, which is the one property of F55 that never fit summation order.

### §1.4 Confirmed on three more datasets, also at zero compute

Wave 9's **four-way concurrent control trial** (`D:\hf_w9\ctl_conc_*.log`) also printed its profiles:

| log | profile loaded | `MaxOutlierRejections` | `BaselineJ` |
|---|---|---|---|
| `ctl_seq_toml999` (**sequential**) | `Default-2026-08-05T10:57:42` | 1 | 0.997840 |
| `ctl_conc_toml999` | **the same profile** | 1 | **0.997840** |
| `ctl_conc_bobp` | `astrodet` | 0 | 0.993122 |
| `ctl_conc_caboose` | `Default` | 1 | 0.994753 → landed **0.996300** |
| `ctl_conc_mufti` | `AA1600MM Copy` | 0 | **0.957603** |

Four processes, four different profiles again. `mufti`'s 0.957603 is **arms B/C's** value in F55's own table
(so arm A's 0.957087 drew a `= 1` profile), and `caboose`'s 0.996300 is one of that run's two recorded landings.

**The sharpest of them is the anomaly F55 had to hedge about.** F55 records *"one four-way concurrent trial did
NOT reproduce the deviation"* and correctly declines to read it as evidence against the fan-out, on the grounds
that the effect appears on ~15 % of runs so a single trial has no power. **The hedge was right and the reason is
now visible in the log rather than left to probability:** `ctl_conc_toml999` drew the *same profile* as the
sequential control, so the values are identical rather than merely close.

### §1.5 What it explains — and every one of these was paid for by measurement

| eliminated by waves 9–10 | why F58 is consistent with it |
|---|---|
| the plugin's `Parallel.For` degree (1/2/4/8/48), swept, inert | it is not a thread race |
| the BUILD (15 runs, 3 binaries, identical to 10 dp) | those fifteen ran in ONE session under ONE **pinned** profile |
| folder state, one artifact at a time | irrelevant to which file the process opened at startup |
| `optimized_settings.json`, the detection cache, OpenCL/`UMat`, `Merge`, rented sorts, `MedianInPlace`, timeouts | same |
| **"stable within a process/session, variable across them … process-level state … acquired once"** | **a profile is acquired once, at startup.** Wave 9 named the shape of the answer, and the search kept looking below the fit |

**And it explains F57 without a second cause.** Wave 9's gate ran under `Default` (`MaxOutlierRejections = 1`),
wave 10's under `astrodet` (`0`); `toml999`'s `BaselineJ` went 0.997840 → 0.983477, and allowing one Grubbs
rejection improves a fit, which is the observed direction. **F57(c)'s answer is a named field** — F45's outlier
rejection deciding the objective from machine state nothing recorded.

### §1.6 Two consequences that are not obvious

- **A pinned run rewrites the default for the next unpinned run.** Wave 10's own F57 probe pinned `Default` last,
  and an unpinned run today loads `Default` and returns wave 9's number. **The act of measuring rewrites the
  default for the next measurement.**
- **`--profile-id` and fan-out are mutually exclusive as things stand.** With the id filter the candidate list
  has one entry, so the second concurrent process runs `SkipWhile` over an empty remainder and `optimize` throws
  *"No active NINA profile could be loaded"*. **Pinning converts a silent wrong answer into a loud failure**,
  which is the right trade and is not a fix.

### §1.7 The asymmetry that is the actual bug

`HarnessSettingsStore` exists precisely so that *"a profile-sourced seed is mutable machine state nothing
records"* cannot reach a run: its `FileOptionsAccessor` reads the pinned file and falls back to **code defaults**,
never to the profile, and `StarDetectionOptions` is built on it. **`AutoFocusOptions` was not.** So the detector
was pinned and the fit was not, and `--settings` has been read as pinning the arm since wave 5.

---

## §2 — The gate: RULE G11 PASSES, 8 of 8

`D:\hf_w11\gate_repro_w11.sh`, 21:58–22:41Z, sequential, `--per-run --max-evals 250`, `--settings` md5
`df7c7cd1…`, **`--profile-id ce3f3e63-…` named in the script header as a first-class input beside the settings
file** — which is F57(b) applied to this wave's own first action, and which the wave-10 gate script does not
contain the string for.

| run | `BestJ` (exact) | 6 dp | wave 10 | | `BaselineJ` | wave 10 | |
|---|---|---|---|---|---|---|---|
| `toml999` | 0.9957838768299878 | 0.995784 | 0.995784 | **MATCH** | 0.983477 | 0.983477 | ok |
| `CWhiteFocus` | 0.9960675916058808 | 0.996068 | 0.996068 | **MATCH** | 0.994320 | 0.994320 | ok |
| `uneven` | 0.9963677194179505 | 0.996368 | 0.996368 | **MATCH** | 0.991794 | 0.991794 | ok |
| `muggsie` | 0.9971948738498605 | 0.997195 | 0.997195 | **MATCH** | 0.993288 | 0.993288 | ok |
| `mccomiskey` | 0.9767460801208465 | 0.976746 | 0.976746 | **MATCH** | 0.846082 | 0.846082 | ok |
| `D18_m24_deep_shed` | 0.9998815090506263 | 0.999882 | 0.999882 | **MATCH** | 0.997405 | 0.997405 | ok |
| `D19_cygnus_deep_shed` | 0.9994870586135448 | 0.999487 | 0.999487 | **MATCH** | 0.999187 | 0.999187 | ok |
| `D20_m24_bright_control` | 0.9997378027339423 | 0.999738 | 0.999738 | **MATCH** | 0.999454 | 0.999454 | ok |

> **RULE G11: PASS, 8 of 8 to 6 dp.** Wave 11 has a coordinate system, and everything downstream is readable
> against it. **And the free control (F41) passes at 8 of 8 too** — every `BaselineJ` reproduces wave 10 on a
> *different build of a different commit*, which is the seed evaluation behaving as a pure function of
> (frames, settings, profile) exactly as §1 says it does under a pinned profile.

**The measurements beside it**, none of them gates:

| field | value | reading |
|---|---|---|
| `ConcurrencyCheck` | `exclusive` × 8 | F55(c): the pass is uncontaminated, *checked* rather than assumed |
| `DetectorVersion` | 2 × 8 | and `strings … \| grep AtrousWaveletFast` hits, retroactively |
| `BuildId` | `40f08e36…` × 8 | **differs from wave 10's**, as a fresh build must; a match would have meant no build happened |
| `ProfileId` | `astrodet (ce3f3e63-…)` × 8 | the pin took |
| `FitInputs` | *absent* | expected — `exe` is the **pre-fix** binary and the field ships with the fix |

**Wave 5's φ table is not quoted anywhere in this document.** It is invalid on two axes — `StarDetectorVersion`
1 → 2 and the profile — and this wave's coordinate system is wave 10's eight values, not wave 5's.

**No `Optimization complete` line was greped for.** `aggregate_summary.json` is the instrument (`uneven` emits no
such line at all, wave 10 §1.4).

---

## §3 — Item 1: F55(b). RULE K passes on all four clauses, and then the fix removes the phenomenon

### §3.1 The probe (`D:\hf_w11\det\determinism_probe_w11_w11.sh`), 22:41–22:45Z

Wave 9's 40-second reproducer on the wave-11 pre-fix binary, with each repeat's `Profile:` line captured.
`D16_esprit550_ha3`, `--max-evals 1`, five pristine copies, **the banks never touched**.

**The prediction was written before the probe ran**, from the `LastUsed` ordering snapshotted in
`profiles_before.txt`: phase C should draw the top five profiles, which split **2 / 3** on
`MaxOutlierRejections`.

| phase | repeat | profile loaded | `MaxOutlierRejections` | `BaselineJ` | `ConcurrencyCheck` |
|---|---|---|---|---|---|
| **S** (sequential) | 1–5 | `astrodet` ×5 | 0 | `0.9791727071693058` ×5 | `exclusive` ×5 |
| **C** (concurrent) | C_1 | `AA1600MM Copy` | **0** | `0.9791727071693058` | `concurrent` |
| | C_2 | `astrodet` | **0** | `0.9791727071693058` | `concurrent` |
| | C_3 | `Default-2026-08-05T10:57:42` | **1** | `0.9885546719484486` | `exclusive` |
| | C_4 | `Default` | **1** | `0.9885546719484486` | `concurrent` |
| | C_5 | `AA1600MM` | **1** | `0.9885546719484486` | `concurrent` |
| **P** (concurrent, `--profile-id`) | P_1…P_4 | — | — | — **rc = 1**, *"No active NINA profile could be loaded"* | — |
| | P_5 | `astrodet` | 0 | `0.9791727071693058` | `exclusive` |

> **Five concurrent processes, FIVE DIFFERENT PROFILES, and a 2 / 3 split exactly as predicted — into wave 9's
> two attractor values, to sixteen decimal places.**

| clause | verdict | evidence |
|---|---|---|
| **K1** distinct `BaselineJ` == distinct `MaxOutlierRejections` | **PASS** | 2 vs 2; `MOR=0 → 0.9791727071693058`, `MOR=1 → 0.9885546719484486`, no collisions |
| **K2** phase S is single-profile, single-valued | **PASS** | 1 profile, 1 value, 5 of 5 |
| **K3** pinning under fan-out fails LOUDLY | **PASS** | exactly 1 success, 4 hard failures, no wrong-profile success |
| **K4** `ConcurrencyCheck`'s first positive control | **PASS** | phase S `exclusive`; phase C contains `concurrent` |

### §3.2 K4 found a real limit in wave 10's shipped guard

Phase C returned **four `concurrent` and one `exclusive`** — and that is correct: `WaitOne(0)` is won by exactly
one of *N* contenders, so **in any fan-out precisely one landing truthfully reports `exclusive`.**

**So `ConcurrencyCheck == "exclusive"` on a SINGLE landing is not evidence that the machine was quiet.** It says
*this process won the mutex*. The field must be read **across a whole arm**, and in wave 10's own two-driver
contamination the first driver's landings would have read `exclusive` throughout — an operator sampling that
driver would have seen a clean bill of health. The field is honest; it is its **scope** that needed pinning down,
and only running it in both directions revealed that. *An instrument that is right about the wrong scope is a new
way to be wrong.*

### §3.3 The fix, and K5 is the clause it is judged on

`AutoFocusOptions` is now built on the **harness accessor** rather than on the active profile, the four fit inputs
are carried in the pinned settings file, and every landing records them as `FitInputs` — **values, not a hash,
because a hash says something moved and values say which one**. `HarnessFitInputs` is one type used both to build
the fit and to render the field, so they cannot diverge; a guard test would only have watched for divergence.

**K5 — post-fix, five concurrent repeats, unpinned:**

| repeat | profile loaded | `BaselineJ` | `FitInputs` |
|---|---|---|---|
| F_1 | `AA1600MM Copy` | `0.9791727071693058` | `MaxOutlierRejections=0;…=0.95;…=True;…=Hybrid` |
| F_2 | `astrodet` | `0.9791727071693058` | *(identical)* |
| F_3 | `AA1600MM` | `0.9791727071693058` | *(identical)* |
| F_4 | `Default-2026-08-05T10:57:42` | `0.9791727071693058` | *(identical)* |
| F_5 | `Default` | `0.9791727071693058` | *(identical)* |

> **K5 PASS. Five different profiles — spanning BOTH `MaxOutlierRejections` groups — and ONE `BaselineJ`,
> identical to the sequential value.** The hazard is *still fully present*; it no longer reaches `J`.
>
> **That the five profiles still differ is what makes the clause discriminating.** A "fix" that pinned the
> profile would have produced one profile and one answer, passed a weaker version of this test, and left every
> future arm exposed the moment two ran at once.

**K7 — the crispest statement of "pinned".** `toml999` at `--max-evals 1` under `--profile-id astrodet` and under
`--profile-id Default`, post-fix: **`0.9834767968919785` both times, identical to full double precision.** Before
the fix those two differ by **0.0144**.

### §3.4 The residual (K6), and what is NOT claimed

`optimize --verbose` already emits `Structure Map K-Sigma Noise Estimate: <σ> … NumIterations=<n>` per detection,
so the σ instrument needed no code. **It was not needed:** phase F is single-valued to full double precision
across five profiles, so there is no residual divergence at the seed level for `KappaSigmaNoiseEstimate` — or
anything else below the fit — to explain.

**`KappaSigmaNoiseEstimate` is therefore withdrawn as F55's mechanism.** Its measured gain stays as a true fact
about the function, pinned by wave 10's unit test; it is a plausible amplifier for some *other* perturbation.
**The trigger RATE wave 10 recorded as owed was owed for a mechanism that was not firing** — and that is the
sharpest lesson available here: *a measured gain is not a measured cause, and it is easy to mistake one for the
other after paying to measure it.*

**What is NOT claimed.** K5 is a **seed-level** result (`--max-evals 1`). F55's LANDING rate was **44 %**, triple
its seed rate, and no landing-level fan-out was measured with the fix in place. **Fan-out is therefore still not
authorised**, and the population pass in §5 ran sequentially and paid the ~3 h.

`--cv-threads <n>` shipped as the F55-family probe knob and was validated by its positive control — the printed
`Cv2.GetNumThreads()` follows the flag (1 → `1`, 4 → `4`, absent → `48`). It was **not needed** either, and it is
recorded rather than used, because wave 9's env-var route silently measured one configuration five times.

---

## §4 — Item 2: F57(c). The answer is one integer, and the negative control is what makes it readable

`toml999`, `--max-evals 1` so `BaselineJ` is the whole measurement, on **copies** (F15), on the **pre-fix**
binary — because this arm measures what the PROFILE supplies. The bisect profiles are fresh GUIDs dropped into
NINA's profile folder and deleted afterwards; **the user's two real profiles were never edited**, and
`make_bisect_profile.py` refuses to run if their shape is not what it expects.

**The read-level audit came first, because it is free**, and it left exactly one unpinned options surface:

| profile-sourced input | reaches the objective? | why not |
|---|---|---|
| **`AutoFocusOptions`** → 4 values | **YES** | the only unpinned surface; 2 of the 4 differ between the profiles |
| detector knobs via `StarDetectionOptions` | no | `FileOptionsAccessor` falls back to **code defaults**, never the profile |
| `PixelScale` | no | resolved per run from the frame header (both printed `0.73944`) |
| image loading | no | **`ImageSettings` is byte-identical between the two profiles**, and `toml999` is mono |
| `AutoFocusBinningConflict` | no | feeds a user prompt; does not resolve binning |
| `FocuserSettings.AutoFocusStepSize` (479 vs 188) | no | the harness infers the step from the frames |

| arm | profile loaded | C4 | `BaselineJ` (exact) |
|---|---|---|---|
| reference | `astrodet` | PASS | `0.9834767968919785` |
| reference | `Default` | PASS | `0.9978404571046091` |
| **C1** `astrodet` + `MOR=1` + `conf=0.99` | `w11-C1-mor1-conf099` | PASS | **`0.9978404571046091`** |
| **C2** `astrodet` + `MOR=1` | `w11-C2-mor1` | PASS | **`0.9978404571046091`** |
| **C3** `astrodet` + `conf=0.99` *(negative control)* | `w11-C3-conf099` | PASS | **`0.9834767968919785`** |

| clause | verdict | evidence |
|---|---|---|
| **C0** the reference points hold | **PASS** | `astrodet` reproduces wave 10 exactly; `Default` to 4.6e-12 (wave 10 quoted 10 dp) |
| **C3** NEGATIVE CONTROL: confidence alone moves nothing | **PASS** | identical to `J_astro` — unreachable at a budget of 0, as predicted |
| **C1** both fields == `J_def` to full double precision | **PASS** | **residue exactly `0.0`** |
| **C2** single-field attribution | **SINGLE-FIELD** | `MOR=1` alone == `J_def`; the confidence is inert |

> **F57(c) is answered: `AutoFocusOptions.MaxOutlierRejections`, one integer, accounts for 100 % of the 0.0144.**
> `OutlierRejectionConfidence` is real but inert behind it — a rejection budget of zero makes the confidence
> unreachable, which is exactly what C3 was written to check and exactly what it found.
>
> **C3 is why C1 and C2 are readable at all.** Had the confidence moved anything, the harness would not have been
> doing what it claimed and both other clauses would have been discarded. Wave 10's gate came back at its
> pre-registered top tier and was still wrong; what killed it was a control built to *exclude*.

**And it is [F45](followups.md#f45--the-grubbs-test-rejects-the-in-focus-point-of-a-near-perfect-curve-and-the-blind-walk-then-buys-an-extra-exposure)
wearing a different hat**: whether the fit may drop one Grubbs outlier was being decided by machine state that
nothing recorded, and allowing the rejection improves the fit — which is the direction observed on every dataset
where the two groups were compared.

---

## §5 — Item 3: F19's successor is refuted before implementation, by the rule fixed in advance to size it

### §5.1 The ladder was already on disk, and it said what to expect

W1–W4 are validated on wave 7's exposure ladder. Wave 7's own aggregates predate `FrameDiagnostics`, but **wave 9
left a 10-rung probe at `D:\hf_w9\wing2\` that has it** — so the wing/inner ratio could be read at **zero
compute**, before a threshold was chosen and before anything was run.

**It was not published from there.** `wing2` ran on the v1 binary under an **unrecorded profile**, which is
precisely the provenance F58 condemns, and publishing a refutation from it would have been the joke telling
itself. It became the **pre-registered prediction** for a pinned re-measurement instead, written into
`ladder_w11.sh`'s header before that ran.

### §5.2 The pinned re-measurement, and every rung reproduces

`exe_fix2`, `pinned_settings_w11.json`, `--profile-id astrodet`, `--max-evals 120` (wave 9's budget exactly), on
wave 7's frames — **no re-render**.

| dataset | rung | wing | inner | **`WingRejectedRatio`** | offline recomputation | wave-9 prediction | | σ_focus |
|---|---|---|---|---|---|---|---|---|
| `D02_rich_135mm` | 0.5 s | 0.6729 | 0.4290 | **1.5683035695977232** | 1.5683035695977232 | 1.5683 | SAME | 0.10063 |
| | 1 s | 0.2909 | 0.2816 | 1.0328751392282651 | 1.0328751392282651 | 1.0329 | SAME | 0.09594 |
| | 2 s | 0.3048 | 0.3465 | 0.8798132420671898 | 0.8798132420671898 | 0.8798 | SAME | 0.09191 |
| | 4 s | 0.0000 | 0.0027 | 0.0 | 0.0 | 0.0 | SAME | **0.05208** |
| | 8 s | 0.0000 | 0.0235 | 0.0 | 0.0 | 0.0 | SAME | 0.06465 |
| `D16_esprit550_ha3` | 0.5 s | 0.0000 | 0.0000 | 1.0 | 1.0 | 1.0 | SAME | 0.35169 |
| | 1 s | 0.0000 | 0.0000 | 1.0 | 1.0 | 1.0 | SAME | 0.26026 |
| | **2 s** | **0.0064** | **0.0000** | **+∞** | +∞ | +∞ | SAME | **0.17117** |
| | 4 s | 0.0000 | 0.0000 | 1.0 | 1.0 | 1.0 | SAME | 0.20421 |
| | 8 s | 0.0000 | 0.0000 | 1.0 | 1.0 | 1.0 | SAME | 0.19659 |

**Ten of ten reproduce, across a binary change (v1 → v2) and a profile change.** And the **shipped field agrees
with an independent offline recomputation on every rung** — two routes to one number, for free.

### §5.3 RULE W has no satisfying threshold

| clause | requirement | measured | implies |
|---|---|---|---|
| **W1** `D02`@0.5 s must fire | ratio ≥ T | **1.5683** | T ≤ 1.5683 |
| **W5** must sit above the bank median | — | 1.39 | T > 1.39 |
| **W2** `D16`@2 s must stay SILENT | ratio < T | **+∞** | **T > +∞ — impossible** |

> **W1 ∧ W5 leave the window (1.39, 1.5683]. W2 empties it. `WingRejectedRatio` is REFUTED BEFORE
> IMPLEMENTATION, by the rule wave 10 fixed in advance to size it** — the same shape as wave 10's own deadband,
> where the rule written to accept a mechanism had no valid input and that *was* the answer.

**And the mechanism is worse than the arithmetic.** On a clean, well-exposed narrowband run the core rejects
**nothing**, so **any** wing rejection at all — 0.64 % here — becomes an infinite ratio. The ratio form is
maximally unstable exactly where the statistic is required to be silent, and `D16` @ 2 s is not an arbitrary
rung: it is where `D16`'s σ_focus is **minimised** (0.17117, against 0.35169 / 0.26026 / 0.20421 / 0.19659), so
firing there asks the user to make their focus worse. **The absolute fraction got this rung RIGHT** (0.0064 ≪
0.20). *On the one control that matters, the successor is not merely no better than its predecessor — it is
strictly worse.*

### §5.4 What ships, and what does not

| | |
|---|---|
| **ships** | `ExposureRecommendation.WingRejectedRatio` as a **MEASUREMENT ONLY**, with a four-state contract — `NaN` (could not look) / `+∞` (the core rejects nothing and the wings do) / `1.0` (both reject nothing: equal rates) / the ratio. Newtonsoft writes the first two as the **strings** `"NaN"` and `"Infinity"`, pinned by a serialization test |
| **does NOT ship** | any verdict, threshold, probe factor, or action. Nothing in the product reads it |

**The predicted hazard class landed on a different dataset than the two named, and that is the point.** The
design named `D17` (ratio 0.59) and `D20` (inner fraction exactly 0.000) as the hazards. The killer was `D16`@2 s
— **the same shape as `D20`** (inner exactly 0.000 ⇒ infinite) on a dataset the design had listed under a
different clause. *Naming the hazard CLASS in advance worked even though the specific dataset was wrong*, which
is more than naming a dataset would have bought.

### §5.5 The successor-of-the-successor, named and NOT evaluated here

`WingRejectedExcess` = wing − inner was named in the design **before this ladder was read**. It is bounded in
[−1, 1], defined when the inner third rejects nothing, and cannot fire when wings and cores reject at the same
rate. **Its verdict is deliberately not computed in this document.** The ladder is now the data that refuted the
ratio, and W6's whole content is that a successor may not be validated on the data that killed its predecessor —
which applies to the ladder exactly as it applied to the 39-run population. *A statistic tuned on the data that
killed the last one has been fitted, not tested*, and this register has now had to write that sentence three
times.

### §5.6 The 39-run population pass was NOT run, and here is the trade

The plan had item 3 decided by a 39-run pass over both banks (~3 h), with K8 riding along inside it for free.
**Item 3 was refuted first, on a NECESSARY clause**, so that pass could no longer change the verdict — and W6
bars using its rows to size `WingRejectedExcess` in any case. What it would still have bought is the population
*range* of a refuted measurement, for three hours, while re-landing both banks under a new settings fingerprint.

**So K8 was run on its own** (the eight RULE G11 runs, ~40 min) and the pass was dropped.

> **What is lost, stated plainly: this wave publishes NO 39-run distribution for `WingRejectedRatio`.** Anyone
> sizing a future wing statistic must measure their own population — which is what W6 requires of them anyway.
> Recorded here rather than left as a silently smaller pass, because *"a scope that quietly shrinks is how a
> partial result gets read as a complete one."*

---

## §6 — K8, the suite, and what the wave leaves open

### §6.1 K8 — the coordinate system survives its own fix, bit for bit

The eight RULE G11 runs re-run on `exe_fix2` with `pinned_settings_w11.json`, 23:24–00:07Z, sequential.

| run | K8 `BestJ` (exact) | gate `BestJ` (exact) | bit-identical |
|---|---|---|---|
| `toml999` | 0.9957838768299878 | 0.9957838768299878 | **YES** |
| `CWhiteFocus` | 0.9960675916058808 | 0.9960675916058808 | **YES** |
| `uneven` | 0.9963677194179505 | 0.9963677194179505 | **YES** |
| `muggsie` | 0.9971948738498605 | 0.9971948738498605 | **YES** |
| `mccomiskey` | 0.9767460801208465 | 0.9767460801208465 | **YES** |
| `D18_m24_deep_shed` | 0.9998815090506263 | 0.9998815090506263 | **YES** |
| `D19_cygnus_deep_shed` | 0.9994870586135448 | 0.9994870586135448 | **YES** |
| `D20_m24_bright_control` | 0.9997378027339423 | 0.9997378027339423 | **YES** |

> **K8 PASS, 8 of 8 — and not merely to 6 dp. Every landing is IDENTICAL TO ALL SIXTEEN DIGITS across a
> different binary and a different settings file.** `ConcurrencyCheck = exclusive` and
> `FitInputs = MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid`
> on all eight.
>
> So the fix is **exactly** behaviour-preserving for a pinned arm: wave 10's coordinate system carries forward
> unchanged, and no future wave inherits an undocumented discontinuity at the commit that removed one.

### §6.2 The suite

**3740 passed, 0 failed, exit code 0.** `develop` was **3722**, so **+18**, and every one is named:

| fixture | tests | what they pin |
|---|---|---|
| `HarnessFitInputsTests` | 10 | the fit reads the FILE; the documented code defaults for a file that lacks the keys (the back-compat clause every pre-wave-11 pinned file depends on); `FitInputs` renders values and moves on all four axes; culture invariance; the dense copy; **F59's validating-setter regression** |
| `ExposureRecommenderTests` (new) | 8 | `WingRejectedRatio`'s four states, the two-thirds disjointness guard, the `"NaN"`/`"Infinity"` serialization contract, and that no verdict is derived from it |

The count was verified, not the tick (F37). Nothing was piped to `tail`, which would have masked the exit code.

### §6.3 What this wave closes, and what it does not

| entry | state |
|---|---|
| **F58** | **new, and it is the wave.** Mechanism identified, reproduced, intervened on, fixed, and the fix shown to remove the phenomenon while the hazard stays present |
| **F57** | **(c) ANSWERED** — one integer. **(b) shipped** as a run rule in `testapp-cli.md`. (a) shipped in wave 10 |
| **F55** | **mechanism identified.** Its measurements stand; `KappaSigmaNoiseEstimate` is withdrawn as the explanation. **Still open at the LANDING level**: K5 is a seed-level result and F55's landing rate was 44 % |
| **F59** | **new, fixed.** Five detector knobs missing from the pinned file since wave 5 |
| **F19** | **the successor is refuted too**, before implementation, by RULE W2. `WingRejectedExcess` named and not evaluated |
| **F15** | **still open**, and still what forces a population pass to run alone |
| **F45** | **implicated**: the Grubbs rejection budget was deciding `J` from unrecorded machine state |

**Explicitly still open, and not to be read as closed by this wave:**

- **Fan-out is NOT authorised.** K5 is a seed-level result. Nothing here measured landings under concurrency with
  the fix in place, and F55's landing rate was triple its seed rate.
- **Four other harness runners have F58's defect** — `BankVerifyRunner` (×2, and it feeds the golden audits),
  `SynthValidateRunner`, `InspectAlignRunner`, `TiltCalibrationRunner`. Flagged in F58(d), not converted: each
  needs its own validation and wave 11's budget went to `optimize`.
- **No 39-run distribution for `WingRejectedRatio`** (§5.6).
- **Wave 8's, wave 9's and wave 10's AF/wizard changes are STILL UNCONFIRMED IN THE APP.** The csproj PostBuild
  xcopy fails silently when NINA is running. Not this wave's item; not to be claimed as confirmed either.

---

## Lessons

**0. A measured gain is not a measured cause — and paying to measure the gain makes the confusion easier.**
Wave 10 named `KappaSigmaNoiseEstimate` as F55's bimodal amplifier, measured its gain, pinned it with a unit
test, and recorded the trigger RATE as the cheapest thing still owed. **The rate was owed for a mechanism that
was not firing.** The real cause was one integer in a file the process opened at startup. The gain measurement is
still true; it was never evidence of causation, and the effort spent obtaining it is exactly what made it feel
like it was.

**1. The cheapest instrument is the one already printed and ignored.** F58 — the whole wave — came out of a
`Profile:` line that `optimize` had been printing since before wave 9, sitting in `D:\hf_w9\det\C_*.log`, at zero
compute. Two waves eliminated the plugin's `Parallel.For` degree, three binaries, folder state one artifact at a
time, OpenCL, `Merge`, rented sorts and load-sensitive timeouts — all correctly, all expensively — while the
answer was in the second line of every log they were reading. **Wave 10's lesson 4 said to check whether a cheap
instrument already exists. It does not go far enough: check whether the instrument already RAN.**

**2. Require the hazard to survive the fix.** K5 does not ask "is the answer stable now"; it asks "is the answer
stable *while five processes still load five different profiles*". A fix that pinned the profile would have
passed the weaker question and left every future arm exposed. The clause was written that way before the fix
existed, and it is the only reason the result means anything.

**3. The negative control is what makes the positive ones readable.** C3 — `OutlierRejectionConfidence` alone must
move NOTHING, because a rejection budget of zero makes it unreachable — is the clause that could have shown the
bisect harness was not doing what it claimed. It passed, so C1's residue of exactly `0.0` is a measurement rather
than a coincidence. Wave 10's gate returned its pre-registered top tier and was still wrong; what killed it was a
control built to exclude.

**4. A validated instrument validated against a DIFFERENT contract is not validated.** Wave 10's population
scorer maps the JSON strings `"NaN"`, `"Infinity"` and `"-Infinity"` all to NaN — correct for a statistic with no
infinite state, and catastrophic for this one. Reused verbatim it would have read `+∞` as *"we could not look"*,
hidden every run the ratio form fails on, and pushed the NaN-rate clause toward firing on runs where the
instrument worked perfectly. **Reuse the instrument; re-derive its contract.**

**5. An instrument can be right about the wrong SCOPE.** `ConcurrencyCheck` works — K4 fired it in both
directions for the first time. But `WaitOne(0)` is won by exactly one of *N* contenders, so **one landing reading
`exclusive` is not evidence the machine was quiet**; it says that process won the mutex. In wave 10's own
two-driver contamination the first driver's landings would all have read `exclusive`. The field is honest; the
*reading* of it needed pinning down, and only running it in both directions revealed that.

**6. Test the property you care about, not the happy path.** F59 was found by a test asserting **density** — *a
value equal to the code default must still reach the file* — and not presence. Presence would have passed. The
bug it exposed had silently dropped five detector knobs from the pinned file since wave 5.

**7. Name the hazard CLASS, not the dataset.** The design named `D17` (ratio 0.59) and `D20` (inner exactly
0.000) as the two ways the ratio form could fail. The killer was `D16` at 2 s — `D20`'s shape on a dataset filed
under the other clause. Naming the class worked; naming only the datasets would not have.

**8. Say what you did not run.** The 39-run population pass was dropped once item 3 fell to a necessary clause.
That is defensible, and it is only defensible **because the results say the distribution is missing** rather than
presenting eight runs where thirty-nine were planned. *A scope that quietly shrinks is how a partial result gets
read as a complete one.*
