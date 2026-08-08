# Synthetic AF bank — followups wave 9 (results)

Design: [`docs/synthetic-af-bank-followups-wave9-design.md`](synthetic-af-bank-followups-wave9-design.md).
Plan: [`plans/synthetic-af-bank-followups-wave9-plan.md`](../plans/synthetic-af-bank-followups-wave9-plan.md).
Wave 8: [`docs/synthetic-af-bank-followups-wave8-results.md`](synthetic-af-bank-followups-wave8-results.md).
Register: [`docs/followups.md`](followups.md).

*The wave opened by contradicting its predecessor. Wave 8's §0.1 deferred [F32](followups.md#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains)'s
confirmation arm a fourth time on the ground that "wave 9 does not end it either" — a prediction that
[F19](followups.md#f19--the-exposure-recommendation-is-decided-by-the-20-brightest-stars-so-a-rich-field-can-never-earn-one)(b)'s
own reporting refuted in the same wave, by printing a saturated set that includes F32's entire synthetic half.*

> ## PROVENANCE — READ BEFORE REPRODUCING ANY NUMBER BELOW
>
> Every measurement in this document was taken on **`StarDetectorVersion` 1**, i.e. the legacy dense-`SepFilter2D`
> à-trous path. This branch has since been rebased onto a `develop` that ships
> [PR #187](https://github.com/ghilios/hocus-focus/pull/187)'s `AtrousWaveletFast` and bumps
> **`StarDetectorVersion` to 2**. The two wavelet paths agree to **≤ 3e-8** but are deliberately **not
> bit-identical** — which is precisely why the version was bumped.
>
> **So the numbers here are not expected to reproduce exactly on the current tree**, and that includes RULE G's
> eight comparability values, the confirmation arm's landings, and the wing statistic's Rule W table. A pattern
> search amplifies a 3e-8 perturbation into a different landing; that is
> [F8](followups.md#f8--optimizer-landings-are-not-reproducible-across-invocations) and
> [F55](followups.md#f55--optimize-is-not-reproducible-when-several-instances-run-at-once-and-the-seed-evaluation-is-what-moves),
> not a defect in this wave.
>
> **What this does and does not invalidate.** The wave's CONCLUSIONS rest on comparisons made *within* one binary
> — arm A vs arm B vs arm C, and the wing statistic against its own control — and those comparisons are
> internally consistent and unaffected. What is void is the ability to re-run a single number today and expect the
> same digits. **A future arm must re-establish RULE G's fixed point on `StarDetectorVersion` 2 before comparing
> anything to wave 5's φ table** — this is [F41](followups.md#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)
> and [F53](followups.md#f53--wave-8s-arm-x-does-not-reproduce-from-wave-8s-own-exe-because-the-arm-ran-on-an-earlier-build-of-it),
> stated in advance for once rather than discovered afterwards.

## Status of this document

| item | state |
|---|---|
| **the full suite** | **3693 passed, 0 failed** (`develop` @ `018eaa0` was 3674 → **+19**) |
| **the wave-8 CI gate** | **CHECKED. The absent check resolved itself** — #184's CI ran and passed 3674/0/0 ~51 s before the merge. Actions is in `major_outage` again NOW. See §0 |
| **RULE G** — sequential gate | **PASS, 8 of 8 to 6 dp.** The wave-8 binning-default trap is verified inert rather than argued inert. See §1.1 |
| **RULE G** — re-applied under the fan-out | **FAIL, 2 of 8.** Same rule, same runs, four processes in flight. See §1.5 |
| **F32's confirmation arm** | **ANSWERED.** The fan-out attempt was voided by its own control (F55); the SEQUENTIAL re-run passes both controls (RULE G 8/8, `BaselineJ` 0 of 39 moved) and **φ = 0.50 does NOT ship** — R1(c), R2 and R3 all fail. See §1.5–§1.6 |
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

**Arm C's scope was narrowed, un-narrowed, and re-narrowed, and the sequence is recorded because the middle step
rested on an estimate that was wrong by 3×.** Drafted narrow (`--continue-rounds 2` is ~2.4× the per-run cost);
withdrawn when fan-out projected the full arm to ~2.6 h; **re-narrowed once measured** — 9 runs in 1.8 h, i.e. ~8 h
for arm C alone. The re-narrowed version rests on a better reason than cost: arm C's question (R2) has no content
on a run where the floor never rejected a candidate.

**In the event the narrowing did not bind.** The binding set was read from arm B's own logs (`keep floor (F32):
0.50; N candidate(s) rejected as infeasible`, N > 0) and came back as **all 39** — the floor rejects *some*
candidate mid-search on essentially every run, even where it does not move the landing. **That predicate is looser
than wave 5's, which was about the LANDING**, and it is recorded as a definitional miss rather than presented as a
choice: arm C ran full scope after all.

**Order B → C → A, with A LAST** ([F15](followups.md#f15--optimize---per-run-overwrites-each-runs-stored-settings)):
`optimize --per-run` rewrites `optimized_settings.json` into the bank's run folders, so the last arm owns both
banks. **The banks end holding the SHIPPED-DEFAULT landing**, and they do — arm A finished last at 09:41Z.

**The fan-out's control is free, and it is the reason this section has a §1.5:** arm A re-measures the same 8 runs
the gate measured sequentially.

### §1.5 THE ARM RAN, AND ITS OWN PRE-REGISTERED CONTROL VOIDED IT

All three arms completed — 117 optimizations, 02:31Z → 09:41Z, **7 h 10 m**. Then RULE G was applied.

> **RULE G FIRES. 2 of the 8 comparability runs do not reproduce under the fan-out.**
>
> | run | sequential gate | arm A, fan-out 4 |
> |---|---|---|
> | `toml999` | **0.997993** | **0.995784** |
> | `D18_m24_deep_shed` | **0.999822** | **0.999882** |
> | the other six | *(exact)* | *(exact)* |
>
> The rule was written as *"a partial reproduction is a failure, not a warning — the eight are one instrument"*,
> and it is applied as written. **No φ verdict is published from this arm.** R1, R2 and R3 are not reported,
> because reporting them would be publishing a number whose instrument has already failed its own check — which
> is the exact shape of the mistake wave 8's R5 refused to make.

**The second control says where it comes from, and it is worse than a fan-out artifact.** `BaselineJ` is the
objective of the run's CURRENT settings — one evaluation of the pinned seed, **no search involved**. The arms
differ only in `--keep-floor` and `--continue-rounds`, neither of which touches it, so it must be identical
across all three. **On 6 of 39 runs it is not** — `D16_esprit550_ha3` moves by 0.0094, far too large for
float-summation order, and the odd arm out varies (A on three runs, B on two, C on one).

**So the seed evaluation is not a function of (frames, settings) alone.** Filed as
[F55](followups.md#f55--optimize-is-not-reproducible-when-several-instances-run-at-once-and-the-seed-evaluation-is-what-moves).

**Two causes excluded by measurement.** Re-running `toml999` at arm A's exact invocation: **sequentially and
alone**, on folders then holding arm A's landing, it returns **0.997993** — so neither the
[F15](followups.md#f15--optimize---per-run-overwrites-each-runs-stored-settings) folder state nor any build or
settings difference explains it. **One four-way concurrent trial did not reproduce the deviation either, and that
is recorded as having no power rather than as exculpatory**: the effect appears on ~15 % of runs, so a single
trial cannot exclude it. Waves 5–8 ran sequentially and all reported bit-identical controls; wave 9 is the first
to fan out and the first to lose them.

**The banks' final landing is still correct.** Arm A ran last (§0.3), so both banks hold the shipped-default
landing, and the F15 obligation is discharged in the direction that needed no φ verdict.

**What this cost and what it bought.** 7 h 10 m of compute and no φ verdict — against a measured, previously
unknown constraint on **every arm this project will ever run**. The fan-out was introduced to make the arm
affordable and it is precisely what made it unreadable; the free control the design attached to it is the only
reason that is known rather than believed.

### §1.6 THE SEQUENTIAL RE-RUN: both controls pass, and φ = 0.50 DOES NOT SHIP

Re-run sequentially the same day — 117 optimizations, 15:50Z → 01:14Z, **9 h 24 m**. (The 28 h estimate assumed
the fan-out had delivered a true 4× speedup; contention meant it delivered ~1.5×.)

| control | fan-out attempt | **sequential** |
|---|---|---|
| **RULE G** | FAIL 2 of 8 | **PASS 8 of 8 to 6 dp** |
| **`BaselineJ`** (F41's tell) | 6 of 39 moved | **0 of 39 moved** |

**Sequential execution bought a clean instrument**, which is what F55 predicted and what justified the 9 h 24 m.
For the first time since wave 5, the φ table is readable.

**11 of 39 runs bind. φ = 0.50 fails three independent pre-registered rules.**

| rule | measured | verdict |
|---|---|---|
| **R1(a)** median Δ`J` (B − A) | +0.000076 | pass |
| **R1(c)** worst σ_focus regression | **`vsn07` 0.00083 → 1.71721** | **FAIL** |
| **R2** restarts recover | **228 %** | **floor is NOT a distinct mechanism** |
| **R3** newly-covered binding runs | 6: **2 improved, 4 regressed** | **FAIL** |

- **R1(c) fails catastrophically, not marginally.** The bar was 20 %; `vsn07` degrades σ_focus by a factor of
  **2000**, `FlyData` by 7.7×. The floor does not trade recall for `J` on these runs — **it destroys the focus
  fit**, the one thing the objective exists to protect.
- **R3 is wave 8's `StructureLayers` lesson, repeating exactly.** Wave 5's evidence was 7 binding runs on a subset
  where the floor looked good; on the runs it never covered, the floor regresses 4 and improves 2.
- **R2 INVERTS wave 6.** At 8 runs restarts recovered 0–36 % (median 13.8 %) of the floor's gain — the finding
  that justified keeping the floor as a distinct mechanism worth confirming. At full-bank scale they recover
  **228 %**: strictly better than the floor, on the floor's own binding set. **A conclusion drawn from 7 runs
  reversed on 11.**

**`MinDetectionKeepFraction` stays default OFF permanently** rather than pending. The greedy trap F32 discovered
is real and is not withdrawn — restarts demonstrably improve `J` on most binding runs — but the keep floor is the
wrong instrument for it, and the honest product change is to expose RESTARTS (the wizard's "Continue optimizing"
button already is one).

**F15 discharged:** arm A ran last, so both banks hold the shipped-default landing, and no re-land is owed since
nothing was adopted.

**Owed:** F55(b) — the nondeterminism itself, a defect independent of this arm — and the wing statistic's
population check (§3.6), which is now unblocked.

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

## §3 — F19's remainder: the wing statistic, and a theorem that explains three waves of failure

### §3.1 The gate floor theorem — why "change `NTarget`" and "widen the trigger" were both refuted

`StarDetector.InertSensitivityBound` proves every candidate reaching the Sensitivity gate satisfies
`sensitivity > PeakResponse × EffectiveClipMultiplier`, and acceptance additionally requires it to exceed
`StarDetectorParams.Sensitivity`. So:

> **Every accepted star's gate statistic strictly exceeds `EffectiveSensitivityGate = max(Sensitivity,
> InertSensitivityBound)` — and therefore ANY order statistic over accepted stars, at ANY rank, on ANY subset of
> frames, is bounded below by that gate.**
>
> **Corollary:** when the optimizer lands a gate at or above `TargetSensitivity` (10), `ExposureIsNotTheLimit` is
> true **by construction**, whatever the sky contains.

`D02_rich_135mm` lands its effective gate at **16.7 / 50.0 / 34.3 / 10.0 / 14.0** across a 16× ladder over which
σ_focus improves **48 %**. Three of the five rungs are structurally silenced.

**This is why both earlier fixes failed their own pre-registered tests.** Wave 7's "change `NTarget`" moved the
RANK; wave 8's "widen the trigger" moved the DISPLAY. Neither touched the POPULATION. It is strictly stronger
than wave 7's leg 2, which established only that the *faintest accepted star* is pinned.

**And it killed this wave's own first candidate family before a line of it was implemented.** §3.4 of the design
had named four candidates — all four order statistics over accepted stars. The refutation cost a proof and a
column of numbers already on disk; implementing them would have cost a build and an arm. *The cheap refutation
arrives before the expensive confirmation, including refutations of your own plan.*

### §3.2 What ships, and why it is a probe rather than a formula

The candidates the gate **rejected** on the wing frames are the only population in a run not floored by the gate.
`WingRejectedFraction` = `rejected / (rejected + accepted)`, pooled over the outer third of the non-recovery
frames by distance from the fitted focus; `WingIsShedding` at ≥ 0.20; the ask is `max(existing, ×2 probe)`.

A **probe**, following `StarCountProbeFactor`'s precedent, because the run records how MANY candidates were
rejected out there and not what SNR they sat at. **Fabricating a magnitude was measured and rejected**: a
`(1/(1−f))²` form asked **1.25×** on `D16` at exactly the exposure where its σ_focus is minimised.

### §3.3 RULE W, and it discriminated

| candidate | W1 `D02`@0.5 ≥ 2× | W2 `D16`@2 < 1.25× | W3 converges | W4 `D16`@0.5 asks | verdict |
|---|---|---|---|---|---|
| shipped statistic | ✗ 1.00× | ✓ | ✓ | ✓ | fires on **neither** |
| wing fraction, `(1/(1−f))²` | ✓ 4.00× | ✗ 1.25× | ✗ | ✗ | fires on **both** |
| wing headroom vs the gate | ✓ 2.00× | ✓ | ✗ | ✗ | — |
| wing probe ALONE | ✓ 2.00× | ✓ | ✓ | ✗ | — |
| **`max(shipped, wing probe)`** | **✓ 2.00×** | **✓ 1.00×** | **✓** | **✓ 3.00×** | **ADOPTED** |

**Five of seven failed, three of them AFTER passing W1** — the clause that looks like the whole point. Both halves
of the winner are load-bearing: the accepted-star path alone fails W1, the probe alone fails W4.

On `D02` the adopted statistic asks 2× at 0.5 / 1 / 2 s and goes **silent at 4 s**, which is exactly where this
binary's σ_focus minimum sits (0.05208).

### §3.4 The ladder MOVED, and the premises were re-checked rather than the rule reused

Rule W was written against wave 7's σ ladder. [F54](followups.md) shows this binary's ladder differs — `D02`'s
optimum is at **4 s** here, not 8 s. So the rule was applied to **this binary's own ladder**, after checking that
every clause's premise survives on both:

| premise | wave 7's ladder | this binary's ladder |
|---|---|---|
| W1: `D02` gains from more than 0.5 s | 44 % better at 8 s | **48 % better at 4 s** |
| W2: `D16` is worse above its derived 2 s | 0.120 / 0.148 vs 0.064 | **0.204 / 0.197 vs 0.171** |
| W3: `D02`'s 8 s rung is at or past the optimum | optimum at 8 s | **optimum at 4 s** |
| W4: `D16`@0.5 s is worse than its 2 s | 0.352 vs 0.064 | **0.352 vs 0.171** |

**All four hold on both.** A moved instrument is a reason to re-check the rule's premises, not a reason to quietly
keep quoting the old numbers.

### §3.5 The verdict is reproduced END TO END by the shipped code

The offline scorer chose the candidate; the shipped `ExposureRecommender` then had to agree. Re-running the ten
rungs with the product implementation gives **W1 2.00× · W2 1.00× · W3 pass · W4 3.00×** — the same four passes.
The σ_focus landings are unchanged from the pre-implementation probe, so the statistic is inert on the optimizer,
which is what it should be (it is computed after the search).

**Tests: 8, four discriminating, each confirmed by NEUTRALIZING** — delete the probe ⇒ W1/W3/W4 fail; `max()` ⇒
plain assignment ⇒ W4 alone fails; fire on any rejection ⇒ W2 alone fails.

### §3.6 Still owed, and blocked structurally rather than by scheduling

The §3.5 population check — the verdict across all 20 synthetic datasets and the real bank — cannot run while
F32's arm is in flight: `optimize --per-run` writes back into the bank's run folders
([F15](followups.md#f15--optimize---per-run-overwrites-each-runs-stored-settings)) with no flag to suppress it, so
a population pass would race the arm on every folder. It runs after arm A, at arm A's exact invocation, which
makes it a free inertness control too.

---

## §3b — F52(c): the advice wave 8 refused to ship

Wave 8 shipped (a) and (b) and withheld (c) because advice from the statistic of the day *"would tell precisely
the users who most need a longer exposure that theirs is already fine"* — on the reporting user's own rig that
statistic read **S/N 1438.6 against a target of 10**. **The separation is kept exactly: facts about COST need no
statistic and are already shown; ADVICE needs one.**

`SearchExposureAdvice` is computed in `ComputeBaselineJAsync` — from the **seed evaluation, which runs before the
search** — so the answer to *"should I abort?"* was available in the first minute rather than after two hours. It
is absent when the wings are healthy, names **Cancel** (which exists), says what Cancel costs in the same
sentence, and quotes no exposure number.

**Tests: 4, three discriminating — and the multi-run one is corrected.** Its first version paired a shedding run
with a HEALTHY one, which the aggregation skips entirely, so "worst" and "average" were identical and it passed
under a deliberately-averaged implementation. **That is the second overclaim this wave caught by neutralizing.**

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

**0. A free control on a shortcut is worth more than the shortcut.** The fan-out existed to make F32's arm
affordable, and it is exactly what made it unreadable — 7 h 10 m of compute, no φ verdict. The only reason that is
KNOWN rather than believed is that the design attached a control to the shortcut before taking it, and the control
cost one re-measurement of eight runs that were being run anyway. **Every shortcut in this project should be
carrying one.**

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
