# Wave 25 — design (pre-registration)

**Written before any wave-25 measurement exists.** No `TestApp.exe`, no `dotnet build`, no `dotnet test` was run
by the agent that wrote this document. Everything below is fixed before the data, and the analysis agent is bound
by §11 and §13.

Predecessor: [`docs/synthetic-af-bank-followups-wave24-results.md`](synthetic-af-bank-followups-wave24-results.md),
**especially its §5.2 and §14**, which decide what this wave is.
Controller's live deviation log for wave 24: `/mnt/d/hf_w24/CONTROLLER_DEVIATIONS.md` (D1-D9).
Charter: [`docs/waves22+-handoff-prompt.md`](waves22+-handoff-prompt.md),
[`docs/waves22-27-autonomous-prompt.md`](waves22-27-autonomous-prompt.md).
Register: [`docs/followups.md`](followups.md).
Plan: [`plans/synthetic-af-bank-followups-wave25-plan.md`](../plans/synthetic-af-bank-followups-wave25-plan.md).

---

## 0. The vessel decision, first and in writing

**CONTINUE on `ghilios/synthetic-af-bank-followups-wave23`, PR #195 (OPEN, unmerged).**

One decisive fact, the same shape as wave 24's and a new instance of it: **wave 25's AFTER binary must carry
`476369a`.** That commit is P1 (`DegenerateReason`, `SampledHfrRange` on the degenerate path), P2 (the binning
revisit bound) and P3 (`currentFactor`/`appliedFactor`/`stepDeferredByBinning`/`binningDeferralBoundReached`).
Wave 25's whole design is a **paired BEFORE/AFTER arm whose two sides must differ by exactly one change**. The
BEFORE side is B14, the binary built at `476369a`, sitting on disk at `D:\hf_w24\exe`. A branch cut from
`develop` would not contain P1/P2/P3, so the AFTER binary would differ from the BEFORE binary by **four** changes
and every clause in RULE W25 and RULE N25 would be confounded.

**PR #195's length is a real cost and it is not the deciding one.** A stacked branch cut from `476369a` would
shorten #195 by exactly nothing that matters — the commits are the same commits — while adding a second review
target and a merge-order dependency, eleven hours before the owner's stop. Cosmetics do not outrank a
single-variable comparison.

**Contingency, pre-registered:** if the owner merges #195 before wave 25's build, cut fresh from the resulting
`develop` and verify P1/P2/P3 are present **by content, not by hash** — `grep -q DegenerateReasonHalfWidthUnresolved
Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/StepSizeRecommender.cs` **and**
`test -f Joko.NINA.Plugins/TestApp/SynthBank/BinningRevisitPolicy.cs`. Both must pass. If either fails, stop and
report; do not build.

---

## 1. Where the framing I was given is wrong

Four corrections. The first is arithmetic and it changes the shape of the wave. The second changes a denominator.
The third is the substance of the wave and the controller was right to demand it. The fourth is a drop.

### 1.1 The price is wrong by 75 minutes, because a "paired arm" is TWO arms

Wave 24 §14 priced wave 25 at **~3 h 15 m**, itemised as *"~1 h code + tests, ~42 m gate, ~1 h 20 m paired S1 arm
across the bank, ~15 m scoring"*. **The ~1 h 20 m is the price of ONE S1 arm, and a paired arm is two of them.**

Wave 24 is the document that forbids the shortcut, in its own §14: *"Pre-register that the comparison is against
wave 24's post-P2 baseline, which does not exist yet — which is a reason for wave 25 to run its S1 BEFORE arm on
B14 rather than reaching for wave 23's numbers."* Wave 23's `/mnt/d/hf_w23/v23/*__S1/` reports are on B13, which
lacks P2, lacks P1's schema and lacks [F79](followups.md)'s ASCII fix. They cannot be the BEFORE side.

**Measured, from the same instrument and the same scenario** — wave 24's own pricing rule — from
`/mnt/d/hf_w23/v23/*__S1/synth_validate_report.json` mtime deltas:

| | |
|---|---|
| deltas landing on S1 rows | **59.6 min** over 18 cells |
| the two S1 cells that produced no report (`D05_tec140_1000mm`, `D19_cygnus_deep_shed`) | **2 x 600 s** = 20 min, embedded in the following S0 cells' deltas |
| **S1 x 20, one arm, B13** | **~80 min** |

So the honest TestApp wall is **~80 m (BEFORE) + ~42 m (gate) + ~85 m (AFTER) = ~3 h 27 m**, and the critical path
with the code work overlapped into the BEFORE arm is **~4 h 15 m**, not 3 h 15 m. §12 prices it step by step and
gives a drop ladder with clock triggers so the wave lands under its own rules rather than by improvisation.

**This is not an argument against the wave.** It is an argument for saying so before starting, because a wave that
discovers at 04:30Z that it is 45 minutes over is a wave that shrinks a denominator at 04:30Z.

### 1.2 The blind population is FIFTEEN, not seventeen. Verify the count yourself — I did

The prompt says *"score on S1 across the 17 datasets that are NOT `D01`/`D02`/`D03`"*. The spec carries **20**
datasets (`<exe>\SynthBank\synthetic-bank-spec.json`, `D01`..`D20`, counted). 20 - 3 = 17 is correct arithmetic
against the wrong exclusion list.

**Two more S1 cells are already published in full and cannot be blind:**

| cell | where it was published | what was published |
|---|---|---|
| `D08_c11_2800mm`/S1 | wave 24 results **§4.3**, both arms | every round: bootstrap step, bin, recommendation, `wasCapped`, deferral reasons, R^2, and the whole terminal block |
| `D11_rc10_585_afbin2`/S1 | wave 24 **`B24-V6`**, and wave 21 before it | `trajectory [24, 41]`, `roundsUsed 2`, `converged True`, `finalStepSize 41`, `stepBehavioral 55.0`, `stepToleranceBand 22.0` — the entire terminal state |

And this design agent has now read both cells' per-round `stepRecommendation` blocks in full, which is how §2's
inertness argument was checked. **So the blind S1 population is 15** — `D04`, `D05`, `D06`, `D07`, `D09`, `D10`,
`D12`, `D13`, `D14`, `D15`, `D16`, `D17`, `D18`, `D19`, `D20` — with **five** labelled controls (`D01`, `D02`,
`D03`, `D08`, `D11`) run on both arms, reported in full, and in **no denominator of any rule**.

### 1.3 The discriminating variable is `SampledHfrRange`, the threshold is `HfrThresholdMultiple = 3.0`, and **neither was chosen by looking at data**

The prompt is right that an `R^2`-keyed gate misses both cells, and right that the stall is at least two
mechanisms. It is wrong in one implication: it treats "find the discriminating variable" as an open search over
`sampledHfrRange` / `halfWidth` / `degenerateReason` / `bootstrapStep` / the fit object. **It is not a search.
The recommender already contains the variable, the threshold and the correct remedy, and the defect is that two
code paths bypass them.**

`StepSizeRecommender` sizes the step from the half-width **W** of the band where fitted HFR climbs from its
minimum to `HfrThresholdMultiple = 3.0` times the minimum. It already publishes:

```csharp
public double SampledHfrRange { get; set; }             // max(HFR)/min(HFR) over the FITTED POINTS
public bool ReachedHfrBand => SampledHfrRange >= StepSizeRecommender.HfrThresholdMultiple;
public const double MaxHalfWidthSampledHalfSpanMultiple = 1.5;   // "trust the model this far past the data"
```

and already does the right thing when the sweep is too shallow **and the fit over-reaches**: cap `W` at
`1.5 x half-span`, set `WasCapped`, and converge over runs at `CappedGrowthRatio` (1.714x at 4 offset steps).

**Read the two converging control cells and the three stalling ones side by side. Every number below is from
published wave-24 artifacts.**

| cell | round | boot step | `sampledHfrRange` | `halfWidth` | `wasCapped` | `degenerateReason` | R^2 | outcome |
|---|---|---|---|---|---|---|---|---|
| `D11`/S1 | r0 | 14 | 1.2532 | 84.0 = 1.5 x 56 | **true** | null | 0.9997 | **converges** 14 -> 24 -> 41 |
| `D11`/S1 | r1 | 24 | 1.6999 | 144.0 = 1.5 x 96 | **true** | null | 0.9999 | " |
| `D08`/S1 | r0 | 21 | 1.2804 | 126.0 = 1.5 x 84 | **true** | null | 0.9994 | **converges** 21 -> 36 -> 62 |
| `D08`/S1 | r2 | 36 | 1.5842 | 216.0 = 1.5 x 144 | **true** | null | 0.9996 | " |
| `D01`/S1 | r0 | 2 | **1.4628** | **6.0877** | **false** | **null** | **1.0000** | **stalls at 2 vs 8** |
| `D02`/S1 | r0 | 2 | **1.2216** | NaN | false | `half-width-unresolved` | -0.2741 | **stalls at 2 vs 6** |
| `D03`/S1 | r0 | 4 | **1.0799** | NaN | false | `half-width-unresolved` | 0.9835 | **stalls at 4 vs 16** |

