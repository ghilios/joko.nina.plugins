# Synthetic AF bank — followups wave 7 (results, in progress)

Design: [`docs/synthetic-af-bank-followups-wave7-design.md`](synthetic-af-bank-followups-wave7-design.md).
Plan: [`plans/synthetic-af-bank-followups-wave7-plan.md`](../plans/synthetic-af-bank-followups-wave7-plan.md).
Wave 6: [`docs/synthetic-af-bank-followups-wave6-results.md`](synthetic-af-bank-followups-wave6-results.md).
Register: [`docs/followups.md`](followups.md).

*The wave's premise was that F18, F19 and F39(b) each move the bank's expected optima and so must share one
re-baseline. Two of the three turned out not to need one at all — and the cheapest arm on the board settled the
axis the bank has never exercised.*

## Status of this document

**Partial.** The code, the decision and the cheap arm are done; the two expensive arms (F19's exposure ladder,
F18's σ_focus arms) and the re-render are specified, wired and **not yet run**. Every section says which.

| item | state |
|---|---|
| **F19** — the exposure statistic | **Decided: no change.** Position stated (design §1); its pre-registered falsification arm is specified and NOT run |
| **F18** — detectability-bounded step size | **Shipped behind two flags**, 6 discriminating unit tests. The bank σ_focus arms are wired and NOT run |
| **F39(b)** — the binning axis | **Measured. The cheap arm answered it**: recall rises on all seven, precision stays 1.000. `optimize` side wired, NOT run |
| **AF chart starting position** | **Done**, 4 discriminating tests, 2 guards |
| The re-render | **Not needed for F19 or F39(b)** (measured, not assumed). Still owed for F18 |
| F32's confirmation arm | **Untouched, deliberately** — see design §0 and "What this wave did NOT disturb" |

## Headline

| what | result |
|---|---|
| F39(b) | **The bank's binning axis works and had never been switched on.** Overall recall rises on **7 of 7** (+0.087 … +0.146), precision is **1.000 at both factors**, and the mechanism is unambiguous: `LowSensitivity` false negatives collapse **184 → 4** on the worst case |
| …and its cost | **Measured, not glossed:** recall@**high** FALLS on 4 of 7 (−0.018 … −0.066). Binning trades linear resolution for SNR, and the price lands on the bright tier via the shape/size/structure gates. **Filed as a new followup** |
| F39(b)'s re-render | **Not required** — the frames' exposures were *already derived at binning 2* (`expectedOptimal.exposureDefinition` says so verbatim). Honouring the factor **removes** an inconsistency rather than creating one |
| F19 | **Closed as a stated position, not an accident.** The named alternative — a faint-end statistic — is **pinned by the gate**: in the only band where the advice is surfaced, the faintest accepted star's SNR sits at `InertSensitivityBound` (1.5 at defaults) regardless of exposure, so `(10/1.5)² = 44` saturates the 4× cap on every run, forever |
| F18 | Shipped as `min(W_3x, max(W_detect, floor))`, with the **floor as the mirror of the existing widening cap** — the recommender bounded how far one run may widen and had no bound on how far it may narrow |
| The AF chart | The report **already** carried `InitialFocusPoint`; the defect was purely the render. The collapse was a **guard**, so the fix reads the loaded run's own report rather than deleting it |

---

## F39(b) — the axis the bank says it tests and never has

`expectedOptimal.detectionBinning = 2` for **D08, D09, D10, D12, D14, D15, D17**. All seven have only ever been
scored at 1, because `OptimizationDiagnosticRunner` calls `HarnessSettingsStore.ResolveForRun(...)` and **discards
the result** — a pure write side-effect. `D08_c11_2800mm`'s spec description calls it *"the first
detectionBinning=2 dataset"*; until this arm it had never been one.

### The cheap instrument answered it

`golden eval` already exposes every detection override and needs no optimizer. It had no `--detection-binning`;
adding one is a single `Int(...)` line routed through `DetectionBinningResolver.ApplyFactor` (plus the per-run
`PixelScale × factor` that `ApplyDetectionImageContext` also applies — a raw field write would have evaluated
every pixel-scale-dependent gate at half the scale being analyzed). **14 evals, ~6 minutes, no optimizer run.**

`--params default --defocus-donut --match centroid --match-radius 12 --settings` pinned. The `UseAdvanced=False`
warning fired on every arm and reported **"Simple-mode presets override 0 recorded advanced knob(s)"**, so the
pinned file is not lying about anything (plan rule 2, checked rather than assumed).

| dataset | recall@all 1 → **2** | recall@high 1 → **2** | precision 1 → 2 | `LowSensitivity` FN 1 → **2** | other FN 1 → 2 |
|---|---|---|---|---|---|
| `D08_c11_2800mm` | 0.870 → **0.962** | 1.000 → 0.970 | 1.000 → 1.000 | 41 → **3** | 0 → 9 |
| `D09_c14_3800mm` | 0.761 → **0.906** | 0.978 → **0.989** | 1.000 → 1.000 | 49 → **7** | 7 → 15 |
| `D10_rc16_3250mm_sparse` | 0.852 → **0.939** | 0.966 → 0.948 | 1.000 → 1.000 | 15 → **1** | 2 → 6 |
| `D12_c14_585_afbin2` | 0.562 → **0.675** | 0.879 → 0.813 | 1.000 → 1.000 | 114 → **34** | 30 → 73 |
| `D14_cdk14_2563mm_e47` | 0.841 → **0.987** | 0.984 → **0.992** | 0.999 → **1.000** | 184 → **4** | 6 → 11 |
| `D15_cdk20_3454mm_e47` | 0.764 → **0.917** | 0.931 → 0.874 | 1.000 → 1.000 | 52 → **0** | 8 → 21 |
| `D17_cdk14_oiii5` | 0.875 → **0.990** | 1.000 → 1.000 | 1.000 → 1.000 | 26 → **2** | 0 → 0 |

**Three things follow.**

1. **The physics-derived factor is right, and the bank now proves it.** Overall recall improves on every one of the
   seven, by 0.087 to 0.146, with **zero** precision cost — the single false positive anywhere in the set (D14 at
   binning 1) disappears at binning 2. The gate rejections these datasets have always shown were an artifact of
   scoring them at a factor their own physics says is wrong.
2. **The mechanism is not inferred.** `golden eval`'s false-negative attribution names it: `LowSensitivity`
   rejections fall 41→3, 49→7, 15→1, 114→34, **184→4**, 52→0, 26→2. Binning raises the per-binned-pixel SNR, so
   the Sensitivity gate stops eating faint stars. Nothing about the gate changed.
3. **It is not free, and the price is on the bright tier.** `recall@high` FALLS on four of seven
   (D08 −0.030, D10 −0.018, **D12 −0.066**, D15 −0.057) while overall recall rises. The "other FN" column is where
   it goes: at half the linear resolution, stars start failing candidate FORMATION and the shape/size gates —
   `TooSmall` and `NO CANDIDATE (structure gap)` appear at binning 2 having been absent at 1, alongside more
   `TooDistorted` / `NotCentered` / `OnBorder`. **Filed as a new followup rather than fixed here.**

**A hypothesis that was checked and was wrong.** The obvious first explanation for a bright-tail loss at binning 2
is [F38](followups.md#f38--the-minhfr-seed-trigger-compares-a-captured-pixel-vertex-against-a-binned-pixel-gate)'s
space mismatch — `MinHFR` gates inside the binned raster, so at factor 2 it would effectively gate at 2.4 captured
px. The attribution table refutes it: **`TooLowHFR` does not appear in any of the fourteen runs.** The loss is
candidate formation and shape, not the HFR gate. (Wave 6's rule about not building a harness to explain a number
before checking the number, applied one level down.)

### Why no re-render was needed, which was not obvious going in

Reading `D08`'s stored `synthetic_meta.json`:

> `exposureDefinition`: *"Exposure t solving snr(t) = … = 10 for the 20th-brightest of 123 on-frame stars … **at
> binning=2 (captureBinning=1×detectionBinning=2)**, clamped to [0.5, 30] s"*

The bank's exposures for these seven **were already derived assuming binning 2** while every run scored them at 1.
`detectionBinning` is a detector-side setting and enters no render input (`GenerateSweep` takes centre / step /
offsetSteps / exposure). So part (b) does not introduce a configuration — it removes an inconsistency, and the
frames do not move. That is what took F39(b) out of the shared re-baseline entirely.

### What shipped

- `golden eval --detection-binning N` (with the `PixelScale × factor` the detector applies).
- `optimize --apply-run-detection-binning` — **opt-in**, absent ⇒ bit-identical. Deliberately applies **only** the
  binning: making the whole per-run `Resolved` authoritative would let a per-run settings file shadow the
  `--settings` file every arm pins, which would break the comparability of every arm this project has run.
- `HarnessSettingsStore.ResolveRunDetectionBinningFactor`, which **prefers the dataset's physics-derived
  `synthetic_meta.json` value** over the settings file (whose value for all seventeen synthetic datasets is
  `kept-from-base`, the field wave 6 added so exactly this is readable) and **prints which source it used**.

**Not yet run:** the `optimize --per-run` arm (σ_focus / R² / `FinalJ` at the new factor) and the F38 bank
regression assertion that becomes possible for the first time.

---

## F19 — the exposure statistic: decided, no change

Full reasoning in [design §1](synthetic-af-bank-followups-wave7-design.md). Three legs, and the second is the one
that decides it:

1. **The recommendation inverts the objective's own knee.** `S_stars` saturates at `nMedian ≥ NTarget = 20`; the
   optimizer stops rewarding stars past 20/frame, and the exposure recommendation asks what exposure would put
   `NTarget` stars above the healthy default gate. `Recommend` reads `NTarget` from the caller's
   `ObjectiveConstants` rather than a literal, so an objective retune moves both together by construction.
2. **The named alternative is pinned by the gate and cannot function as a control signal.** "The SNR at the star
   count the fit actually consumes" is the faintest accepted star's SNR. A candidate is accepted iff it exceeds
   `max(Sensitivity, InertSensitivityBound)`, and the exposure block is surfaced **only** when
   `Sensitivity ≤ 1.0` — so the binding bound is the inert one, **1.5** at shipped defaults. A longer exposure does
   not raise that number; it admits more faint stars whose faintest again sits at the bound. `t·(10/1.5)² = 44·t`
   saturates `MaxExposureFactor = 4` on every run, forever. The measured variable is a fixed point of the
   actuator's own gate.
3. **The residue belongs to F18.** What is genuinely left of F19 — a rich field whose *wing* frames are starved —
   is a sweep-geometry defect, and the product already says so for the adjacent signal (`FlatRejectedCount` is
   documented as *"narrow the sweep, never expose longer"*). Answering it a second time through the exposure knob
   would put two recommendations on one page pulling opposite ways on the same evidence.

Also relevant, and post-dating F19: the recommendation shipped in v4.0.0.12 with **three** verdicts, and the
"are there enough stars?" half is already answered by `StarCountIsTheLimit` / `StarFieldIsExhausted` — with a
**probe and a stopping rule** rather than a formula, because whether more exposure reveals more stars depends on a
luminosity function one sweep cannot measure.

**What can still refute this, pre-registered and NOT yet run.** Arm E: `D16_esprit550_ha3` (F19's own poster
child) and `D02_rich_135mm` rendered at 0.5 / 1 / 2 / 4 / 8 s, fixed detector settings, σ_focus read off each rung.
**If any longer rung improves σ_focus by more than 20 % relative to the derived-exposure rung on either dataset,
the position is refuted and F19 stays Open.** The instrument shipped (`exposureSecondsOverride` on the dataset
spec, mirroring the three existing pins); the arm has not been run.

**Because F19 decided "no change", the wave's re-render footprint is F18's alone** — the exposure axis of
`SynthBankDerivations` does not move, so nothing outside F18's step derivation can shift a rendered frame.

---

## F18 — shipped behind two flags; the arms are wired and owed

`min(W_3x, max(W_detect, floor))`, where `W_detect` is the outermost non-recovery frame that still detected `NHard`
stars — **measured** from `RunEvaluationMetrics.FrameStarCounts` / `FrameFocuserPositions` / `FrameIsRecovery`,
never extrapolated.

**The two decisions F18 left open, both closed:**

- **(a) Do focus-recovery steps extend the sweep past the band?** *Yes for the 3× band, no for detectability* —
  and which of those two the SHIPPED default should be is settled by measurement, not argument: arm D (bound only)
  and arm S (bound + size for the executed sweep) with a rule fixed in advance. Sizing the base step for a
  conditional path costs **20 % of the lever arm on every successful run**, which is not obviously worth paying.
  `MaxUsefulHalfSpan` is reported so a consumer can say a recovery point beyond it buys nothing; *acting* on it is
  engine-side and filed separately.
- **(b) What floor does `W_detect` need?** Two guards. Fewer than 3 qualifying frames ⇒ `W_detect` is **NaN, not
  zero** — "unmeasurable", never "nothing is detectable". And `MinHalfWidthSampledHalfSpanMultiple = 0.5`, **the
  mirror of the existing `MaxHalfWidthSampledHalfSpanMultiple = 1.5`**: the recommender has always bounded how far
  one run may widen and had no bound on how far it may narrow. At 0.5 the worst one-run shrink is 0.57×.

**On F18's own 3800 mm run**: `min(6221, 5310) = 5310` → step **1517** (arm D) or **1214** (arm S). F18's entry
quotes 1060–1330, which divides by 4–5 — i.e. it is the arm-S figure. Both are pinned as unit tests.

**F21 / F25 / F26, stated so a passing bank cannot be read as four entries closed:**

| entry | what this design does | status |
|---|---|---|
| F21 (half-width unstable at R² = 1.0000) | the 0.5× floor bounds a one-round collapse to 0.57× | **magnitude only.** `FindHalfWidth`'s outward search is undiagnosed; F21's instrumentation step is still owed |
| F25 (degenerate fit → *wider* sweep) | `W_detect` cannot exceed what was sampled, so the 4×→7× over-reach is bounded | **magnitude only.** No fit-quality gate: at R² = −0.223 the recommender still answers instead of recognising it has no curve |
| F26 (stuck binning starves the step update) | nothing | **not covered.** It is the wizard's update ORDERING, downstream of F22 |
| F45 (Grubbs rejects the in-focus point) | nothing | **different component** (the AF engine's blind walk). Its part (b) wants the same bank σ_focus instrument, which is a reason to build it, not to bundle it |

**`optimize` deliberately stays on the pre-F18 recommender**, so every F32/F35 arm's reported step remains
comparable. The F18 arms run through `synth-validate`, which is the recommender's own convergence driver.

**Not yet run:** arms C/D/S over `synth-validate --scenarios S0,S1,S2`; the `SynthBankDerivations` analytic twin
(`W_detect*`); the `--dry-run` diff and the re-render it scopes.

---

## The AF chart's starting focuser position

**Verified rather than implemented, per the brief.** `HocusFocusReport.GenerateReport` already serializes
`InitialFocusPoint {Position, Value}` (`:93-96`) and `FinalHFR` (`:105`). Confirmed on disk:
`%LOCALAPPDATA%\NINA\AutoFocus\2026-08-05--21-47-43--90d513b9….json` reads `Position 25000.0`, `Value 0.70298`,
`FinalHFR 0.70526`, against `CalculatedFocusPoint.Position 24999.996`. Six of the six most recent reports carry a
populated `InitialFocusPoint`. No serialization work was needed.

**The defect was the render, and the collapse it produced is a GUARD.**
`HocusFocusVM.SetCurveFittings:594` resets `InitialFocuserPosition` / `InitialHFR` / `FinalHFR` whenever the loaded
chart is not the run this VM produced — step 3 of [`af-chart-reload-sync-design.md`](af-chart-reload-sync-design.md),
which fixed a real 2026-07-29 field report where one run's curve rendered over another run's markers and info rows.
Core's `LoadChart` rebuilds only `FocusPoints` / `PlotFocusPoints` / `FinalFocusPoint` / `LastAutoFocusPoint` /
method / fitting, so without the reset those rows keep the previous **live** run. Deleting it reintroduces the bug.

**So the fix reads the loaded run's own values.** Core keeps exactly one handle on which report was loaded —
`LastAutoFocusPoint.Timestamp`, copied verbatim from `report.Timestamp` (`AutoFocusToolVM.cs:305`). A new
`ILoadedAutoFocusReportSource` re-reads that report from `ReportDirectory`: the file NAME's second-stamp narrows
the candidates (±5 s, a cheap directory listing with no parsing) and the report's own `Timestamp`, compared for
**exact** equality, decides. The window exists because the report's `Timestamp` and its file name come from two
separate `DateTime.Now` calls with a serialization between them; the exact match is what stops a neighbouring
second's report being adopted — which would be the stale-value bug in new clothes.

Deserializing as `HocusFocusReport` rather than the base type is what recovers `FinalHFR`, which core drops.

**Fail-visible, by construction.** Every path that cannot produce a value *from this report* writes the sentinel
that collapses the row (`-1` / `0.0` / `0.0`) — byte-identical to the pre-fix behaviour. So the change can only add
information about the loaded run: it can never substitute another run's number, and it can never present a missing
value as a measured 0. `Position ≤ 0` counts as "not recorded" (a missing `InitialFocusPoint` deserializes to
`new FocusPoint()`, i.e. `Position = 0`, and the engine's own not-known sentinel is `-1`). A core-written or
other-plugin report has no `FinalHFR`, so the HFR-Change row collapses and Start-HFR shows alone — exactly what
that report knows.

**Tests: 4 discriminating, 2 guards, both confirmed by reverting.**

| test | discriminates against |
|---|---|
| foreign chart renders the loaded report's 25000 / 0.703 / 0.705 | the pre-fix unconditional collapse |
| `InitialFocusPoint.Position = 0` ⇒ `-1` while `FinalHFR` still renders | the pre-fix collapse |
| file-name second ≠ report `Timestamp` ⇒ still found | the pre-fix collapse |
| a report 2 s away is **not** adopted | a **filename-only** match (verified separately by loosening the match: exactly this test fails) |
| no report on disk ⇒ sentinels | — **guard** |
| same-run reload ⇒ live values preserved | — **guard** (the `af-chart-reload-sync` regression) |

---

## What this wave did NOT disturb

- **F32's confirmation arm.** Nothing in this wave has re-rendered a frame, so every synthetic dataset — including
  `D18` / `D19` / `D20` — is bit-for-bit what wave 5's φ arms and wave 6's Arm R were measured on. The arm remains
  runnable and comparable **today**. When F18's re-render happens, design §0's rule applies: the moved datasets'
  frames are preserved first, and an arm run on the new bank must re-run wave 5's φ arms rather than compare
  across.
- **Every prior arm's landings.** `optimize` is unchanged with no flags: `--apply-run-detection-binning` absent is
  bit-identical, and `optimize` was deliberately left on the pre-F18 step recommender.
- **The bank's stored settings ([F15](followups.md#f15--optimize---per-run-overwrites-each-runs-stored-settings)).**
  No `optimize --per-run` arm has been run in this wave, so no run folder's `optimized_settings.json` has moved.
  The wave-6 mapping still describes the bank.

## Verification

- Full suite green: **3652** passed, 0 failed (develop was 3635 → **+17**).
- Discriminating counts, each confirmed by neutralizing the change and re-running:
  **F18 6 discriminating / 3 guards**, **AF chart 4 / 2**, **F39(b) 4 tests** (the resolution precedence, the
  fallback message, the no-source default, and `ApplyFactor`'s PixelScale carry).
- Per [F37](followups.md#f37--the-ci-test-host-crashes-natively-accessviolationexception-aborting-2000-tests-with-zero-failures),
  a red CI check is checked against the native test-host crash — verify the test COUNT — before being read as a
  regression.

## Reproduce

```
# F39(b) arm G1 -- the cheap instrument, no optimizer, no re-render (~6 min)
D:\hf_w7\f39b_golden.sh          # outputs in D:\hf_w7\golden

# still owed, in plan order:
#   Step 2  Arm E  -- F19's exposure ladder (needs exposureSecondsOverride rungs added to the spec)
#   Step 4         -- SynthBankDerivations' W_detect* analytic twin
#   Step 6 arm G2  -- optimize --per-run --apply-run-detection-binning on the seven
#   Step 7         -- synth-bank --dry-run diff, then --overwrite on exactly the moved set
#   Step 8  C/D/S  -- synth-validate --scenarios S0,S1,S2 (+ --step-detect-bound / --step-size-for-executed-sweep)
```

## Lessons so far

**1. "Each of these costs a re-baseline" was a hypothesis, and two of the three did not.** F39(b) needed none —
the frames' exposures were already derived at the factor nobody was applying — and F19 needed none because the
decision was "no change". Reading the stored `expectedOptimal.exposureDefinition` before scheduling a re-render
cost one minute and removed most of the wave's expensive half. Wave 6's `D02` lesson, one level up: check whether
the expensive step is necessary before planning around it.

**2. The cheapest arm on the board was the decisive one.** `golden eval` needed one flag and six minutes to settle
an axis the bank has claimed to test since it was built, and it came with its own mechanism attribution — no
optimizer landing, no σ, nothing to interpret.

**3. Check the obvious explanation before believing it.** The bright-tier loss at binning 2 looks exactly like
F38's binned-vs-captured `MinHFR` mismatch. The attribution table has no `TooLowHFR` at all: it is candidate
formation and shape gates. The instrument that would have confirmed the wrong story was never run because the
instrument that names the answer was already in the output.

**4. A guard is not an oversight.** The AF chart's collapsed rows were the fix for a prior field report. The
change that was actually needed was to give the guard something true to render, not to remove it — and the test
that proves it is the one asserting a report two seconds away is still rejected.

**5. State the position AND what would refute it.** F19 closes on a mechanism argument, but the argument makes a
claim about the world ("a field with 20 bright stars gains nothing from more exposure") that a five-rung ladder
can falsify. The rule is written down before the arm runs, so the arm cannot be read after the fact.
