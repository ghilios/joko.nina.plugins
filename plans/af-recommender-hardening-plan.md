# AF recommender hardening — implementation plan (wave 1: F23 only)

Design spec: **never committed.** `docs/af-recommender-hardening-design.md` does not exist in the repository
and no revision of it does, so this plan is the design of record. Its "Signals already available" table
(below) is what actually held up under checking; the spec's version of that claim did not. See
[F27](../docs/followups.md).
Baseline measurements: [`docs/synthetic-af-bank-baseline-results.md`](../docs/synthetic-af-bank-baseline-results.md).
Followups: [`docs/followups.md`](../docs/followups.md) — F23 is the only entry this plan touches.

Branch `ghilios/objective-precision-term` off `develop`; PR at the end (never push `develop`).
Commit with the privacy-email env vars per `CLAUDE.md`.

## Scope, and what is deliberately excluded

Wave 1 is **F23 alone**: the optimizer objective rewards star count and fit quality and carries no
false-positive cost, so the search drives `BrightnessSensitivity` (`StarDetectorParams.Sensitivity`) to
its `0.0` floor — free star count under a blind objective — and config-A precision collapses to 0.451.

Explicitly **out of scope for this plan**, because the spec sequences them deliberately and several move
the bank's own expected-optimal values:

| entry | why not here |
|---|---|
| F19 | changes derived exposures ⇒ moves the bank's expected optima; must not share a validation run |
| F20 | recommender-side; needs its own V2, batched with wave 2 |
| F21, F25 | `StepSizeRecommender`; V1-matrix-only validation |
| F26 | predicted to dissolve with F23 — **re-measured** here, not fixed here |
| F22 | same: symptom of F23, re-measured not fixed |
| F24 | a real trade to characterise, not a bug; wants its own spec |

Anything discovered along the way is **FLAGGED in `docs/followups.md`, never fixed inline.**

## Step 0 — pin the measured baseline before touching product code

The spec's regression protocol: `docs/synthetic-af-bank-expectations.json` holds *aspirational* bands
(`regressionRule` at `:83-89` is data that no code reads); the *measured* baseline is missing.

**0a. Transcribe the published measurement.** Create `docs/synthetic-af-bank-baseline.json` beside the
expectations file — a pruned projection of the `afbank-verify/3` shape, keyed
`runId → config → {recallHigh, precision, afR2}` plus the C0 σ_focus, taken verbatim from the V2 table at
`docs/synthetic-af-bank-baseline-results.md:115-134`. Carry `schema`, `source`, `detectorCommit` and an
explicit `provenance` note saying these are transcribed, not regenerated. This is the artifact the
regression rule (recall@high or precision drop > 0.02, σ_focus worse > 20%, AF R² drop > 0.005) compares
against.

**0b. Re-measure it at HEAD anyway, and treat that as the operative reference.** This is not redundancy:

