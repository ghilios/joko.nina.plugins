# Synthetic AF bank — followups wave 9 (design)

Plan: [`plans/synthetic-af-bank-followups-wave9-plan.md`](../plans/synthetic-af-bank-followups-wave9-plan.md).
Wave 8: [`docs/synthetic-af-bank-followups-wave8-results.md`](synthetic-af-bank-followups-wave8-results.md).
Register: [`docs/followups.md`](followups.md).

*Wave 8's §0.1 deferred [F32](followups.md#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains)'s
confirmation arm a fourth time on the stated ground that "wave 9 does not end it either". **That conclusion is
wrong, and it was refuted by wave 8's own instrument.** This wave opens by saying so, and by running the arm
first.*

---

## §0 — Pre-flight, and the two things that must be settled before any compute is committed

### §0.1 The wave-8 CI gate: CHECKED, and it is not the failure mode the hand-off feared

Wave 8's results doc closes with *"no checks, and it is not this branch … CI must be re-checked once Actions
recovers, before this merges."* PR #184 merged anyway, so the first act of this wave is to find out whether that
re-check ever happened. **It did.**

| check | measured |
|---|---|
| PR #184 | **MERGED** 2026-08-06T23:33:27Z → `018eaa0` |
| `gh pr checks 184` | **1 passed** — "Run unit tests" on head `57e759e` |
| the run | `31130194942`, started 23:13:05Z, completed **23:32:36Z** — ~51 s before the merge |
| **the test COUNT** (F37's rule) | **`Passed: 3674, Failed: 0, Skipped: 0, Total: 3674`** — the whole suite, not a truncated host |
| the three RED runs on the branch/`develop` | **job-level `cancelled`**, zero test output — outage infrastructure, not regressions |
| `develop` @ `018eaa0` | **0 checks** — the push-event run never scheduled |
| githubstatus.com, **now** | indicator `major`; **`Actions: major_outage` STILL** |

**So the absent check resolved itself, and the resolution is verifiable rather than assumed:** Actions recovered
for long enough to run the PR check on the final head SHA, it passed with the full 3674, and the merge followed.
The red runs are the *other* half of F37's rule — a red check that is not about the code — and they are
`cancelled` with no test output at all, which is what an outage looks like and is not what a native host crash
looks like (that one reports zero failures with a *reduced* count).

> **What this changes for wave 9: Actions is degraded RIGHT NOW.** `develop`'s own merge commit has no check at
> all, so the "absent check" failure mode is live, not historical. **This wave's PR is gated on a LOCAL full-suite
> run with its count recorded, and on re-reading githubstatus.com at PR time** — an absent check is not evidence
> of anything and will be reported as absent, never as passed.

### §0.2 The ordering, and why it is not the one wave 8 predicted

Wave 8's §0.1 priced F32's deferral on the belief that F19(c)'s re-render was still ahead of it, and that the
floor list was `D01`–`D05`, `D07`, `D13`, `D14` — seven datasets, none of them F32's. **When F19(b)'s new
reporting actually PRINTED the saturated set, it came back as 13 of 20**: eleven at the floor **including `D18`,
`D19` and `D20` — F32's entire synthetic half** — plus **two at the ceiling** (`D10` 335.6 s → 30 s, `D17` 40.4 s
→ 30 s) that nobody had ever counted.

That is the trigger wave 8 wrote down. F19(c) can now re-render exactly the three datasets F32's arm needs held
still, so **F32 goes first** and everything that can re-render queues behind it.

**The order, and it is a hard gate:**

| # | item | may re-render? | depends on |
|---|---|---|---|
| **1** | **F32's confirmation arm** | must NOT be disturbed | — |
| **2** | **F19(c)** — floor *and* ceiling | possibly | item 1 complete |
| **3** | **F19's real remainder** — the wing statistic | no (validates on frames already on disk) | — |
| **4** | **F52(c)** — the abort/re-expose advice | no | item 3 |
| **5** | **F49 / F51** — the two field defects | no | item 3 |

Items 3–5 are product-side and re-render nothing, so they run *concurrently* with item 1 rather than after it.
Item 2 is the only one that can touch a frame, and it is the only one that waits.

### §0.3 [F15](followups.md#f15--optimize---per-run-overwrites-each-runs-stored-settings) — which landing the banks end holding, decided BEFORE the arm runs

`optimize --per-run` rewrites `optimized_settings.json` **into the bank's run folders**, so whichever sub-arm runs
last owns both banks. Wave 8's adoption arm B currently owns the synthetic folders (the F39(b)-adopted landing).

**Decision: the banks end holding the SHIPPED-DEFAULT landing.** That is arm A — no keep floor (the shipped
default is `MinDetectionKeepFraction = null`), F39(b) binning applied (the shipped default since wave 8). So the
sub-arms run **C → B → A**, with A last.

