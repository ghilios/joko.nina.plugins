# Synthetic AF bank — followups wave 9 (results)

Design: [`docs/synthetic-af-bank-followups-wave9-design.md`](synthetic-af-bank-followups-wave9-design.md).
Plan: [`plans/synthetic-af-bank-followups-wave9-plan.md`](../plans/synthetic-af-bank-followups-wave9-plan.md).
Wave 8: [`docs/synthetic-af-bank-followups-wave8-results.md`](synthetic-af-bank-followups-wave8-results.md).
Register: [`docs/followups.md`](followups.md).

*The wave opened by contradicting its predecessor. Wave 8's §0.1 deferred [F32](followups.md#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains)'s
confirmation arm a fourth time on the ground that "wave 9 does not end it either" — a prediction that
[F19](followups.md#f19--the-exposure-recommendation-is-decided-by-the-20-brightest-stars-so-a-rich-field-can-never-earn-one)(b)'s
own reporting refuted in the same wave, by printing a saturated set that includes F32's entire synthetic half.*

## Status of this document

| item | state |
|---|---|
| **the wave-8 CI gate** | **CHECKED. The absent check resolved itself** — #184's CI ran and passed 3674/0/0 ~51 s before the merge. Actions is in `major_outage` again NOW. See §0 |
| **RULE G** — the comparability gate | **PASS, 8 of 8 to 6 dp.** The wave-8 binning-default trap is verified inert rather than argued inert. See §1.1 |
| **F32's confirmation arm** | **RUNNING** at the time of writing (~5 h, 3 arms × 39 runs, fan-out 3). Scored by `D:\hf_w9\score_f32.py` against R1/R2/R3. See §1 |
| **F19(c)** — floor and ceiling | **RESOLVED, and it was FREE. The floor stays, the ceiling stays, NOTHING re-renders.** And it produced a rank correlation the entry never had. See §2 |
| **F19's remainder** — the wing statistic | See §3. The pre-registered candidate family was **refuted before implementation, by a proof plus data already on disk** |
| **F49** — the empty remedy | **SHIPPED.** See §5 |
| **F51** — the hidden re-run button | **SHIPPED, and (a) had TWO causes rather than the one the entry names.** See §5 |
| **F53** — new | **Wave 8's arm X does not reproduce from wave 8's own `exe`.** See §4 |
| the re-render | **NONE** |

---

## §0 — The wave-8 CI gate, settled before any compute

Wave 8's results doc closed with *"no checks … CI must be re-checked once Actions recovers, before this merges."*
PR #184 merged anyway, so the first act of this wave was to find out whether that re-check ever happened.

| check | measured |
|---|---|
| PR #184 | **MERGED** 2026-08-06T23:33:27Z → `018eaa0` |
| `gh pr checks 184` | **1 passed** — "Run unit tests" on head `57e759e` |
| the run | `31130194942`, 23:13:05Z → **23:32:36Z**, i.e. ~51 s before the merge |
| **the test COUNT** (F37's rule) | **`Passed: 3674, Failed: 0, Skipped: 0`** — the whole suite, not a truncated host |
| the three RED runs on the branch / `develop` | job-level **`cancelled`**, zero test output |
| `develop` @ `018eaa0` | **0 checks** |
| githubstatus.com **at wave-9 start** | indicator `major`; **`Actions: major_outage` STILL** |

**The absent check resolved itself, and that is verifiable rather than assumed.** Actions recovered long enough to
run the check on the final head SHA; it passed with the full count; the merge followed a minute later. The red runs
are F37's *other* failure mode — a red check that is not about the code — and they are `cancelled` with no test
output at all, which is what an outage looks like and is not what the native host crash looks like (that one
reports zero failures with a *reduced* count).

**What it changes for this wave: Actions is degraded NOW.** `develop`'s own merge commit has no check at all, so
the dangerous mode is live rather than historical. This wave's PR is gated on a LOCAL full-suite run with its count
recorded, and on re-reading the status page at PR time.

---

## §1 — F32's confirmation arm

### §1.1 RULE G — the comparability gate, and the trap wave 8 created

Wave 8 flipped `optimize --per-run`'s **default** to apply each run's derived detection binning. For F32's
8-run comparability subset that *should* be a no-op, for a structural reason — but this project does not run arms
on arguments, so it was measured first, sequentially, on the arm's exact invocation.

> **RULE G, fixed before the gate ran.** All 8 must reproduce wave 5's feature-OFF arm to **6 decimal places**. Any
> miss ⇒ STOP: the comparison to wave 5's φ table is void and nothing in the arm is readable against it.

| run | expected | measured | |
|---|---|---|---|
| `toml999` | 0.997993 | **0.997993** | OK |
| `CWhiteFocus` | 0.998476 | **0.998476** | OK |
| `uneven` | 0.996368 | **0.996368** | OK |
| `muggsie` | 0.997195 | **0.997195** | OK |
| `mccomiskey` | 0.994025 | **0.994025** | OK |
| `D18_m24_deep_shed` | 0.999822 | **0.999822** | OK |
| `D19_cygnus_deep_shed` | 0.999557 | **0.999557** | OK |
| `D20_m24_bright_control` | 0.999766 | **0.999766** | OK |

**PASS, 8 of 8.** And both fallback paths were read rather than assumed — the log states its source per run:

- the 5 real runs: `1 from harness_settings.json (may be kept-from-base — check DetectionBinningSource): Bin1`;
- `D18`/`D19`/`D20`: `1 from synthetic_meta.json expectedOptimal.detectionBinning (physics-derived)`.

**The other 17 synthetic datasets are NOT comparable to any prior wave** and are not tabled as if they were: they
carry `detectionBinning` 2 or have never run at their own factor, so wave 8's default flip changes what they
detect at. Their arm-A numbers are a new baseline.

### §1.2 The arms, and a scope reduction that was withdrawn once it was priced

Three arms — **A** shipped default, **B** `--keep-floor 0.50`, **C** `--continue-rounds 2` — over **both full
banks** (20 synthetic + 19 real; `astrodet` excluded per [F14](followups.md#f14--astrodet-is-frameless)), at
`--max-evals 250` with `--settings` pinned to the file that is **byte-identical to waves 5/6/7/8's**
(`md5 df7c7cd1…`), which is what makes RULE G legal.

The third arm exists because wave 6 measured the two mechanisms as **complementary**: restarts alone recover only
0–36 % (median 13.8 %) of the floor's gain where the floor helps, and the floor's two FAILURES are exactly where
restarts do best.

**The first draft narrowed arm C to the binding set, and that reduction was withdrawn.** `--continue-rounds 2` is
~2.4× the per-run cost, so 39 runs sequentially is ~8 h — but at fan-out 3 it is ~2.6 h, inside F32's own ~6 h
estimate for the whole arm. The narrowing bought nothing except a definitional argument about "binding" before arm
A had run.

**Order C → B → A, with A LAST** ([F15](followups.md#f15--optimize---per-run-overwrites-each-runs-stored-settings)):
`optimize --per-run` rewrites `optimized_settings.json` into the bank's run folders, so the last arm owns both
banks. **The banks end holding the SHIPPED-DEFAULT landing.** If R1 adopts φ = 0.50 as a default, arm B is re-run
alone before the PR — recorded here so the obligation cannot be lost after the fact.

**The fan-out's control is free:** arm A re-measures the same 8 runs the gate measured sequentially. A
disagreement is a concurrency artifact, not a result. An early independent signal is already in: §4's control ran
with three `optimize` processes in flight and matched a different binary to six decimal places.

*(Results to follow when the arm completes; scored by `D:\hf_w9\score_f32.py` against R1/R2/R3.)*

---

## §2 — F19(c): the free check, and it decided the expensive step was unnecessary

F19(c) is *"re-check `MinExposureSeconds = 0.5 s"*, filed as expensive because moving the clamp re-derives and
re-renders 11 datasets. **The check that decides whether to spend that is free, and it ran first.**

> **RULE F19c, fixed before it ran.** (c) resolves as *"the floor stays"* unless a floor-clamped dataset can be
> shown, **from data already on disk**, to have its σ_focus minimum BELOW 0.5 s.

**The floor stays.** Every one of the 11 clamped datasets asks for **less** than 0.5 s, so a lower floor moves all
eleven **down** — while the one dataset with a measured exposure ladder moves the other way: `D02` improves **44 %
of σ_focus at 8 s**, four orders of magnitude above its 0.001 s ask. Lowering the floor makes `D02` worse; raising
it is not what a floor is for. **The floor is the symptom; the statistic is the defect.**

### And the free check produced a number the entry never had

`synth-bank --dry-run` on the wave-9 binary reproduces the saturated set exactly (**13 of 20**, 11 floor + 2
ceiling) — and printing the on-frame star count the 20th-brightest is drawn from turns F19's central *argument*
into a *measurement*:

| dataset | on-frame stars | raw ask | clamp |
|---|---|---|---|
| `D10_rc16_3250mm_sparse` | **26** | **335.6 s** | ceiling |
| `D13_apo200_1800mm` | 177 | 0.264 s | floor |
| `D07_rc10_2000mm` | 428 | 0.066 s | floor |
| `D14_cdk14_2563mm_e47` | 640 | 0.055 s | floor |
| **`D17_cdk14_oiii5`** | **799** | **40.4 s** | **ceiling** |
| `D03` / `D20` / `D05` / `D02` | 969 / 1151 / 2346 / 3590 | 0.003 / 0.004 / 0.014 / **0.001** s | floor |
| `D19` / `D04` / `D01` / `D18` | 4914 / 7480 / 19201 / **27084** | 0.008 / 0.004 / 0.003 / 0.004 s | floor |

**Spearman ρ(on-frame star count, raw ask) = −0.68 over the 13.** The two CEILING datasets are the two
sparsest-or-faintest; all eleven FLOOR datasets are the rich ones. *"Sized by field richness rather than by whether
the stars the fit depends on are above the noise"* stops being an argument and becomes a rank correlation over the
whole bank, **in both directions at once**.

**The one exception is the honest one, and it is F19's own poster child.** `D17_cdk14_oiii5` carries 799 on-frame
stars — more than four of the floor-clamped datasets — and still asks 40.4 s, because an OIII filter genuinely
starves it. Richness is the dominant term, not the whole story, and the shipped statistic cannot tell the two
apart. That is the wing problem, not a clamp problem.

### The ceiling, which had never been examined

- **Product: the 30 s cap STAYS. Verified, not changed.** `D10`'s 335.6 s over a 9-point sweep is ~50 minutes of
  pure integration and `AutoFocusEngineOptions.AutoFocusTimeout` would kill the run.
  `StarSignalCopy.DescribeExposureDerivation` already names *which* bound bound it (F19(b) verified that).
- **Bank fidelity: `D10` and `D17` are RENDERED at 30 s while their own physics asks 335.6 / 40.4 s.** Two of the
  twenty datasets are deliberately photon-starved relative to their derivation and no write-up had said so.
  **Recorded, not re-rendered** — a `D10` at 335 s would be a dataset no user could capture.
- **Rule CEIL** deferred it with a **stated price**, which is the thing wave 8's §0.1 got wrong by deferring on a
  prediction instead.

---

## §4 — F53: wave 8's arm X does not reproduce from wave 8's own `exe`

Found while re-running arm X's exact command to build this wave's wing instrument.

| | `D02_rich_135mm` @ 0.5 s, `--max-evals 120`, same pinned settings |
|---|---|
| wave 8's arm X log | `bestJ = 0.996328`, effective gate **16.667** |
| `D:\hf_w8\exe` **today** | `bestJ = 0.996486`, effective gate **33.333** |
| wave 9's `exe2` | `bestJ = 0.996486` — identical to the line above |

**The tell is unambiguous:** wave 8's arm X log contains **no `detection binning (F39b)` line at all**, and that
line is unconditional once wave 8's own default flip landed. So arm X ran against a build of `D:\hf_w8\exe` that
**predates the flip**, and a later step in the same wave rebuilt the directory. The artifact keeps only the last
build.

**Three things it is not**, each excluded by measurement: **not concurrency** (the control ran with three
`optimize` processes in flight and matched a different binary to 6 dp); **not this wave's code** (two binaries, one
predating every line of wave 9, agree); **not `ApplyFactor`**, which is the identity at factor 1 when
`DetectionBinning` is already 1 — which is also why wave 8's own adoption control found the 13 binning-1 datasets
bit-identical.

**It does not overturn wave 8's F19 verdict.** Both landings put the EFFECTIVE gate (16.667 and 33.333) far above
`TargetSensitivity = 10`, and every accepted star's gate statistic strictly exceeds that gate, so
`ExposureIsNotTheLimit` is true **by construction** in either. R5 fired for a structural reason, not a numerical
one. What is void is the reproducibility of the table, not its conclusion.

[F41](followups.md#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary) says a prior wave's
control arm is not a control for a later wave's binary. **This is the sharper case: it is not a control for its OWN
recorded binary either**, when the arm directory is a build output a later step overwrites.

---

## §5 — F49 and F51, the two field defects

### F49 — the block now ends on an instruction, and the branch is narrower than the entry proposed

`StarSignalCopy.RemedyFor` had three ranked branches, **all** exposure or binning remedies. On a rich,
well-exposed field where the optimizer *chose* a floor gate, `ExposureIsNotTheLimit` is true ⇒ `IncreasesExposure`
is false (killing 1 and 3) and branch 2's `!ExposureIsNotTheLimit` is false ⇒ **every branch fell through and it
returned `string.Empty`**, on a page whose contract is "diagnosis plus exactly one instruction".

A fourth, last-ranked branch names the **gate**, and only controls that **exist** — `BrightnessSensitivity` is
bound in `OptionsDataTemplates.xaml`; `MinDetectionKeepFraction` has **no XAML binding anywhere** and is
deliberately not named (and would not bind here regardless: it rejects landings keeping *fewer* stars than the
seed, and this one keeps ~7× **more**).

**It REQUIRES a measurement, which the entry's proposed wording did not.** The sentence CLAIMS brightness was not
what was missing, which is knowable only from `ExposureIsNotTheLimit`. On an unmeasured run all that is known is
that the gate is floored — asserting it there breaks the entry's own *"no star-poor claim without evidence"* rule
from the opposite direction. **Two existing golden tests caught exactly that on the first attempt**, which is the
best argument for whole-string goldens in this fixture.

The sharpest test here is one that already existed: `ExposureCopy_ExposureIsNotTheLimit_…` asserted a whole body
string that **ended after the admission sentence**. The defect was written down as an expectation and had passed
ever since.

### F51 — and (a) had TWO causes, not the one the entry names

The entry blames `IncreasesExposure`. The property **also** required `HasExposureBlock`, which is
`HasLowStarSignal` — Sensitivity ≤ 1.0. So the button was hidden by the exposure condition on run 2 **and by the
block's own condition on runs 1, 3 and 4**, whose gates were healthy and which recommended 214, 474 and 482 with
no way to act on any of them. The entry's own table records those three runs; nothing in it had noticed the second
gate.

| part | shipped |
|---|---|
| **(a)** | `lastRunWasLive && !IsUseCurrentMode && (IncreasesExposure ‖ StepSizeOrOffsetChanged)`. `HasExposureBlock` is **gone**, and the row **moved out of the Star signal block** into the Auto-focus block, beside the values it now carries |
| **(b)** | `ApplyRecaptureGeometry` carries the recommended step size and offset into the re-capture, applied **before** `ApplyFocusRecovery` so recovery widens from the recommended offset; cleared on **every** exit path so an ordinary Live Start is byte-identical |
| **(b), "or say plainly"** | shipped **as well as** carrying it — `CaptureNewSweepCarriesText` states the geometry the next sweep will use. A sweep geometry the user cannot see is how this stayed invisible for a whole session |
| **(c)** | already true and now **reachable**; what forced the two unwanted Accepts was (a) hiding the control |

**The house rule survives in both directions.** `StarSignalCopy`'s Live sentence naming the button fires on a
strict subset of the new visibility, so copy can never name a hidden button; and in the new step-only state, where
the Star signal block is not on screen at all, the row carries its own sentence.

---

## Lessons

**1. A deferral priced on a PREDICTION is not priced.** Wave 8's §0.1 deferred F32 because it forecast that F19(c)
would re-render the bank ahead of it. F19(c)'s check cost minutes and says nothing re-renders — so the deferral
bought nothing and cost a wave. *Price the deferral by running the cheap check, not by predicting its result.*

**2. The cheap refutation arrives before the expensive confirmation — including refutations of your own plan.**
This wave's §3.4 candidate family was written down, then killed by a proof about the gate plus numbers already on
disk, before a line of it was implemented. The acceptance rule (§3.2) was fixed first and did not have to move,
which is the whole reason the family could be replaced without the exercise becoming fishing.

**3. "Reproduce:" names a COMMAND, not a result.** F53: a wave's own artifact directory is a build output, and a
later step in the same wave can overwrite it. Every `D:\hf_w*\exe` reproduce line in `docs/` inherits this.

**4. Print the population beside the statistic.** F19(c)'s rank correlation cost one extra column in an existing
dry-run. The entry had ARGUED "sized by field richness" since 2026-08-02; one column measured it, in both
directions, across the whole bank.
