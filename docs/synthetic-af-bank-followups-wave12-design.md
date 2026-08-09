# Synthetic AF bank — followups wave 12 (design)

Wave 11: [`docs/synthetic-af-bank-followups-wave11-results.md`](synthetic-af-bank-followups-wave11-results.md).
Register: [`docs/followups.md`](followups.md).
Plan: [`plans/synthetic-af-bank-followups-wave12-plan.md`](../plans/synthetic-af-bank-followups-wave12-plan.md).

## §0 — What wave 11 settled, and is not re-litigated here

Stated once so no arm re-derives it, and so a reader can tell what this wave is allowed to assume:

- **[F58](followups.md#f58--concurrent-optimize-processes-each-acquire-a-different-nina-profile-and-the-profile-decides-the-fit-f55s-two-attractors-are-two-values-of-maxoutlierrejections):
  F55 and F57 are ONE defect, and it is profile acquisition.** `Profile.Load` holds the `.profile` open
  (`FileShare.Read`); a second process's load throws; `SelectProfile` returns `false`; and `TryLoad`'s
  `SkipWhile` **silently takes the next profile by `LastUsed`**. So *N* concurrent unpinned `optimize` processes
  ran under *N* different profiles, the fit reads four values from the profile, and this machine's **nine
  profiles partition 2 / 7 on `MaxOutlierRejections`**. That one integer is F55's "bimodality".
- **Proven by intervention, not by correlation.** RULE C's C2 gave `MaxOutlierRejections = 1` alone `== J_def`
  with residue **exactly `0.0`**, and its negative control C3 (`OutlierRejectionConfidence` alone) moved nothing.
- **`KappaSigmaNoiseEstimate` is WITHDRAWN as the mechanism.** Its measured gain stays true; it was never
  evidence of causation.
- **A PINNED run rewrites the default for the next UNPINNED one** (`Profile.Load` stamps `LastUsed` and saves).
- **`--profile-id` + fan-out FAILS LOUDLY** (1 success, 4 hard failures). That is the right trade and is **not**
  a fix.
- **Wave 5's φ table is invalid on THREE axes** — detector version, profile, and the fit inputs. It is not
  quoted in this wave.

## §1 — The first gate: RULE G12

Wave 11's eight values are the fixed point, and they are now bit-reproducible on **both** the pre-fix and
post-fix binaries — K8 reproduced them **identically to all sixteen digits** across a different binary and a
different settings file. So this wave's coordinate system is not a hope; it is a measured invariant, and the only
thing G12 adds is a **third binary**.

| run | `BestJ` (6 dp) |
|---|---|
| `toml999` | **0.995784** |
| `CWhiteFocus` | **0.996068** |
| `uneven` | **0.996368** |
| `muggsie` | **0.997195** |
| `mccomiskey` | **0.976746** |
| `D18_m24_deep_shed` | **0.999882** |
| `D19_cygnus_deep_shed` | **0.999487** |
| `D20_m24_bright_control` | **0.999738** |

**The invocation.** `D:\hf_w11\pop\k8_gate_on_fixed.sh`'s shape, on a FRESH build from `develop` after PR #189
(`96f10b3`), `optimize --per-run --max-evals 250`, **sequential**, with BOTH of

- `--settings D:\hf_w11\pinned_settings_w11.json` (md5 `a67ffc06…`), and
- `--profile-id ce3f3e63-8fd3-4b72-a0ca-d90db9441382` (`astrodet`)

**named in the script header as first-class inputs.** (F57(b): an arm pinned in one dimension and floating in
another is not pinned. F42: the settings file is the other half.)

> **RULE G12 — ONE pass/fail clause.** All eight `BestJ` must reproduce the table above **to 6 dp**. *A partial
> reproduction is a failure, not a warning: the eight are one instrument.* Bit-identity to sixteen digits is
> **reported** as the stronger observation K8 earned, but 6 dp is the clause.

**The free controls are FIELDS now, so they are diffed rather than re-derived** (F53(a), F57(a), F58):

| field | required reading | why |
|---|---|---|
| `BuildId` | **MUST DIFFER** from wave 11's `5cb7e474…` | a match means no build happened |
| `DetectorVersion` | `2` ×8 | the output contract the eight values were measured under |
| `ProfileId` | `astrodet (ce3f3e63-…)` ×8 | the pin took |
| `FitInputs` | `MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid` ×8 | the fix is live and reading the FILE |
| `ConcurrencyCheck` | `exclusive` **across all eight** | and see the trap below |
| `BaselineJ` | wave 11's eight values | F41's free control; *a number agreeing too well is also an instrument failure* |

> **TRAP, and it is this wave's most load-bearing one.** `ConcurrencyCheck == "exclusive"` **on a single landing
> is not evidence the machine was quiet.** `WaitOne(0)` is won by exactly ONE of *N* contenders, so in any
> fan-out **precisely one landing truthfully reports `exclusive`**. It must be read **across a whole arm**. In
> wave 10's two-driver contamination the first driver's landings would ALL have read `exclusive`. This matters
> most for item 1, which fans out on purpose — there the field is not a hygiene check, it is a **positive
> control that the fan-out actually overlapped**.

---

## §2 — Item 1: F55 at the LANDING level. Authorise fan-out, or do not.

**This is the only item that pays for itself, and it runs first for that reason.**

### §2.1 What is and is not already known

Wave 11's K5 proved agreement **at the SEED level only**: `--max-evals 1`, five concurrent processes on five
different profiles, one `BaselineJ`. F55's **LANDING** rate was **44 %** — *triple* its seed rate — and many runs
had a bit-identical `BaselineJ` and a different landing (`LinwoodFocus` 0.952882 vs 0.917207, a gap of 0.036).
**Nothing has measured landings under concurrency with the fix in place.** Until that is measured every arm is
sequential and a 39-run pass costs ~3 h.

### §2.2 The experiment is wave 9's arm A, which is what caught F55 in the first place

Run **the eight RULE G12 runs at fan-out 4** and require the same eight values. Identical invocation to the gate
except for the concurrency and the profile pinning.

**The hazard must survive the test.** `--profile-id` + fan-out fails loudly (wave 11, K3), so the workers run
**UNPINNED** — which is now safe *precisely because of the fix*: the detector comes from `--settings` and, since
wave 11, so do the four fit inputs. **The unpinned fan-out is therefore not a shortcut; it is the experiment.**
Each worker acquires whichever profile the race gives it, exactly as F58 describes, and the claim under test is
that this no longer reaches `J`.

*(The fallback, if the race happens not to spread: give each worker its OWN profile copy — fresh GUID, identical
content — with `D:\hf_w11\bisect\make_bisect_profile.py`, which already does this and refuses if the source
profile's shape is not what it expects. It is a fallback and not the plan, because it perturbs the machine state
this arm is trying to observe.)*

### §2.3 RULE A12 — pre-registered, and the FAILURE is what is pre-registered

| clause | requirement |
|---|---|
| **A1** *(the pass/fail clause)* | all eight `BestJ` reproduce §1's table to **6 dp** |
| **A2** *(discriminating — without it A1 means nothing)* | the eight landings record **≥ 2 distinct `ProfileId`s**, spanning **both** `MaxOutlierRejections` groups of the re-snapshotted profile set |
| **A3** *(the fan-out actually overlapped)* | `ConcurrencyCheck` reads `concurrent` on **at least one landing per batch**; all-`exclusive` means the workers did not overlap and the arm did not run |
| **A4** *(the crispest statement of the fix)* | `FitInputs` is **IDENTICAL on all eight** despite A2's differing `ProfileId`s |

> **The outcomes, written before the arm runs:**
>
> - **A1 ∧ A2 ∧ A3 ∧ A4 ⇒ FAN-OUT IS AUTHORISED AT DEGREE 4**, and *the authorisation names the degree*. It says
>   nothing about degree 8 or 48.
> - **ANY miss on A1 ⇒ still sequential**, and the results must say **which run and by how much**.
> - **A2 or A3 unsatisfied ⇒ NO AUTHORISATION REGARDLESS OF AGREEMENT.** If the workers did not load different
>   profiles, or did not overlap, the test did not exercise the hazard and its agreement is worth nothing. *That
>   is K5's discriminating clause, and it is the entire reason K5 means anything.*

**Power.** At F55's measured 44 % per-run landing rate, `P(8 of 8 reproduce | the defect is still live)` is
`0.56^8 ≈ 0.0097`. So 8/8 is a ~99 %-power test of the landing-level claim, which is why eight runs suffice and
a 39-run pass is not needed to authorise.

### §2.4 The machine state moves, so it is re-snapshotted first

`D:\hf_w11\profiles_before.txt` is the **wave-11** snapshot (9 profiles, 2 / 7 on `MaxOutlierRejections`). The
fall-through set is the **top N by `LastUsed`**, and **every pinned run reorders that** — including this wave's
own gate, which pins `astrodet` eight times immediately before. So the set is re-snapshotted to
`D:\hf_w12\profiles_before_w12.txt` **after the gate and before the fan-out**, and A2 is evaluated against that
snapshot rather than against wave 11's.

### §2.5 What this buys, and what it does not

A pass makes every subsequent arm in this and future waves ~4× cheaper. It also **answers the deferred wavelet
bisect for free in one direction**: an arm that reproduces 8/8 under a deliberately perturbed process environment
tells you how much the pattern search amplifies an arbitrary perturbation — which is exactly what
[F57](followups.md#f57--a---settings-pinned-arm-is-not-pinned-the-active-nina-profile-moves-baselinej-by-0014-and-every-cross-wave-comparison-inherits-it)(d)
and [F8](followups.md#f8--optimizer-landings-are-not-reproducible-across-invocations) want to know. It does
**not** close F8: F8 is about landings differing across *invocations* generally, and this measures one degree of
one hazard.

---

## §3 — Item 2: the four harness runners that still have F58's defect (F58(d))

Flagged in wave 11 and deliberately not converted, because each needs its own validation and wave 11's budget
went to `optimize`:

| site | what it feeds |
|---|---|
| `BankVerifyRunner.cs:160` | the AF fit in the recall/precision harness — **and its numbers underpin the golden audits** |
| `BankVerifyRunner.cs:518` | the `SensorModel` (tilt) fit |
| `SynthValidateRunner.cs:243` | `synth-validate`'s convergence scoring |
| `InspectAlignRunner.cs:103` | `inspect-align` |
| `TiltCalibrationRunner.cs:178` | `tilt` calibration |

Every one builds `new AutoFocusOptions(profileService)` and therefore takes its fit from **whichever profile is
ACTIVE** — while building `StarDetectionOptions` on the harness accessor two lines above. **That asymmetry is the
bug**, and it is the same asymmetry F58 named in `optimize`.

**`HocusFocusPlugin.cs:119` also constructs it from the profile and is CORRECT.** In the live app those ARE the
user's settings. It is listed here so nobody "fixes" it.

### §3.1 The change is small because the seam already exists

`optimize`'s conversion is one line — `new AutoFocusOptions(profileService, accessor)` — plus carrying the keys
in the settings file, and **`HarnessSettingsStore.ExportFromProfile` already copies the whole `AutoFocusOptions`
surface** (wave 11), so a file exported today already has them and a file that lacks them falls back to
documented code defaults. `HarnessFitInputs` and the `FitInputs` provenance string are already written.

So each runner gets: the accessor, and **`FitInputs` rendered into its own output** — the console banner it
already prints, and the report it already writes where it writes one. *A reader diffs a field; nobody diffs a
banner* — but a runner with no provenance record at all gets the banner, because that is strictly better than
nothing and does not require inventing a record format per runner.

### §3.2 `bank-verify` gets its own before/after, because its numbers underpin the golden work

The other three runners are validated by the test suite plus the fact that the conversion is textually identical
to `optimize`'s. **`bank-verify` is not**, because the golden audits quote its recall/precision.

> **RULE B12, pre-registered.** Run `bank-verify` on the golden-scored real-bank runs **before and after** the
> conversion, both **pinned** to `astrodet` and to `pinned_settings_w11.json` — whose four fit inputs are
> `astrodet`'s effective values. Under that pairing the conversion is **behaviour-preserving by construction**,
> so:
>
> - **every scored quantity identical ⇒ PASS**, and the conversion is safe to ship;
> - **any difference ⇒ FAIL**, and it is a finding: either the fit inputs do not round-trip through the file, or
>   `bank-verify` reads something from the profile that nobody has audited. The difference is then documented as
>   a discontinuity at this commit rather than discovered later by someone comparing across it.
>
> This is K8's shape applied to `bank-verify`: *the fix must not move the coordinate system it is fixing.*

**And the reason the before/after must be PINNED is F58 itself.** Two `bank-verify` results measured under
different active profiles are not comparable — which is exactly what this item is fixing, and would be exactly
how it could be measured wrong.

---

## §4 — Item 3: F19's remainder — and the CLOSE is pre-registered as an outcome

### §4.1 Two wing statistics are already refuted

| statistic | refuted by | how |
|---|---|---|
| `WingRejectedFraction` (absolute) | wave 10, **RULE P** | **30 of 39 = 76.9 %** fire rate; the threshold's whole discriminating power over 39 runs was ONE dataset |
| `WingRejectedRatio` (wing ÷ inner) | wave 11, **RULE W2** | `D16`@2 s has an inner rejected fraction of **EXACTLY 0.000**, so the ratio is **+∞** and it fires at the rung where `D16`'s σ_focus is **MINIMISED**. W1 ∧ W5 leave `(1.39, 1.5683]`; W2 empties it |

`WingRejectedExcess = wing − inner` was **named** in wave 11's design and **deliberately not evaluated
anywhere**, because by then the exposure ladder was the data that had refuted the ratio, and **W6 applies to the
ladder exactly as it applied to the 39-run population**. *A statistic tuned on the data that killed the last one
has been fitted, not tested* — this register has now had to write that sentence three times, so this wave gives
the excess **its own arm on data it has not seen**.

### §4.2 The arm: a FRESH exposure ladder, on datasets the wing statistics have never laddered

Both prior refutations happened on data that is now barred:

- the **39-run population** (killed the fraction), and
- **wave 7's `D02` / `D16` exposure ladder** (killed the ratio).

So the arm renders a **new** 5-rung ladder on **three** datasets, with `synth-bank --spec` +
`exposureSecondsOverride`, exactly as wave 7's arm E did (~1 min per rung for two datasets — the ladder is
cheap; it is the *thinking* about it that has been expensive):

| dataset | why it is on the ladder | expected role |
|---|---|---|
| `D17_cdk14_oiii5` | the register's own poster child: **799 on-frame stars and still asks 40.4 s**, because an OIII filter genuinely starves it. Richness is not the whole story and this is the dataset that proves it | the **W1** anchor — more exposure must demonstrably help |
| `D20_m24_bright_control` | a **known instance of the hazard class**: in the 39-run population its inner rejected fraction is **exactly 0.000**. That is `D16`@2 s's shape on a dataset the ratio's ladder never saw | the **W2** anchor — silent at its own σ_focus minimum |
| `D05_tec140_1000mm` | mid-population on every axis (wing fraction 0.600, σ_focus 0.07688); it is on the ladder so the verdict does not rest on two extremes | **W3 / W5** breadth |

Rungs **0.5 / 2 / 8 / 30 / 120 s**, geometric ×4, chosen to bracket both derived exposures (`D20` asks 0.004 s
and is floor-clamped; `D17` asks 40.4 s and is ceiling-clamped at 30 s). `--max-evals 120`, matching wave 9's and
wave 11's ladder invocations exactly, so the ladders differ in what is under test and not in the search budget.

### §4.3 RULE W12, fixed before the ladder runs

**W1–W4 are unchanged in FORM**; their anchors are re-derived from **this ladder's own σ_focus**, which is the
same move wave 9 made when the ladder moved between binaries
([F54](followups.md#f54--f39bs-default-flip-moves-a-landing-at-a-resolved-factor-of-1-where-it-is-documented-as-a-no-op)).
σ_focus is the ground truth — *what the user actually gets* — and the statistic is the predictor being scored
against it.

| clause | requirement |
|---|---|
| **W0** *(validity, evaluated first)* | the ladder must be **informative**: each dataset must have a σ_focus minimum and at least one rung **≥ 20 % worse** than it. A dataset that fails W0 cannot anchor W1/W2 and is reported as void rather than scored |
| **W1** *(fires where starvation is worst)* | at each dataset's **most-starved rung** — the worst-σ_focus rung among those **shorter** than the minimising rung, provided it is **≥ 20 % worse** — the excess must be **≥ T** |
| **W2** *(silent where it must be)* | at each dataset's σ_focus-**minimising** rung, the excess must be **< T** |
| **W3** *(converges)* | the excess must be **< T at every rung at or above** the minimising rung |
| **W4** *(the ask is readable)* | wherever W1 requires firing the excess must be a finite number in `[−1, 1]`; **`NaN` is "could not look" and must never read as "we looked and nothing was shedding"** |
| **W5** *(selective)* | T must sit **above the excess's own bank median** — or it is the same defect in a new coordinate |
| **W6** *(no fitting)* | the excess may **not** be validated on the 39-run population **nor** on wave 7's `D02`/`D16` ladder |

> **W1 IS ONE RUNG PER DATASET, AND THAT IS DELIBERATE.** Wave 9's rule named exactly two rungs (`D02`@0.5 s must
> ask, `D16`@0.5 s must ask) — one per dataset, each the most-starved. Writing W1 as *"every materially-worse
> shorter rung must fire"* would hold the excess to a **stricter** bar than the two statistics it is replacing,
> and refuting a candidate on a rule its predecessors never had to pass is not a refutation, it is a moved
> goalpost. **The stricter reading is still measured and reported — as a DESCRIPTIVE column, never as a clause.**
> Wave 9's adopted statistic fired at `D02` 0.5 / 1 / 2 s and went silent at 4 s, so how many starved rungs a
> candidate covers is a real quality signal; it is simply not the bar.

> **THE OUTCOMES, and the close is one of them:**
>
> - **W1 ∧ W5 leave a non-empty threshold window and W2/W3/W4 do not empty it ⇒ the excess is a LIVE
>   CANDIDATE.** It is **not adopted** on this wave's evidence: it would then owe a population validation on data
>   it has not seen, which is what W6 requires of it too. No verdict, threshold, or action ships this wave either
>   way.
> - **The window is empty ⇒ F19's REMAINDER CLOSES**, as: ***no statistic over gate rejections separates a
>   starved wing from a detector that rejects noise everywhere.*** That is a real answer and a better one than a
>   fourth candidate — three statistics over one quantity have now failed on the same control, and the honest
>   reading is that the quantity does not carry the signal, not that the arithmetic over it keeps being unlucky.

### §4.4 Two instruments, both free, and one contract that must NOT be reused

- **W5's median comes off disk at zero compute.** Wave 10's 39-run population landings are still at
  `D:\hf_w10\pop\`, and `score_wing_pop_w11.py`'s `ratio_offline()` already pools wing and inner exactly as
  `ExposureRecommender.WingRejectedRatioOf` does. The excess is `wf − inf_` from the same two pools.
  **W6 permits this**: W5 is a *necessary constraint that only shrinks the threshold window*, so computing it
  from barred data is **conservative** — it cannot manufacture a pass. (If the excess ever survives everything
  *and* its survival turns on this median, the verdict must be re-taken on a fresh population, and the results
  must say so.)
- **The offline pools validate themselves, for free.** The offline `wf / inf_` must reproduce the **shipped**
  `WingRejectedRatio` field on every rung. If it does, the offline `wf` and `inf_` are validated — and therefore
  so is the excess computed from them. *Two routes to one number, at no cost*, which is the control wave 11 got
  on its ladder.
- **THE CONTRACT THAT MUST NOT BE REUSED.** Newtonsoft writes `NaN` and `Infinity` as the **strings**
  `"NaN"`/`"Infinity"`. Wave 10's `score_wing_pop.py` maps **both** to `nan` — correct for its statistic, and
  **wrong for anything with an infinite state**. Use `score_wing_pop_w11.py`'s `num()`. *A validated instrument
  validated against a DIFFERENT contract is not validated.*

### §4.5 The excess on the BARRED ladder is a prediction, not a verdict

Wave 11's ladder gives, for free, `D02`@0.5 s excess `0.6729 − 0.4290 = 0.243823` and `D16`@2 s excess
`0.0064 − 0.0000 = 0.006410` — so on the ratio's own killing ground the excess **survives** the rung that killed
the ratio, and RULE W12 would leave the window **(0.0889, 0.2438]**. That is written into the arm's script header
**as the pre-registered prediction**, exactly as wave 11 wrote `wing2`'s numbers into `ladder_w11.sh` before it
ran. **It is a prediction and not a verdict**: W6 bars the verdict coming from there, and a statistic that looks
good on the data that killed its predecessor is precisely the thing W6 exists to distrust.

**This is what makes the arm worth running rather than a formality.** The excess is *not* obviously doomed — on
the barred ladder it passes every clause — so the fresh ladder is a real test with a real chance of either
outcome, which is the only kind worth pre-registering.

**And W5's floor is already measured, at zero compute**: the excess's median over wave 10's 39 population
landings is **0.088948**. The same pooling reproduces wave 11's published median wing/inner **ratio** of 1.39
(1.3860 over the 29 firing runs with a finite ratio) — *the scorer validated against a number this project
already published, before it scored anything new.*

---

## §5 — Deferred, with reasons and prices

| deferred | reason, and the price of doing it |
|---|---|
| **the landing-level wavelet bisect** (`D:\hf_w10\exe_v1wav`, built wave 10, **still unused**) | the seed-level answer is exact and identical, so this measures only whether the pattern search amplifies it — which is [F8](followups.md#f8--optimizer-landings-are-not-reproducible-across-invocations), not the wavelet. **Item 1 may answer it for free**: an arm that reproduces 8/8 under a perturbed process environment bounds how much the search amplifies *any* perturbation. ~40 min if run standalone |
| **[F59](followups.md#f59--the-settings-export-drops-every-knob-whose-setter-validates-so-pinned_settingsjson-has-been-missing-five-detector-knobs-since-wave-5)'s five knobs** | `MaxDistortion`, `StarCenterTolerance`, `SaturationThreshold`, `HotpixelThreshold`, `Sensitivity` have been at **code defaults** in `pinned_settings.json` since wave 5. Waves 5–11 are **internally valid** (same file, same defaults throughout); what is false is that the file DESCRIBES the detector. **Re-pinning them at profile values WOULD MOVE THE COORDINATE SYSTEM** and needs a new gate baseline — a full 8-run re-establishment plus every downstream number re-read. **That is a deliberate decision, not a cleanup**, and it is priced here rather than done |
| **F52(d), F46(b), F54, F50** | nothing depends on them |
| **[F15](followups.md#f15--optimize---per-run-overwrites-each-runs-stored-settings)** | **still UNFIXED**: `optimize --per-run` rewrites run folders with no suppress flag, so a population pass still cannot run beside an arm. It constrains this wave's scheduling and is not fixed by it |
| **wave 8/9/10 AF/wizard changes, still UNCONFIRMED IN THE APP** | the csproj PostBuild xcopy **fails silently when NINA is running**. Not this wave's item; not to be claimed as confirmed either |

---

## §6 — Traps this wave carries

1. **`ConcurrencyCheck == "exclusive"` on ONE landing is not evidence the machine was quiet.** Read it across a
   whole arm. §1's trap box; it is why A3 exists.
2. **The profile set is machine state and it MOVES.** Re-snapshot before any fan-out arm (§2.4).
3. **`"NaN"`/`"Infinity"` are STRINGS.** Use `score_wing_pop_w11.py`'s `num()` (§4.4).
4. **`strings <dll> | grep AtrousWaveletFast`** still distinguishes v1 from v2 retroactively.
5. **The console `Optimization complete` line is NOT reliably emitted** (`uneven` has none).
   `aggregate_summary.json` is the instrument.
6. **`lumos` exits rc=3 reproducibly**; `astrodet` the DATASET is frameless (F14); `Panos` has a degenerate
   sigma fit.
7. **Never rebuild an arm's directory mid-wave** (F53(c)). Wave 12 uses `D:\hf_w12\exe` (gate + item 1, the
   pre-item-2 binary) and `D:\hf_w12\exe2` (post-item-2), and neither is rebuilt.
8. **On CI: verify the test COUNT, not the tick** (F37). An ABSENT check is more dangerous than a red one.

## §7 — Measurement discipline this wave is held to

- **A measured gain is not a measured cause**, and paying to measure the gain makes the confusion easier.
- **The cheapest instrument is the one already printed and ignored.** Before building one, grep the logs already
  on disk. F58 — wave 11's entire headline — came out of a `Profile:` line `optimize` had been printing since
  before wave 9.
- **Require the hazard to SURVIVE the fix** (A2/A3).
- **The negative control is what makes the positive ones readable** (RULE B12's pinned pairing; W0).
- **Test the property you care about, not the happy path.**
- **Name the hazard CLASS, not the dataset.** Wave 11 named `D17` and `D20` as the ratio's failure modes; the
  killer was `D16`@2 s — `D20`'s shape filed under the other clause.
- **Say what you did NOT run**, including what it would have cost.
- **Pin `--settings` AND `--profile-id` on every arm** — except item 1, where unpinning is the experiment and is
  argued for explicitly (§2.2).
