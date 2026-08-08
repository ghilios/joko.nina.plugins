# Synthetic AF bank — followups wave 10 (results)

Design: [`docs/synthetic-af-bank-followups-wave10-design.md`](synthetic-af-bank-followups-wave10-design.md).
Plan: [`plans/synthetic-af-bank-followups-wave10-plan.md`](../plans/synthetic-af-bank-followups-wave10-plan.md).
Wave 9: [`docs/synthetic-af-bank-followups-wave9-results.md`](synthetic-af-bank-followups-wave9-results.md).
Register: [`docs/followups.md`](followups.md).

> ## PROVENANCE — the thing wave 9 had to write by hand, and which is now a FIELD
>
> Every number below was measured on **`StarDetectorVersion` 2** (PR #187's `AtrousWaveletFast`), from
> `D:\hf_w10\exe`, built from `02c62d8` — `TestApp.dll` sha256 `5e58b669…`, `NINA.Joko.Plugins.HocusFocus.dll`
> sha256 `ccb3e3e9…` — **under the NINA profile `astrodet (ce3f3e63-8fd3-4b72-a0ca-d90db9441382)`.**
> `D:\hf_w10\exe` **was never rebuilt** ([F53](followups.md#f53--wave-8s-arm-x-does-not-reproduce-from-wave-8s-own-exe-because-the-arm-ran-on-an-earlier-build-of-it)(c));
> later wave-10 builds went to `exe_v1wav` (the wavelet bisect) and `exe3` (item 3).
>
> **The profile is in this banner because of what this wave found.** It was not in wave 9's, and that omission is
> exactly [F57](followups.md#f57--a---settings-pinned-arm-is-not-pinned-the-active-nina-profile-moves-baselinej-by-0014-and-every-cross-wave-comparison-inherits-it):
> `--settings` pins the DETECTOR knobs, the harness still loads whichever profile is ACTIVE, and that moves
> `BaselineJ` by 0.0144. **No number here may be compared to a number measured under a different profile** — a
> rule this wave learned by breaking it.
>
> This banner is written by hand for the LAST time: F53(a) shipped here, so `optimize` now prints — and every
> landing now stores — a `BuildId` (the assembly MVID, regenerated on every build), a `DetectorVersion`, a
> `ProfileId`, and a `ConcurrencyCheck`. **A reader diffs a field; nobody diffs a banner.**

## Status of this document

| item | state |
|---|---|
| **RULE G10-A** — the fixed point reproducing against itself | **PASS, 8 of 8 to 6 dp.** See §2.4 |
| **RULE G10-B** — how many landings a ≤ 3e-8 change moves | fired at its top tier (6 of 8) and is **VOID**: its own pre-registered control refuted the attribution. **The wavelet is exonerated; the active NINA profile is the cause.** See §1.2–§1.3 |
| **item 1** — F19's population check | **RULE P FIRES (P2: 30 of 39 = 76.9 %). The wing verdict is WITHDRAWN**, along with the 2× ask, the verdict override and F52(c)'s abort advice. See §2 |
| **item 2** — F55(b), the nondeterminism | **NOT solved.** The build-selects-the-attractor hypothesis is DEAD (15 runs, 3 binaries, identical to 10 dp); the amplifier's GAIN is measured and pinned by a test; the trigger RATE is still owed. See §3 |
| **item 3** — the step recommender | **F49(c) SHIPPED** with an exact ratio; **F21's hypothesis refuted** (its round 0 reproduces, its collapse does not); **the deadband REFUTED before implementation** by the rule fixed to size it. See §4 |
| **F53** — the build stamp | **SHIPPED** (`BuildId` + `DetectorVersion`, joined by `ProfileId` for F57). The field that claimed to identify the build is shown not to. See §3.3 |
| **F49(c)** — what a capped step says | **SHIPPED**, with a ratio that is exact and measured against 22 on-disk rounds. See §4.1 |
| **F55(c)** — concurrency | **SHIPPED as a FIELD.** This wave violated its own rule and needed it. See §1b |
| **F57** — new | **A `--settings`-pinned arm is not pinned: the active NINA profile moves `BaselineJ` by 0.0144.** See §1.3 |
| **the full suite** | **3722 passed, 0 failed** (`develop` was 3708 → **+14**, every one accounted for) |

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

### §1.2 RULE G10-B fired at its top tier — AND IT IS VOID. The cause is not the wavelet.

> **6 of 8 landings moved.** The pre-registered table said: 0 ⇒ inert; 1–3 ⇒ the expected pattern-search
> amplification ([F8](followups.md#f8--optimizer-landings-are-not-reproducible-across-invocations)); **≥ 5 ⇒ the
> "≤ 3e-8" claim describes the per-pixel RESIDUAL and not the detector's BEHAVIOUR.**

That reading was written before the numbers and the numbers came back at its top tier, so it was published: a
detector change bounded at 3e-8 per pixel appeared to move six product landings and one **seed evaluation** — a
single evaluation of the pinned seed, no search, nothing to amplify it — by **0.0144**. Every printed seed input
was identical.

**Then the control that had been pre-registered for exactly this refuted it (§3.1).** Fifteen runs — five each on
wave 9's v1 binary, wave 10's v2 binary, and a **bisect build** (wave 10's tree with the wavelet reverted to the
legacy dense path), interleaved in one session on identical folder copies — all returned **`0.9834767969`,
identical to ten decimal places.**

- **The wavelet is exonerated outright.** v2 and the bisect differ *only* in the wavelet and agree exactly.
- **The binary is exonerated.** v1 and v2 agree exactly.
- **Folder state is exonerated**, one artifact at a time: removing `optimized_settings.json`,
  `hocusfocus_star_detection.json`, `autofocus_report_Region0.json` or `run_meta.json` changed nothing, and the
  frames and `harness_settings.json` predate both waves.

**What was left is the one input nobody compares:**

| | wave 9's gate | wave 10's gate |
|---|---|---|
| `--settings` | `hf_w9\pinned_settings.json` | `hf_w10\pinned_settings.json` — **byte-identical**, `md5 df7c7cd1…` |
| **`Profile:`** | **`Default (b10b1d6d-…)`** | **`astrodet (ce3f3e63-…)`** |

**Re-running `toml999` today under `--profile-id b10b1d6d-…` returns `0.9978404571` — wave 9's value to 6 dp.**

**So G10-B is VOID, not merely uncertain.** It compared two waves that ran under different NINA profiles, and it
therefore measures the profile at least as much as the binary. Nothing in this wave implicates PR #187. Filed as
[F57](followups.md#f57--a---settings-pinned-arm-is-not-pinned-the-active-nina-profile-moves-baselinej-by-0014-and-every-cross-wave-comparison-inherits-it),
rewritten from what it originally claimed.

### §1.3 The finding that replaces it, and it is larger

**`--settings` pins the DETECTOR knobs. This project has treated that as pinning the arm. It does not.** The
harness also loads whichever NINA profile is ACTIVE, and that profile moves `BaselineJ` — the objective of a
fixed seed on fixed frames, with no search anywhere in it — by **0.0144**, which is larger than the entire Δ`J`
any wave has ever argued about.

[F42](followups.md#f42--every-build-directory-silently-gets-its-own-detector-settings-and-the-run-instructions-require-a-new-one-per-arm)
**predicted this in words** — *"`TryLoad("")` picks whichever profile is ACTIVE — two runs of the same data
minutes apart were seeded from different telescopes"* — and the remedy it prompted, pinning the detector
settings, does not close it. **The prediction was right, the remedy was incomplete, and the residue went
unmeasured until a control written for an unrelated hypothesis forced it out.**

Every cross-wave `BaselineJ`/landing comparison in the register is exposed to this. Waves 5–9's controls passed,
which is evidence the active profile happened to be stable across them — **it is not evidence that it was
pinned.** `ProfileId` now goes into `OptimizerProvenance` beside this wave's `BuildId` and `DetectorVersion`:
the same argument, one input further out.

**RULE G10-A is NOT affected and still passes 8 of 8** (§2.4). It compares two runs *within* wave 10, under one
profile — which is exactly why this wave's own measurements remain readable while its cross-wave one does not.

**And the lesson is about the shape of the control, not about profiles.** The three-way probe was designed to
separate the wavelet from the binary. It answered a question nobody had asked, because it was built to *exclude
things* rather than to confirm a favourite. A control that can only confirm would have returned "6 of 8, as
predicted" and this wave would have shipped a false finding about PR #187.

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

## §3 — Item 2: F55(b), the nondeterminism

### §3.1 The cross-build probe, which answered a question nobody asked

Five sequential repeats each of wave 9's **v1** binary, wave 10's **v2**, and a **bisect build** (wave 10's tree
with `StarDetector.cs`'s two wavelet call sites reverted to the legacy dense path), **interleaved in one
session** — blocked runs would have confounded the binary with elapsed time and machine warm-up, which is the
class of confound F55 is made of — each on its own pristine copy of `toml999`, `--max-evals 1` so `BaselineJ` is
the whole measurement.

> **All 15 returned `0.9834767969`. Identical to ten decimal places.**

**Three things follow.**

1. **The BUILD does not select the attractor.** Wave 9's sharpest open clue was five sequential repeats at the
   *other* attractor *"from one build"*, raising the possibility that part of F55 is build-to-build variation.
   Here two binaries a wave apart agree exactly. **That hypothesis is dead**, and F55's mechanism is still open.
2. **The wavelet is exonerated** (§1.2), and with it F57's original claim.
3. **The seed evaluation IS a pure function of (frames, settings, profile).** Under a fixed profile it is
   perfectly reproducible across binaries, folder states and sessions — which sharpens F55 rather than solving
   it: whatever moves under CONCURRENCY is not moving under any of these.

### §3.2 What was NOT run, and why

**The landing-level wavelet bisect was not run.** `exe_v1wav` exists and the command is one line, but it is
8 runs × 2 binaries ≈ 80 minutes, and it was not pre-registered. Running it would have displaced item 3, which
was. **The seed-level answer is already in and is exact**; what remains open is only whether the pattern search
amplifies an identical seed into different landings — which is [F8](followups.md#f8--optimizer-landings-are-not-reproducible-across-invocations),
not a new question. It is written into F57's next step with its exact invocation.

**`KappaSigmaNoiseEstimate` was not probed on the bank.** Design §2.4a names it as the specific bimodal
amplifier: an OpenCV parallel reduction feeding a convergence test at `|Δσ| ≤ 1e-5`, where one flipped comparison
buys an extra iteration and moves σ macroscopically. **The gain is now measured and permanent** — a unit test
asserts that one extra iteration moves σ by ≥ 100× the tolerance that decides whether to take it, so if that ever
stops being true the entry says so. **The trigger RATE is still unmeasured**, and that is what the 40-second
probe would give.

**Eliminated by reading, and recorded because it is the most F55-shaped thing in the path:**
`CvImageUtility.CalculateStatistics` selects its median with a **quickselect whose pivot comes from
`Random.Shared`** — process-level shared state consumed in a thread-interleaving-dependent order, exactly the
signature. It is nevertheless **inert**: quickselect returns the value at rank *n* whatever the pivots were, and
`Mat.GetArray` returns a **copy**, so the in-place permutation never reaches the image.

### §3.3 F53 — the build stamp, shipped

`OptimizerProvenance.ProducerVersion` claimed to identify the build and **cannot**: two builds of one version
share it, which is exactly how wave 8's arm X became irreproducible from its own recorded `exe`. `BuildId` (the
assembly **MVID**, regenerated on every build) and `DetectorVersion` now go into every landing and are printed by
`optimize`. **And `ProfileId` joins them** — F57, the same argument one input further out.

**A retroactive instrument, for the artifacts that predate the stamp:** `strings <dll> | grep AtrousWaveletFast`
distinguishes a v1 build from a v2 build without running it. It is how wave 10 established that all four wave-9
build directories are v1 rather than assuming it.

---

## §4 — Item 3: the step recommender

### §4.1 F49(c) — SHIPPED, and the ratio it quotes is exact

A capped recommendation now states that it is a **partial step**, names what it converges toward
(`3 × HFR_min`), reports the HFR dynamic range **the sweep actually measured**, and quotes the **exact** factor
the next capped run will apply.

**The mechanism is arithmetic, not a mystery.** While the cap binds,
`step′ = (MaxHalfWidthSampledHalfSpanMultiple × P / PointsPerSide) × step` — **1.714× at 4 offset steps, 2.143×
at 4 offset + 1 recovery** — and nothing in it depends on the extrapolated half-width the cap exists to distrust.
That is why the ratio is quotable to a user and **a projected run count is not**.

**Measured against 22 capped rounds already on disk**, at zero compute: wave 7's F18 control arm has 22 capped
rounds across six datasets and every one lands at a realized ratio of **1.667–1.750** (the scatter is integer
rounding of the step). The field session behind F49/F51 ran **100 → 214 → 459** at P = 5: 100 × 2.143 = 214.3,
214 × 2.143 = 458.6. The entry derives 459 independently from the screenshot's focuser axis, so those are two
routes to the same number.

**And the sequence terminates**, which is the half of the user's complaint that is not a defect: the widening is
geometric until the sweep contains the band, and their runs 3 and 4 asked +3 % and +5 %. A unit test replays the
session and asserts the cap releases.

### §4.2 F21 — round 0 reproduces EXACTLY; the collapse does not

F21's own reproduce line, run on this binary: `D17_cdk14_oiii5`, S0, `--max-rounds 2`.

| | F21 as recorded | wave 10 |
|---|---|---|
| round 0 `HalfWidth` | 143.6 | **143.583** |
| round 0 step | 41 | **41** |
| round 1 | `HalfWidth` **12.1**, step **3** | **did not occur — converged in 1 round** |

**The instrument is right and the pathology did not recur** — the same shape as F25, whose 4×→7× over-reach also
failed to reproduce on re-measurement. So F21's headline remains a real observation of a real run and **not** a
reproducible case.

**Its stated hypothesis is not supported.** F21 blames `FindHalfWidth`'s coarse walk terminating early. But
`FindHalfWidth` is exact for a hyperbola — it returns `√8 · HFR_min / κ` — so its output is **proportional to the
fitted vertex HFR**. Wave 7's control arm, on disk, shows exactly that: over uncapped rounds `halfWidth / vertexY`
holds to **×1.03–×1.14** within a dataset while `halfWidth` itself spans up to **×1.69**, and the ratio differs
strongly *between* datasets as `√8/κ` should. **The walk is not what moves; the FIT's vertex is** — and
`R² = 1.0000` is no evidence against that, because R² measures fit to the sampled points and says nothing about
whether the vertex is identifiable from them. *Confirmed on six datasets, but not on F21's own, because F21's own
case would not reproduce.*

### §4.3 The deadband — REFUTED before implementation, by the rule fixed to size it

Design §3.3(B) fixed the rule in advance: *"a deadband narrower than the recommender's own reproducibility is a
deadband that does nothing"*, so the threshold must sit **at or above the measured run-to-run spread**.

**The rule has no valid input, and that is the answer.** Measured:

- **With a fixed seed the recommender is BIT-REPRODUCIBLE** — all six S0 half-widths are identical to full double
  precision between wave 7's v1 binary and wave 10's v2 (130.132 / 129.313 / 399.122 / 396.872 / 168.835 /
  73.358). Spread **zero**, so a rule-compliant deadband is zero and does nothing.
- **The variability F21 describes comes from a different noise realization**, and its only recorded instance did
  not reproduce (§4.2).
- **And the asks it would have to suppress are large and possibly correct.** On S0 — where the bootstrap IS each
  dataset's expected optimum — the recommender asks for a median **19.9 %** change (max 40 %). A deadband wide
  enough to cover that would suppress **4 of 6** of them, which is RULE S's **S2** clause (*"the A3 final-step
  assertion must pass on at least as many cells"*) failing by construction. Whether the recommender or the bank
  is right about those 20 % gaps is F18's open question, and a deadband would bury it.

**So RULE S was never applied: the mechanism it was written to accept was refuted before implementation.** That
is the same shape as wave 9's §3.4 — the cheap refutation arriving before the expensive confirmation, including
of one's own plan — and it cost one measurement on data already on disk.

**Explicitly NOT closed:** F25's fit-quality gate, F26's deferral bound, and F18's `W_detect` default all stay
open and untouched. F18/F21/F25/F26/F49(c)/F51 are one component read six ways, and a shipped copy change is not
four entries closed.

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

**1. Design the control to EXCLUDE, not to confirm — and this wave's headline is what that bought.** The gate
came back at RULE G10-B's top tier (6 of 8 landings moved), with a seed evaluation moving 0.0144 on inputs that
printed identical. That was written up as "a ≤ 3e-8 detector change moves product answers", and it was **wrong**.
The pre-registered three-way probe — v1, v2, and a wavelet bisect, interleaved — returned fifteen identical
values and killed it, and one-artifact-at-a-time folder elimination killed the alternatives, leaving the one
input nobody compares: **the active NINA profile**. A control built to confirm would have returned "6 of 8, as
predicted" and this wave would have shipped a false finding about PR #187.

**1b. A rule can be right and its remedy still incomplete.** F42 wrote down *"`TryLoad("")` picks whichever
profile is ACTIVE"* and prompted `--settings` pinning. That pins the DETECTOR knobs and leaves the profile
floating, and the residue moves `BaselineJ` — a fixed seed on fixed frames — by more than any Δ`J` this project
has argued about. **A prediction is not a measurement, and a partial remedy reads exactly like a complete one
until something forces the difference out.**

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
