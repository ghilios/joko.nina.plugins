# Synthetic AF bank — followups wave 11 (results)

Design: [`docs/synthetic-af-bank-followups-wave11-design.md`](synthetic-af-bank-followups-wave11-design.md).
Plan: [`plans/synthetic-af-bank-followups-wave11-plan.md`](../plans/synthetic-af-bank-followups-wave11-plan.md).
Wave 10: [`docs/synthetic-af-bank-followups-wave10-results.md`](synthetic-af-bank-followups-wave10-results.md).
Register: [`docs/followups.md`](followups.md).

> ## PROVENANCE — and this is the last wave that needs to say most of it in prose
>
> Every measurement below was produced by **`D:\hf_w11\exe`** (pre-fix) or **`D:\hf_w11\exe_fixed`**, both built
> from `a54c48b` (PR #188's merge commit, `develop`'s head), **`StarDetectorVersion` 2**, **under the NINA profile
> `astrodet (ce3f3e63-8fd3-4b72-a0ca-d90db9441382)`, PINNED WITH `--profile-id` ON EVERY INVOCATION.**
>
> | | |
> |---|---|
> | `exe` (pre-fix) | `TestApp.dll` sha256 `71ce1cd8…`, `HocusFocus.dll` sha256 `0aca4c20…`, `BuildId 40f08e36…` |
> | `--settings` | `D:\hf_w11\pinned_settings.json`, md5 `df7c7cd1…` — byte-identical to waves 5–10 (F42) |
> | `--settings` (post-fix) | `D:\hf_w11\pinned_settings_w11.json`, md5 `a67ffc06…` — the same **plus** the four fit inputs |
>
> **`--profile-id` is in this banner because of what the wave found**, and it is not hygiene: measured before the
> gate ran, an **unpinned** `optimize` on this machine today loads `Default` and returns `toml999`
> `currentJ = 0.99784` — **wave 9's value, not the wave 10 the banks were measured under**. An unpinned wave-11
> gate would have reproduced the wrong wave and called it a pass.
>
> `exe` and `exe_fixed` are each built once and never rebuilt (F53(c)).

## Status of this document

| item | state |
|---|---|
| **F58** — new, and it is the wave's headline | **F55 and F57 are ONE defect: concurrent `optimize` processes each acquire a DIFFERENT NINA profile, and the two "attractors" are two values of `MaxOutlierRejections`.** Found at **zero compute**, out of logs wave 9 left on disk. See §1 |
| **RULE G11** — the gate, the wave's only pass/fail clause | **PASS, 8 of 8 to 6 dp**, and all eight `BaselineJ` reproduce wave 10 exactly. See §2 |
| **item 1** — F55(b) | *(pending — §3)* |
| **item 2** — F57(c) | *(pending — §4)* |
| **item 3** — F19's successor | **RULE W2 has no satisfying threshold on wave 9's on-disk ladder**; the pinned re-measurement is *(pending — §5)* |
| **the full suite** | *(pending — §6)* |

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

*(§3 item 1 · §4 item 2 · §5 item 3 · §6 the suite — pending)*