This is wave 8's own principle applied unchanged (*"every run folder now holds the adopted landing"*), and it is
stable under either verdict:

- **verdict "do not adopt φ"** → the banks already hold the correct landing, nothing more to do;
- **verdict "adopt φ = 0.50 as a default"** → arm B becomes the shipped default and the banks are re-landed by
  re-running **arm B alone** before the PR. Recorded here so the obligation cannot be forgotten after the fact.

The alternative ordering (B last) is rejected: it would leave the banks holding a landing produced by a flag that
is default OFF, which is exactly the state wave 7 was criticised for leaving behind.

---

## §1 — Item 1: F32's confirmation arm

Deferred across waves 5, 6, 7 and 8. `MinDetectionKeepFraction` shipped **default OFF** in wave 5 pending exactly
this. Wave 6 changed its shape.

### §1.1 Three arms, because wave 6 measured the two mechanisms as complementary

Wave 6's arm R established that the shedding corner is a **greedy trap** (restarts alone improve `J` on 5 of 7
binding runs with no constraint at all) but that restarts recover only **0–36 %** of the floor's gain. And the
floor's two FAILURES are exactly where restarts do best — `mccomiskey` −0.031 under the floor but **+0.0034** from
restarts, `muggsie` −0.0023 under the floor and untouched by them. **Complementary, not competing.** So:

| arm | invocation | question |
|---|---|---|
| **A** | *(no flag)* | the shipped-default control, and the F15 landing (§0.3) |
| **B** | `--keep-floor 0.50` | does the recommended floor confirm at full-bank scale? |
| **C** | `--continue-rounds 2` | how much of B's gain is merely un-sticking a greedy search? |

All three: `optimize --per-run --max-evals 250 --settings D:\hf_w9\pinned_settings.json`, one binary, no binning
flag. `--max-evals 250` is wave 5's and wave 6's value and is what makes the comparison legal.

### §1.2 Scope: both full banks, plus `bank-verify` for real-bank RECALL

Per F32's entry, verbatim: *"Adoption still needs the confirmation arm — both full banks plus `bank-verify` for
real-bank recall, since the efficacy criterion could only be evaluated by keep-% proxy here."*

