# Synthetic AF bank — followups wave 7 (design)

Plan: [`plans/synthetic-af-bank-followups-wave7-plan.md`](../plans/synthetic-af-bank-followups-wave7-plan.md).
Wave 6: [`docs/synthetic-af-bank-followups-wave6-results.md`](synthetic-af-bank-followups-wave6-results.md).
Register: [`docs/followups.md`](followups.md).

*Three of this wave's four items ([F19](followups.md#f19--the-exposure-recommendation-is-decided-by-the-20-brightest-stars-so-a-rich-field-can-never-earn-one),
[F18](followups.md#f18--step-size-is-sized-by-curve-geometry-alone-so-the-sweep-outruns-what-the-detector-can-see),
[F39](followups.md#f39--the-harness-records-a-detection-binning-the-run-never-applied-and-7-datasets-have-never-run-at-theirs)(b))
move what the synthetic bank's EXPECTED OPTIMA are. Each on its own costs a re-baseline, so they run under one.
The fourth (the AF chart's starting focuser position) touches no bank at all and is specified here only because it
ships in the same PR.*

---

## 0. Ordering: what this wave does to F32's confirmation arm, decided up front

[F32](followups.md#f32--j-is-saturated-near-10-so-the-optimizer-trades-enormous-recall-for-numerically-trivial-gains)'s
confirmation arm (both banks at φ = 0.50, plus `bank-verify`, plus the `--continue-rounds` arm wave 6 added to its
design) **has not run**. It wants the bank it is measured on to be the bank wave 5's φ table was measured on, or its
numbers cannot confirm anything. So the ordering has to be decided before, not during.

**What actually moves the frames.** Only two of the three items can:

| item | changes rendered frames? | why |
|---|---|---|
| F39(b) | **No** | `detectionBinning` is a DETECTOR-side setting. `SynthBankRunner.GenerateSweep` renders from `centerPosition / stepSize / offsetSteps / exposureSeconds`; the factor is metadata. See §3 — the bank's exposures were *already derived at binning 2*, so honouring it makes the bank self-consistent rather than different |
| F19 | only if the decision is "change" | it moves `SynthBankDerivations`' exposure derivation, hence `expectedOptimal.exposureSeconds`, hence the render |
| F18 | **yes, on the datasets whose `step*` moves** | it moves the step derivation, hence `expectedOptimal.stepSizeSteps`, hence every sweep position |

**Decision.** F32's confirmation arm is **not** run in this wave, and this wave does **not** silently invalidate it:

1. **§1 closes F19 as "no change"** (subject to a pre-registered falsification test), so the exposure axis of the
   derivation does not move at all. That is the cheap half of the ordering problem, gone.
2. **F18's re-render footprint is determined by `synth-bank --dry-run` BEFORE anything is rendered** — the wave-6
   discipline. The dry-run diff names exactly which datasets' `step*` moves. Nothing else is re-rendered.
3. **Every dataset this wave re-renders gets its pre-wave frames and `synthetic_meta.json` preserved** under
   `D:\hf_w7\oldframes` / `D:\hf_w7\snapshots`, exactly as wave 6 preserved `D01`/`D02`. F32's confirmation arm can
   then be run on the preserved frames and stay comparable to wave 5's φ table.
4. **If the dry-run shows `D18` / `D19` / `D20` do not move, the question is moot** and we say so with the diff as
   the evidence rather than as an assumption. Those three are F32's entire synthetic half; its other five runs
   (`toml999`, `CWhiteFocus`, `uneven`, `muggsie`, `mccomiskey`) are on the REAL bank, which this wave never touches.

**What F32's entry gains from this wave**, recorded there rather than discovered later: the arm must state which
frame set it used, and — if it uses the new bank — must re-run wave 5's φ arms on that bank rather than comparing
across it. That is [F41](followups.md#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)
applied to frames instead of to binaries.

---

## 1. F19 — the exposure statistic: STATED POSITION, no change

F19 asks for a decision, not a patch: *is `NTarget = 20` the right population for the exposure question, or should
it be a faint-end statistic?* The entry is explicit that the answer must be a position rather than an accident.

### The position

**`NTarget = 20` stays. F19 closes as "working as intended", with the reasoning stated below and one pre-registered
test that can still refute it (§1.4).** The half of F19's complaint that is a real defect — "a rig can be told
exposure is fine while its faint end is noise-dominated" — is **already answered by a different mechanism** that
shipped after F19 was written, and the half that is left is a sweep-geometry defect owned by F18.

### 1.1 The recommendation is only ever surfaced inside a band where the candidate statistic is pinned

The exposure block is not offered on every run. `StarDetectionOptimizerWizardVM.cs:271` gates it:

```
HasLowStarSignal => ExposureRecommender.SensitivityIsAtFloor(VariantSensitivity)   // Sensitivity <= 1.0
```

So the whole question is: *inside a landing whose Sensitivity gate is at its search floor, what statistic should
size the exposure?*

F19's named candidate is "the SNR at the star count the fit actually consumes". The fit consumes every accepted
star on the frame, so that statistic is the **faintest accepted star's** gate SNR. And in this band that quantity is
a property of the gate, not of the sky:

- A candidate is accepted iff its gate statistic exceeds `max(Sensitivity, InertSensitivityBound)`
  (`StarDetector.InertSensitivityBound = PeakResponse × MinEffectiveClipMultiplier`, **1.5** at shipped defaults —
  `StarDetector.cs:1506-1527`).
- With `Sensitivity ≤ 1.0` — which is the *only* band this advice is shown in — the binding bound is the inert one.
- A longer exposure does not raise the faintest accepted star's SNR. It admits **more** faint stars, and the new
  faintest one again sits just above the same bound.

So `S_faint ≈ 1.5`, **independent of exposure**, and `t·(TargetSensitivity/S_faint)² = t·(10/1.5)² = 44·t` — which
saturates `MaxExposureFactor = 4` on every run, forever, and never converges. A control loop whose measured variable
is pinned by its own actuator's gate is not a control loop. **That is a mechanism-level disqualification of the
specific candidate F19 names**, not a preference.

*(This is a derivation from the acceptance rule, so it is stated as one. §1.4 measures it rather than leaving it
asserted.)*

### 1.2 The "are there enough stars?" half already has its own mechanism, and it is not this statistic

F19 was filed 2026-08-02. The exposure recommendation shipped in v4.0.0.12 (PR #159) with **three** verdicts, not
one:

| verdict | condition | what it does |
|---|---|---|
| `ExposureIsNotTheLimit` | `S_now ≥ 10` and some frame had ≥ `NTarget` stars | says exposure is not the bottleneck |
| `StarCountIsTheLimit` | `S_now ≥ 10`, **every** frame short of `NTarget`, and the gate rejected something (or is provably inert) | offers a **fixed ×2 probe**, explicitly not a derived figure |
| `StarFieldIsExhausted` | `S_now ≥ 10`, every frame short, gate rejected nothing and is not inert | offers nothing — the field has no more to give |

The `ExposureRecommender` class remarks state the split's reason in exactly F19's language: *"the per-frame
statistic answers 'are the stars I found bright enough?' … so it cannot also answer 'are there enough stars?'.
On a star-poor field the two questions have opposite answers."* The star-count question is answered by a
**probe with a stopping rule** — because whether more exposure reveals more stars depends on the field's luminosity
function, which one sweep cannot measure. Replacing `NTarget` with a faint-end rank would try to answer that
question with a formula, which is the thing PR #159 concluded cannot be done.

### 1.3 The residue is F18's, not F19's

What is genuinely left of F19 is the case it opens with: a rich field whose *wing* frames are starved even though
its in-focus frame is not. F18's own measurement is that population:

| distance from focus | 1770 | 3540 | 5310 | 7080 | 8850 |
|---|---|---|---|---|---|
| stars | 10 | 10 | 6, 3 | 1, 1 | **0** |

The correct response to a starved wing is **narrow the sweep**, not **expose longer** — and the product already
says so for the adjacent signal: `ExposureRecommendation.FlatRejectedCount` is documented as *"callers surface it as
'narrow the sweep', never as 'expose longer'"*. F18 (§2) is that fix, measured, on the same wave. Answering the
same symptom a second time through the exposure knob would put two recommendations on the same page pulling in
opposite directions on the same evidence.

### 1.4 The pre-registered test that can still refute this

The position above rests on a claim that has to be measurable: **on a field that already carries 20 bright stars,
more exposure does not materially improve the autofocus result.** If that is false, the entry cannot close as
working-as-intended no matter which statistic is nearest to hand.

**Arm E (exposure ladder).** Render two rich datasets at a fixed exposure ladder, fixed detector settings, and read
σ_focus:

- `D16_esprit550_ha3` — F19's own poster child: 550 mm behind a **3 nm Hα** (≈ 64× less flux than L), 2.9° field,
  6835 on-frame stars. **Derives 2 s** (band 1.079–3.102 s), *not* the 0.5 s floor F19 records — that number has
  drifted since the entry was filed and is corrected there; the ladder brackets the measured value either way.
- `D02_rich_135mm` — a rich, broadband control at the other end of the focal-length range, whose frames wave 6
  proved bit-identical across a re-render (so any movement here belongs to the exposure, not to the field).
- Ladder: **0.5, 1, 2, 4, 8 s** (D16), and the same relative ladder on D02.
- Instrument: `synth-bank` with a new `exposureSecondsOverride` (three lines, mirroring the existing
  `stepSizeOverride` / `detectionBinningOverride` / `donutOverride` at `SynthBankSpec.cs:141-143`), then
  `optimize --per-run --settings <pinned>` for σ_focus and R², and `golden eval` for recall.

**Pre-registered decision rule, fixed before the arm runs:**

> Let `σ*` be σ_focus at the derived exposure (the bank's own `expectedOptimal.exposureSeconds`). If **any** longer
> rung improves σ_focus by **more than 20 % relative** to `σ*` on **either** dataset, the position "20 good stars is
> what the fit needs" is **REFUTED**, F19 stays **Open**, and the candidate becomes a faint-end statistic with a
> floor at the effective gate (§1.1's disqualification applies to the *unfloored* candidate only). Otherwise F19
> closes **working as intended** and this design's §1.1–1.3 is its stated position.

**The confound, stated rather than discovered.** A longer exposure changes two things at once: it adds stars, and it
measures the stars already there more precisely. Arm E cannot separate them. It does not need to — the practical
question F19 poses is *"should a rich field ever earn an exposure recommendation?"*, and a σ_focus improvement past
the derived exposure answers **yes** whichever mechanism produced it. A null result answers **no** equally cleanly.

**Cost.** Two datasets × 5 rungs = 10 renders + 10 optimize runs. This is the cheap item, and it is the one that
decides whether §2 and §3 have to carry an exposure axis at all.

---

## 2. F18 — bound the step by DETECTABILITY as well as by curve geometry

`StepSizeRecommender` sets the half-width from the fitted HFR curve alone: the offset at which the model reaches
`HfrThresholdMultiple = 3.0 × min`, then `step = W / PointsPerSide` with `PointsPerSide = 3.5` and
`DefaultOffsetSteps = 4`. Nothing in it asks whether stars are still **detectable** out there. F18 measured the
consequence on a 3800 mm rig: the band put the half-width at 6221 steps while the outermost position still yielding
the `NHard = 3` stars the objective requires was at **5310** — and the executed sweep (4 offset + 1 focus-recovery
step per side) reached **8850**, 43 % beyond the band, into frames with zero stars.

This changes the shipped recommender for every user, so it gets a design, a flag, and bank validation with σ_focus
as the acceptance metric — not an inline patch.

### 2.1 The rule

```
W_3x     = the existing fitted 3x-min-HFR half-width (unchanged, including its 1.5x sampled-half-span cap)
W_detect = the largest |position - fitted vertex| over NON-RECOVERY frames whose star count >= NHard
half_width = min(W_3x, max(W_detect, W_floor))
step       = round(half_width / PointsPerSide)
```

`W_detect` is **measured, not extrapolated**, which is what makes it fit the recommender's converge-over-runs
philosophy: it can only ever report something the sweep actually sampled. The inputs are already plumbed —
`RunEvaluationMetrics.FrameStarCounts` (`OptimizationObjective.cs:283`), `FrameFocuserPositions` (`:314`) and
`FrameIsRecovery` (`:348`) — and the wizard's call site already holds them
(`StarDetectionOptimizerWizardVM.cs:3491-3506` builds the curves from the same `bestEval.Metrics` that
`:3512` passes the fit from).

On F18's own run this gives `min(6221, 5310) = 5310`, i.e. **step 1517** instead of 1777 — the ±4 offset sweep now
ends at 6068, inside the fitted band and inside what the rig measured. F18's entry quotes ≈ 1060–1330 for the same
run; that figure divides by 4–5 rather than by 3.5, so it is the **executed-sweep** number (§2.2's arm S gives
5310 / 4.375 = **1214**, inside F18's range). Both are reproduced as unit tests, and separating them is the point
of running the two arms rather than picking one.

### 2.2 Decision (a) — should focus-recovery steps extend the sweep past the band?

**Position: yes for the 3× band, no for detectability — and the two are separated.**

Focus-recovery steps are *deliberately* far from focus; that is their entire purpose, and the objective already
exempts them from its hard floor (`JRun`'s `FrameIsRecovery` exemption, `OptimizationObjective.cs:443-455`). Sizing
the base step so that recovery points fall inside the 3× band would shrink **every** nominal sweep by ~14 % to
protect a path that only executes when a run has already failed:

| rule | divisor at `offsetSteps = 4`, `recovery = 1` | step vs today |
|---|---|---|
| today | 3.5 (offset points only) | 1.00× |
| size for the executed sweep | 5 / (4/3.5) = 4.375 | **0.80×** |

Losing 20 % of the lever arm on every successful run to bound a conditional one is not obviously right, and F18
does not claim it is — it asks the question. **So it is answered by measurement, not by argument**: the change ships
behind a flag with two arms and a pre-registered rule (§2.5), and the entry records which arm won.

What is **not** deferred: a recovery point beyond `W_detect` buys nothing at any setting, because nothing is
detectable there. `StepSizeRecommendation` therefore gains `MaxUsefulHalfSpan = W_detect` (NaN when unmeasurable) so
a caller can say so. **Acting** on it — clamping where the engine's recovery step may land — is engine-side, is not
what F18 is about, and is filed as its own followup rather than smuggled in here.

### 2.3 Decision (b) — the floor, so a starless run cannot collapse the sweep

Two guards, and the second is the principled one:

1. **Unmeasurable ⇒ no bound.** If fewer than `MinFramesForDetectHalfWidth = 3` non-recovery frames clear `NHard`,
   or the fitted vertex is non-finite, `W_detect` is **NaN** and the recommendation is byte-identical to today's.
   An unmeasurable detectability bound is *absent*, never *zero* — that is the difference between "we could not
   measure it" and "nothing is detectable", and only the first is what a thin run actually tells you.
2. **`MinHalfWidthSampledHalfSpanMultiple = 0.5`, the mirror of the existing cap.** The recommender already bounds
   how far it may **widen** in one run (`MaxHalfWidthSampledHalfSpanMultiple = 1.5` × the sampled half-span, with
   the documented rationale that a shallow sweep's extrapolated half-width is a claim the data cannot support). It
   has no bound on how far it may **narrow**, and that asymmetry is the hole. Flooring `W_detect` at 0.5 × the
   sampled half-span bounds one run's shrink to `2·step/3.5 = 0.57×`, so a pathological run halves the step and
   converges over runs instead of collapsing in one.

**This floor also bounds [F21](followups.md#f21--stepsizerecommenders-half-width-is-not-stable-against-noise-even-on-a-perfect-fit)'s
symptom**, and that is worth saying precisely because it is only the symptom — see §2.4.

### 2.4 What this design covers among F21 / F25 / F26, and what it does not

F18, F21, F25 and F26 are the same component read four ways. Stated explicitly so nobody reads a passing bank as
four entries closed:

| entry | covered by this design? | precisely what |
|---|---|---|
| **F18** | **yes** | the defect itself: `min(W_3x, W_detect)`, plus the executed-sweep question decided by arm S (§2.5) |
| **F21** — half-width unstable against noise (143.6 → 12.1 on `D17` at R² = 1.0000) | **magnitude only** | the 0.5× floor bounds a one-round collapse to 0.57× the step. It does **not** diagnose `FindHalfWidth`'s outward search, which is what actually returned 12.1. F21's own next step — instrument `FindHalfWidth` on the two saved `D17` sweeps and confirm the coarse-walk hypothesis — **remains owed**, and F21 stays Open |
| **F25** — a degenerate fit is answered with a *wider* sweep | **magnitude only** | `W_detect` cannot exceed what the sweep sampled, so `min(W_3x, W_detect)` bounds the 4×→7× over-reach whenever star counts are measurable. It does **not** add the **fit-quality gate** F25 asks for: at R² = −0.223 the recommender still answers instead of recognising it has no curve. F25 stays Open, and so does its second half (a no-op recommendation scored `converged`) |
| **F26** — a stuck binning recommendation starves the step update | **no** | it is the wizard's binning-first update ORDERING, downstream of F22, not the recommender's arithmetic. Untouched here. (Wave 5 already refuted its precision-gate clause; the deferral cost is real and its guard is still owed) |
| **F45** — Grubbs rejects the in-focus point | **no, and it is not the same component** | it is `TrendlineFitting` / `MathUtility.RejectionTest` inside the AF engine's blind walk. Adjacent, per the wave brief. Its part (b) — a MAD floor tied to the points' own σ — *does* want the same bank σ_focus instrument, which is why this wave builds it, but it is a separate change with its own arm |

### 2.5 Validation: two arms, σ_focus as the acceptance metric, rule fixed first

Both arms are harness flags on ONE binary whose absence is bit-identical to today
([F41](followups.md#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary): a prior wave's arm
is not a control), with `--settings` pinned on every arm
([F42](followups.md#f42--every-build-directory-silently-gets-its-own-detector-settings-and-the-run-instructions-require-a-new-one-per-arm)).

| arm | flag | what it measures |
|---|---|---|
| **C** (control) | none | today's recommender. Must reproduce the wave-6 numbers exactly where they overlap |
| **D** | `--step-detect-bound` | `min(W_3x, W_detect)` with the §2.3 floor, divisor unchanged at 3.5 |
| **S** | `--step-detect-bound --step-size-for-executed-sweep` | additionally sizes for `offsetSteps + recoverySteps` per side |

Instrument: `synth-validate --scenarios S0,S1,S2` over the bank (S0 is the self-test whose bootstrap already IS the
expected optimum; S1/S2 are the ×0.25 and ×4 step perturbations the recommender must converge from), reading
per-round σ_focus, R², and the A1/A3 assertions.

**Pre-registered rules:**

1. **Arm D ships if** median σ_focus over the bank is no worse than arm C by more than **2 %** (the
   `regressionRule.sigmaFocusWorseFractionMax` band's spirit at run level), **and** no dataset regresses σ_focus by
   more than **20 %**, **and** the A3 final-step assertion passes on at least as many (dataset, scenario) cells as
   arm C.
2. **Arm S ships instead of D only if** it beats D on median σ_focus by more than **5 %**. Below that, D wins on the
   principle that a conditional path does not get to shrink every unconditional one.
3. **`BaselineJ` must be identical across C/D/S on every run** before any σ is read — F41's free check. `BaselineJ`
   is produced by no search, so two arms of one binary that disagree on it have an instrument fault, not a result.
4. A number that agrees **too well** with the hypothesis is also an instrument fault (wave 6's keep-readout lesson).
   Specifically: if arm D's `W_detect` equals the sampled half-span on most datasets, it is reporting the sweep's
   edge rather than detectability, and the arm is void until that is explained.

### 2.6 What moves in the bank derivation, and why it must

`SynthBankDerivations` mirrors the recommender deliberately (`PointsPerSide = 3.5` at `:66`, the
`step* = √8·HFR_eff/(κ·PointsPerSide)` closed form at `:70-85`). If the shipped recommender gains a detectability
bound and the derivation does not, the bank's A1/A3 assertions would walk the recommender toward a target this
design says is wrong — the bank would be asserting the defect.

So the derivation gains the analytic twin: `W_detect*` = the largest defocus offset at which at least `NHard` truth
stars still exceed the gate at the dataset's derived exposure, computed from the same radiometry
(`SolveExposureForTargetGateSnr`'s flux/σ model, already present) and the same defocus-vs-peak-fraction model the
exposure derivation uses. `step*` becomes `min(W_3x*, max(W_detect*, floor))/PointsPerSide`, matching the runtime
rule term for term.

**This is what makes the re-render necessary, and it is the only thing that does.** Its footprint is measured by
`--dry-run` before a single frame is written (§0, §4 of the plan).

---

## 3. F39(b) — run the seven `detectionBinning = 2` datasets at their factor

`expectedOptimal.detectionBinning = 2` for **D08, D09, D10, D12, D14, D15, D17**, and all seven have only ever been
scored at **1**. `D08_c11_2800mm`'s own spec description calls it *"the first detectionBinning=2 dataset"*; it has
never been one. Part (a) shipped in wave 6 (`DetectionBinningSource` is a field). This is part (b).

### 3.1 The mechanism, and the one line that is wrong

`OptimizationDiagnosticRunner.cs:477` calls `HarnessSettingsStore.ResolveForRun(...)` and **discards the returned
`Resolved`** — it is a pure write side-effect. Nothing else maps `harness_settings.json`'s `DetectionBinning` back
into `StarDetectorParams`: `BuildStarDetectorParams` / `BuildDefaultStarDetectorParams` never touch it, and only
`ApplyDetectionImageContext` does, which the headless runners never call.

### 3.2 The change: apply the factor, and nothing else

**Do NOT wire the whole `Resolved` into the run.** That would let a per-run settings file shadow the `--settings`
file every arm pins, which would break the comparability of every arm this project has run. Apply **only** the
detection binning, and only under an explicit opt-in:

```
--apply-run-detection-binning     # absent => bit-identical to today (verifiable, and verified)
```

When present, the runner reads the resolved `DetectionBinning` and calls the existing
`DetectionBinningResolver.ApplyFactor(p, factor)` (`DetectionBinningResolver.cs:129`) on the run's seed and baseline
params. `ApplyFactor` is the right primitive rather than a raw field write because it also rescales
`StarDetectorParams.PixelScale`, which carries the factor — a raw write would leave every pixel-scale-dependent
gate evaluated at the wrong scale, which is precisely the class of bug `PixelScaleForFrame` exists to prevent.

**Source of the factor.** The settings file's value for these seven is `Bin2` **inherited** (`DetectionBinningSource:
kept-from-base`), not derived — F39's own part (a) is what makes that readable. The *authoritative* value is the
dataset's own `synthetic_meta.json` `expectedOptimal.detectionBinning`, which is physics-derived. The flag therefore
prefers the run's `synthetic_meta.json` when present and falls back to the settings file, and **prints which source
it used** on every run. Honouring an inherited guess silently is the defect F39 is about; honouring it *loudly*, or
preferring the derived value, is not.

### 3.3 Why this needs no re-render, which is not obvious

Reading `D08`'s stored `synthetic_meta.json`:

> `exposureDefinition`: *"Exposure t solving snr(t) = … = 10 for the 20th-brightest of 123 on-frame stars … **at
> binning=2 (captureBinning=1×detectionBinning=2)**, clamped to [0.5, 30] s"*

The bank's exposures for these seven were **already derived assuming binning 2** while every run scored them at 1.
So part (b) does not introduce a new configuration — it **removes an existing inconsistency** between the frames'
exposure and the detector's factor. `detectionBinning` is a detector-side setting and enters no render input
(`GenerateSweep` takes centre / step / offsetSteps / exposure), so the frames do not move.

That also means the arm is honest about its own direction: recall at binning 2 should improve on these long
focal-length rigs *because the exposure was chosen for it*, and a null result would be the interesting one.

### 3.4 The cheap instrument first

`golden eval` already exposes every detection override as a flag and needs no optimize run — that is wave 6's
"check whether a cheap instrument already exists" lesson, and it applies here. It has flags for `--sensitivity`,
`--star-clip`, `--min-hfr`, `--noise-clip` and a dozen more (`GoldenEvalRunner.cs:409-478`) but **no**
`--detection-binning`. Adding one is a single `Int(...)` line routed through `ApplyFactor`.

So the arm is staged cheapest-first:

1. **`golden eval --detection-binning 2` vs `1`** on the seven datasets: recall/precision per SNR tier, per region,
   with FN gate attribution. No optimizer, no landing, ~minutes. This alone answers "does the bank's binning axis
   behave as the physics says".
2. **`optimize --per-run --apply-run-detection-binning`** vs the same without: σ_focus, R², `FinalJ`, landings. This
   is the re-baseline of those seven rows.

### 3.5 What it unlocks

[F38](followups.md#f38--the-minhfr-seed-trigger-compares-a-captured-pixel-vertex-against-a-binned-pixel-gate) —
shipped in wave 4 — is **latent headless and active in the wizard**, and the population it bites on is exactly a
run with `DetectionBinning > 1`. The bank has never had one, so it cannot currently regression-test F38's fix. After
part (b) it can, and the plan adds that assertion rather than leaving it implied.

---

## 4. The AF chart's starting focuser position (no bank, no re-baseline)

### 4.1 Verified, not implemented: the report already carries it

`HocusFocusReport.GenerateReport` (`HocusFocusReport.cs:93-96`) already serializes

```
InitialFocusPoint { Position = initialFocusPosition, Value = initialHFR }
```

and `FinalHFR` (`:105`). Confirmed on real reports on disk — `%LOCALAPPDATA%\NINA\AutoFocus\2026-08-05--21-47-43--90d513b9….json`
reads `InitialFocusPoint {Position: 25000.0, Value: 0.70298}` and `FinalHFR: 0.70526`, against
`CalculatedFocusPoint {Position: 24999.996}`. Six of the six most recent reports carry a populated
`InitialFocusPoint`. **"Add it to the saved json" is already done and needs no work.**

### 4.2 The actual defect is the rendering, and the collapse is a guard

`HocusFocusVM.SetCurveFittings:594`:

```csharp
if (LastAutoFocusPoint?.Timestamp != lastGeneratedReportTimestamp) {
    // The loaded chart is not the run this VM generated (foreign run, or another VM's report): the
    // initial-position / Start-HFR / HFR-change rows would show a stale run, so collapse them instead.
    InitialFocuserPosition = -1;
    InitialHFR = 0.0;
    FinalHFR = 0.0;
}
```

That is **not** an oversight. It is step 3 of [`docs/af-chart-reload-sync-design.md`](af-chart-reload-sync-design.md),
which fixed a real field report (2026-07-29): the chart showed one run's curve over another run's markers and info
rows. NINA core's `AutoFocusToolVM.LoadChart` rebuilds only `FocusPoints` / `PlotFocusPoints` / `FinalFocusPoint` /
`LastAutoFocusPoint` / method / fitting and then calls `SetCurveFittings` — it cannot see the plugin-only rows, so
without the reset they keep the previous **live** run's values. **Deleting the reset reintroduces exactly the bug
that work fixed.**

The consequence today is that the focuser-position change is visible only in the moments after a live AF run in this
pane, and never when browsing chart history.

### 4.3 The fix: read the loaded run's OWN values

Replace *collapse* with *re-populate from the loaded report, and collapse only when that fails*.

**Where the values come from.** Core deserializes the report as the base `AutoFocusReport` and keeps nothing but
`LastAutoFocusPoint.Timestamp = report.Timestamp` (`AutoFocusToolVM.cs:305`). The timestamp is therefore the only
handle the plugin has on *which* report was loaded — and it is enough, because the report file name is
`yyyy-MM-dd--HH-mm-ss--{profileId}.json` in `HocusFocusVM.ReportDirectory`.

```
ILoadedAutoFocusReportSource.TryFind(DateTime timestamp) -> HocusFocusReport
```

Default implementation: enumerate `ReportDirectory`'s file NAMES (cheap, no parsing), keep candidates whose
filename second-stamp is within ±5 s of the target, order by |Δ|, deserialize each as `HocusFocusReport` and return
the first whose `Timestamp` matches **exactly**. Typically one file is parsed. The ±5 s window exists because the
report's `Timestamp` and the file name come from two separate `DateTime.Now` calls (`HocusFocusReport.cs:91` and
`HocusFocusVM.cs:516`) with a JSON serialization between them, so a second boundary can split them; the exact
`Timestamp` equality is what actually decides.

Deserializing as `HocusFocusReport` (not the base type) is what recovers `FinalHFR`, which core drops.

**Where they go.** In `SetCurveFittings`, on the foreign-chart branch only:

| field | source | when the source is absent/invalid |
|---|---|---|
| `InitialFocuserPosition` | `round(report.InitialFocusPoint.Position)` | **-1** (row collapses) |
| `InitialHFR` | `report.InitialFocusPoint.Value` | **0.0** (row collapses) |
| `FinalHFR` | `report.FinalHFR` | **0.0** (row collapses) |

The same-run branch is untouched: when the watcher re-loads this VM's own just-written chart, the live values are
preserved exactly as today.

### 4.4 Fail-visible, stated as rules

The requirement is that a report with no initial position collapses the row rather than displaying 0 or the previous
run's number. The XAML already renders the sentinels correctly —
`HF_IntNegativeToVisibilityConverter` on `InitialFocuserPosition` (`DataTemplates.xaml:431-432`) hides the
`"25000 → "` prefix and leaves the final position; `HF_DoubleZeroToVisibilityConverter` gates the Start-HFR
(`:437`) and HFR-Change (`:453`) rows — so the whole job is to produce the right sentinel:

1. **No report found** (deleted, unreadable, foreign directory, non-HocusFocus writer with a different naming
   scheme) ⇒ all three sentinels. Identical to today's behaviour, so the fix strictly adds information and can never
   subtract it.
2. **`InitialFocusPoint` missing or `Position <= 0`** ⇒ `-1`. `new FocusPoint()`'s default is `Position = 0`, which
   is indistinguishable from "not recorded"; the engine's own not-known sentinel is `-1`
   (`AutoFocusEngine.cs:2697`). A focuser at literal 0 is not a case worth rendering wrongly for.
3. **`Position` or `Value` non-finite** ⇒ sentinel. Never render NaN.
4. **A core-written or other-plugin report** has no `FinalHFR` ⇒ 0.0 ⇒ the HFR-Change row collapses and the
   Start-HFR row shows on its own, which is exactly what that report knows.
5. **Any exception in the lookup** ⇒ sentinels, logged at Debug. A chart must render even if the report file is
   mid-write.

### 4.5 Tests must discriminate

Wave 6's rule: count the tests that fail when the fix is reverted, not the tests you wrote. `HocusFocusVMChartReloadTests`
already has the harness (`SimulateCoreLoadChart` mirrors core's `LoadChart` writes byte for byte, and
`MarkReportGenerated` is internal for exactly this). The report source is injected, so tests can write **real report
JSON** to a temp directory and go end to end:

| test | reverting the fix… |
|---|---|
| foreign chart with a real report on disk ⇒ rows show **that report's** 25000 / 0.703 / 0.705 | **FAILS** (reads -1 / 0 / 0) |
| foreign chart, report has `InitialFocusPoint.Position = 0` ⇒ `InitialFocuserPosition == -1` while `FinalHFR` still populates | **FAILS** (FinalHFR would be 0) |
| foreign chart, no matching report on disk ⇒ all three sentinels | passes either way — labelled a **guard** |
| same-run reload ⇒ live values preserved | passes either way — labelled a **guard** (the existing regression) |
| timestamp within the ±5 s window but not an exact match ⇒ not adopted | **FAILS** (a looser match would adopt the wrong run — this is the stale-value bug in a new dress) |

Three discriminating, two guards, and the labels are in the test file rather than in a claim.

---

## 5. Non-goals

- **F32's confirmation arm.** §0. Deliberately not run here; what this wave owes it is recorded in its entry.
- **Acting on `MaxUsefulHalfSpan`** in the AF engine's focus-recovery placement (§2.2). Reported, not enforced.
- **F25's fit-quality gate, F21's `FindHalfWidth` diagnosis, F26's deferral bound.** §2.4 says which of the four
  this design covers and which it does not; none of the three is closed by it.
- **F45's three fixes.** Adjacent (the AF engine's blind walk and Grubbs scale), not this component. Part (b) wants
  the bank σ_focus instrument this wave builds, which is a reason to build it, not to bundle the change.
- **Making `ResolveForRun`'s whole `Resolved` authoritative on the headless path** (§3.2). It would shadow every
  arm's pinned `--settings`.
- **Any change to `NTarget` in `ObjectiveConstants`.** §1 keeps it; note the exposure recommendation reads it from
  the caller's constants rather than a literal, so an objective retune moves both together by construction.
