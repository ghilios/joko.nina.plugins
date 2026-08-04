# Synthetic AF bank — followups wave 3 (plan)

Wave 2: [`docs/synthetic-af-bank-followups-wave2-results.md`](../docs/synthetic-af-bank-followups-wave2-results.md).
Register: [`docs/followups.md`](../docs/followups.md) — F35, F20, F32, F33.
Branch: `ghilios/synth-bank-followups-wave3` (cut off `develop`, carries the F35 D03/D05 addendum).

*The question: ship F20's real fix (F35), re-score F24 against the metric it should always have used, and
measure the objective's dynamic range on both banks before anyone proposes a new term for it.*

---

## Two premises in the brief are wrong, and they change the work

Both were checked in code before any of the plan below was written. Neither invalidates F35's measurements —
the `golden eval --min-hfr` sweep stands exactly as recorded. What they change is *where the fix goes* and
*whether part (c) is a fix at all*.

### P1 — the trigger statistic is not available where F35 says it is

F35 step 1 and the brief both say `BestFit.Minimum.Y` is "already computed and already read by the wizard as
`baselineInFocusHfr`". It is — but **only after the search has finished**, in `BuildSummaryAsync`
(`StarDetectionOptimizerWizardVM.cs:3115`). The seeding decision has to be made *before*.

The engine seam cannot see it either. `StarDetectionOptimizer.OptimizeAsync`
(`StarDetectionOptimizer.cs:92`) is the one place all five callers share, and its evaluator returns
`RunEvaluationMetrics`, which carries the vertex **X only**:

```
RunEvaluationData.cs:817-818   var minX = bestFit.Minimum.X;
                               metrics.BestFocusPosition = ... minX ...;
```

`RunEvaluationMetrics` has `SigmaFocus`, `RSquared`, `FrameStarCounts`, `BestFocusPosition`, `FrameStarHFRs` —
**and no `Minimum.Y` of any kind**. So `BestFit.Minimum.Y <= MinHFR` is not expressible at the shared seam
without a contract change affecting all five callers.

It *is* cheaply available at the two callers that matter, because both already run a full fit before the search:

| caller | pre-search fit | engine call |
|---|---|---|
| Wizard | `SeedFitIsUsableAsync:2796` → `AnalyzeWithProgressAsync:2912` returns `List<RunEvaluationResult>`, `BestFit` intact | `:3011` |
| TestApp `optimize` | `perRunBaseline` built at `OptimizationDiagnosticRunner.cs:580-583` | `:640`, 57 lines later |
| TestApp `synth-validate` | **none** — seed straight to search (`SynthValidateRunner.cs:667-700`) | `:700` |
| TestApp `tilt` | **none** | `TiltCalibrationRunner.cs:469` |

**Consequence for the design.** The trigger is computed by the caller (which has the fit) and the *clamp* is
applied by the engine (which owns the axis bounds and `theta0`). That keeps one implementation of the rule and
lets `synth-validate` / `tilt` opt out by simply not asking — rather than silently diverging, which is the
failure shape the `--sensitivity-floor` precedent already has (`OptimizerVariable.cs:133-145`: TestApp passes a
floor, the wizard silently takes the default).

### P2 — the `MinHFR` axis has no 0.25 grid, so part (c) is not the fix it is described as

F35 step 3 and the brief both say the axis is `Continuous(0.1, 5.0, step 0.25)` so "from 1.2 the reachable grid
is 0.95 → 0.70 → 0.45 → 0.20" and "0.45 → 0.20 is a 2.25× jump in a sub-pixel regime".

**`Continuous` is not quantized.** `OptimizerVariable.Quantize` (`:83-92`) is *identity + clamp* for
`Continuous`; only `Integer` rounds. `InitialStep` is documented as "the initial pattern-search step"
(`:54-55`), and Phase B **halves it on every sweep that finds no improving move**, down to
`InitialStep × StepFloorFraction` = `0.25 × 0.125` = **0.03125**
(`StarDetectionOptimizer.cs:464-467`, `:565`, `OptimizerSettings.StepFloorFraction:38`).

