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

## §1b — THE WAVE RAN ITS OWN POPULATION PASS TWICE AT ONCE, AND THE RULE AGAINST IT WAS ALREADY WRITTEN DOWN

This belongs above the results it nearly destroyed, not in a footnote.

**What happened.** The population pass must be sequential — F55 measures concurrent `optimize` moving **44 % of
landings**. That was written into this wave's design, into its plan, and into the header of the script that ran
it. A background launcher had been started to chain the pass behind the gate; it reported **"completed"**, and a
`ps` check showed nothing surviving, so a second launch was issued. **The launcher's shell had exited; its
`nohup`'d driver had not**, and it lived in a process namespace the checking shell could not see. Two drivers,
two `optimize` processes, the same bank folders, for eleven minutes.

**What caught it.** Not the rule, and not vigilance — an instrument built for something else. A trace diff,
written to find the FIRST divergent evaluation between two runs of one invocation, printed an impossible
improvement ladder; the log turned out to contain two complete optimizations.

**That instrument had to be repaired before it could catch anything.** Its first version diffed the raw
`[Phase] evals=N bestJ=…` lines — which come partly from a **wall-clock progress timer** that merely repeats the
current best. A busier machine logs a different number of lines at different evaluation counts, so the first
version reported "divergence" on every pair of runs, whether or not the search diverged. Asked the question this
wave is bound by — *what would this do if the thing it checks were completely broken?* — the answer was **exactly
the same thing**. Keeping only the entries where `bestJ` CHANGES leaves the improvement events, whose evaluation
index is exact.

**And the guard written in response was itself broken, in the most on-the-nose way available.** `grep -c` prints
`0` **and exits 1** when there is no match, so `RUNNING=$(… | grep -ci … || echo 0)` produced the two-line string
`"0\n0"`; the numeric comparison then errored and the guard **fell through, reporting SAFE unconditionally**.
That is wave 9's *"an instrument that is not connected reports PERFECT AGREEMENT"*, reproduced inside the guard
written to enforce it, within the hour. It was fixed and then **validated against a positive control** — a
deliberately-started `optimize` — before being trusted. Both clauses fired.

**What ships because of it.** `optimize` now claims a named mutex and records `ConcurrencyCheck`
(`exclusive` / `concurrent` / `unknown`) into **every landing it writes**. Three values and not two, for the same
reason `WingRejectedFraction` is NaN-never-0: a check that could not run must not read as a check that ran and
found nothing. **A rule that depends on the operator noticing a violation is not a control; it is a hope.** The
register is already full of the mechanical version of that move — `BaselineJ` as a free control (F41),
`DetectionBinningSource` as a field rather than a sentence (F39(a)), `BuildId` rather than a version string
(F53).

**Cost:** eleven minutes of compute, discarded in full. The pass was deleted and restarted from zero at 14:06Z
under the validated guard; nothing in §2 is measured on the contaminated data.

---

## §2 — Item 1: F19's population check

### §2.1 The real bank, all 19 runs

Shipped-default invocation, sequential, `--settings` pinned. `WingRejectedFraction` is the shipped statistic;
`inner` is **the control it never had** — its own quantity computed on the INNER third of the sweep instead of
the outer third.

| run | wing | inner | wing/inner | fires |
|---|---|---|---|---|
| `CWhiteFocus` | 0.879 | 0.795 | 1.11 | yes |
| `FlyData` | 0.675 | 0.572 | 1.18 | yes |
| **`LinwoodFocus`** | **0.375** | **0.537** | **0.70** | **yes** |
| `Panos` | 0.817 | 0.442 | 1.85 | yes |
| `SorenVance` | 0.520 | 0.242 | 2.15 | yes |
| `bobp` | 0.610 | 0.247 | 2.47 | yes |
| `bobp_m101` | 0.446 | 0.264 | 1.69 | yes |
| `caboose` | **0.000** | 0.000 | — | no |
| **`cwhite_2026`** | **0.868** | **0.883** | **0.98** | **yes** |
| `fmeschia_Focus` | 0.352 | 0.317 | 1.11 | yes |
| `lumos` | **NaN** | 0.376 | — | no |
| `mccomiskey` | **0.000** | 0.000 | — | no |
| `mufti` | 0.661 | 0.577 | 1.15 | yes |
| `muggsie` | 0.855 | 0.617 | 1.39 | yes |
| `standard_example1` | 0.863 | 0.775 | 1.11 | yes |
| `timmer` | 0.819 | 0.524 | 1.56 | yes |
| **`toml999`** | **0.544** | **0.567** | **0.96** | **yes** |
| `uneven` | 0.727 | 0.635 | 1.14 | yes |
| **`vsn07`** | **0.853** | **0.905** | **0.94** | **yes** |