- The V2 run's optimizer prepass output directories are **gone**. `optimize --per-run` writes
  `optimized_settings.json` both into `<out>/<runId>/` *and* into the run's own source folder
  (`OptimizationDiagnosticRunner.cs:698-703`), so the copies still sitting in
  `D:\SyntheticAutofocusBank\D*\attempt01\` are whichever prepass ran **last** and overwrote the others.
  Their landed Sensitivities (5 at zero: D09/D10/D11/D12/D17, with D15 at 10.0) do **not** match the
  published config-A set (6 at zero, including D15), so they cannot be used as the A reference.
- `bank-verify --opt-a`/`--opt-b` only ever *read* settings from disk (`BankVerifyRunner.cs:468-479`);
  they never run the optimizer. So without a fresh prepass there is no config A to score.
- Comparing an after-change prepass against a table produced by a prepass whose inputs cannot be
  reconstructed conflates the change with anything else that has moved since. A same-session HEAD arm
  removes that.

Arm **H** (HEAD, unchanged code) therefore runs the full prepass + verify described in Step 4. If H's
numbers differ materially from the published table, that discrepancy is itself a finding to flag before
any conclusion is drawn about the mechanisms.

## The mechanism, in the gate's own units

Both mechanisms turn on one fact about the Sensitivity gate
(`StarDetector.cs:1746`, `:1763`):

```
sensitivity = starCandidate.NormalizedBrightness / srcImageNoiseSigma
if (sensitivity <= p.Sensitivity) reject as LowSensitivity
```

so `p.Sensitivity` is a **peak-SNR floor in σ units**, and `Star.MeasuredSensitivity` (`:1824`) records
the accepted star's own value. Three consequences the design work depends on:

1. **The accepted-star SNR sample is left-censored at exactly `p.Sensitivity`.** Every accepted star has
   `MeasuredSensitivity > p.Sensitivity`. A statistic counting accepted stars below a fixed floor `F` is
   therefore *identically zero* whenever `p.Sensitivity ≥ F`, and can only become non-zero below it.
2. **Any Sensitivity floor at or below ≈1.5 is provably inert at shipped defaults.** The structure/clip
   stage guarantees `sensitivity ≥ PeakResponse × StarClippingMultiplier` = `0.75 × 2.0` = 1.5, so a
   floor of 1.0 rejects nothing and would be a no-op change. (Note `ExposureRecommender`'s
   `SensitivityFloorThreshold = 1.0` is a *detector-hit-its-floor* predicate, not a detection floor — do
   not reuse it here.)
3. **With the donut master on, an extended candidate's `sensitivity` may instead be the integrated-flux
   matched-filter SNR** (`StarDetector.cs:1755-1762`) — a different statistic. It only ever *raises* the
   value, so it cannot manufacture a false marginal-SNR flag; but it does mean the floor is more
   permissive per-pixel for extended candidates. The donut master is not itself a searchable axis (it
   gates which axes exist, `OptimizerVariable.cs:118-119`), so the statistic's meaning is fixed within a
   single optimize run.

## Mechanism (b) — a floor on the searchable Sensitivity range

The crude baseline that (a) must beat. `OptimizerVariable.CreateCuratedSet` gains an optional
`sensitivityLower`; `DefaultSensitivityLower` stays **0.0** so shipping behaviour is unchanged and the
floor is exercised only through the harness's `--sensitivity-floor`.

**The floor value was calibrated, not guessed.** `golden eval --params default --sensitivity <S>` scores
exact precision/recall against synthetic truth in minutes, varying only the gate. Over the three
most-affected datasets (precision, with recall@high in brackets):

| gate `S` | D09 | D17 | D13 |
|---|---|---|---|
| 0 – 2.5 | 0.804 [0.911] | 0.697 [1.000] | 0.850 [1.000] |
| 3 | 0.822 [0.911] | 0.697 [1.000] | 0.851 [1.000] |
| 4 | 0.939 [0.911] | 0.744 [1.000] | 0.857 [1.000] |
| 5 | 0.995 [0.911] | **0.892** [1.000] | **0.878** [1.000] |
| **6** | **1.000** [0.911] | **0.950** [1.000] | **0.945** [1.000] |
| 8 | 1.000 [0.911] | 0.994 [1.000] | 0.996 [1.000] |
| 10 | 1.000 [0.911] | 1.000 [1.000] | 1.000 [1.000] |

Three things fall out, and all three change the experiment:

1. **A floor at 2.5 would have been a no-op.** 0, 1.5 and 2.5 are bit-identical on all three datasets —
   the inert region is not merely the ≈1.5 the algebra guarantees, it reaches 2.5–3.0 in practice,
   because no candidate happens to land in (1.5, 3]. The plan's original 2.5 would have "implemented"
   mechanism (b) and measured nothing.
2. **5.0 — the textbook 5σ threshold, and this term's first value — is not enough.** It leaves D17 at
   0.892 and D13 at 0.878, both under the 0.90 acceptance bar. `MarginalSnrFloor` is therefore **6.0**,
   which clears all three.
3. **recall@high never moves anywhere in the sweep.** The bright tier is simply not at risk from this
   gate, which is why raising the floor to 6 costs nothing against the stated acceptance metric. Past 8
   the trade inverts — real stars start being lost for precision that is already exhausted.

So **both mechanisms use floor 6.0**. That is the better experiment anyway: with the threshold held
equal, the head-to-head isolates exactly one variable — *soft and data-adaptive* versus *hard and
unconditional* — instead of confounding it with a different cut. The discriminating dataset is D13,
which landed at Sensitivity 2.5 under HEAD: (b) must force it to 6, while (a) should stop wherever its
sub-6 tail thins out.

`CreateWarmStartSet` clamps bands with `Math.Max(v.Lower, …)` (`OptimizerVariable.cs:233`), so the floor
propagates into a warm start automatically — asserted, because otherwise a `--continue-rounds` pass would
silently escape back below it.

## Mechanism (a) — a marginal-SNR false-positive proxy in `J`

### Signal choice

The spec names three candidate golden-free signals. Measured against what is actually reachable in the
optimizer's evaluation path:

| candidate | reachable today? | verdict |
|---|---|---|
| accepted-star SNR distribution (`FrameStarSnrs`) | **yes** — populated at `RunEvaluationLoader.cs:316-318` → `RunEvaluationData.cs:676` → `:796`, currently inert in `J` | **chosen** — no new plumbing, no detector change, physically-scaled units |
| accepted-to-structure-candidate ratio | no — `StructureCandidates`/`TotalDetected` exist on `StarDetectorMetrics` (`IStarDetector.cs:732-733`) but are not carried into `FrameDetectionResult` | needs 4 new plumbing edits; denominator is confounded by a Region crop / outlier filtering |
| `CollectRejectedCandidateDiagnostics` records | no — `RejectedCandidates` never reaches `HocusFocusStarDetectionResult` (`HocusFocusStarDetection.cs:818`) | needs plumbing *and* a per-detection allocation cost in the hot loop |

Choosing the already-plumbed signal keeps wave 1 to a pure objective change with a provable bit-identity
story, which is what makes the head-to-head against (b) interpretable.

### The term

A new multiplicative sub-score `SMarginalSnr`, following the `SHfrOutlier` template exactly
(`OptimizationObjective.cs:623-688`):

- Pool the accepted-star SNRs of the **near-focus** frames — the same window plumbing
  (`NearFocusWindowSteps · StepSize` around `BestFocusPosition`) that `SDefocusPrecision`, `SHfrOutlier`
  and `SCoverage` use, with the same recovery-frame exemption. The window is not optional: far from
  focus a *real* star spreads out and its per-pixel peak SNR legitimately collapses, so an absolute floor
  applied to all frames would penalise correct defocused detections.
- `frac = count(snr < MarginalSnrFloor) / pooledCount`.
- `penalty = 1 − MarginalSnrStrength · max(0, frac − MarginalSnrThreshold)`, clamped to
  `[MarginalSnrMinFactor, 1]` — reusing the existing private `PenaltyFromFraction(frac, threshold,
  strength, minFactor)` overload (`OptimizationObjective.cs:586`).
- Fallback pool (no window formable) guarded by `MinFramesForPenalty` / `MinAcceptedForPenalty`, exactly
  as `SHfrOutlier` does.
- Multiplied into `J` after the weighted sum, beside the other three penalties
  (`OptimizationObjective.cs:399-412`).

### Constants and their defaults

| constant | default | reasoning |
|---|---|---|
| `MarginalSnrFloor` | `5.0` | a 5σ peak — the classic detection threshold. Must exceed the ≈1.5 provably-inert bound. The gate delivers a peak-SNR floor between `F` and `F/PeakResponse ≈ 1.33F`, so 5.0 means "accepted stars have a peak between 5σ and ~6.7σ". |
| `MarginalSnrThreshold` | `0.05` | tolerated marginal fraction before the penalty bites; matches `HfrOutlierThreshold`. |
| `MarginalSnrStrength` | `1.0` | matches `HfrOutlierStrength`. At a Sensitivity-0 landing the marginal fraction is large, so the penalty is worth several percent of `J` — orders of magnitude more than the ~1e-3 differences the search is currently deciding on. |
| `MarginalSnrMinFactor` | `0.5` | at most a 2× reduction from this term alone; the hard floors own the hard-fail path. Matches `HfrOutlierMinFactor`. |

**Why this is not just mechanism (b) with extra steps.** Because the sample is censored at
`p.Sensitivity`, the term is exactly inert above the floor — which makes it a *soft, data-adaptive*
version of (b), and that difference is measurable:

- It is continuous. A dataset can still buy its way below 5.0 if the recall gain is worth the penalty.
- It charges for **admitted marginal detections**, not for the knob's value. A field whose structure map
  produces few sub-5σ candidates pays nothing for going low — D13 landed at 2.5 with precision 0.985
  under HEAD, and (b) would force it up while (a) should leave it alone if its low tail is thin.
- Below the floor it charges regardless of *which* knob opened the door (NoiseClip, StructureLayers), not
  only the Sensitivity axis.

The head-to-head is precisely a test of whether that adaptivity is real. If (a) merely reproduces (b)'s
landings, say so — that is a legitimate result and (b) is then the simpler change to ship.

### Bit-identity contract

Following the established convention (`OptimizationObjectiveTests.cs:279`, `:392`, `:1270`):

- `SMarginalSnr` returns **exactly 1.0** when `FrameStarSnrs` is null/empty, when the pooled sample is
  empty, or when no pooled star falls below the floor. Exact `Is.EqualTo(1.0)`, no tolerance.
- `MarginalSnrStrength = 0` disables the term entirely; `--legacy-objective` sets it to 0 alongside
  `HfrOutlierStrength = 0` and `Wcov = 0` (`OptimizationDiagnosticRunner.cs:557-563`), preserving the A/B
  "before" arm.
- `JRun` stays byte-identical to `LegacyJRun` for metrics carrying no per-star SNR data, unlabeled and
  labeled, with `Wtie = 0`.

Note honestly what this does **not** claim: unlike `SDefocusPrecision`, this term is *not* inert on the
production optimizer path — `FrameStarSnrs` is populated there, which is the entire point. Bit-identity
holds for callers without the data, not for the optimizer.

### Threading the constants

`new ObjectiveConstants()` is constructed at `OptimizationDiagnosticRunner.cs:554-556`, and the same
instance feeds the optimizer, `baselineJ`, the hard floor, the summary and the exposure recommendation —
so a CLI override must be applied between `:556` and `:564`, the way `--legacy-objective` does. Other
bare construction sites that would silently keep defaults if a knob needed overriding:
`SynthValidateRunner.cs:642`, `TiltCalibrationRunner.cs:426`, `StarDetectionOptimizerWizardVM.cs:528`
and `:2954-2956`, plus `StarDetectionOptimizer.cs:89`'s `?? new ObjectiveConstants()`. Wave 1 changes
defaults only, so all of these inherit the new behaviour by construction — which is the intent (the live
wizard must get the same fix as the harness).

## Step 3 — tests

All in `OptimizationObjectiveTests.cs` (the sole unit-test owner of the objective surface) and
`OptimizerVariableTests.cs`:

1. `SMarginalSnr_NoData_ReturnsExactlyOne` — null `FrameStarSnrs`.
2. `SMarginalSnr_AllAboveFloor_ReturnsExactlyOne` — the censored-above-the-floor case.
3. `SMarginalSnr_NearFocusMarginalFraction_PenalisesByShape` — the arithmetic of the penalty.
4. `SMarginalSnr_OnlyDefocusedExtremesAreMarginal_ReturnsExactlyOne` — the window is load-bearing.
5. `SMarginalSnr_RecoveryFramesExempt` — matches the recovery-exemption suite at `:681-826`.
6. `SMarginalSnr_StrengthZero_ReturnsExactlyOne` — the `--legacy-objective` escape hatch.
7. `JRun_BitIdentical_WhenNoStarSnrData` — labeled and unlabeled, `Wtie = 0`, against `LegacyJRun`.
8. `SMarginalSnr_FallbackPool_ThinRunNotPenalised` — the `MinFramesForPenalty` guard.
9. `CreateCuratedSet_SensitivityAxis_HasFloorAtTwoPointFive` and the updated
   `WarmStartSet_MovedAxis_GetsNarrowBandAroundRecommended`.

Full suite gate: `dotnet.exe test <sln> -c Debug --nologo` (Windows `dotnet` via WSL interop; no `dotnet`
in WSL). The flaky `SendAsync_WritesOnABackgroundThread` EAT test is unrelated and fails only under
concurrent optimizer load.

## Step 4 — validation

### What each run costs (measured, not estimated)

| step | cost |
|---|---|
| synthetic `optimize --per-run` (17 datasets, 250 evals) | **~26 min** (observed 19:36→20:02 on the V2 prepass) |
| real-bank `optimize --per-run` (~20 runs) | **~70–85 min** (`docs/star-detection-optimizer-speedup-results.md:141-162`: 4085 s for 16 runs) |
| `bank-verify`, C0 nc-sweep {2,3,4} + A + B | ~1 h 15 m |
| `bank-verify`, `--nc-sweep 2` + A only (2 configs) | ~30 min |

Cost scales with **configs × frames × 2 detections** (`BankVerifyRunner.cs:389`, `:397-422`), so dropping
the nc-sweep to a single value is a real 3× saving on the C0 side — and C0 is unaffected by either
mechanism, so the full sweep is only needed once, on arm H.

### The matrix

Three code arms, each needing its own `optimize` prepass because both mechanisms change where the search
lands:

| arm | code state | synthetic prepass | real prepass |
|---|---|---|---|
| **H** | HEAD, unchanged | A (`optimize --per-run`) + B (`--donut`) | A |
| **b** | `--sensitivity-floor 5` | A | A |
| **a** | `SMarginalSnr` term (floor 5) | A | A |

All three arms run from **one binary**, selected by flag — `--marginal-snr-strength 0` reproduces HEAD's
objective exactly, `--sensitivity-floor 5 --marginal-snr-strength 0` is arm b, and bare defaults are arm
a. That removes "did I build the right arm?" as a failure mode. Arm H is additionally run from a
separately-staged build of unmodified HEAD, and the two must agree on a spot-checked dataset.

Config A = no `--donut`; config B = `--donut` (a *separate prepass*, since `--donut` un-gates the nine
defocus axes at `OptimizerVariable.cs:169-194`). Note `bank-verify`'s `--opt-b` slot always coerces the
donut master on (`BankVerifyRunner.cs:359`, `:483-499`), so it cannot be borrowed as a second plain
settings slot — each arm's config A needs its own verify pass.

Verify passes:
- Arm H, synthetic: `--nc-sweep 2,3,4 --opt-a <H_A> --opt-b <H_B>` — the full reference, comparable to the
  published table.
- Arms b and a, synthetic: `--nc-sweep 2 --opt-a <arm_A>` — C0@nc2 (unchanged control) + that arm's A.
- All three arms, real bank: `--nc-sweep 2 --opt-a <arm_A> --pixel-scale header`.

**Honest total: ~6 h, not the spec's ~4 h.** The spec's estimate assumed a single mechanism; measuring two
against a re-measured baseline is three prepasses on each bank. Everything runs detached with progress
files. `bank-verify` writes `<out>/bank_verify_progress.log` (flushed per step) and an incremental
`<out>/bank_verify/<runId>/verify.json` per run, so partial results are readable mid-flight; `optimize`
writes nothing until a run completes, so its stdout is redirected to a log and tailed.

**Build note:** the TestApp exe is file-locked while running. Rebuild to a separate output directory
(`dotnet.exe build … -o <scratch>`) when a run is in flight, and launch each arm from its own copy.

### Acceptance gates

Scored on the synthetic bank, config A, against arm H (and cross-checked against the pinned published
baseline):

1. **Precision ≥ 0.90 on every dataset.** Six are currently below; the worst is D09 at 0.451.
2. **recall@high loses ≤ 0.02 on every dataset.** (Synthetic "high" = golden peak SNR ≥ 20, not ≥ 12 —
   the report's `recall@SNR≥12` column header and `goldenSNRge12` field name are misnomers on this bank.)
3. **σ_focus no worse than 20%** on any dataset.
4. Real bank: no recall@SNR≥12 or precision regression beyond the same rule. Real-bank precision is a
   lower bound (F11), so it guards transfer, it does not decide the winner.

If **both** mechanisms pass, (a) ships if it is at least as good on precision *and* strictly better on
recall or on the datasets (b) forces upward unnecessarily; otherwise (b) ships as the simpler change and
(a) is written up as measured-and-rejected. If **neither** passes, retune (a) once — `MarginalSnrThreshold`
0.05 → 0.15 and `MarginalSnrStrength` 1.0 → 0.5 if recall regressed; floor 5.0 → 8.0 if precision did not
recover — and re-run the synthetic arm only. More than one retune round is a signal to stop and report.

## Step 5 — re-measure all eight followups

After the winning mechanism is fixed, re-measure and report which of F19–F26 still reproduce. The spec's
prediction on record: **F22 and F26 substantially weaken or disappear**; F19/F20/F21/F25 are untouched
because they are recommender-side, downstream of the landing rather than caused by it.

| entry | how it is re-measured |
|---|---|
| F19 | inspect the derived exposures + `S_now` population; no run needed (unchanged by wave 1) |
| F20 | D01/D02/D03 recall@high and `MinHFR` landings from the arm's verify + `optimized_settings.json` |
| F21 | `synth-validate --datasets D17_cdk14_oiii5 --scenarios S0` across ≥5 seeds — half-width spread |
| F22 | measured-vs-optical in-focus HFR across all 17, and whether the binning recommendation still flips |
| F23 | the acceptance table itself |
| F24 | requires the config-B prepass on the winning arm; report the A/B precision delta |
| F25 | `synth-validate --datasets D05_tec140_1000mm --scenarios S2 --max-rounds 4` |
| F26 | `synth-validate --datasets D08_c11_2800mm --scenarios S1` + `D12_c14_585_afbin2 --scenarios S6` — does the step still hold for four rounds? |

`synth-validate` has `--datasets` and `--scenarios` filters, so these are minutes each, not another
matrix. Its `--out` must **not** be inside `D:\SyntheticAutofocusBank` (the runner hard-refuses:
`SynthValidateRunner.cs:117-125`) because round folders look like valid `attempt01` runs to
`OptimizationRunDiscovery` and would corrupt the bank.

**If F22 and F26 do not dissolve, stop and report before wave 2** — the spec's wave-2 grouping depends on
that prediction holding.

## Step 6 — write-up and PR

- Results doc `docs/f23-objective-precision-term-results.md` with the full A-vs-b-vs-a table, the arm-H
  reproducibility check against the published baseline, and the eight-followup re-measurement.
- Update `docs/followups.md`: F23's status, and F22/F26 with what the re-measurement showed. Any new
  finding gets a new entry — flagged, not fixed.
- Update `docs/synthetic-af-bank-baseline.json` to the winning arm's measurement once merged.
- PR against `develop`.

## Risks

| risk | mitigation |
|---|---|
| A proxy that works on synthetic data does not transfer to real frames | score both banks on every arm; the synthetic one measures the metric exactly, the real one guards transfer |
| The published V2 baseline cannot be reproduced (prepass outputs are gone; the in-bank settings copies match neither published config) | arm H re-measures at HEAD in the same session; a material discrepancy is flagged before any conclusion is drawn |
| `MarginalSnrFloor` is a tuning constant chosen from first principles, not calibrated | it is stated in absolute σ units with a documented inert bound (≈1.5); one bounded retune round is budgeted, and more than one means stop and report |
| The term is inert above the floor, so it may merely reproduce (b) | that is the measurement, and reporting it is the honest outcome; D13 (landed 2.5, precision 0.985) is the discriminating dataset |
| Detection binning makes `MeasuredSensitivity` incomparable across runs | the term is per-run and binning is not a searchable axis, so the statistic's meaning is fixed within a run |
| Harness assertions mis-score the result | already bitten three times; any surprising FAIL is checked against the raw trajectory before it is believed |