**All five cells have `sampledHfrRange` far below 3.0. All five sweeps failed to sample the band the step is sized
from. Two converge and three stall, and the difference is entirely whether `MaxHalfWidthSampledHalfSpanMultiple`
got applied.**

- `D11` and `D08` converge because the fit over-reached, the cap bound, and `W` became `1.5 x half-span`
  **exactly** — check the arithmetic in the table: 1.5 x 56 = 84, 1.5 x 96 = 144, 1.5 x 84 = 126, 1.5 x 144 = 216.
  Four for four. The cap is not a safety net here; it **is** the convergence mechanism.
- `D02` and `D03` stall because `FindHalfWidth` returned NaN on both sides, so `Recommend` takes the
  `half-width-unresolved` **early return at line 298 and never reaches the cap at line 309**. The recommendation
  is the caller's own current step, held. `D03`'s own harness assertion says so out loud and is already flagged
  as a failure in the published artifact: *"A4 v=2 WasCapped=False but truth predicts True (halfWidth=46.1 vs cap
  boundary 24, ratio 1.92)"*. **The harness's truth model already asserts the correct behaviour on `D03`. I did
  not invent it.**
- `D01` stalls because the fit **under-reached**: `halfWidth = 6.0877` against a cap boundary of 12.0, so
  `if (halfWidth > maxHalfWidth)` is false and nothing binds. **The cap is an upper bound and there is no lower
  bound.** A hyperbola fitted to a 1.46x slice of curve interpolates that slice perfectly — R^2 = 0.9999999999999
  — and gets its asymptotic slope badly wrong, so the extrapolated 3x crossing lands *inside* the sampled span.
  R^2 is measuring the wrong thing, exactly as [F21](followups.md) says.

**The asymmetry has already been named in this very file, about a different bound.** `MinHalfWidthSampledHalfSpanMultiple`'s
doc comment reads: *"The recommender has always bounded how far one run may WIDEN the sweep ... It had no bound
on how far one run may NARROW it, and that asymmetry is what lets a run ... collapse the sweep in a single step."*
Wave 7 fixed that asymmetry for the **detectability** bound. **The identical asymmetry in the band bound is
unfixed, and it is what `D01` falls through.**

So the rule, stated once:

> **When the sweep demonstrably did not sample the 3x band, the answer the data supports is the one the cap
> already gives: `W = MaxHalfWidthSampledHalfSpanMultiple x half-span`. Take it as a FLOOR as well as a ceiling,
> and take it on the `half-width-unresolved` exit too.**

**Not one number in that sentence was chosen by looking at a measurement.** `3.0` is `HfrThresholdMultiple`, the
constant that *defines* what the step is sized from. `1.5` is `MaxHalfWidthSampledHalfSpanMultiple`, the constant
that already governs the majority path. Both predate this series. **A threshold that cannot be moved by the data
cannot be contaminated by it** — which is the real answer to the blindness question, and §10 says what blindness
this wave still has to buy (the outcome) and how.

It also disposes of the [F14](followups.md)/`S16`/`D20` fence cleanly: the fence forbids **harvesting a bar from
the values in hand**. Nothing here is harvested. Wave 24's `D24` correctly refused to invent a
`sampledHfrRange` bar with the values in front of it; this design does not invent one either — it reads the bar
off the product's own definition of the quantity.

**Two mechanisms, one variable, two edits — which is exactly what the prompt asked for and it is worth being
precise about.** The prompt says *"treat the non-degenerate stall as its own mechanism with its own fix, or say
why it cannot be."* It is its own mechanism (a missing lower bound on a computed `W`) with its own fix (§3, P4a),
in a different place in the function from the degenerate one (an early return that skips the bound entirely,
§3, P4b). They share a decision variable and a constant. Reporting them as one thing would be wrong; fixing them
with two different numbers would also be wrong.

### 1.4 One-sidedness is STRUCTURAL here, not a declaration

The prompt requires the wave to *"declare the threshold ONE-SIDED in writing"* because `D24` found the wide end
empty (0 of 7 S2 cells degenerate). **This design is one-sided by construction, which is stronger than a
declaration**: the change is a `Math.Max` against an existing ceiling. It can only ever raise `W`, it engages only
when `SampledHfrRange` is finite and `< 3.0`, and on `SampledHfrRange >= 3.0` it is a literal no-op with no code
path to take. The wide end — a sweep that sampled *too much* curve and should shrink — is **not addressed by any
clause in this wave**, is **unmeasured rather than untriggered**, and `RULE G25`'s `G25-P3c` turns the one-sidedness
into a measurement on eight real landings rather than a promise.

### 1.5 `lumos`'s zero-star frame: real, and NOT this wave's. Dropped, with the reason

It is not a rabbit hole. It is a genuine goal-1 item on a real dataset and wave 24 scoped it precisely (~10 m of
`af-fit`/`review` on `AutoFocus_20260708_231255/attempt01` at two named focuser positions, to decide whether the
zero-star frame is a *frame* property or a *parameter-space* property).

**It is dropped from wave 25 anyway, on the clock, and the reason is not that it is worthless.** It carries no
rule, it gates no decision this wave makes, and §1.1 has already established the wave is ~45 minutes tighter than
it was priced. Ten minutes of TestApp is ten minutes the critical path does not have. **It belongs at the top of
wave 26**, which wave 24 sized at ~30 m (the P23 flat-direction arm) and which has room. Recorded in §12's drop
ladder as **not scheduled**, not as a drop that fires under pressure — a drop taken in advance is a decision, a
drop taken at 04:40Z is an accident.

### 1.6 Is a fix wave the right call at all?

Yes, and the register is not near dry. Against the owner's three goals:

- **Goal 2 (step-size accuracy)** is served directly and with product code: three published cells stall at 2, 2
  and 4 against truths of 8, 6 and 16 — a 4x, 3x and 4x under-recommendation delivered to a user's profile as a
  recommendation, and on `D01` with no qualification at all because the fit is not degenerate.
- **Goal 1 (bridge detected gaps)** is served secondarily: `W25` measures the bridge on a blind 15.
- **Goal 3** is untouched and says so, as wave 24 did.

Wave 24 was not dry (`F79` verified and extended, a new derivation-drift entry, `F34`'s `D01` attribution
corrected, the trap list corrected). Wave 25 opens with a product defect already localised to two lines. **The
two-consecutive-dry-waves stop is not armed and is not in view.**

---

## 2. THE PRE-REGISTRATION FENCE — what I read, what I did not, and what binds the analysis agent

| | |
|---|---|
| **MAY read, and did** | **source, in full** (`StepSizeRecommender.cs`, `StarDetectionOptimizerWizardVM.cs:286-388, 4110-4125`, `SynthValidateRunner.cs:525-565, 790-800`, `SynthValidationReport.cs:67-91`); wave 23's and wave 24's published results and register entries; `/mnt/d/hf_w24/CONTROLLER_DEVIATIONS.md`; the spec's **dataset list** and its `maxRounds`; per-cell **wall times** from wave 23 `run.log`/report mtimes, for pricing; wave 24's instruments' **text**, to design `RULE V25`'s FAIL end |
| **MAY read, and did, and it is DECLARED as a value read** | the per-round `stepRecommendation` and `terminal` blocks of exactly **five** S1 cells, all previously published: `/mnt/d/hf_w24/after/{D01_ultrawide_40mm,D02_rich_135mm,D03_redcat_250mm,D08_c11_2800mm,D11_rc10_585_afbin2}__S1/synth_validate_report.json`. This is how §1.3's argument was checked and how §8's arithmetic prediction was computed. **All five are labelled controls, excluded from every denominator in every rule below** |
| **MAY NOT read, and did not** | any `stepRecommendation` field — `sampledHfrRange`, `halfWidth`, `wasCapped`, `degenerateReason` — of **any other dataset, at any scenario**. Not from `/mnt/d/hf_w24/regress/` (S0 x 20), not from `/mnt/d/hf_w24/after/` or `before/` (S4), not from `/mnt/d/hf_w24/census/` (S2), not from `/mnt/d/hf_w23/v23/`. The only probe run against the gate logs was `grep -c -i 'sampledHfrRange'`, which returned **0 for 8 of 8** — an existence probe that returned "the field is not in the log" and leaked no value |
| **counts and schema only** | 20 datasets in the spec; `maxRounds = 4` on wave 23's and wave 24's S1 reports; 18 of 20 S1 reports produced in wave 23 and the two absent cells' **names**. All three are addressability ([F74](followups.md)), not outcomes |