**16 of 19 fire. Median wing/inner = 1.15.**

### §2.2 Two findings, and the second one is the sharper

**(a) The absolute fraction does not track the wing effect it claims to measure.** Four runs — `LinwoodFocus`
0.70, `vsn07` 0.94, `toml999` 0.96, `cwhite_2026` 0.98 — reject **fewer** candidates in their wings than in their
cores, and all four fire. The statistic's justification is that the wing rejections are faint stars a longer
exposure could convert; where the core rejects at the same rate — with the stars at their brightest and most
numerous — the rejected population is dominated by noise the detector is *supposed* to reject. **The statistic
cannot separate "the wings are losing faint stars" from "the detector is rejecting noise everywhere."**

> **Corrected against the full population (§2.3).** Read on the real bank alone this looked like *"the wings are
> not special"*: median wing/inner 1.15. Over all 39 runs the median is **1.39**, so on average the wings **do**
> reject somewhat more than the cores. **The defect is not that the wing effect is absent — it is that a
> threshold on the ABSOLUTE fraction does not track it.** `D17_cdk14_oiii5` fires at a wing/inner of **0.59**;
> `D20_m24_bright_control` fires with an inner fraction of exactly **0.000**. Same verdict, opposite structures.

**(b) The threshold's entire discriminating power over 39 runs is ONE dataset.** Sorted, the bank's fractions
are **0.000 ×8**, then **0.006** (`D16_esprit550_ha3`), then **0.246 … 0.883** — nothing else below 0.246. So
every threshold in **(0.006, 0.246)** produces exactly the same 30 fires, and the only thing 0.20 separates is
`D16` from the rest.

> **Also corrected.** From the real half this read *"every threshold in (0, 0.352)"*, i.e. that the threshold did
> no work at all. `D16`'s 0.006 is on the synthetic half and it is the run W2 requires to stay silent, so the
> threshold **is** load-bearing — for exactly one dataset out of thirty-nine. That is a weaker claim than the one
> the real half supported, and it is the true one.

**Where the 0.20 came from, and why it looked reasonable.** The unit fixtures span a rejected fraction of
**0.002** (`wingRejected: 1, wingAccepted: 500`, the "healthy wings" pole) to **0.75** (`600 / 800`, the
"shedding" pole), and 0.20 sits sensibly between them. **Exactly one run in 39 resembles the healthy pole.** The
threshold was calibrated against a range the data barely occupies — which a unit test cannot notice, and only a
population can.

**And every firing run asks exactly 2×.** `rawFactor = max((10/S_now)², 2.0)`, and the accepted-star term is far
below 2 on every firing run, so the probe always wins. The recommendation carries no information beyond *"it
fired"*.

### §2.3 THE VERDICT — RULE P fires on P2, and the withdrawal ships

39 runs, both full banks, sequential, shipped-default invocation.

| clause | measured | verdict |
|---|---|---|
| **P1** the pre-registered fires | `D17` **fires**; `D10` does **not** | **silent** (P1 needed neither to fire) |
| **P2** fire rate | **30 of 39 = 76.9 %** | **FIRES — REFUTED** |
| **P3** `D16` at its derived exposure | 0.006, does not fire | **silent** — W2 survives |
| **P4** NaN rate | 0 of 39 | **silent** — the instrument was connected |

**RULE P fires. `WingIsShedding` does not stay a shipped default**, and the withdrawal named in §1.2b ships in
this PR: the verdict and its three action sites go, `WingRejectedFraction` stays.

**Two things worth stating that P1 does not capture.** `D10_rc16_3250mm_sparse` — the bank's most starved
dataset, 26 on-frame stars, whose own physics asks 335.6 s against the 30 s it is rendered at — **does not
fire**. And `D17`, which does fire and which P1 required, fires with wings that reject **less** than its core
(0.297 vs 0.507). P1 was written as "at least one of the two", and it is satisfied; but the statistic reaches the
right answer on `D17` by a route that is not the one it claims, and misses `D10` entirely.