So the axis already resolves to ~0.03 px near wherever the search lands. The 0.25 figure is the *first* descent
stride, not the resolution.

And on the motivating rigs it is irrelevant either way: `MinHFR` is **not** in Phase A — `CoarseGrid` is over
`Sensitivity × StarClippingMultiplier` only (`:299-303`) — so it moves solely through Phase B, which requires a
**strictly improving** move. On D01/D02, `J` is identically 0 across the neighbourhood, so no move ever improves,
the steps contract in place, and `MinHFR` never moves at all. That is F20's cold-start plateau, and **no step
size fixes it**. The seed does.

**Consequence for the design.** Part (c) is demoted from "the third part of the fix" to a flagged correction in
the register. Retuning `InitialStep` would change the first stride and the floor for every rig that *does* have a
gradient — a live behaviour change on the whole bank, justified by nothing measured, and squarely the F28 lesson
("verify a fix changes real OUTPUT before claiming a user benefit"). **We flag it; we do not ship it in this
wave.** The one genuinely-needed piece survives: `MinHFRLower = 0.1` (`OptimizerVariable.cs:156`) silently
clamps any seed below 0.1 at `theta0` (`StarDetectionOptimizer.cs:115`), so the seeding rule must either stay
≥ 0.1 or lower that bound. At 0.25–0.35 it stays ≥ 0.1, so nothing is required.

---

## What the seed is sized from

F35 (b) says size it from pixel scale and sampling, never from a measured HFR. The code makes this simpler than
it sounds: **`MinHFR` is already dimensionally self-contained.** From `StarDetector.cs:483-486`:

> "Software binning: from here the WHOLE pipeline runs in binned pixels, so every pixel-unit param (MinHFR,
> MinimumStarBoundingBoxSize, NoiseReductionRadius, …) stays in the range it was calibrated for regardless of
> the rig"

So there is no arcsec/px conversion to apply — the seed is a **constant in binned detection pixels**, and its
justification is a sampling argument rather than a per-rig computation:

- HFR is bounded below by pixel quantization. A star whose flux lands in essentially one pixel measures a few
  tenths of a pixel; it cannot measure near zero.
- The measured floor is **0.25–0.35 px with FP = 0 in all 32 configurations** across D01/D02/D03/D05
  (`docs/followups.md:999-1014`), and the gain saturates by 0.5.
- `D05_tec140_1000mm` (truth vertex 1.80 px) is **flat at 0.985 recall from 1.2 all the way to 0.1** — a rig that
  does not need the seed is provably unperturbed.

**Chosen value: 0.30 px.** Mid-range of the measured-safe band, one full halving-step of margin above the
`MinHFRLower = 0.1` clamp, and strictly below D01's 0.238 px truth vertex is *not* required — the gate is
`star.HFR <= p.MinHFR` (inclusive, `StarDetector.cs:1873-1880`) and the sweep measures 5 vertex-frame stars
surviving at 0.35, which already clears `NHard = 3`.

This value is **not** derived from any measured HFR, so it inherits neither the wing-fit's 2.3× vertex
over-prediction nor the survivor medians' left-censoring at `MinHFR` itself.

---

## Tasks

### T1 — `MinHfrSeed`: the rule, as a pure function, test-first

New `Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/MinHfrSeed.cs`.

```csharp
/// Returns the MinHFR to seed the search with, or null to leave the seed alone.
public static double? Resolve(double fitVertexHfr, double currentMinHfr)
```

Rule: return `SeedFloor` (0.30) iff `fitVertexHfr` is finite **and** `fitVertexHfr <= currentMinHfr` **and**
`SeedFloor < currentMinHfr`; else null.

Tests (`$TS/StarDetection/Optimization/MinHfrSeedTests.cs`), written first:

1. D01 shape — vertex 0.548 (the wing fit's own over-prediction), current 1.2 ⇒ 0.30.
2. D05 shape — vertex 1.804, current 1.2 ⇒ **null** (untouched; this is the control the global seed rests on).
3. Vertex NaN / ±∞ ⇒ null (a run with no fit must not be silently re-gated).
4. Current already below the floor (0.2) ⇒ null — **never raises the gate**.
5. Vertex exactly == current ⇒ seeds (the gate is inclusive, so a vertex *at* the gate is already being rejected).
6. `SeedFloor` is ≥ `OptimizerVariable`'s `MinHFRLower`, asserted against the axis so the silent `theta0` clamp
   at `StarDetectionOptimizer.cs:115` can never apply.

### T2 — apply the seed: engine clamps, callers decide

**Engine.** Add `public double? MinHfrSeedFloor { get; set; }` to `OptimizerSettings`
(`StarDetectionOptimizer.cs:23-39`). In `OptimizeAsync`, between `:104` and `:112` (before `theta0` is read):

```csharp
if (settings.MinHfrSeedFloor is double floor && floor < seed.MinHFR) { seed.MinHFR = floor; }
```

Placed there so the seeded value flows into `theta0`, into `seedJ` (the never-regress floor), and into
`RevertNeutralAxes`' notion of the seed — i.e. the search genuinely starts from it. One insertion, no new
plumbing, and every caller that does not set the field is bit-identical.

**Wizard.** `SeedFitIsUsableAsync` already holds `List<RunEvaluationResult>` with `BestFit` intact
(`:2810`). Carry run 0's `BestFit?.Minimum.Y` to a field, and at `:2981` — immediately after the existing
in-place seed mutation at `:2980`, the precedent for exactly this — set
`optimizerSettings.MinHfrSeedFloor = MinHfrSeed.Resolve(...)`.

**TestApp `optimize`.** `perRunBaseline` at `OptimizationDiagnosticRunner.cs:580-583` already holds the fits;
set the same field before `:640`.

**`synth-validate` / `tilt`.** Leave alone. Neither has a pre-search fit and neither is a user-facing focus
path; both stay bit-identical. Recorded in the register so the divergence is deliberate and visible rather
than discovered later.

**Open decision — `--start-from-current`.** In that mode the seed *is* the user's stored `MinHFR`
(`StarDetectionOptimizerWizardVM.cs:2972`, `OptimizationDiagnosticRunner.cs:239-242`), so stamping it discards
an explicit user setting. The rule already only ever lowers, never raises; the plan takes **stamp anyway**, on
the grounds that a user whose stored gate is destroying their curve is exactly the population F20 names. Flag
for review.

### T3 — validate against the success criterion that F35 actually states

The criterion is **"the hard floor passes and the search gets a gradient"**, not recall.

**Decided: both banks before the PR.** The safe-floor evidence is entirely synthetic (the sweep covers
D01/D02/D03/D05), and F33's rule is that no objective-adjacent change may be accepted on the synthetic bank
alone. Both runs go out detached, in parallel, against a build in its own `-o` directory (the exe is
file-locked while running).

```
TestApp optimize --per-run --runs "D:\SyntheticAutofocusBank" --out D:\hf_w3\seed_A   --max-evals 250
TestApp optimize --per-run --runs "D:\Autofocus Bank"        --out D:\hf_w3\seed_real --max-evals 250
```

Real-bank pass conditions: the four `BaselineJ = 0` runs (`Panos`, `LinwoodFocus`, `SorenVance`, `lumos`) are
the population the seed targets — at least one must move — and **no other real run may regress**, measured as
landed `MinHFR` unchanged wherever the trigger did not fire.

Pass conditions:
- **D01 and D02**: `BaselineJ` / `FinalJ` no longer both exactly 0 — i.e. `NHard = 3` clears. This is the whole
  finding; currently both report `currentJ=0 bestJ=0 hard-floor FAIL`.
- **D05**: landing and recall unchanged vs `D:\hf_f23\H_A` — the control.
- **D03**: expected to benefit most (`0.393 → 0.585` in the sweep), so a *non*-improvement here means the seed
  is not reaching the detector and the wiring is wrong.
- Every other dataset: no recall regression beyond noise.

Cheap pre-check first, before the ~55 min optimize run — confirm the constant does what the sweep says on the
one frame that matters:

```
TestApp golden eval --runs "D:\SyntheticAutofocusBank\D01_ultrawide_40mm" --params default \
    --min-hfr 0.30 --match centroid --out D:\hf_w3\f35_precheck\d01
```

(`--match-radius` is inert on this bank — each dataset's `synthetic_meta.json` carries `matchRadiusPx = 12.0`,
which overrides the CLI at `GoldenEvalRunner.cs:179-181`.)

### T4 — F24 restated: score the nine donut axes against ΔRecall

Target: `D16_esprit550_ha3` (−0.147) and `D04_esprit_550mm` (−0.113) — unobstructed 550 mm refractors where the
donut relaxations should be inert and are not.

**No code change needed.** All nine axes have 1:1 `golden eval` flags (`GoldenEvalRunner.cs:461-483`):
`--defocus-gates`, `--defocus-structure` + `--structure-layer-boost`, `--defocus-size-ref`,
`--defocus-min-factor`, `--defocus-center-factor`, `--donut-morph-close`, `--donut-hole-fraction`,
`--donut-streak-ecc`, `--donut-bloom-radius`.

Four traps the arm design must respect, all verified in `StarDetector.cs`:

1. **Under master-ON, `DefocusAwareGates` is provably inert** — `ComputeEffectiveMaxDistortion:1452`
   short-circuits on `(!DefocusAwareDistortion && !DefocusAwareDonutDetection)`, and centering is the same OR at
   `:1820`/`:1927`. Only **8 of 9** are separable. Say so rather than reporting a null result as a finding.
2. **`--structure-layer-boost` alone is a no-op** — read only when `DefocusAwareStructure` (`:610-615`);
   master-ON-flag-OFF silently applies `DonutDefaultStructureLayerBoost = 2` (`:114`). Neutral arm is
   `--defocus-structure --structure-layer-boost 0`.
3. **Master-ON carries three mechanisms no axis controls**: the default +2 structure boost, the
   `EffectiveClipMultiplier` cap at 2.0 (`:1484-1490`), and the integrated-SNR relaxation for
   `d >= DefocusDistortionSizeReference` (`:1793-1800`). So the arm set needs a **fourth arm — master ON, all
   nine at neutral** — to isolate the un-gated residue. Without it the per-axis deltas will not sum to the
   observed −0.147 and the gap will be misattributed.
4. `golden eval` **silently ignores malformed numeric overrides** (`DiagnosticUtil.cs:34-51` — no unknown-flag
   detection). Every arm must assert its `params:` / `+overrides[...]` line in `golden_eval.txt` before its
   numbers are read. A typo yields a complete, plausible report at the un-overridden value.

Arms: C0 (donut off) · master-ON-neutral · then one axis off-neutral per arm × 8 × 2 datasets. ~40 s each is the
D01/D02/D03/D05 figure and is **unverified for D16/D04** (different frame counts and star densities) — measure
one arm before scheduling the rest.

### T5 — F32/F33: the measurement, and the one cheap reporting change

**The measurement is done** (`scratchpad/f32_dynrange.py`, all inputs already on disk). Results in
`docs/synthetic-af-bank-followups-wave3-results.md`; headline numbers in the register updates below. It reads
recall from the `/3` real-bank run, which is valid: `recallHigh` derives only from `match.Pairs`
(`BankVerifyRunner.cs:488`) and the `/5` repair touches only the false-positive list — precision from that run is
void and is not read.

**The reporting change (F33 step 1).** Report the effective gate `max(Sensitivity, PeakResponse × StarClip)`
wherever a landing's Sensitivity is quoted. A helper already exists — `ExposureRecommender`'s F28
`InertSensitivityBound` — so this is reuse, not new arithmetic.

Scope it to **derive-on-read, no schema bump**: all five inputs are already in the schema-2/3 DTO, verified
against the on-disk `mccomiskey` landing, so every landing already on disk gains the column for free. A stored
*settable* field would trip `TiltAdapterWizardVMTests.cs:1274` (which reflects over every read+write property);
a **get-only computed** property is filtered out by the `CanWrite` check at `:1296` — that is the cheap path.

Sites: `OptimizedStarDetectionSettings`, the wizard's `VariantSensitivity`/`BaselineSensitivity` pair
(`StarDetectionOptimizerWizardVM.cs:3174-3175`) and its XAML, `OptimizationDiagnosticRunner`'s `(AT FLOOR)`
console lines (`:455-458`) and `optimize_summary.txt` (`:912`), and `BankVerifyRunner`'s `ConfigMetrics.sensitivity`
(`:403`).

**Report-only.** Do *not* change `SensitivityIsAtFloor` / `HasLowStarSignal` semantics — those drive UI
visibility and whether `Recommend` is called at all, which is a behaviour change and not what "report alongside"
asks for.

### T6 — flag what wave 2 did not re-verify

Register-only, no code. F1–F8, F18, F21, F25, F26 were measured on the same optimizer landings F32/F33
reinterpret and have **not** been re-checked against the repaired metric. This is an inference from what changed,
not a finding. Each gets a dated note saying so, naming whether it depends on precision (needs re-measurement at
`/5`) or only on recall/σ (unaffected).

### T7 — ship

`docs/synthetic-af-bank-followups-wave3-results.md`, register updates, full suite
(`dotnet.exe test ... -c Debug --nologo`, 3354 passing at the merge; the flaky
`SendAsync_WritesOnABackgroundThread` is known-unrelated), PR to `develop`. Never push `develop`.

---

## Register corrections this wave must make

Not new work — corrections to entries that are now measurably wrong. All FLAG-only per the wave-3 brief.

1. **F35 step 3 is based on a non-existent grid** (P2 above). The axis resolves to 0.03125; the blocker is the
   flat objective, not the stride.
2. **F35 step 1 / F20 step 2 name a statistic that is not available pre-search** (P1 above).
3. **F20's "the only two runs of nineteen" is wrong.** `BaselineJ` is exactly 0.0000 on **four** real-bank runs,
   not two: `Panos`, `LinwoodFocus`, plus `SorenVance` and `lumos`. The latter two detect **zero** stars at C0, so
   they are a different population (genuinely no signal) — and `SorenVance` climbs 0 → 0.9701 while `lumos` stays
   at 0, which is direct evidence that the cold-start plateau is sometimes escapable.
4. **F33's `mccomiskey` arithmetic uses the default PeakResponse, not the landed one.** The entry says
   `0.75 × 10 = 7.5`; the landing's own `StarPeakResponse` is **0.98**, so the effective gate is **9.81**.
5. **F33 names one misread landing; there are more.** `caboose` also lands Sensitivity 0.0 with an effective gate
   of **5.05**. On the synthetic bank, 2 of the 6 "Sensitivity 0.0" landings F23 cites (D11 → 2.36, D12 → 2.10)
   are likewise not floor landings.

---

## Risks

- **The seed is a knob the search cannot climb back out of** (F20/F35, restated in the brief). Mitigated by: the
  rule only ever lowers; it fires only when the fitted vertex is at or below the gate; the value is measured-safe
  with FP = 0 across 32 configurations; and D05 is the control proving a rig that does not need it is unperturbed.
  **Not** mitigated for the real bank — the `MinHFR` sweep is synthetic-only, and F33's whole lesson is that the
  two banks disagree in sign. **Decided: T3 runs BOTH banks before the PR** (see T3).
- **Stamping the seed changes the wizard's changed-parameters table** (`:3073-3082` diffs against
  `runs[0].Baseline` via `CreateCuratedSet`). A seeded-then-unmoved `MinHFR` will display as a change the search
  did not make. No existing convention covers this; `:2980` has the same property today for the donut master and
  does not label it.
- **`golden eval` cannot turn `LocallyAdaptiveBinarization` off** (`--adaptive-binarize` sets true only,
  `GoldenEvalRunner.cs:420`); `bank-verify` can. If any F24 arm needs it off, that is a code change.