**Wave 24's `D01`/`D02`/`D03` stall values are in the published results document and are quoted in §1.3.** They
are not blind, they are not in a denominator, and no threshold in this wave was derived from them (§1.3).

**What binds the analysis agent.** It may run **no** `TestApp.exe`, **no** build and **no** test, and may write
nothing under `/mnt/d/hf_w25/{gate,before,after}`. It may not invent a bar `RULE W25`, `RULE D25` or any
"reported, no bar" clause does not carry. It must apply §5-§9's branch tables **as written**, including the
`W-UNEXERCISED` and `N-UNEVALUATED` ends, and must report an empty denominator as UNEVALUATED and never as
`1.0000`.

---

## 3. What SHIPS, the ship rule, and the UNSHIP rule — all fixed and committed BEFORE the data

Two changes. **P4 is the product change; P5 is a harness observability correction.** The charter's §0.1 permits an
unconditional ship where the ship is not what a rule decides; P5 is in that class and says so, P4 is not.

### 3.1 P4 — the product change: a sweep that never sampled the band gets the widest step it supports

`Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/StepSizeRecommender.cs`.

**P4a — the missing lower bound (the `D01` mechanism).** Immediately after the existing cap block (line ~309-313),
before the F18 detectability bound:

```csharp
// The cap trusts the model at most 1.5x the sampled half-span PAST the data. It has never had a mirror:
// a fit whose extrapolated 3x crossing lands INSIDE the sampled span is trusted completely, even when the
// sweep's own HFRs prove the 3x band was never reached. A hyperbola fitted to a 1.5x slice of curve
// interpolates that slice perfectly and gets its asymptote wrong, so the crossing comes back too SMALL and
// the recommender answers "hold what you have" from a fit with R^2 = 1.
var wasBandFloored = false;
if (BandDemonstrablyUnsampled(sampledHfrRange) && maxHalfWidth > 0.0 && halfWidth < maxHalfWidth) {
    halfWidth = maxHalfWidth;
    wasBandFloored = true;
}
```

**P4b — the exit that skips the bound entirely (the `D02`/`D03` mechanism).** At the
`DegenerateReasonHalfWidthUnresolved` return (line ~298), when the band is demonstrably unsampled, **do not take
the degenerate exit**: set `halfWidth = maxHalfWidth`, `wasBandFloored = true`, leave `DegenerateReason` null, and
continue down the ordinary path (detectability bound, `ResolvePointsPerSide`, `ClampStep`).

> **Why this is not degenerate, and it matters for the UI.** `IsDegenerate` means "this is a HELD value, not a
> measurement". A floored recommendation is a **measurement of the sweep**: the sweep's own HFRs prove the 3x
> crossing lies beyond everything sampled, so the honest answer is "widen to the most this data supports". That
> is a statement with content, and `WasCapped`'s existing doc comment describes it word for word — *"a deliberate
> partial step toward the answer rather than the answer: re-run with it and the next sweep, being deeper,
> produces a better-grounded one."*
>
> **`D24-A`'s schema invariant survives**: `halfWidth == NaN` **iff** `degenerateReason != null` still holds,
> because the floored rounds now have a finite `halfWidth` **and** a null reason.

**The could-not-look guard, and it is the single most important line in the change.**

```csharp
/// <summary>
/// True only when the sweep's HFR dynamic range was MEASURED and is below the band the step is sized from.
/// NaN means "could not look" and must NOT engage the floor: `x < 3.0` is false for NaN, but writing the
/// condition that way makes the safety depend on IEEE-754 trivia. F79's defect shape is a check that fails
/// CLOSED to a value instead of reporting that it could not look, and this is the mirror of it.
/// </summary>
private static bool BandDemonstrablyUnsampled(double sampledHfrRange) =>
    double.IsFinite(sampledHfrRange) && sampledHfrRange < HfrThresholdMultiple;
```

**The scope limit, declared:** the `no-fit` and `non-finite-vertex` exits are **NOT** floored. `no-fit` has no fit
object and therefore no sampled span; `non-finite-vertex` has no origin to measure an offset from. Wave 24's
`D24-B` measured **0** occurrences of either in the field (`half-width-unresolved` 2 of 2, `no-fit` 0,
`non-finite-vertex` 0), so this limit costs nothing that has ever been observed. It is a limit, not an oversight,
and `T7` tests it.

**The new observable (F76 — a fix that cannot report whether it engaged is not finished):**

| where | member |
|---|---|
| `StepSizeRecommendation` | `public bool WasBandFloored { get; set; }` |
| `OptimizationSummary` (wizard) | `public bool StepSizeWasBandFloored { get; set; }`, assigned at `StarDetectionOptimizerWizardVM.cs:~4118` alongside `StepSizeWasCapped` |
| `StepRecommendationSnapshot` (report) | `[JsonProperty("wasBandFloored")] public bool WasBandFloored { get; set; }` |

**`WasCapped` is NOT overloaded, and that is a deliberate decision with a measured reason.** The harness's `A4`
assertion compares `WasCapped` against a truth model. On `D01`/S1 r0 it currently reads *"WasCapped=False matches
truth (halfWidth=10.4 vs cap boundary 12, ratio 0.87)"* — setting `WasCapped` there would flip a passing assertion
to a failure for a reason that has nothing to do with the fix. Keeping the flags separate makes
`N25-D`'s prediction ("A4 is bit-identical across arms on 100% of paired cells") **provable from the code**: the
floor branch never writes `wasCapped`, and it is entered only when `halfWidth < maxHalfWidth`, which is exactly
when the cap branch was not entered. The two are mutually exclusive by construction.

**Known residual, reported and NOT fixed here:** `D03`/S1's `A4` will keep failing, because the harness's truth
model predicts `WasCapped=True` where the product now says `WasBandFloored=True`. That is a harness truth-model
gap, it is named in §13 as a register entry and a wave-26 item, and **bundling it into a product wave would put
an assertion change and a product change in the same arm.**

