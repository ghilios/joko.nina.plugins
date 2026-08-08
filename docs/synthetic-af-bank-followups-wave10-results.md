# Synthetic AF bank — followups wave 10 (results)

Design: [`docs/synthetic-af-bank-followups-wave10-design.md`](synthetic-af-bank-followups-wave10-design.md).
Plan: [`plans/synthetic-af-bank-followups-wave10-plan.md`](../plans/synthetic-af-bank-followups-wave10-plan.md).
Wave 9: [`docs/synthetic-af-bank-followups-wave9-results.md`](synthetic-af-bank-followups-wave9-results.md).
Register: [`docs/followups.md`](followups.md).

> ## PROVENANCE — the thing wave 9 had to write by hand, and which is now a FIELD
>
> Every number below was measured on **`StarDetectorVersion` 2** (PR #187's `AtrousWaveletFast`), from
> `D:\hf_w10\exe`, built from `02c62d8` — `TestApp.dll` sha256 `5e58b669…`, `NINA.Joko.Plugins.HocusFocus.dll`
> sha256 `ccb3e3e9…`. **That directory was never rebuilt** ([F53](followups.md#f53--wave-8s-arm-x-does-not-reproduce-from-wave-8s-own-exe-because-the-arm-ran-on-an-earlier-build-of-it)(c));
> later wave-10 builds went to `exe2` / `exe_v1wav`.
>
> This banner is written by hand for the LAST time: F53(a) shipped in this wave, so `optimize` now prints — and
> every landing now stores — a `BuildId` (the assembly MVID, which the compiler regenerates on every build) and a
> `DetectorVersion`. A reader diffs a field; nobody diffs a banner.

## Status of this document

| item | state |
|---|---|
| **RULE G10-A** — the fixed point reproducing against itself | see §1.3 |
| **RULE G10-B** — how many landings a ≤ 3e-8 change moves | **FIRES AT THE TOP TIER: 6 of 8.** And the seed evaluation — no search at all — moved on `toml999`. See §1 |
| **item 1** — F19's population check | see §2 |
| **item 2** — F55(b), the nondeterminism | see §3 |
| **item 3** — the step recommender | see §4 |
| **F53** — the build stamp | **SHIPPED.** And the field that claimed to identify the build is shown not to. See §3.4 |
| **F49(c)** — what a capped step says | **SHIPPED**, with a ratio that is exact and measured against 22 on-disk rounds. See §4 |

---

## §1 — The gate, and it is the wave's first finding rather than its permission slip

### §1.1 What was measured

The eight comparability runs at wave 5's exact invocation (`--per-run --max-evals 250`, `--settings` pinned to
`md5 df7c7cd1…`, byte-identical to waves 5–9, [F42](followups.md#f42--every-build-directory-silently-gets-its-own-detector-settings-and-the-run-instructions-require-a-new-one-per-arm)),
sequentially, nothing else running ([F55](followups.md#f55--optimize-is-not-reproducible-when-several-instances-run-at-once-and-the-seed-evaluation-is-what-moves)).
13:04–13:46Z.

| run | `BaselineJ` v1 | v2 | | `BestJ` v1 | v2 | |
|---|---|---|---|---|---|---|
| `toml999` | 0.997840 | **0.983477** | **MOVED** | 0.997993 | **0.995784** | **MOVED** |
| `CWhiteFocus` | 0.994320 | 0.994320 | = | 0.998476 | **0.996068** | **MOVED** |
| `uneven` | 0.991794 | 0.991794 | = | 0.996368 | 0.996368 | = |
| `muggsie` | 0.993288 | 0.993288 | = | 0.997195 | 0.997195 | = |
| `mccomiskey` | 0.846082 | 0.846082 | = | 0.994025 | **0.976746** | **MOVED** |
| `D18_m24_deep_shed` | 0.997405 | 0.997405 | = | 0.999822 | **0.999882** | **MOVED** |
| `D19_cygnus_deep_shed` | 0.999187 | 0.999187 | = | 0.999557 | **0.999487** | **MOVED** |
| `D20_m24_bright_control` | 0.999454 | 0.999454 | = | 0.999766 | **0.999738** | **MOVED** |

### §1.2 RULE G10-B fires at its top tier, and the reading was fixed before the numbers arrived

> **6 of 8 landings moved.** The pre-registered table said: 0 ⇒ inert; **1–3 ⇒ the expected pattern-search
> amplification** ([F8](followups.md#f8--optimizer-landings-are-not-reproducible-across-invocations)); **≥ 5 ⇒
> the "≤ 3e-8" claim describes the per-pixel RESIDUAL and not the detector's BEHAVIOUR, and PR #187 needs
> re-examining as a change that moves product landings.**

**And the seed evaluation moved, which is the sharper half.** `BaselineJ` is a **single evaluation of the pinned
seed with no search involved**, so nothing amplifies it. On `toml999` it moves by **0.0144** — six orders of
magnitude above 3e-8 — between two binaries whose every printed seed input is identical: same settings file
(same export stamp, same "0 advanced knobs overridden" warning), `Sensitivity=10`,
`StarClippingMultiplier=2`, `NoiseClippingMultiplier=4`, `StructureLayers=4`, inferred step 21, exposure 5 s,
`PixelScale 0.73944 arcsec/px (frame header)`, detection binning `1 from harness_settings.json`.

**This is not a defect in PR #187's own claim.** The two wavelet paths do agree to ≤ 3e-8 per pixel, and the
version bump exists precisely because they are not bit-identical. What the gate measures is the **consequence**:
a detector whose per-pixel output moves in the eighth decimal moves 6 of 8 product landings and one seed
evaluation in the second. *A numerical-equivalence bound on an intermediate is not an equivalence bound on the
answer* — the star gate is a threshold, and a threshold turns an eighth-decimal difference into a whole star
appearing or not appearing.

**What it changes.** Wave 5's φ table, wave 6's arm R and wave 9's F32 verdict are all readable **within** their
own binaries and none of them is retracted. What is now measured rather than assumed is that they cannot be
compared **across** the version boundary at all — which is what wave 9's provenance banner asked for and what
this table supplies with a number.

### §1.3 The confound the seed result has to survive, and the control that settles it

`BaselineJ` is read on folders that a previous arm wrote into
([F15](followups.md#f15--optimize---per-run-overwrites-each-runs-stored-settings)), and wave 9's gate ran on a
wave-8-era folder state while wave 10's ran on wave 9's arm-A landing. So the honest reading is that **two things
differ between the two measurements — the binary and the folder state** — and one of them is not the wavelet.

- **`harness_settings.json` is excluded outright.** `toml999`'s is dated **2026-07-31 12:43**, i.e. untouched
  since before wave 8, so it is byte-identical across both gates.
- **`optimized_settings.json` is the live one.** F55's own investigation measured it as inert (run with and
  without the file present, identical) — but on `D16` and on the v1 binary, not here.
- **The decisive control is §3.1's cross-build probe**, which runs `toml999` on wave 9's v1 binary against
  TODAY's folder state. Same folder, different binary: if it returns wave 9's 0.997840, folder state is excluded
  and the BINARY is the cause; if it returns 0.983477, the folder state is the cause and the binary is
  exonerated. **The seed-evaluation claim above is held open until that control reports.**

**And "the binary" is not yet "the wavelet", which is a second step.** The two exes differ by PR #187 *plus*
whatever wave-9 code landed after `D:\hf_w9\exe` was built (its `.dll` is stamped 2026-08-06 19:40, and several
wave-9 commits post-date it). Those additions are all post-search or UI — wave 9 verified the wing statistic is
computed *after* the search and is inert on it, and F49/F51/F52(c) are copy — so PR #187 is the only plausible
search-relevant term. **Plausible is not measured**, which is why §3.2's bisect build exists: the wave-10 tree
with `StarDetector.cs`'s two call sites reverted to the legacy dense path isolates the wavelet from every other
difference at once. Until it reports, this section attributes to *the binary*, not to *the wavelet*.

*(`D:\hf_w9\exe`, `exe2`, `exe_f56` and `exe_bisect` were each confirmed to be v1 builds before any of this was
attributed — `strings <dll> | grep AtrousWaveletFast` returns 0 on all four and 1 on `D:\hf_w10\exe`. That is a
retroactive build-identity check that works on every artifact directory already on disk, and it is how F53's
question was answered for the binaries that predate F53's stamp.)*

### §1.4 A smaller thing the gate caught about its own instrument

`uneven`'s log contains **no `Optimization complete` line at all**, while every other run's does — so the gate
script's own `grep` for that line reports a failure on a run that succeeded. The run is fine: its
`aggregate_summary.json` has `LoadOk=True` and `BestJ = 0.996368`, exactly reproducing v1. **The JSON is the
instrument; the console line is not.** Recorded because a scripted arm that gates on the console line would drop
a good run, and because the first reading of this file was taken while it was still being flushed and briefly
looked like a crash.

---

## §2 — Item 1: F19's population check

*(pending — the pass is running)*

---

## §3 — Item 2: F55(b)

*(pending)*

---

## §4 — Item 3: the step recommender

*(pending)*
