# Synthetic AF bank — followups wave 13 (design)

Plan: [`plans/synthetic-af-bank-followups-wave13-plan.md`](../plans/synthetic-af-bank-followups-wave13-plan.md).
Wave 12: [`docs/synthetic-af-bank-followups-wave12-results.md`](synthetic-af-bank-followups-wave12-results.md).
Register: [`docs/followups.md`](followups.md).

> ## PROVENANCE — fixed before any arm runs
>
> | input | value |
> |---|---|
> | tree | `develop` @ `f9f2074` — PR [#190](https://github.com/ghilios/hocus-focus/pull/190)'s merge commit, **VERIFIED MERGED** 2026-08-09T13:24:54Z |
> | binary | `D:\hf_w13\exe`, `TestApp.dll` sha256 `c4bf3280…`, `NINA.Joko.Plugins.HocusFocus.dll` sha256 `4c057544…` |
> | detector | `strings … \| grep AtrousWaveletFast` **hits** ⇒ `DetectorVersion` 2 |
> | settings **S0** | `D:\hf_w11\pinned_settings_w11.json`, md5 **`a67ffc06…`** — `MaxOutlierRejections` = **0** |
> | settings **S1** | `D:\hf_w13\pinned_settings_w13_mor1.json` — **byte-identical to S0 except that one key = 1**; md5 recorded when written |
> | profile | `astrodet` `ce3f3e63-8fd3-4b72-a0ca-d90db9441382`, pinned on **every** arm |
> | profile set | `D:\hf_w13\profiles_before_w13.txt`, snapshotted **before** any arm |
>
> **No fan-out this wave.** Every arm here is `--profile-id`-pinned, and a pinned arm cannot fan out at all
> (wave 11's K3). Wave 12 authorised fan-out at degree 4 for UNPINNED arms and measured it at **1.33×**, not 4×
> ([F60](followups.md)) — so there is nothing to trade here even if pinning were negotiable.
>
> **The profile set moved again since wave 12**, exactly as [F58](followups.md) says it must: `Default` is now
> #2 by `LastUsed` where `AA1600MM` was. Nothing in this wave draws from that ordering, because nothing is
> unpinned — the snapshot is taken so that fact is recorded rather than assumed.

---

## RULE G13 — the gate, on a fourth binary

Wave 11's eight values have now reproduced across **three** binaries and two settings files, bit-identically.
This wave adds a fourth binary and nothing else.

| run | expected `BestJ` (6 dp) | run | expected `BestJ` (6 dp) |
|---|---|---|---|
| `toml999` | 0.995784 | `mccomiskey` | 0.976746 |
| `CWhiteFocus` | 0.996068 | `D18_m24_deep_shed` | 0.999882 |
| `uneven` | 0.996368 | `D19_cygnus_deep_shed` | 0.999487 |
| `muggsie` | 0.997195 | `D20_m24_bright_control` | 0.999738 |

> **RULE G13, ONE PASS/FAIL CLAUSE: all eight `BestJ` reproduce the table to 6 dp. A partial reproduction is a
> FAILURE, not a warning** — the eight are one instrument. Bit-identity to sixteen digits is *reported* as the
> stronger observation; 6 dp is the clause.

Sequential, `--per-run --max-evals 250`, **both pins named in the script header**, scored by
`D:\hf_w12\score_w12.py` (reused as is — it already carries the eight values, wave 11's `BaselineJ`, and the
field checks). Free controls, all FIELDS on every landing and **diffed rather than asserted**:

| field | expected |
|---|---|
| `BuildId` | **must DIFFER** from wave 12's `103d61c4…` — a match would mean no build happened |
| `DetectorVersion` | 2 ×8 |
| `ProfileId` | `astrodet` ×8 — the pin took |
| `FitInputs` | `MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid` ×8 |
| `ConcurrencyCheck` | `exclusive` **read across the whole arm** — one landing's `exclusive` proves nothing (`WaitOne(0)` is won by exactly one of N contenders) |
| `BaselineJ` | reproduces wave 11 — [F41](followups.md)'s free control |

Wave 5's φ table is invalid on three axes and is not quoted anywhere in this wave.

---

## Item 1 — DECIDE `MaxOutlierRejections` AS A PRODUCT DEFAULT

### Why this is the wave's item

One integer is the through-line of waves 9–12, and **nobody has asked which value is right**. It moved
`BaselineJ` by 0.0144 ([F57](followups.md)), it *is* the two "attractors" that cost waves 9–10
([F55](followups.md)/[F58](followups.md)), and it moved tilt θ by 7.6 % ([F61](followups.md)). Every wave since
5 has treated it as machine state to be pinned.

**It is also a shipped product default.** `AutoFocusOptions.cs:62` resolves it with a code default of **1**, and
`InitializeOptions` sets **1**. It decides whether the AF fit may drop one Grubbs outlier — which is exactly
[F45](followups.md)'s complaint: the Grubbs test rejects the **in-focus** point of a near-perfect curve, and the
blind walk then buys an extra exposure.

**A standing fact that falls out of this and holds regardless of the outcome:** the harness has measured
**waves 5–12 at `MaxOutlierRejections` = 0**, because that is `astrodet`'s value and `astrodet` is the pinned
profile. That is *not* the product default. Every σ_focus and every `J` this project has published was measured
with the rejection **disabled**, and no wave has said so.

### The arm is a SETTINGS sweep, not a profile sweep

Both values are supplied through `--settings` — S0 and S1 differ in exactly one key. The fit inputs have been
pinned in that file since wave 11 ([F58](followups.md)(d), [F61](followups.md)), so this measures the knob
**without reintroducing the defect that made the knob interesting**. Nothing in this wave switches profiles to
change a fit.

### The four instruments, and why the primary one is not σ_focus

**σ_focus is computed on the POST-rejection point set.** A value that is allowed to discard its worst-fitting
point is then scored on the fit that remains. So is `J`, and so is R². **Every quantity the project has argued
about is scored by the fit under test**, and the arm that decides a product default cannot be one of them. The
primary arbiter has to be *out of sample*.

The synthetic bank supplies one, for free and with no new code:
`synthetic_meta.json → renderRequest.OptimalFocuserPosition` is the generator's **true** best-focus position,
and the sweep is symmetric about it. `|fitted vertex − truth|` cannot be improved by discarding an inconvenient
point — discarding the in-focus point moves the vertex *away* from truth, which is F45's complaint stated as a
measurement.

| | instrument | population | quantities |
|---|---|---|---|
| **I1** | `af-fit` budget table | **20 synthetic** datasets | vertex `minPos` at budgets 0..3 on ONE detection pass, plus σ_focus, R², redχ², #rejected, rejected position. Scored against `OptimalFocuserPosition`. **PRIMARY** |
| **I2** | `af-fit` budget table | **19 real** bank runs | same, no truth: fire rate, vertex displacement, Δσ_focus, and **how far the rejected point sits from the fitted focus** (F45's specific claim) |
| **I3** | `bank-verify` ×2 (S0, S1) | 19 real runs | recall/recallHigh/precision (**negative control**), `sigmaFocus`/`afR2`/`afChi` (AF fit), `sStars`/`sR2`/`sRMS`/`sChi`/`sTheta` (sensor fit) |
| **I4** | `optimize --max-evals 1` ×2 | **39-run population** | `BaselineJ`, `BaselineSigmaFocus` — the seed evaluation, identical detector settings, only the fit differs |
| **I5** | `optimize --max-evals 250` at S1 | the 8 gate runs | the LANDING: `ChangedParams`, `BrightnessSensitivity`, `RecommendedStep`. **The gate arm is MOR=0's half, for free** |

**Why `af-fit` and not a new tool.** It already exists, and it already prints the whole answer: budgets 0, 1, 2
and 3 evaluated on **one** detection pass, so the two arms are perfectly paired and cost half what two passes
would. *The cheapest instrument is the one already printed and ignored* — this one has been printed since wave 6
and read only for a single run.

**And its detector coordinate system cannot bias it.** I1/I2 compare budget 0 against budget 1 **within a single
run, on one set of measured points**. Whatever produced those points is common to both budgets and cancels
exactly. That is why I2 may use each real run's own saved detector params (`af-fit`'s default, and the most
product-faithful choice available — they are the knobs the run actually executed with) without violating the
pinned-file discipline, and why I1's fall-through to S0 on the synthetic bank is equally harmless. `--step-size`
is passed explicitly on every invocation so no input is left unrecorded.

### RULE M13 — the decision rule, fixed before the data

Let **A** = `MaxOutlierRejections` 0 (no rejection) and **B** = 1 (one Grubbs rejection allowed; the shipped
default). For dataset *d*, `e_b(d) = |minPos_b(d) − truth(d)| / step(d)`, in **step units** so datasets with
different focusers are comparable.

- **D0 — VALIDITY, and it is a hard ABORT.** In I3, `recallHigh`, `recallAll` and `precision` must be
  **identical to full double precision** on all 19 runs across both arms, and I4's per-frame star counts must be
  identical between arms. `MaxOutlierRejections` reaches the *fit*; it cannot reach the detector. If a detector
  number moves, the arm is measuring something other than what it claims and **the wave reports no decision**.
- **D1 — PRIMARY, out of sample, on the 20 synthetic datasets.** X dominates iff
  **(a)** X's vertex error is strictly lower on **≥ 15 of 20** datasets (two-sided sign test *p* ≤ 0.041), **and**
  **(b)** the median paired |Δe| over the datasets where the two differ is **≥ 0.10 step**.
  **Ties are reported FIRST.** A dataset where budget 1 rejects nothing is a tie by construction, and if all 20
  are ties the knob is inert on this population and **D1 cannot fire in either direction** — which is itself the
  answer, not a failed measurement.
  *0.10 step is 10× the printed resolution of `minPos` (`G6`, ≤ 0.001 step on every dataset in the bank), and it
  is the smallest displacement that could plausibly cost the blind walk anything.*
- **D2 — CORROBORATING ONLY, in sample, on the 19 real runs.** `sigmaFocus` (I3) and σ_focus (I2).
  **Explicitly NOT decisive**, for the reason above: B is scored after being allowed to drop its worst point.
  It may corroborate D1 or contradict it; **a contradiction is reported as a contradiction**, and is not
  resolved by averaging or by preferring whichever agrees with D1.
- **D3 — THE SENSOR FIT, and watch for F61's asymmetry.** Paired Δ in `sStars`, `sR2`, `sRMS`, `sChi`, `sTheta`
  over the 19 real runs. **Pre-registered prediction (F61): `sStars` and `sTheta` MOVE.** If the sensor fit
  prefers the OPPOSITE value to the AF fit, **say so and do not average them — they are different products.**
- **D4 — THE OBJECTIVE, reported as a magnitude and NOT as a decision input.** ΔBaselineJ and
  ΔBaselineSigmaFocus over the 39-run population. `J` is computed **by** the fit under test, so a value allowed
  to drop a point scores itself. It measures how large a disturbance this knob is; it does not vote.
- **D5 — THE LANDING.** Does the optimizer's *recommendation* move (I5)? The landed knob values and the
  recommended step are detector quantities in their own units and ARE comparable across arms; `BestJ` is not,
  and is not compared.

> ### THE SHIP RULE, and the "no change" outcome is FIRST-CLASS
>
> **Change the shipped default only if D0 passes AND D1 dominates for the other value AND D3 does not point the
> other way materially.**
>
> - If **D1 does not fire**, the finding is pre-registered, in advance, as: **NO CHANGE — a knob that moved every
>   number this project argued about does not decide the product.** That is a real answer and it ships as one.
> - If **D1 fires for B (= 1, the current default)**, the finding is that **F45's complaint does not generalise**:
>   allowing the rejection is better out of sample on the population, and the default stays 1 for a *measured*
>   reason instead of an inherited one. F45's single-run observation stands as a single run.
> - If **D1 fires for A (= 0)**, the default changes to 0 and F45(b)'s MAD-floor question becomes live, because
>   the failure would then be the rejection *firing at all* on a good fit.
> - If **D1 and D3 point in opposite directions**, **ship nothing** and record the asymmetry as the finding: the
>   AF fit and the sensor model want different values, and they are different products.

### What this arm cannot answer, said in advance

- **It cannot decide for a user whose curve is worse than anything in the bank.** The bank's real runs are 19
  and its synthetics are 20 well-formed sweeps; a rig that produces one wild point per sweep is exactly where a
  rejection budget earns its keep, and no frame in either bank is that rig.
- **It cannot separate `MaxOutlierRejections` from `OutlierRejectionConfidence`.** Confidence is pinned at 0.95
  in both arms. F58 measured that confidence does not move `J` *when the budget is 0* — which is what a budget of
  zero predicts and says nothing about its effect at budget 1. That pairing is not swept here.
- **I5 is 8 runs, not 39.** It bounds "does the recommendation move" on the gate set only.

---

## Item 2 — CONFIRM WAVES 8–12's AF/WIZARD CHANGES IN THE APP

**This has been carried for four waves and must not be carried a fifth silently.** The mechanism that keeps
defeating it is known: the csproj PostBuild `xcopy` **fails silently while NINA is running**, so a build that
reports success can leave the old DLL in NINA's plugin folder.

The changes to confirm, **by name**, from the plugin-side diff `e8eb8b0..f9f2074` (wave 8's base to HEAD):

| # | change | wave | file(s) |
|---|---|---|---|
| **A1** | AF chart info rows survive a **round trip** through another run, not just a reload | 8 | `HocusFocusVM.cs` |
| **A2** | the loaded-report lookup (it never worked — `HocusFocusReport` cannot be read back) | 8 | `LoadedAutoFocusReportSource.cs` |
| **A3** | the exposure-recommendation block: **gate the display, never the measurement** | 8/9 | `ExposureRecommender.cs`, `Optimization/DataTemplates.xaml` |
| **A4** | in-run guidance split: **how long the search is taking, and why — but not what to do** | 8 | `StarDetectionOptimizerWizardVM.cs` |
| **A5** | the guidance block **ends on an instruction**, and the re-run button **stops hiding** | 9 | `StarDetectionOptimizerWizardVM.cs`, `DataTemplates.xaml` |
| **A6** | F52(c)'s abort advice, now that it has a statistic behind it | 9 | `StarDetectionOptimizerWizardVM.cs` |
| **A7** | F32's exposed **RESTART** (measured better than the floor) | 9 | `StarDetectionOptimizer.cs`, `DataTemplates.xaml` |
| **A8** | a **capped** step recommendation says what it converges toward | 10 | `StepSizeRecommender.cs` |
| **A9** | `StarSignalCopy` — the copyable star-signal block | 8/9 | `StarSignalCopy.cs`, `DataTemplates.xaml` |

**Procedure, and the order is the fix.** Close NINA → build → **verify the plugin DLL's timestamp and sha256 in
NINA's plugin folder BEFORE launching** → launch with `Start-Process` (**not** the MCP `launch_executable`) →
connect the **simulator camera first** (the Simulator tilt port silently needs it).

> **HARD TIME BOUND: 60 minutes**, and the blocked-report is pre-registered so "we ran out of time" cannot
> become "not this wave's item" again. If blocked, the results doc states: **exactly what was tried, exactly
> what failed, and which of A1–A9 remain unconfirmed BY NAME** — with the count. Partial confirmation is
> reported per item, never as a summary verdict.

---

## Item 3 — F15, the last structural blocker

`optimize --per-run` writes `optimized_settings.json` (and `hocusfocus_star_detection.json`) into **each run's
source folder** as well as `--out`, with no suppress flag
(`OptimizationDiagnosticRunner.cs:1008–1032`). Now that fan-out is authorised, this is the only thing still
forcing whole passes to be serialized against each other: two passes over the same bank collide in the bank.

**The interaction that makes it sharper than it looks**, and F15 already records it: `bank-verify --opt-a/--opt-b`
and `golden eval --params optimized` read the **run-folder** copy by default. So a prepass and a later scoring
run that were meant to be independent can silently share an arm.

**The fix.** Write only to `--out` unless the caller opts in; when the caller does opt in, **snapshot the file
that was there** rather than destroying it. The opt-in flag is what `review --runs <same>` auto-discovery needs,
so the capability is kept and only the default changes. Guarded by unit tests, including one that fails if the
run-folder write happens without the flag.

---

## DEFERRED, with reasons and prices

- **F59's five knobs** (`MaxDistortion`, `StarCenterTolerance`, `SaturationThreshold`, `HotpixelThreshold`,
  `Sensitivity`) stay at code defaults. Waves 5–12 are internally valid; the file does not DESCRIBE the detector.
  Re-pinning **moves the coordinate system** RULE G13 will have reproduced a fourth time, and needs a new gate
  baseline: **price ≈ one 8-run gate (~45 m) plus the re-derivation of every cross-wave comparison in the
  register.** It is a decision, not a cleanup.
- **The landing-level wavelet bisect** (`D:\hf_w10\exe_v1wav`, built wave 10, **still unused**). Now doubly
  bounded: the seed-level answer is exact and identical, and wave 12's fan-out arm showed the search does not
  amplify an arbitrary process-level perturbation. **This wave recommends CLOSING F57(d)** rather than running
  it, and says so in the register.
- **F61(b)** — re-running a historical tilt calibration under pinned fit inputs. Nothing depends on it; the
  honest statement remains "unrecorded input", not "known error".
- **A 39-run population pass at `--max-evals 250` per arm.** Priced at **~2 ¼ h each with fan-out, ~3 h
  sequential** (F60's measurement, not the assumption it replaced) — and it would not decide anything, because
  `BestJ` is computed by the fit under test and is not comparable across arms (D4). I4 buys the population
  statistic at `--max-evals 1` for ~55 m total.
- **F52(d), F46(b), F54, F50**: nothing depends on them.

## Budget, pre-registered so the wave can be scored against it

| arm | expected |
|---|---|
| RULE G13 | ~45 m (wave 12 measured 43 m 10 s) |
| I1 — af-fit ×20 synthetic | ~60 m |
| I2 — af-fit ×19 real | ~40 m |
| I3 — bank-verify ×2 | ~25 m (wave 12 measured 11 m per pass) |
| I4 — optimize --max-evals 1 ×2 ×39 | ~55 m |
| I5 — optimize --max-evals 250 ×8 at S1 | ~45 m |
| **total compute** | **~4 ½ h** |

The gate runs on a quiet machine. The later arms may overlap with editing and building, which cannot move a
landing (wave 12's RULE A12 showed four-way process contention does not move a digit) but **can** move a
timing — so no timing claim is made from an arm that shared the machine, and any that does says so.