**UI (F79's shape, in the product).** `StepSizeText` currently gates the "partial step" wording on
`StepSizeWasCapped`. Change the gate to `StepSizeWasCapped || StepSizeWasBandFloored` and nothing else: the
existing wording — *"this sweep reaches {N}x its minimum HFR and the step is sized from where HFR reaches 3x ...
re-run auto-focus to refine"* — is already correct for a floored recommendation, and the sampled range it quotes
is the very number that engaged the floor. **No new XAML row**, so the `DataTemplates.xaml` invariant is not
triggered; `StepSizeText` already renders in the wizard's step block.

**SHIP RULE for P4, fixed before the data:**

> **P4 SHIPS if and only if `RULE G25` is not `G-DIRECTION-VIOLATED` AND `RULE N25` returns `N-PRESERVED`.**
> `RULE W25`'s verdict does **not** decide the ship. A `W-UNEXERCISED` — the blind 15 never engaged the floor —
> still ships, on the labelled controls plus the unit tests, which is precisely the licence wave 24 gave P2 and
> for the same stated reason.

### 3.2 P5 — the harness message stops naming a cause it never checks ([F34](followups.md))

`Joko.NINA.Plugins/TestApp/SynthValidateRunner.cs:551-553` prints, on **every** stall:

```
"stalled (round applied nothing, but step {n} is outside the {b} tolerance band of step_behavioral {s}) -- "
+ "a no-op recommendation from a degenerate fit, not convergence"
```

It asserts a cause it never checks. Wave 24 measured the consequence: `D01`/S1 is **not degenerate**
(`degenerateReason = null`, `halfWidth = 6.0877`, R^2 = 0.99999999999994) and the harness blamed a degenerate fit
anyway — and wave 23's register entry then grouped `D01` with `D02` and `D03` on the strength of it. **A wrong
message became a wrong register entry.**

Fix: read `roundReport.StepRecommendation.DegenerateReason` and branch. When non-null, keep today's wording with
the reason token appended. When null, say what is actually true, quoting the number that explains it:

```
"... -- a no-op recommendation from a NON-degenerate fit (sampled HFR range {r:0.###}x, band 3x), not convergence"
```

**SHIP RULE for P5: UNCONDITIONAL.** It changes no number, it is the deliverable [F34](followups.md)'s tail asked
for verbatim, and `RULE M25` measures it with its FAIL end on published artifacts.

### 3.3 The UNSHIP rule, with the restore target named exactly

Wave 24 §10.2 records that P2's revert boundary was specified as one file and landed as two, and asks the next
wave to name the exact restore target. Done, and the file-sharing problem is handled:

| | files touched | backup taken |
|---|---|---|
| **P4** | `StepSizeRecommender.cs`, `StarDetectionOptimizerWizardVM.cs`, `SynthValidationReport.cs`, `SynthValidateRunner.cs` (the snapshot assignment at ~797 only) | `/mnt/d/hf_w25/w25_code_backups/pre_P4/` |
| **P5** | `SynthValidateRunner.cs` (the stall message at 551-553 only) | `/mnt/d/hf_w25/w25_code_backups/post_P4_pre_P5/` |

**P4 and P5 share `SynthValidateRunner.cs`, so the backup set is taken TWICE**, before each edit. To unship P4
while keeping P5: restore all four files from `pre_P4/`, then re-apply P5 from the diff
`post_P4_pre_P5/SynthValidateRunner.cs` -> the final file, limited to lines 551-553. Every restore verified
`cmp`-identical. **`cp` and byte backups, NEVER a VCS revert** — the tree carries this uncommitted
pre-registration.

### 3.4 Tests — seven new, four shown RED against pre-change behaviour by named mutants

Suite baseline **3966** by COUNT (wave 24 §15, controller-verified). Wave 25 expects **3973**.

| # | test | file | pre-change |
|---|---|---|---|
| **T1** | `UnderReachedFit_WithBandUnsampled_FloorsHalfWidthToTheSweepBound` — the `D01` shape: a fit whose 3x crossing lands inside the sampled span while `SampledHfrRange < 3` gets `halfWidth == 1.5 x halfSpan` and `WasBandFloored` | `StepSizeRecommenderTests.cs` | **MUST FAIL.** Mutant: delete the P4a block |
| **T2** | `HalfWidthUnresolved_WithBandUnsampled_RecommendsTheSweepBound_NotTheHeldStep` — the `D02`/`D03` shape | `StepSizeRecommenderTests.cs` | **MUST FAIL.** Mutant: restore the early `return Degenerate(...)` |
| **T3** | `HalfWidthUnresolved_WithBandUnsampled_IsNotReportedAsDegenerate` — `DegenerateReason == null`, `halfWidth` finite, `D24-A`'s invariant intact | `StepSizeRecommenderTests.cs` | **MUST FAIL.** Same mutant as T2 |
| **T4** | `StallMessage_NamesTheNonDegenerateCause_WhenDegenerateReasonIsNull` — P5 | the `SynthValidateRunner` test file wave 24 added to | **MUST FAIL.** Mutant: restore the unconditional string |
| **T5** | `BandReached_LeavesTheRecommendationBitIdentical` — `SampledHfrRange >= 3` is a literal no-op | `StepSizeRecommenderTests.cs` | companion, passes pre-change |
| **T6** | `SampledHfrRangeNaN_DoesNotEngageTheFloor` — could-not-look, the F79 mirror | `StepSizeRecommenderTests.cs` | companion, passes pre-change |
| **T7** | `NoFitAndNonFiniteVertexExits_AreNotFloored` — the declared scope limit | `StepSizeRecommenderTests.cs` | companion, passes pre-change |

The four MUST-FAIL tests reference members that do not exist pre-change and therefore **cannot be demonstrated red
by checking out old source** — that is why each names a **mutant**: a specific one-block reversion inside the new
code that restores the pre-change behaviour, applied with `cp` from a byte backup, run, and restored with a
`cmp`-verified copy. This is wave 24's §10.4 mechanism and its exact wording is required in the evidence file
`/mnt/d/hf_w25/prechange_test_evidence.txt`.

---

## 4. RULE V25 — the `--verify-derivation` pre-flight. Blocking, and it runs before anything

[D9](../../mnt/d/hf_w24/CONTROLLER_DEVIATIONS.md) offered two remedies. **Take (b), the post-derivation existence
check, and extend it to the class.** (a) — parameterise the wave id once and derive nothing — is the better end
state and the wrong thing to attempt in a single wave: it requires editing five instruments that are deliberately
carried forward unchanged, and **editing an instrument mid-series is how it stops being the same instrument.**
Move (a) to wave 26 or later. That is wave 24 §9.2's own recommendation and I concur with it unchanged.

`verify_derivation_w25.py <dir>` checks, over every `*.py` and `*.sh` in `dir`:

| clause | check |
|---|---|
| **`V25-A`** | every `os.path.join(HERE, "X")` target and every imported sibling module **exists on disk**. Rate over targets found; **0 targets found is `V-UNEVALUATED`, never 1.0000** |
| **`V25-B`** | no `w24` / `W24` / `G24` / `B24` / `R24` / `D24` / `prov_w24` / `hf_w24` token outside the declared `ALLOW` list. Count |
| **`V25-C`** | no **typed** ordinal or count in a printed string literal: any literal containing `nine`..`sixteen`, `NINE`..`SIXTEENTH`, or a `waves 11/12/...` style enumeration, on a line that does not also contain `len(` or a `%` format substitution. Count |

`ALLOW` is a declared constant, one entry per line with a reason: `/mnt/d/hf_w24/exe` (the BEFORE binary),
`/mnt/d/hf_w24/after` (RULE M25's FAIL-end fixture), `/mnt/d/hf_w24/gate` (`G25-P3c`'s baseline landings),
`/mnt/d/hf_w23/gate` (`G25-P4`'s FAIL end), `/mnt/d/hf_w23/v23` (`N25-E`), and lines inside a
`PRIOR_BUILD_IDS` literal (historical labels of past waves' binaries, deliberately not rewritten).

```
V25-A < 100 %  OR  V25-B > 0  OR  V25-C > 0     ->  V-DRIFT   (BLOCKING: fix and re-run; no measurement starts)
otherwise                                        ->  V-CLEAN
```

**Both ends measured on real artifacts, FAIL end first, and the FAIL end is published prior work.** Run against
`/mnt/d/hf_w24/` it must return `V-DRIFT` and must name at least:

- `prov_w24.py:179` — `"differs from waves 11/12/13/14/15/16/17/18-B1/18-B2: YES"`, **nine waves typed while
  thirteen are checked**, twenty lines from the count line the charter records as fixed (`V25-C`)
- `prov_w24.py:4,5,227,304` — the file's own printed name is `prov_w23.py` (`V25-B`)
- `v23_fingerprint_w24.py:2,171` — *"the wave-21 synth-validate reports"* / *"38 of 38 wave-21 f21 files"* on a
  population that is **wave 23's** (`V25-B`)

Three of wave 24's eight drifts, in one second, on files that are on disk right now. `--self-test` demonstrates
both directions and runs **before** the check is quoted.

---

## 5. RULE G25 — the gate. One new binary, the FIFTEENTH

`optimize --per-run --max-evals 250`, mixed real + synthetic, sequential, one `TestApp.exe`, no NINA, no fan-out,
pinned `D:\hf_w11\pinned_settings_w11.json` md5 `a67ffc06164c81613aef5c4f8324b9b8`, **not re-pinned**
([F63](followups.md)(b)). Eight runs: `toml999`, `CWhiteFocus`, `uneven`, `muggsie`, `mccomiskey`,
`D18_m24_deep_shed`, `D19_cygnus_deep_shed`, `D20_m24_bright_control`.

### 5.1 Do I expect the gate to move?

**`FinalJ`: no, and it must not.** `StepSizeRecommender` is called **after** optimization, on the winning fit; it
contributes nothing to the objective. Wave 24 changed the same class and reproduced K8 bit-identically on 8 of 8.
Any `FinalJ` movement is `G-STOP`.

**`RecommendedStepSize`: possibly, and that is the point.** Unlike wave 24's P1, this change is **not** inert on
the shipping path by design — it exists to move a recommendation. Whether it moves on these eight landings is
unknown to me (§2: `sampledHfrRange` is not printed in the gate log, `grep -c` returned 0 for 8 of 8, and I read
no landing's step block). **Both answers are real results and neither is a failure.**

### 5.2 The clauses

| clause | statement | source of truth |
|---|---|---|
| **`G25-1`** | `FinalJ` **bit-identical** to K8 at all sixteen digits, 8 of 8 | the landing JSON's `FinalJ`, read as a field |
| **`G25-2`** | aggregate summaries present, 8 of 8 | landings |
| **`G25-P1`** | `optimize/seed` reads `NoiseReductionRadius=3`, 8 of 8 — the anti-leak clause | the log's PARAMS-DUMP block |
| **`G25-P2`** | exactly one `optimize/detected`, `optimize/baseline`, `optimize/seed` **block** each, 8 of 8, parsed as `BEGIN`/`END` pairs in Python on bytes | the log |
| **`G25-P3a`** | `RecommendedStepSize` present and finite, 8 of 8 | the landing JSON |
| **`G25-P3c`** | **NEW — the directional clause.** Each run's `RecommendedStepSize` against wave 24's eight integers (`CWhiteFocus` 101, `D18` 18, `D19` 35, `D20` 19, `mccomiskey` 31, `muggsie` 550, `toml999` 16, `uneven` 488), classified `UNCHANGED` / `WIDENED` (strictly greater) / `NARROWED` (strictly less) | the landing JSON, both waves |
| **`G25-P4`** | 0 bytes >= `0x80` in the gate logs, counted in Python **on bytes, never `grep`** — because `grep` is the instrument [F79](followups.md) breaks | the log files |

**`G25-P3c` is this wave's one-sidedness, measured on eight real optical trains.** The change is a `Math.Max`
against an existing ceiling; it has no code path that lowers a recommendation. So:

- **`NARROWED >= 1` is proof the shipped implementation is not the pre-registered one** and is an immediate STOP.
- `WIDENED >= 1` is the change engaging on real data. Each is **named with both integers**. Not a failure.
- `UNCHANGED == 8` is recorded as **`P3c-INERT-ON-THE-GATE`** — these eight sweeps reached the band or were
  already capped — and is neither a pass nor a fail.

Its FAIL end is demonstrated **before** the gate runs, on a **copy** of a real wave-24 landing JSON with
`RecommendedStepSize` decremented by one, which must produce `NARROWED` and `rc=1`.

### 5.3 The branch table, fixed before the data

```
G25-P3c NARROWED >= 1                            ->  G-DIRECTION-VIOLATED  (STOP; P4 does not ship; §3.3 unship)
else G25-1 < 8 of 8                              ->  G-STOP                (STOP; the coordinate system moved)
else any of G25-2 / P1 / P2 / P3a < 8 of 8       ->  G-PARTIAL             (arms may NOT start)
else G25-P4 != 0 non-ASCII bytes                 ->  G-ASCII-INCOMPLETE    (NON-BLOCKING; recorded; arms may start)
else                                             ->  G-PASS                (arms may start)
```

All five reachable. `G-ASCII-INCOMPLETE` is deliberately non-blocking, as in wave 24: it re-verifies a fix already
verified once, and a re-verification that can block a wave is worth less than it costs.

### 5.4 The interlock ([F75](followups.md))

`G25_PASSED` is written by `score_g25_w25.py --arm-gate` and by nothing else, on `rc == 0` and only then, carrying
the `BuildId`. `G25_P3C_OK` is written by `score_p3c_w25.py` on `rc == 0` and only then. **The S1 AFTER arm
aborts unless BOTH markers exist.** Two markers rather than one because `G25-P3c` is a new clause and editing the
carried-forward gate scorer beyond its `sed` is exactly the derivation hazard `RULE V25` exists to bound.

**FAIL end first, and the missing-marker assertion is recorded this time.** Wave 24 §1.3 notes the plan's
`test ! -f G25_PASSED && echo "ASSERTED: the FAIL end left NO marker"` line was specified and not captured. It is
required in the artifact here, on both markers, with `tee -a`.

- FAIL end: `score_g25_w25.py --arm-gate /mnt/d/hf_w19/gate` -> `rc=1` on the BuildId novelty clause, **no marker**
- PASS end: `score_g25_w25.py --arm-gate /mnt/d/hf_w25/gate` -> `rc=0`, marker written

`prov_w25.py` carries **fourteen** prior BuildIds (thirteen plus `dd6ca32ef7f941c2a54753398cc2cf6b`) and its
printed word is computed from `len(PRIOR_BUILD_IDS)`, checked by `V25-C`.

---

## 6. The arms

**One arm, run twice.** `synth-validate`, scenario **S1**, **all 20 datasets**, `--max-rounds 4` (the shipped
default and wave 23's and wave 24's value, verified from both waves' reports), sequential, one `TestApp.exe`,
`timeout 600` per cell, `< /dev/null` on every invocation, `--out` asserted under `/mnt/d/hf_w25/`.

| arm | binary | tree | out |
|---|---|---|---|
| **BEFORE** | **B14, the fourteenth** — `D:\hf_w24\exe`, `476369a`. `TestApp.dll` `5398582df572485c245c2586cdcdbce3daa59e6291a5b5e95bad9d20cbbb53d8`, plugin `c067fb5057b332001a8507ee42d65df94b291141ff99cb31e95f250bd74985fb`. **Not rebuilt, not moved, not written into** | `476369a` | `/mnt/d/hf_w25/before/` |
| **AFTER** | **B15, the FIFTEENTH**, built once, never rebuilt mid-wave ([F53](followups.md)(c)) | wave 25's commit | `/mnt/d/hf_w25/after/` |

**`timeout 600` is kept, deliberately.** Wave 23 lost `D05_tec140_1000mm`/S1 and `D19_cygnus_deep_shed`/S1 to it.
Raising it would make wave 25's instrument differ from the one that produced every prior S1 number, and it would
uncap the AFTER arm — whose cells take **more** rounds where the floor engages. Keeping it means both sides fail
identically where they fail, which is what a paired arm needs. **Both cells are named here, before the data**, and
`W25-V1` / `N25-V1` set minimum denominators that survive losing them.

**Binary identity is the dll sha256 pair, never the apphost `TestApp.exe`** — [F66](followups.md)'s seventh
counter-example, wave 24's apphost being byte-identical across two different binaries.

---

## 7. RULE W25 — did the floor bridge the stall, on a population that did not motivate it?

**Population: the 15 blind datasets' S1 cells, paired.** The five labelled controls are excluded from every
denominator in this rule.

### 7.1 Validity gates

| clause | statement |
|---|---|
| **`W25-V1`** | **paired blind cells >= 12** (expected 15; `D05` and `D19` are the named timeout risks). A cell present on one arm and absent on the other is **NAMED on both sides**, never silently dropped. Cells requested but not run are named, not subtracted |
| **`W25-V2`** | `profileId`, `specSha256`, `maxRounds`, `fitInputs` cardinality **1** within each arm, read as the binary resolved them; `fitInputs` **equal across arms**; `specSha256` equal to `bf10522e670a119e260a8b3d06c2cc6ec52ce513dde25ca9ca13f426c38672d6` on both |
| **`W25-V3`** | exactly **two** distinct binaries, each used for exactly one arm, both dll pairs matching the recorded values |
| **`W25-V4`** | `G25_PASSED` **and** `G25_P3C_OK` present, the former carrying B15's `BuildId`. The AFTER driver aborts without both |
| **`W25-V5`** | **schema addressability, and F68's "which copy".** Every AFTER round with a non-null `stepRecommendation` carries `wasBandFloored` as a **bool**, not missing and not null. Rate with denominator printed. **`wasBandFloored` does NOT exist on the BEFORE arm and NO clause reads it there** |

### 7.2 The clauses

| clause | statement | denominator |
|---|---|---|
| **`W25-A`** | **the primary.** `terminal.converged == true`, reported as a BEFORE rate and an AFTER rate **over the same paired denominator**, plus a per-cell table classifying each as `BRIDGED` (false -> true), `BROKEN` (true -> false) or `SAME` | paired blind cells |
| **`W25-B`** | **the engagement marker** ([F76](followups.md)). Cells with `wasBandFloored == true` on at least one AFTER round; and the total count of floored rounds. Both printed | paired blind cells / AFTER rounds |
| **`W25-C`** | **landed in the band**: `abs(finalStepSize - stepBehavioral) <= stepToleranceBand`, BEFORE and AFTER. Separates "converged" from "ended up right", which `W25-A` conflates | paired blind cells |
| **`W25-D`** | **distance**: `abs(finalStepSize - stepBehavioral) / stepBehavioral`, BEFORE vs AFTER, classified `IMPROVED` / `SAME` / `REGRESSED`, `SAME` by exact `repr()` equality with **no epsilon** | paired blind cells |
| **`W25-K`** | **the labelled controls, in NO denominator, reported in full** — §8's arithmetic predictions, measured | 5 cells, named |

### 7.3 The branch table, fixed before the data

```
W25-V1..V5 any fails                                    ->  W-UNEVALUATED
else W25-B == 0 cells (the floor never engaged)         ->  W-UNEXERCISED   (NOT a pass and NOT a fail)
else W25-A BROKEN >= 1                                  ->  W-HARMFUL       (and RULE N25 decides the unship)
else W25-A BRIDGED >= 1                                 ->  W-BRIDGED
else (engaged, nothing bridged, nothing broken)         ->  W-INERT
```

All five ends reachable. **`W-UNEXERCISED` is pre-registered here, in these words, as a real result and not a
null** — wave 24's `B24` is the precedent and its licence transfers: it would license saying the mechanism is
**rarer than the three published cells suggest**, and it would license **nothing** about a rate, about coverage,
or about P4 being unnecessary.

**What `W-BRIDGED` licenses**, also fixed in advance: that the floor bridges stalls on datasets that did not
motivate it, at the measured rate over the printed denominator, on this scenario and this instrument. **It does
not license a comparison to wave 23's `V23-G` S1 `13 of 17`** — see §9.

---

## 8. Satisfiability: instrument precision, and population addressability

**Instrument precision — the three controls' outcomes are ARITHMETIC, computed here before the data.**
While the floor binds, `halfWidth = 1.5 x halfSpan` and `halfSpan = offsetSteps x step = 4 x step`, so
`step' = round(1.5 x 4 x step / 3.5) = round(1.714 x step)`:

| cell | boot | round 1 | round 2 | `stepBehavioral` | band | **predicted terminal** |
|---|---|---|---|---|---|---|
| `D01_ultrawide_40mm`/S1 | 2 | 3 | **5** | 8.0 | +/- 3.2 -> `[4.8, 11.2]` | **converged by round 2**, `finalStepSize` in band |
| `D02_rich_135mm`/S1 | 2 | 3 | **5** | 6.0 | +/- 2.4 -> `[3.6, 8.4]` | **converged by round 2** |
| `D03_redcat_250mm`/S1 | 4 | 7 | **12** | 16.0 | +/- 6.4 -> `[9.6, 22.4]` | **converged by round 2** |
| `D08_c11_2800mm`/S1 | 21 | — | — | 82.0 | +/- 32.8 | **UNCHANGED**: every round already capped, floor is a no-op |
| `D11_rc10_585_afbin2`/S1 | 14 | — | — | 55.0 | +/- 22.0 | **UNCHANGED**: same reason |

**The instrument resolves it.** `finalStepSize` is an integer and the bands are 3.2, 2.4 and 6.4 wide; the
predicted values sit 0.2, 1.4 and 2.4 inside their bands, not on a boundary. `--max-rounds 4` is enough for all
three with a round to spare.

**Stated honestly: this is a LOWER BOUND on the widening, not the trajectory.** Each round re-bootstraps at the
new step, so the next sweep is wider, its `sampledHfrRange` grows, and once it reaches 3.0 the ordinary geometry
takes over and can jump further than 1.714x. **The pre-registered bar is therefore terminal, not per-round:
`converged == true` with `finalStepSize` inside the band in `roundsUsed <= 4`, on all three of `D01`, `D02`,
`D03`.** If any of the three misses it, the shipped implementation is not the one designed here and §13 must say
so rather than reinterpret the bar. `D08` and `D11` must be **UNCHANGED** on all seventeen `N25-A` fields.

**[F34](followups.md)'s OWN pre-registrable bar, and it is in two halves that must be treated differently.**
The entry reads: *"Pre-registrable bar: `V23-G` on S1 above **13 of 17**, and all three cells reaching
`roundsUsed > 1`."*

| half | verdict | why |
|---|---|---|
| *"all three cells reaching `roundsUsed > 1`"* | **ADOPTED, and strengthened.** The bar above is `converged == true` inside the band in `roundsUsed <= 4` on `D01`, `D02` and `D03`, which implies `roundsUsed > 1` and is strictly harder | it is a statement about three named cells, unaffected by which binary measured them, and it is exactly what a stalled cell failing to leave round 1 means |
| *"`V23-G` on S1 above 13 of 17"* | **NOT ADOPTED.** It cannot be scored | `13 of 17` is a B13 number, F34 was written before wave 24 established that P2 changes the round loop and P1 changes the schema, and wave 24 §7 forbids the comparison in terms. §9.2's `N25-E` is what would make it scoreable again, and it is measured this wave rather than assumed |

**The class of change, declared before the arm ([F62](followups.md)).** P4 is **not** a rejection change: it moves
no frame and no star into or out of any fit. It reads `bestFit.Outputs` and `bestFit.Inputs`, both already fixed,
and changes only a scalar derived from them **after** fitting. Therefore `sigmaFocus`, `J`, `R^2`, reduced
chi-squared and the star counts are untouched by construction, and no clause here uses a within-fit statistic as a
vote. `G25-1`'s bit-identical `FinalJ` on 8 of 8 is the out-of-sample check that this declaration is true.

**Population addressability ([F74](followups.md)) — checked, not assumed:**

| | |
|---|---|
| S1 applies to all 20 datasets | wave 23 requested 20 and produced 18 reports; the 2 absent are named |
| the decision variable exists on **both** arms | `sampledHfrRange` shipped in P1 at `476369a` = B14; `wasCapped`, `halfWidth`, `degenerateReason` older still |
| the engagement marker exists on the **AFTER arm only** | `wasBandFloored` is new. **`W25-B`, `W25-V5` and nothing else read it, and only on the AFTER arm.** This is [F68](followups.md)'s "which copy of a field, and is it the live one", answered per clause |
| `stepBehavioral` / `stepToleranceBand` | in `terminal`, both arms, spec-derived and identical across arms — asserted by `N25-A`'s field list, not assumed |
| `RecommendedStepSize` on the gate | the **landing JSON**, not the log line. The log does not carry it and does not carry `sampledHfrRange` either (measured: `grep -c` = 0 on 8 of 8) |
| the FAIL-end fixture for `RULE M25` | `/mnt/d/hf_w24/after/D01_ultrawide_40mm__S1/synth_validate_report.json` **exists and trips the clause** — verified by reading it: `stoppedReason` contains *"from a degenerate fit"*, r0's `degenerateReason` is `null` |

---

## 8bis. The [F68](followups.md) matrix — every clause, on one page

F68 requires each clause to carry **P**opulation, **S**tatistic, **A**ggregation, **E**mpty-set answer, and
**FIELD** (*which copy is read, and is it the live one*), plus **(d)** instrument precision and **(e)** population
addressability. Nothing below is left to the scorer's discretion.

| clause | **P** population | **S** statistic | **A** aggregation | **E** empty set | **FIELD** — which copy, and is it live |
|---|---|---|---|---|---|
| `V25-A` | `os.path.join(HERE,…)` targets + sibling imports across `/mnt/d/hf_w25/*.py,*.sh` | target exists on disk | rate over targets found | **`V-UNEVALUATED`**, never `1.0000` | the file text on disk at check time, re-read per run |
| `V25-B` | same files | prev-wave tokens outside `ALLOW` | count | 0 tokens = clean | same |
| `V25-C` | same files | typed ordinals/counts in printed literals | count | 0 = clean | same |
| `G25-1` | 8 gate landings | `FinalJ` vs `K8`, 16 digits | count of exact matches / 8 | **`G-STOP`** (a gate with no landings is not a pass) | the **landing JSON**'s `FinalJ`, written by this run's binary |
| `G25-P2` | 8 gate logs | `BEGIN`/`END` **block** pairs, parsed in Python **on bytes** | count with exactly one block / 8 | `G-PARTIAL` | the log bytes. **Never `grep`** ([F79](followups.md)); the driver's convenience print counts LINES and disagrees ([D8](../../mnt/d/hf_w24/CONTROLLER_DEVIATIONS.md)) — the scorer's block count is the clause |
| `G25-P3c` | 8 gate landings | `RecommendedStepSize` vs wave 24's eight integers | 3-way classification, all three counts printed | `G-PARTIAL` via `G25-P3a` | the **landing JSON** on both sides — wave 24's from `/mnt/d/hf_w24/gate/`, this wave's from `/mnt/d/hf_w25/gate/`. Not the log; the log does not carry it |
| `G25-P4` | 8 gate log files | bytes `>= 0x80` | files with >= 1 such byte / 8, and the total byte count | 0 files = `P4-ASCII-CLEAN` | raw bytes of the file, `open(p,'rb')` |
| `W25-V1` | 20 requested S1 cells x 2 arms | cell has a report on **both** arms | paired blind count vs minimum 12 | `W-UNEVALUATED` | file existence; absent cells **named**, never subtracted |
| `W25-V5` | AFTER rounds with non-null `stepRecommendation` | `wasBandFloored` is a bool | rate, denominator printed | `W-UNEVALUATED` | `rounds[i].stepRecommendation.wasBandFloored`, **AFTER arm only** — the field does not exist on B14 |
| `W25-A` | paired **blind** cells (15) | `terminal.converged` | BEFORE rate, AFTER rate, per-cell 3-way table | `W-UNEVALUATED` | `datasets[0].scenarios[0].terminal.converged`, one copy, written by the runner at loop exit |
| `W25-B` | paired blind cells / AFTER rounds | `wasBandFloored == true` | cells with >= 1, and total rounds; **both denominators printed** | **`W-UNEXERCISED`** | as `W25-V5`. **Corroborated ([F66](followups.md)(c)):** every round claiming `wasBandFloored` must **also** satisfy `halfWidth == MaxHalfWidthSampledHalfSpanMultiple x offsetSteps x bootstrapStep` to `repr()` bit-identity — a second field that must move the same way, because a two-directional demonstration proves branches are *distinguishable*, not *correctly assigned*. **Verified on the published controls before the data**: `D11` r0 `1.5 x 4 x 14 = 84.0`, r1 `= 144.0`; `D08` r0 `= 126.0`, r2 `= 216.0` — four for four against the reports. Any uncorroborated round is **NAMED**, and the results document may not count it in `W25-B`'s numerator without saying so (a round whose fit dropped points has a shorter span and is a legitimate near-miss, not a fabricated engagement) |
| `W25-C` | paired blind cells | `abs(finalStepSize - stepBehavioral) <= stepToleranceBand` | rate, both arms | `W-UNEVALUATED` | all three from `terminal`; `stepBehavioral`/`stepToleranceBand` asserted **equal across arms** first |
| `W25-D` / `N25-C` | blind 15 / all 20 | `abs(finalStepSize - stepBehavioral)/stepBehavioral` | IMPROVED / SAME / REGRESSED, **all three counts printed**; SAME by exact `repr()`, no epsilon | UNEVALUATED | `terminal` |
| `N25-A` | paired cells whose **BEFORE** r0 is capped, band-reached, or `"NaN"` | 4 fields identical across arms | rate; **denominator printed** | **UNEVALUATED, never `1.0000`** | `rounds[0].stepRecommendation.{stepSize,halfWidth,wasCapped,degenerateReason}`. Doubles by `repr()`; `"NaN"` compared **as a string**, never coerced |
| `N25-B` | all 20 paired | converged BEFORE and not AFTER | count; must be 0 | UNEVALUATED | `terminal.converged` |
| `N25-D` | all 20 paired | round-0 `A4` `(verdict, detail)` sequence | rate of identical sequences | UNEVALUATED | `rounds[0].assertions[]`, **in order**; a length mismatch is a difference and prints both lengths |
| `N25-E` | cells present in **both** `/mnt/d/hf_w25/before/` and `/mnt/d/hf_w23/v23/*__S1/` | the 17 `R24-A` terminal fields | identical count / denominator | UNEVALUATED | wave 23's **published reports on disk**, read-only, fingerprinted BEFORE and AFTER (fingerprint class 4) |
| `N25-F` | all 20 paired | `terminal.stoppedReason` differs | reported, **no threshold**; each difference attributed or **NAMED** | 0 differences is a valid answer | `terminal.stoppedReason`, folded to ASCII for printing with the fold count printed |
| `M25-A` / `M25-B` | AFTER cells / `/mnt/d/hf_w24/after/*` | `"from a degenerate fit"` in `stoppedReason` **and** last round's `degenerateReason is None` | count; `M25-A` must be 0, `M25-B` must be >= 1 including `D01` by name | `M25-B` = 0 -> **`M-UNEVALUATED`** | `terminal.stoppedReason` + `rounds[-1].stepRecommendation.degenerateReason`. **The last round, not round 0** — the message describes the round that stopped the loop |

**(d) instrument precision** is §8's arithmetic table for `W25-K`; for `G25-1` it is sixteen significant digits by
`repr()`, never a `G6` console print ([F68](followups.md)(d)'s defect); for `N25-A`/`W25-D` it is `repr()`
bit-identity with no epsilon anywhere. **(e) population addressability** is §8's table above.

**Marker payload ([F74](followups.md)) — an interlock that transmits one bit where it could transmit the path is a
handshake with the payload thrown away.** Every marker this wave writes carries its evidence path, and every
consumer **parses the path out of the marker** rather than rebuilding it from a template:

| marker | written by | payload |
|---|---|---|
| `G25_PASSED` | `score_g25_w25.py --arm-gate`, on `rc == 0` only | `BuildId=…`, `gate=<dir>`, `landings=<n>` |
| `G25_P3C_OK` | `score_p3c_w25.py`, on `rc == 0` only | `gate=<dir>`, `baseline=/mnt/d/hf_w24/gate`, `unchanged=`/`widened=`/`narrowed=` |
| `W25_BEFORE_READY` / `W25_AFTER_READY` | `s1_arm_w25.sh`, **last**, and only if the manifest has >= 17 rows | `manifest=<tsv>`, `binary=<dir>`, `testapp_sha256=`, `plugin_sha256=`, `cells=<n>` |

**No `os.walk` fallback anywhere** ([F74](followups.md)), and **no marker is written by a human** — a step a person
performs is still a writer, and it is the weakest kind ([F75](followups.md)). The plan is grepped for this.

---

## 9. RULE N25 — do-no-harm. The clause that can UNSHIP P4

**Population: all 20 paired S1 cells, controls included.** Do-no-harm is not blind and must not be: a safety net
with a shrunken population is a smaller net. This is the treatment wave 24 gave `R24`.

| clause | statement |
|---|---|
| **`N25-V1`** | **paired cells >= 17** (expected 20; two named timeout risks plus one of slack). Absences named on both sides |
| **`N25-A`** | **round-0 inertness — the provable prediction.** For every paired cell whose **BEFORE** round 0 has `wasCapped == true` **OR** `sampledHfrRange >= 3.0` **OR** `sampledHfrRange == "NaN"`: round 0's `stepSize` (int, exact), `halfWidth` (double, `repr()` **bit-identity**, `"NaN"` compared **as a string** and never coerced to zero), `wasCapped` (bool) and `degenerateReason` (string or null) must be **identical across arms**. **Predicted 100 %** — the floor branch is entered only when `halfWidth < maxHalfWidth`, which is exactly when the cap branch was not, and it is guarded by `BandDemonstrablyUnsampled`, which is false for NaN. Denominator printed; **0 is `UNEVALUATED`, never `1.0000`** |
| **`N25-B`** | **convergence not lost.** Cells `converged == true` BEFORE and `false` AFTER. **Must be 0** |
| **`N25-C`** | **distance not worsened.** `W25-D`'s statistic over all 20 paired cells: `IMPROVED` / `SAME` / `REGRESSED` counts, all three printed |
| **`N25-D`** | **assertion `A4` untouched.** Round 0's ordered `A4` `(verdict, detail)` sequence identical across arms, per cell. **Predicted 100 %** — §3.1: the change never writes `wasCapped`, which is all `A4` reads |
| **`N25-E`** | **the comparability clause, and it costs ZERO TestApp minutes.** The BEFORE arm (B14) against wave 23's `/mnt/d/hf_w23/v23/*__S1/` (B13), on the cells both produced (expected 18), on the same **17 terminal fields** `R24-A` used. Reported: identical count over the printed denominator. **NO BAR** |
| **`N25-F`** | **prose, reported and NOT thresholded**, with its causes named in advance. `terminal.stoppedReason` differences BEFORE vs AFTER, each attributed to (i) **P5's message change** or (ii) **a cell that no longer stalls**. Any difference attributable to neither is **NAMED** |

### 9.1 The branch table, fixed before the data

```
N25-V1 fails                                     ->  N-UNEVALUATED  (P4 does NOT ship; it is not licensed by an unrun rule)
else N25-A < 100 %                               ->  N-BROKEN       (UNSHIP, §3.3)
else N25-B > 0                                   ->  N-BROKEN       (UNSHIP)
else N25-D < 100 %                               ->  N-BROKEN       (UNSHIP)
else N25-C REGRESSED > 0 AND IMPROVED == 0       ->  N-BROKEN       (UNSHIP)
otherwise                                        ->  N-PRESERVED    (P4 MAY SHIP)
```

All ends reachable. `N25-A` and `N25-D` are the two with **predicted answers**, which wave 24 rightly calls the
only kind worth running: both are derivable from the code before the arm, so a failure is unambiguous evidence
that the shipped code is not the designed code — not a judgement call about how much change is acceptable.

### 9.2 `N25-E` is the honest answer to "is wave 25 comparable to `13 of 17`?"

**No, not a priori, and the wave does not assert it either way — it measures it.**

Wave 23's `V23-G` S1 `13 of 17` was taken on **B13**: pre-P2, pre-P1's schema, pre-[F79](followups.md). Wave 25's
S1 numbers are on **B14** and **B15**. Different binary, and P2 in between is a change to the round loop itself.
**So wave 25's own before/after baseline is its own B14 BEFORE arm, full stop**, and every rate in §7 and §9 is
BEFORE-vs-AFTER **within this wave**.

`N25-E` then answers the question the series actually wants answered, for free: **how many S1 cells did P2
move?** Both outcomes are informative and neither is a failure —

- **all paired cells identical** -> P2 did not move S1, wave 23's `13 of 17` **is** comparable to wave 25's B14
  BEFORE arm, and the series recovers a baseline it had written off;
- **any differ** -> they are named, and `13 of 17` is formally retired as uncomparable, with the evidence.

Wave 24 quoted no new accuracy rate precisely so nothing would be contaminated. `N25-E` spends that restraint
correctly: it converts an assertion about comparability into a measurement of it.

---

## 10. RULE M25 — the message stops naming a cause it never checks. FAIL end on real published artifacts

**Population: all AFTER-arm cells (20 requested), plus a published FAIL-end fixture.**

| clause | statement |
|---|---|
| **`M25-B`** | **THE FAIL END, RUN FIRST, ON REAL PUBLISHED ARTIFACTS.** The same clause over `/mnt/d/hf_w24/after/*/synth_validate_report.json` must find **at least one** contradiction, and **`D01_ultrawide_40mm`/S1 must be among the names**. Names printed. If it finds none, the instrument is broken and `RULE M25` is `M-UNEVALUATED` |
| **`M25-A`** | AFTER-arm cells whose `terminal.stoppedReason` contains the substring `"from a degenerate fit"` while the **last round's** `stepRecommendation.degenerateReason` is `null`. **Must be 0** |
| **`M25-C`** | reported: the new message text, quoted verbatim, on every AFTER cell that still stalls |

```
M25-B finds 0, or D01 is not among the names   ->  M-UNEVALUATED
else M25-A > 0                                 ->  M-CONTRADICTED
else                                           ->  M-CORRECTED
```

**This is the clause about the wave's own deliverable whose FAIL end is measured on real artifacts** — the shape
wave 24's `G25-P4` predecessor used, and stronger, because the FAIL-end artifact is a *published prior result
whose defect this wave exists to fix*. It is run **before** the AFTER arm, so the instrument is validated against
a known answer before it is pointed at unknown data.

---

## 11. What the results document may and may not say

Fixed here so the analysis agent cannot invent it, and so a wave that finds nothing says so.

**MAY say**

- P4's verdict under `RULE G25` + `RULE N25`, in those rules' words.
- `RULE W25`'s verdict in **its** words, including `W-UNEXERCISED` as a real result.
- The three labelled controls' bridge, **as labelled controls, in no denominator**, against §8's pre-registered
  arithmetic bar.
- `G25-P3c`'s classification of the eight gate landings, each named with both integers.
- `N25-E`'s comparability finding, either way.

**MUST NOT say**

- **No comparison to wave 23's `V23-G` S1 `13 of 17` except through `N25-E`.** Different binary. §9.2.
- **No wide-end claim of any kind.** The wave-shrinking half of the problem is untouched by every clause here and
  is **unmeasured, not untriggered**. [F25](followups.md)'s own wide-end example is published and cannot be used
  blind.
- **No rate over a denominator that includes a labelled control**, and no rate at all if its denominator is 0.
- **No bar invented for a "reported, no bar" clause** — `N25-C`, `N25-E`, `N25-F`, `M25-C`, `W25-K`,
  `G25-P3c`'s `WIDENED`/`UNCHANGED` split.
- **Nothing about setups outside the bank.**
- **`RULE F14` / `RULE S16` / `RULE D20` are three permanent fences.** Not re-scored, not converted, not
  harvested. No clause in this wave reads any of them.
- **[F70](followups.md)(b')/PR #192 was REJECTED by the owner** and is not re-proposed anywhere.

---

## 12. Timing, priced step by step, with a drop ladder on the clock

Prices marked **measured** come from the same instrument on the same scenario ([F21](followups.md), and wave 24
§12's rule); prices marked *derived* carry a band.

| # | step | price | TestApp? |
|---|---|---|---|
| 0 | `RULE V25` pre-flight + layout + the derivations + every `--self-test` | ~15 m | no |
| 1 | **S1 BEFORE arm on B14, 20 cells** | **~80 m** (measured; band 70-100) | **yes** |
| 2 | code (P4 + P5) + 7 tests + the four mutant red demos — **runs INSIDE step 1's window** | ~60 m | no |
| 3 | build B15 + freshness + two-binary provenance `839c38b..<W25>` and `476369a..<W25>` | ~10 m | no |
| 4 | **RULE G25 gate, 8 runs** + `G25-P3c` + `G25-P4` + the interlock, FAIL ends first | **~42 m** (measured, seven consecutive waves at ~5.2 m/run) | **yes** |
| 5 | **S1 AFTER arm on B15, 20 cells** | **~85 m** (*derived* from step 1 + floored cells using more rounds; band 70-110) | **yes** |
| 6 | scoring (`W25`, `N25`, `M25`), the full suite by COUNT, register, hand-off | ~25 m | no |

**TestApp wall ~3 h 27 m. Critical path ~4 h 15 m** = 15 + max(80, 60) + 10 + 42 + 85 + 25.

**The ~80 m is corroborated by a second, independent route, which is why it is quoted as measured rather than
banded.** [F21](followups.md)'s own budget rows record per-scenario `synth-validate` costs of **S1 234 s mean /
130 s median**; 234 s x 20 cells = **78 min**, against the 80 min this design measured from wave 23's report
mtimes. Two routes, two minutes apart. **F21 also records the instrument choice this wave inherits — *"keep
`timeout 600` per cell"* — so §6's decision to keep it is the register's, not a convenience.**

**Drop ladder, in order, with clock triggers:**

| | item | trigger | cost of dropping |
|---|---|---|---|
| **not scheduled** | `lumos`'s zero-star frame (~10 m) | decided in advance, §1.5 | goes to wave 26's opener |
| **D1** | `G25-P4`, the ASCII census (~3 m) | the gate has not started by **03:15Z** | re-verifies a fix wave 24 already verified; `G-ASCII-INCOMPLETE` is already non-blocking |
| **D2** | `N25-E` (~4 m scoring, 0 TestApp) | scoring not started by **04:40Z** | the comparability question returns to wave 26 unanswered |
| **D3** | `D08`/S1 and `D11`/S1 from the **AFTER** arm only (~8 m) | the AFTER arm has not started by **03:30Z** | they are controls for §8's UNCHANGED prediction; **both names printed**, the blind 15 and `D01`/`D02`/`D03` are untouched, and no denominator moves |
| **HARD STOP** | the AFTER arm | not finished by **05:10Z** | kill it; score over whatever paired cells exist. If `W25-V1` (>= 12) or `N25-V1` (>= 17) fails, the verdict is `W-UNEVALUATED` / `N-UNEVALUATED` **and that is the result** — P4 does not ship on an unrun rule |

**The drop ladder never shrinks a blind denominator.** D3 removes two labelled controls, which are in no
denominator by construction; everything above it is an instrument or a scoring convenience.

---

## 13. Register entries this wave owes

Fixed in advance so the analysis agent cannot invent them, and so a wave that finds nothing says so.

| entry | action, conditional on the verdict |
|---|---|
| **[F34](followups.md)** | **CLOSED** if `RULE M25` is `M-CORRECTED` — the message names the cause it checks, with the FAIL end measured on the published artifact that motivated it. **Extended, not closed**, otherwise |
| **[F25](followups.md)** | **STATUS from `RULE W25`**, in `W25`'s own words. If `W-BRIDGED`: the narrow-end half is bridged and the **wide end remains unmeasured**. If `W-UNEXERCISED`: the mechanism is rarer than the three published cells suggest, and nothing more |
| **[F21](followups.md)** | **EXTENDED** — a third population on which R^2 is no defence: `D01` stalls at R^2 = 0.99999999999994 with `sampledHfrRange` = 1.4628, and the corrective variable is the sampled range, not the fit quality |
| **NEW — the missing lower bound** | **NEW ENTRY** if P4 ships. The mechanism is not "a degenerate fit": it is that `MaxHalfWidthSampledHalfSpanMultiple` is a **ceiling with no floor**, and that `half-width-unresolved` returns before the ceiling is ever consulted. Neither F25 nor F34 nor F26 owns it |
| **NEW — the `A4` truth-model gap** | **NEW ENTRY, wave-26 item.** `SynthValidateRunner`'s `A4` compares against `WasCapped` alone and will keep flagging `D03`/S1 now that the product answers with `WasBandFloored`. Deliberately **not** fixed here: an assertion change and a product change in one arm are not separable |
| **[F80](followups.md) / the derivation-drift entry** | **EXTENDED with `RULE V25`'s result.** The pre-flight's FAIL end names how many of wave 24's eight drifts a ten-line mechanical check catches, on files on disk |
| **[F76](followups.md)** | **CITED, not extended** — P4 ships with `WasBandFloored`, and `W25-B` is what proves it engaged |
| **[F26](followups.md)** | **NOT DISTURBED.** Wave 23 §13's harness-not-product scope correction stands. P2 is not re-scored; `N25-E` measures its effect on S1 and does not re-litigate its verdict |
| **[F22](followups.md)** | **NOT BUNDLED.** The `RecommendFromHfr` hysteresis owes a coordinate-system re-baseline and F26's own next step says it should not be bundled |
| **the trap list** | **UNTOUCHED this wave.** `lumos` is §1.5's declared drop; `Panos`'s sigma claim still costs one `af-fit` |

**Is there a finding worth an entry?** The design already contains one, before any measurement: **the stall's
cause is a missing floor on a bound that has had a ceiling for eighteen months, and `half-width-unresolved`
returns before the bound is consulted at all.** Whether it ships is what the arms decide.