- **Synthetic bank:** all 20 datasets.
- **Real bank:** the 19 top-level runs `bank-verify` scores (`astrodet` excluded — [F14](followups.md#f14--astrodet-is-frameless), frameless;
  the `_`-prefixed folders are scratch arms, not runs).
- **Recall:** `bank-verify --opt-a` against arms A and B, which is the measurement wave 5 could not make.

**Arms A and B run the FULL population; arm C runs the BINDING set. That reduction was drafted, withdrawn on an
ESTIMATE, and reinstated on a MEASUREMENT** — the sequence is recorded because the estimate was wrong by 3×.

1. **Drafted:** narrow arm C, because `--continue-rounds 2` is ~2.4× the per-run cost (wave 6: 8 runs in 1 h 35 m
   against wave 5's 40 m) and 39 runs sequentially is ~8 h.
2. **Withdrawn:** at fan-out 3 that projected to ~2.6 h, inside F32's own ~6 h estimate, so the narrowing looked
   like it bought nothing but a definitional argument.
3. **Reinstated:** measured at fan-out 4, arm C did **9 runs in 1.8 h** — ~8 h for arm C alone and ~11 h for the
   wave. The projection was wrong because it priced the *median* run against a population whose real-bank runs
   are 5–25 minutes each under `--continue-rounds 2`.

**And the reinstated version rests on a better reason than cost.** Arm C's question is R2 — *how much of arm B's
gain is merely un-sticking a greedy search?* On a run where the floor never rejected a single candidate there is
no gain to attribute, so a restart there answers a question nobody asked. **The binding set is read from arm B's
own logs** (`keep floor (F32): 0.50; N candidate(s) rejected as infeasible`, N > 0), **unioned with the 8-run
wave-5/6 comparability subset** so the cross-wave comparison to wave 6's arm R survives regardless.

**So the order becomes B → C(binding) → A**, not C → B → A. The binding set is a property of B alone, so C needs
no dependency on A — and **A still runs LAST and still owns the banks' final landing** (§0.3), which is the whole
constraint the original ordering existed to satisfy. An interruption after B and A leaves the banks in the correct
resting state; only an interruption *inside* C leaves a named, small subset to restore.

### §1.3 THE COMPARABILITY GATE — run FIRST, and the arm does not start until it passes

Wave 8 flipped `optimize`'s **default** to apply each run's derived detection binning. For the 8-run comparability
subset this should be a no-op, and the reason is structural:

- the **5 real runs** have no `synthetic_meta.json`, so `HarnessSettingsStore.ResolveRunDetectionBinningFactor`
  falls back to the pinned settings file's `DetectionBinning`, which is **`Bin1`** (verified in the file);
- **`D18` / `D19` / `D20`** carry `expectedOptimal.detectionBinning = 1` in their own metadata (verified).

**That is an argument, and this project does not run arms on arguments.** The gate measures it: the same 8 runs,
this binary, arm-A's exact invocation, against the values on record from wave 5's feature-OFF arm — which wave 6's
arm R round 0 independently reproduced to 6 dp, which is what makes them usable as a fixed point.

| run | expected `BestJ` |
|---|---|
| `toml999` | 0.997993 |
| `CWhiteFocus` | 0.998476 |
| `uneven` | 0.996368 |
| `muggsie` | 0.997195 |
| `mccomiskey` | 0.994025 |
| `D18_m24_deep_shed` | 0.999822 |
| `D19_cygnus_deep_shed` | 0.999557 |
| `D20_m24_bright_control` | 0.999766 |

> **RULE G, fixed before the gate ran.** All 8 must reproduce to **6 decimal places**. If any does not, **STOP**:
> the cross-wave comparison to wave 5's φ table is void, nothing in the arm is readable against it, and the wave
> reports that instead of a φ verdict. A partial reproduction is a failure, not a warning — the eight are one
> instrument, not eight.

**And the OTHER 17 synthetic datasets are NOT comparable to any prior wave.** They have `detectionBinning` 2 (or
were never run at their own factor), so wave 8's default flip changes what they detect at. Their arm-A numbers are
a **new baseline**, and this wave will say so rather than tabling them next to wave 5's.

### §1.4 Concurrency, and the free control that checks it

39 runs × 3 arms sequentially is ~14 h. The arms run with a **fan-out of 3 concurrent runs**, which is safe under
F15 (each process owns a different bank folder) and brings the wave to ~3.5 h.

**The determinism risk is real and it is controlled for free.** Arm A re-measures the same 8 runs the gate just
measured sequentially. If concurrency perturbed anything — a thread-count-dependent partition, a timing-dependent
stopping rule — those 8 values move. **Gate = sequential fixed point; arm A = the same 8 under concurrency.** A
disagreement between them is a concurrency artifact and invalidates the fan-out, not the arm's design.

### §1.5 Pre-registered rules

> **R1 — adoption.** φ = 0.50 is adopted as a **default** only if, across the binding runs of **both** banks:
> (a) the median Δ`J` (B − A) is **≥ 0**, and (b) `bank-verify` `recall@SNR≥12` under B is **≥** under A on a
> majority of the real-bank runs with **no** run regressing by more than 0.05, and (c) no run's σ_focus regresses
> by more than 20 %. Failing any clause, it stays default OFF and ships as the harness flag it already is.
>
> **R2 — attribution.** If arm C recovers **≥ 50 %** of arm B's median `J` gain over arm A on the binding set, the
> floor is not buying a distinct mechanism and the honest product change is to expose *restarts*, not the floor.
> (Wave 6's rule, restated at full-bank scale; it fired the other way at 8 runs — 0–36 %, median 13.8 %.)
>
> **R3 — the population that did not motivate it.** F32's evidence is 7 binding runs. If the floor **regresses**
> `J` on more of the newly-covered runs than it improves, R1 fails regardless of the binding-set median. This is
> wave 8's R6 rule transplanted, and it exists because wave 8's `StructureLayers` fix looked like a clean 2-for-2
> win on the two cells that motivated it and refuted itself on the other five.

### §1.6 One fact that belongs in the write-up

`D18` / `D19` / `D20` — the arm's entire synthetic half — sit on **exposures that are floor clamps rather than
derived values** (0.004 / 0.008 / 0.004 s asked, 0.5 s rendered). Every φ arm sees the same frames so this does not
invalidate the arm, but it bounds what the synthetic half can say about a *photon-starved* population: it has
never contained one.

---

## §2 — Item 2: F19(c), and the free check that comes before scheduling it

F19(c) is *"re-check `MinExposureSeconds = 0.5 s`"*. It is filed as expensive because moving the floor re-derives
**11 datasets** and re-renders them. **The check that decides whether to spend that is free, and it runs first.**

### §2.1 The floor: the evidence points at the statistic, not the clamp

Every floor-clamped dataset asks for **less** than 0.5 s — `D02` 0.001, `D01`/`D03` 0.003, `D04`/`D18`/`D20` 0.004,
`D19` 0.008, `D05` 0.014, `D14` 0.055, `D07` 0.066, `D13` 0.264. So **lowering `MinExposureSeconds` moves every one
of them DOWN.**

And on the one dataset where the exposure/σ_focus relationship has actually been measured — `D02`, wave 7's arm E —
**8 s is 44 % BETTER than the derived 0.5 s.** Lowering the floor moves `D02` the wrong way by four orders of
magnitude. Raising the floor is not what a floor is for: a floor exists to stop an absurdly short exposure, not to
supply an exposure the derivation failed to find.

> **RULE F19c, fixed before the check ran.** F19(c) resolves as **"the floor stays"** unless a floor-clamped
> dataset can be shown, from data already on disk, to have its σ_focus minimum *below* 0.5 s. There is exactly one
> dataset with a measured exposure ladder (`D02`) and its σ_focus falls monotonically as exposure **rises**. If the
> rule holds, **nothing re-renders**, and item 1 was worth running for its own sake rather than as a prerequisite.

**The finding this produces is not "no change".** It is that the floor is the *symptom*: the arithmetic saturates
against it because the statistic feeding the arithmetic is wrong for these fields, which is item 3. Moving the
clamp would relocate the symptom without touching the cause — and would have re-rendered 11 datasets to do it.

### §2.2 The ceiling, which has never been examined

`D10_rc16_3250mm_sparse` asks **335.6 s** and gets 30 (**11× short**); `D17_cdk14_oiii5` asks 40.4 s and gets 30.
This is the same saturation from the other side and it is **not obviously the same defect**: at the floor the
arithmetic is asking for less than any sane exposure, while at the ceiling it is asking for more than an AF sweep
can spend.

Two separable questions, and they have different answers:

1. **Product — should `MaxRecommendedExposureSeconds = 30 s` move?** Almost certainly not: 335 s/frame over a
   9-point sweep is ~50 minutes of pure integration, and `AutoFocusEngineOptions.AutoFocusTimeout` would kill it.
   The cap is doing its job. What matters is that the product SAYS the ask was ceiling-clamped — and F19(b)
   verified `StarSignalCopy.DescribeExposureDerivation` already names which bound bound it. **Verified, not
   changed.**
2. **Bank fidelity — `D10` and `D17` are RENDERED at 30 s while their own physics asks for 335.6 / 40.4 s.** So two
   of the twenty datasets are deliberately photon-starved relative to their derivation, and no write-up has ever
   said so. That is a fact about the bank that belongs in `synthetic-af-bank-expectations.json`'s derivation record
   and in F19, **not** a reason to re-render: a `D10` rendered at 335 s would be a dataset no user could ever
   capture.

> **RULE CEIL.** The ceiling moves only if a dataset's σ_focus is measured to improve materially between 30 s and
> its raw ask. No such ladder exists and rendering one costs a re-render of the item-1-critical bank. **Recorded as
> a bank-fidelity flag and a work item, not actioned this wave** — and stated as a deferral with a price, which is
> what wave 8's §0.1 got wrong by deferring on a prediction instead.

### §2.3 If a re-render ever does happen

Pre-registered now so it is not decided under time pressure later: preserve the pre-render frames first
(`D:\hf_w9\oldframes`, as wave 6 used `D:\hf_w7\oldframes`), and **re-run wave 5's φ arms on the new bank** rather
than comparing across it — [F41](followups.md#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)
applied to frames instead of binaries.

---

## §3 — Item 3: F19's real remainder — a statistic that can see the wing frames

**This is the item everything else in the wave is downstream of.** Two obvious fixes are already refuted by
pre-registered tests: *"change `NTarget`"* (wave 7) and *"widen the trigger"* (wave 8, rule R5).

### §3.1 What is broken, stated mechanically

`ExposureRecommender.Recommend` computes

```
S_now = median over non-recovery FRAMES of ( that frame's NTarget-th brightest accepted-star SNR )
RawSeconds = t_current × (TargetSensitivity / S_now)²
```

On `D02_rich_135mm` — 2746 stars above the gate at the sweep's outermost frame — `S_now` measures **991.8 to
2308.8** against a target of **10**, and it **rises with exposure** (992 → 1226 → 1864 → 2309). So the raw ask is
0.000 s at every rung of a 16× range over which σ_focus improves 44 %, and the statistic **saturates harder the
more exposure you give it** — it can never converge toward asking for more.

**The 20th-brightest star of a 2746-star field carries no information about the faint stars holding the V-curve's
wings.** Both aggregations point the same way: the per-frame reduction takes a *bright* rank, and the across-frame
reduction takes the *median frame* — which on a focus sweep is a near-focus frame. The wing frames, which are the
ones the fit's slope rests on, are visible to neither.

**The class's own docs defend the median-across-frames choice**, and the defence is not silly: pairing a per-frame
statistic with a worst-FRAME aggregation *"would hand the entire recommendation to whichever single frame had a
passing cloud, a satellite trail, or a guiding bump"*. **Any candidate must answer that objection rather than
ignore it** — which is why the candidates below aggregate over a wing *set*, never over one frame.

### §3.2 THE ACCEPTANCE RULE — written down BEFORE the statistic is built

Validated on data **already on disk**: `D:\hf_w7\armE\t{0.5,1,2,4,8}` carries `D02_rich_135mm` and
`D16_esprit550_ha3` at five exposures with known σ_focus outcomes.

| dataset | 0.5 s | 1 s | 2 s | 4 s | 8 s |
|---|---|---|---|---|---|
| `D02_rich_135mm` (derived **0.5 s**) | 0.10927 | 0.09125 | 0.09482 | 0.08182 | **0.06109** *(−44 %)* |
| `D16_esprit550_ha3` (derived **2 s**) | 0.35169 | 0.19762 | **0.06423** | 0.12034 | 0.14828 |

> **RULE W, all four clauses, fixed before any statistic is written. A candidate that fails ANY clause is not
> adopted.**
>
> **W1 — the defect.** On `D02` at **0.5 s**, the candidate must ask for materially more: `IncreasesExposure` true
> **and** `RecommendedSeconds ≥ 2 × CurrentSeconds` (≥ 1.0 s). *(8 s is measured 44 % better, so 2× is a floor on
> what "materially" can mean, not a target.)*
>
> **W2 — the control.** On `D16` at its derived **2 s**, the candidate must **NOT** ask for materially more:
> `IncreasesExposure` false, or `RecommendedSeconds < 1.25 × CurrentSeconds`. *(4 s and 8 s are measured WORSE —
> 0.064 → 0.120 → 0.148. `D16` is the whole reason the arm-E ladder was rendered.)*
>
> **W3 — convergence.** On `D02`, `RecommendedSeconds / CurrentSeconds` must be **non-increasing** across the rungs
> 0.5 → 8 s, and must reach "no material increase" (< 1.25×) by the **8 s** rung. *(The shipped statistic fails
> this by construction: it saturates HARDER as exposure rises, so it could never converge even if its trigger were
> widened. A candidate with the same pathology is the same defect wearing a different number.)*
>
> **W4 — no regression where the shipped statistic is RIGHT.** On `D16` at **0.5 s** (below its derived value) the
> candidate must still ask for more — `IncreasesExposure` true. *(The shipped recommender correctly returns 1.5 s
> there. A candidate that loses this has traded one blind spot for another.)*
>
> **A candidate that fires on both `D02`@0.5 and `D16`@2, or on neither, HAS NOT BEEN VALIDATED.** W1 and W2 are
> the discriminating pair; W3 and W4 exist so that passing them by accident is harder than passing them by design.

### §3.3 The instrument, and what it would do if the thing it checks were broken

Wave 8 hit *"the instrument agrees with the thing it is supposed to check"* **three times**. So, explicitly:

- The per-frame diagnostics are computed **unconditionally**, never gated on `SensitivityIsAtFloor`,
  `HasLowStarSignal`, or any other product display condition. That single line is what made F19 answerable at all
  (wave 8's lesson 4) and the new instrument must not re-introduce it.
- **What would this instrument do if the wing hypothesis were completely wrong?** It would show wing frames whose
  `NTarget`-th-brightest SNR is *indistinguishable* from the near-focus frames on `D02` — in which case no wing
  statistic can produce W1, and the wave reports that the wing hypothesis is refuted. The instrument is capable of
  killing its own hypothesis, which is the property being asserted here.
- **W2 is a control that can genuinely fail.** A naive wing-SNR statistic very plausibly fires on `D16`@2 s too
  (its wings are defocused narrowband frames). If it does, that candidate is dead — and that is the outcome the
  rule exists to force rather than to rationalize around.

**Step 1 is measurement, not code.** Extend the harness to record per-frame wing diagnostics (focuser position,
accepted-star count, `NTarget`-th SNR, gate/flat rejections), re-run arm X's 10 optimizations over the frames
already on disk (**~8 min, no re-render**), and *look at the wing structure before choosing a form*.

### §3.4 The candidate family — DRAFTED, THEN REFUTED BEFORE IT WAS IMPLEMENTED

The family this section first named was **C1** `S_wing` (the `NTarget`-th-brightest reduction aggregated over the
*wing* frames rather than all frames), **C2** `n_wing` (wing accepted-star count against `NTarget`), **C3**
`min(S_now, S_wing)`, and **C4** C1-gated-by-C2. Every one of them is an order statistic over **ACCEPTED** stars.

**All four are structurally incapable of firing on `D02`, and it takes a proof plus data already on disk to see
it — no run required.**

> **The gate floor theorem.** `StarDetector.InertSensitivityBound` proves that every candidate reaching the
> Sensitivity gate satisfies `sensitivity > PeakResponse × EffectiveClipMultiplier`, and acceptance additionally
> requires it to exceed `StarDetectorParams.Sensitivity`. So **every accepted star's gate statistic strictly
> exceeds `EffectiveSensitivityGate = max(Sensitivity, InertSensitivityBound)`**, and therefore **any order
> statistic over accepted stars — any rank, on any subset of frames — is bounded below by that gate.**
>
> Hence: **if `EffectiveSensitivityGate ≥ TargetSensitivity (= 10)`, `signalIsSufficient` is TRUE
> unconditionally**, whatever the sky contains. `ExposureIsNotTheLimit` is then a property of the *landing*, not
> a measurement of the frames.

Wave 8's arm X, read for this rather than for `S_now`, says `D02_rich_135mm` lands its effective gate at
**16.667 / 48.5 / 49.0 / 9.0 / 9.0** across the five rungs. **Three of the five are structurally silenced.** No
wing statistic over accepted stars could ever have satisfied W1 there, so the whole family would have been
implemented, measured, and refuted at the cost of a build and an arm.

**This sharpens F19 far beyond "the 20th-brightest star saturates".** The rank is not the defect; the
*population* is. `S_now` measures a set the detector has already filtered to be above the target, so the
recommendation asks "are the stars I kept bright enough?" — a question whose answer is yes **by construction**
whenever the optimizer lands a gate at or above 10. It is closely related to, and strictly stronger than, wave 7's
leg 2 (*"the named alternative is PINNED BY THE GATE"*), which said the *faintest accepted star* is pinned; the
theorem says **every** statistic over accepted stars is.

### §3.4a The corrected family — the quantities that are NOT floored by the gate

**Rule W (§3.2) is untouched.** It was fixed before any of this and it does not depend on which family is tried;
that is the entire point of writing the acceptance rule before the statistic.

Only two quantities in a run can see stars the exposure failed to deliver, because only they are not computed over
the accepted set:

| # | candidate | shape |
|---|---|---|
| **E1** | **wing rejected-fraction** | on the wing frames, `lowSensRejections / (lowSensRejections + accepted)` — candidates that FORMED and fell below the gate are exactly the stars a longer exposure could convert |
| **E2** | **wing gate-headroom** | on the wing frames, `median(acceptedSNR) / EffectiveSensitivityGate` — gate-RELATIVE, so it is not floored at 1 by construction: it says whether the wing stars merely scrape the gate or clear it comfortably |
| **E3** | `max(existing ask, E1's ask)` | a strict WIDENING of the shipped statistic, so it cannot regress a run the shipped statistic already serves — **W4 comes free** |
| **E4** | E1 gated by E2 | fire on rejections only when the wing stars are also bunched at the gate — the conjunction that separates "faint because DEFOCUSED" (unavoidable, more exposure will not help) from "faint because UNDEREXPOSED" |

The first-order evidence for E1 is already visible in arm X's `GateRejectedCount` and it discriminates on the pair
Rule W is built from: **`D02` @ 0.5 s = 3752 rejections; `D16` @ 2 s = 0.** W4's case (`D16` @ 0.5 s) is carried by
the shipped `S_now` path, which is correct there (6.5 < 10) — which is why E3's widening shape matters.

**Two hazards, both named before the arm rather than discovered in it.**

1. **[F28](followups.md#f28--lowsensitivity-reads-exactly-zero-precisely-when-the-sensitivity-gate-is-floored): a
   rejection count of ZERO is empty by construction when the gate is inert.** `D16` lands `Sensitivity = 0` with
   an effective gate of 0.19–0.56 at every rung, so its zero rejections are not evidence of anything.
   `GateIsProvablyInert` already exists to say so and E1 must consult it — a candidate that reads an inert
   counter as "the wings are fine" has re-committed F28.
2. **E1's raw count is confounded by the landed gate.** `D02`'s rejections run 3752 → 9696 → 7500 → 127 → 3 while
   its gate runs 16.7 → 48.5 → 49 → 9 → 9; the rise from 0.5 s to 1 s is the gate more than doubling, not the sky
   changing. **This is why E1 is a FRACTION of the candidates formed and why the per-frame instrument records
   accepted counts beside rejections.** W3 (convergence) is the clause that will catch it if the normalization is
   still wrong.

The candidate that satisfies **Rule W** ships. If none does, the wave reports that the wing hypothesis failed its
own pre-registered test and F19 stays open with a narrower next step — and the gate floor theorem above is worth
the wave on its own, because it says what F19's fix can NEVER be built from.

### §3.5 The population check

A candidate that passes W1–W4 on two datasets is a mechanism, not a product rule — wave 8's lesson 1. Before it
ships, its verdict is measured on **all 20 synthetic datasets and the real bank**, and **every dataset where it
newly asks for ≥ 2× must have a stated reason**. `D10` and `D17` are the pre-registered *expected* fires (§2.2:
both are ceiling-clamped, i.e. genuinely starved by their own derivation), so a candidate that does NOT fire on
those two is suspicious in the other direction.

---

## §4 — Item 4: F52(c), the abort/re-expose recommendation

(a) and (b) **shipped in wave 8** and are on `develop`: one INFO line per 10 completed evaluations (phase,
evaluation/budget, elapsed, s/evaluation, incumbent `J`, cost note), and the wizard shows the **rate** with a
bounded *"at most N more, usually much less"*, which knob made the search expensive (quantified against the run's
own seed via `StarDetector.EffectiveStructureLayers`), and what Cancel costs. **Elapsed was deliberately NOT
added** — `ProgressCountElapsedText` already renders `X / Y (M:SS)` every second.

(c) was deliberately NOT built, because advice derived from the current statistic *"would tell precisely the users
who most need a longer exposure that theirs is already fine"* — on the user's own rig that statistic read **S/N
1438.6 against a target of 10**.

**The separation stays exactly as wave 8 drew it: facts about COST need no new statistic and are already shown;
ADVICE needs one.** So (c) is built on item 3's statistic and on nothing else.

**Design.** The advice is computed from the **seed evaluation**, which the optimizer performs at the start of the
search (it is what `ProgressSeedJ` / `ProgressSeedSigma` already report) — so a user asking *"should I abort?"* two
hours in gets an answer that was available in the first minute, rather than one that needs the search to finish.
It names **Cancel**, which exists (the house rule at `ShowOptimizeAgainAtRecommendedBinning`: never describe an
action whose control is hidden), and it is **absent** when the wings are healthy, because advice that always fires
says nothing.

> **RULE A.** (c) ships only if item 3's statistic satisfies Rule W. If Rule W fails, (c) does **not** ship and
> F52's entry records a second wave of being blocked on F19 — which is a result, not a gap.

---

## §5 — Item 5: F49 and F51, the two field defects

Both are downstream of item 3's statistic, which is why they ride in this wave rather than being touched twice.

### §5.1 F49 — a diagnosis with no instruction

`StarSignalCopy.RemedyFor` has exactly three ranked branches and **all three are exposure or binning remedies**:

| # | remedy | gate |
|---|---|---|
| 1 | change the detection binning factor first | `DetectionBinningDiffers && IncreasesExposure` |
| 2 | auto-focus through a broadband filter instead | `HasRecommendation && !ExposureIsNotTheLimit && !IncreasesExposure` |
| 3 | get frames at the longer exposure | `IncreasesExposure` |

On a rich, well-exposed field where the optimizer **chose** a floor gate, `ExposureIsNotTheLimit` is true ⇒
`IncreasesExposure` is false ⇒ branch 2's `!ExposureIsNotTheLimit` is false ⇒ **every branch falls through and it
returns `string.Empty`** — on a page whose contract is *diagnosis plus exactly one instruction*.

**Item 3 does not close this by itself, and that is the point of doing them together.** If the wing statistic makes
the recommender ask for more exposure on rich fields, branch 3 fires and this population shrinks — but it does not
vanish: a field whose wings are *also* well exposed still lands here, with the gate floored and nothing to say. So
F49 needs a **fourth branch**, and its trigger must be written against the NEW statistic rather than against the
one item 3 is replacing.

**The remedy must name a control that EXISTS.** `MinDetectionKeepFraction` has **no XAML binding anywhere** — it is
`--keep-floor` on the harness only — so it cannot be named. (And it would not bind here regardless: it rejects
landings keeping *fewer* stars than the seed, and this one keeps ~7× **more**. The pathology is admission, not
shedding — the *other* end of an axis F32 has only ever measured at the shedding end, which §1 does not change.)
What does exist: the **Brightness Sensitivity** option and the wizard's **"use current settings"** mode. The honest
instruction names the gate, not the exposure.

### §5.2 F51 — the re-run button hides exactly when the run needs re-running

```csharp
public bool ShowCaptureNewSweep =>
    HasExposureBlock && lastRunWasLive && !IsUseCurrentMode
    && (SelectedSummary?.ExposureAdvice?.IncreasesExposure ?? false);
```

Gated on `IncreasesExposure`, so **the button hides exactly when F19 misfires**. On the field session's run 2 the
first three conjuncts were true and `IncreasesExposure` was false, because the exposure row read *"2 s (unchanged;
measured star S/N 1438.6; target 10)"*.

**And even visible it would not have solved it.** `RunLiveAttemptAsync` overrides only
`OverrideAutoFocusExposureTime` and takes everything else from `autoFocusEngine.GetOptions()` — i.e. **the
profile's step size**. The step-size recommendation, the one that was asking to change on *every* run of that
session (100 → 214 → 459 → 474 → 482), is not carried. So the user had to **Accept a landing they did not want,
twice**, to re-run wider — and that Accept is how `Sensitivity 0.000` reached their profile.

Three pieces, matching F51's own next-step:

- **(a)** gate `ShowCaptureNewSweep` on *any* material recommendation — exposure **or** step size — not on
  `IncreasesExposure` alone;
- **(b)** make the re-capture apply the recommended **step size** as well (`AutoFocusEngineOptions.AutoFocusStepSize`
  is right there beside the exposure override), or say plainly that it will not;
- **(c)** neither should require Accept: **re-capturing is not adopting.**

The house rule cuts both ways here, and (a) must respect it: if the button becomes visible on a step-size-only
recommendation, the body copy must gain a sentence that names it, or the button appears with nothing explaining
why.

---

## §6 — Carried in from wave 8, and worth reading before trusting any harness

Two AF-chart fixes landed late on #184 and the second matters far beyond the AF chart.

- **The loaded-run info rows never worked, for any report, since wave 7.** `HocusFocusReport` carries three
  INTERFACE-typed properties (`IStarDetectionOptions`, `IAutoFocusOptions`, `IFocuserSettings`); Newtonsoft
  serializes them and cannot construct them, so `DeserializeObject<HocusFocusReport>` threw on **every real
  report**, the source caught it and returned null, and every row collapsed. Fixed on the **read path only** (an
  error handler that skips unconstructable members), deliberately not by changing what is written. **Known trade:
  the handler swallows ALL member errors**, bounded by the caller's exact-`Timestamp` check.
- A separate sequencing gap: the rows are re-read on **every** load now, because skipping when the timestamps
  matched was correct only for the watcher reloading the just-written chart and wrong after a round trip.
- **The test fixture wrote reports WITHOUT the option blocks the production writer always writes**, so it tested a
  shape that cannot exist and passed for a whole release over a feature that never worked once. That is the
  question this wave asks of every harness it builds: *what would this do if the thing it is checking were
  completely broken?*
- **Not yet confirmed in the app by the user.** This wave does not claim it is; if the user has not confirmed, it
  stays open.
- **OPERATIONAL TRAP:** the plugin csproj's PostBuild `xcopy` fails **silently** when NINA is running, so a build
  reports "Copying …" and succeeds while deploying nothing. NINA was verified **not running** before this wave's
  build.

---

## §7 — Measurement discipline this wave is bound by

Earlier waves' (each cost or saved real time), then wave 8's:

- **PIN `--settings` on every arm** ([F42](followups.md#f42--every-build-directory-silently-gets-its-own-detector-settings-and-the-run-instructions-require-a-new-one-per-arm)),
  and read the `UseAdvanced=False` warning — a file with `UseAdvanced=False` has its advanced knobs RECOMPUTED
  from the `Simple_*` presets, so editing them there does nothing. **This wave's pinned file is byte-identical to
  waves 5/6/7/8's** (`md5 df7c7cd1…`), which is what makes the gate in §1.3 legal.
- **`BaselineJ` is a free control** ([F41](followups.md#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary));
  a number agreeing TOO WELL is also an instrument failure.
- **Check whether a cheap instrument already exists, and whether the expensive step is necessary at all, BEFORE
  planning around it.** §2 is exactly this, and it has saved two waves already.
- **Change one thing. `--dry-run` the derived-parameter diff BEFORE rendering.**
- **Count tests that DISCRIMINATE.** Revert the fix, see which fail.
- **WRITE THE FALSIFICATION RULE DOWN BEFORE THE ARM RUNS.** §1.5 (R1/R2/R3), §1.3 (G), §2 (F19c, CEIL), §3.2 (W),
  §4 (A).
- **THE INSTRUMENT AGREES WITH THE THING IT IS SUPPOSED TO CHECK** — the single most expensive pattern in this
  project; wave 8 hit it three times. §3.3 answers it explicitly for this wave's one new instrument.
- **THE POPULATION THAT DID NOT MOTIVATE A HYPOTHESIS IS WHAT TESTS IT.** §1.5 R3 and §3.5.
- **CHECK WHAT A CONTROL FILTER MATCHES**, not just that it is the same filter — wave 8's derived-parameter strip
  produced 26 lines of fake movement because the `exposure definition` PROSE contains the substring
  `detectionBinning=`.
- **A GATE'S FALSE-NEGATIVE COUNT IS AN UPPER BOUND, NOT AN ESTIMATE**
  ([F50](followups.md#f50--a-false-negative-gate-count-is-an-upper-bound-on-what-relieving-that-gate-buys-not-an-estimate)).
- **VERIFY YOUR OWN TEST COMMENTS' DISCRIMINATING CLAIMS BY NEUTRALIZING.**
- **On a red CI check: verify the test COUNT AND check githubstatus.com. An ABSENT check is the more dangerous
  case** — and §0.1 is that rule catching both halves at once.

New findings are **FLAGGED in `docs/followups.md`**, not fixed inline.