### §2.4 The free controls, and they are what make the verdict readable

**RULE G10-A: PASS, 8 of 8 to 6 dp.** The population pass re-measured the gate's eight runs at the identical
invocation and returned identical landings — `toml999` 0.995784, `CWhiteFocus` 0.996068, `uneven` 0.996368,
`muggsie` 0.997195, `mccomiskey` 0.976746, `D18` 0.999882, `D19` 0.999487, `D20` 0.999738. **The fixed point
reproduces against itself, so every wave-10 comparison rests on an instrument that was checked rather than
assumed** — and F55 is not firing at rest.

**And it is evidence on F57's folder-state confound.** Those eight ran on folders whose `optimized_settings.json`
had been rewritten **twice** in between — once by the gate, once by the eleven minutes of contaminated
double-running — and every landing reproduced to 6 dp. Folder state does not move a landing on these runs. That
is not yet `toml999`'s seed evaluation on the v1 binary (§3.1's control), but it points the same way.

---

## §3 — Item 2: F55(b)

*(pending)*

---

## §4 — Item 3: the step recommender

*(pending)*

---

## Lessons

*(assembled as they were learned; numbers filled in from the sections above)*

**0. A rule that depends on the operator noticing a violation is not a control; it is a hope.** F55(c) — "treat
concurrent `optimize` as invalid, and say so in the run instructions" — was written into this wave's design, its
plan, and the header of the script that ran the pass. The pass then ran twice at once. The fix is not more
discipline; it is `ConcurrencyCheck` stamped into every landing, so a scorer can refuse contaminated data
without anyone having been watching. The register already contains the same move three times: `BaselineJ` as a
free control (F41), `DetectionBinningSource` as a field rather than a sentence (F39(a)), `BuildId` rather than a
version string (F53).

**1. A numerical-equivalence bound on an intermediate is not an equivalence bound on the answer.** PR #187's
wavelet paths agree to ≤ 3e-8 per pixel and the claim is true. Six of eight product landings still moved, and one
seed evaluation — no search, nothing to amplify it — moved by 0.0144. The star gate is a **threshold**, and a
threshold converts an eighth-decimal difference into a whole star. Any future change justified as "equivalent to
N decimals" needs the bound on the quantity the product REPORTS.

**2. A unit fixture's range is not the population's range.** *(see §2)* The wing statistic's threshold was
chosen against fixtures spanning a rejected fraction of 0.005 to 0.75. Whether that is the range real runs
occupy is not a question a unit test can ask, and it was not asked until a full-bank population was measured.

**3. Ask an instrument what it would say if the thing it checks were completely broken — and then make it say
it.** Three of this wave's own instruments failed that question, and all three were caught:

- the **trace diff** would have reported divergence on every pair of runs, because it diffed log lines that come
  partly from a wall-clock timer;
- the **concurrency guard** would have reported SAFE on every invocation — `grep -c` exits 1 on no match, so
  `$(… || echo 0)` produced `"0\n0"` and the numeric comparison errored through. It was then **validated against
  a positive control**, a deliberately-started `optimize`, before being trusted;
- the **RULE P scorer** treated a dataset that had not been measured yet as one that *did not fire*, so a partial
  run reported P1 as REFUTED. That is the scorer's own NaN-never-0 rule — the one it enforces on
  `WingRejectedFraction` — being violated by the scorer. It now reports UNEVALUATED and refuses to present a
  partial pass as a population verdict.

The question is cheap. Asking it only of other people's instruments is the mistake.

**4. Check whether a cheap instrument already exists — including one a previous wave left on disk.** The exact
geometric ratio a capped step recommendation applies (`1.5 × P / PointsPerSide`) was validated against **22
capped rounds across six datasets** already sitting in wave 7's artifacts, at zero compute. The same files
refuted this wave's own deadband proposal before a line of it was written.

**5. Name the successor before you need it, and bar it from the data that killed its predecessor.** The
ratio-form wing statistic was written down mid-pass, with its acceptance rule, precisely so it could not be
adopted on the population that refuted the original. A statistic tuned on the data that killed the last one has
been fitted, not tested — which is wave 9's R3, one level up.
