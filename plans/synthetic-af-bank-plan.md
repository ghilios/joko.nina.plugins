# Synthetic Autofocus Bank + Validation Run — Implementation Plan

Design spec: [`docs/synthetic-af-bank-design.md`](../docs/synthetic-af-bank-design.md).

Branch `ghilios/synthetic-af-bank` off `develop`; PR at the end (never push `develop`).
Commit with the privacy email env vars per `CLAUDE.md`.

Issues discovered by validation are **flagged as `docs/followups.md` entries, never fixed here**.
This work bootstraps a regression baseline; behavior changes are separate work.

## Constraints that shape the whole plan

- **No new persisted options.** IMX585 rides the existing `SensorModel` option ⇒ no
  `Resources/OptionsDataTemplates.xaml` work. No `StarDetectorMetrics` fields added ⇒ no
  `AutoFocus/DataTemplates.xaml` work.
- **Build/test via Windows `dotnet.exe`** from WSL (`wslpath -w` the solution path); there is no
  `dotnet` in WSL. Suite at milestones + a final gate, not after every edit. The EAT test
  `SendAsync_WritesOnABackgroundThread` is known-flaky and unrelated.
- **`dotnet test` deploys the plugin** (the csproj PostBuild xcopies into NINA's plugin folder), so
  NINA must not be running during a test run.
- **Long runs detached** with flushed progress files; WSL buffers stdout.

## Execution sequence

| # | Task | Tier |
|---|---|---|
| 0 | Branch; write `docs/synthetic-af-bank-design.md` + `plans/synthetic-af-bank-plan.md` | cheap |
| 1 | G1 truth seam + G8a byte-identical test | Sonnet + review |
| 2 | G2 IMX585; G3 FITS writer (parallel) | cheap/mechanical |
| 3 | G4 derivations; G5 golden policy (parallel) | Sonnet |
| 4 | G6 runner + G7 spec JSON; G8b/c tests | Sonnet |
| 5 | Smoke: `--dry-run` → generate D06 `--verify` → discovery + golden eval on it | operational |
| 6 | V-P1 pixel-scale patch (+ `cwhite_2026` anchor guard); V-P2 match radius; V-P3 | Sonnet + review |
| 7 | V1 driver: skeleton, scenarios + truth derivations, update policy + assertion engine, reports | Sonnet + careful review of the assertion engine |
| 8 | Fixed-point + cap unit tests | Sonnet |
| 9 | Full test suite gate #1 | operational |
| 10 | Generate the full bank (~30–60 min detached); determinism spot-check | operational |
| 11 | V1: S0 all datasets → CLI parity + `--max-evals` stability → full matrix detached (~4–6 h) | operational |
| 12 | V2: A/B prepasses detached (~4–8 h) → `bank-verify` full parity → expectations band check | operational |
| 13 | `docs/synthetic-af-bank-baseline-results.md` + followups triage | mechanical writing + review |
| 14 | Full test suite final gate; PR | operational |

**Harness self-verification order** (any failure stops downstream reads):
S0 everywhere → CLI parity → fixed-point unit test → `cwhite_2026` anchor (profile mode) → determinism.

## Task 1 — G1 truth seam

New `CameraSimulator/Rendering/StarTruth.cs`; `StarFieldCompositor.Render(request, ICollection<StarTruth>, token)`
overload **on the concrete class** (`IStarFieldCompositor` untouched — the camera seam and its MEF
composition must not change); existing `Render` delegates with a null sink; `BuildStampJobs`
(`StarFieldCompositor.cs:182-237`) appends one truth per accepted job, including wing-spill stars kept
by `psfMargin` / `WorstCaseKernelRadius`.

Also in this task, because the golden policy depends on it:
- `PsfKernel.MaxPeak` + `PsfKernel.PhasePeak(px, py)` (new — no peak accessor exists today).
- `PsfKernel.InnerRadiusPixels` (computed in `GenerateAnalytic`, currently discarded).
- `StarFieldCompositor.MaxAbsDefocusMicrons` private static → `internal static`.

**G8a**: `FakeCatalogReader` scene ⇒ truth matches injected pixel positions and
`MeasuredHfrPixels ≈ DefocusModel`; and the rendered `ushort[]` is **byte-identical** with and without
a sink. The byte-identity assertion is the one that must not be skipped.

## Task 2 — G2 IMX585, G3 FITS writer (parallel, disjoint files)

**G2**: `SonySensorModel.IMX585` in `Interfaces/ICameraSimulatorOptions.cs` (with `[Description]`) +
`SensorRegistry` row `("IMX585", 3840, 2160, 2.90, 12, 40000.0, qe, 3.3, 252, 1.0, 0.8, 460, 0.003, 0.0)`.
`SensorRegistryTests` needs new rows in the geometry and e⁻/ADU `[TestCase]` tables; the count
assertion self-updates from the enum; the QE assertion forces `QeCurve.SonyBsiVisible`. Update the
stale "four sensor definitions" class comment.

**G3**: new `TestApp/MonoFits16Writer.cs` (pure: `ushort[]` + ordered `FitsCard` list → BZERO=32768
big-endian int16, 2880-byte blocks). Cards in order: SIMPLE, BITPIX, NAXIS, NAXIS1, NAXIS2, BZERO,
BSCALE, XBINNING, YBINNING, XPIXSZ, YPIXSZ, FOCALLEN, EXPTIME, GAIN, FOCUSPOS, INSTRUME, END.
`XPIXSZ` = physical µm × **capture binning** (NINA convention — `PixelScaleForFrame` multiplies by
`BinX`, so writing the physical size would double-count). 12/14-bit sensors left-shift `16 − bits`
**after** `BinFrame` (which clips at `(1 << bitDepth) − 1`). `ExportLinearRunner.WriteMonoFits16`
becomes a thin adapter so there is one implementation.

## Task 3 — G4 derivations, G5 golden policy (parallel)

**G4**: `TestApp/SynthBank/SynthBankSpec.cs` POCOs + `SynthBankDerivations.cs` —
`step* = √8·HFR_eff/(κ·3.5)` with `HFR_eff = max(HFR_min, 0.70px)`;
`binning* = RecommendFromHfr(in-focus HFR in captured px)`; exposure band = `t` where the
20th-brightest on-frame star's gate SNR (`≈0.85·peak/σ`, `σ = bin·√((sky+dark)·t + RN²)`) hits 7…20 on
the median sweep frame, clamped [0.5, 30] s; `donut* = ε>0 ∧ r_in@extreme>2px ∧ HFR@extreme≥9px`.
Kernel-cap guard: extreme defocus ≤ `0.9 × MaxAbsDefocusMicrons` (the compositor's 500 px clamp, not
the generator's 512 px throw).

**G5**: `TestApp/SynthBank/GoldenFromTruth.cs`, pure and linkable into Tests (as `GoldenStarSet.cs`
already is). Binning transform `c_binned = (c+0.5)/bin − 0.5`, HFR/radii ÷ bin. Tiers by
`peakSNR = Flux·KernelPeakFraction·bin²/σ_bg`: high ≥20, medium 10–20, low 5–10, 3.5–5 → `unresolved`,
<3.5 → omitted. Close pairs by union-find on `HFR_pair` (flux-weighted): <2.0× → one merged box at the
flux-weighted centroid; 2.0–4.0× with flux ratio <10 → both `unresolved`, ratio ≥10 → bright stays;
≥4.0× → independent. Box `w=h=2·ceil(max(2·HFR_frame, r_out+3σ_min, 4))`; edge-clipped → `unresolved`;
off-frame donut arcs → `unresolved` over the intersection. Saturation (peak e⁻ ≥ 0.98× digital
saturation) → keep, tier high, `"saturated": true`, excluded from HFR-assessable. Emit
`<frame>.fits.golden.json` (schemaVersion **2**, `method:"synthetic"` — add `method` to the C# record;
it exists in the Python writer but not in C#) + `<frame>.fits.truth.json` (full dump + per-star
disposition; survives `bank-clean`).

## Task 4 — G6 runner, G7 spec, G8b/c

`TestApp/SynthBankRunner.cs` + `Program.cs` dispatch (flat `args[0].Equals(...)` guard, args passed
through whole; `DiagnosticUtil.GetArg` / `HasFlag` for parsing; exit codes 2 usage / 1 exception /
3 assertion):

```
synth-bank --spec <json> --out D:\SyntheticAutofocusBank
           [--datasets ids] [--overwrite] [--dry-run] [--verify] [--catalog path]
```

Layout `<out>\<datasetId>\attempt01\NN_FrameNN_BitDepth16_Bayered0_Focuser<pos>.fits`, 9 frames at
`x0 ± 4·step` ascending. `run_meta.json` in `attempt01` (apriori `donutAware` + reason);
`synthetic_meta.json` at the **dataset root** (outside `bank-clean`'s per-run walk and outside
`optimize --per-run`'s write target — F15). `NoiseSeed = SeedMixer.Combine(bankSeed, datasetIndex, frameIndex)`,
recorded. `--dry-run` prints derived params + kernel-guard table + per-dataset catalog counts (closes
R5 before any render). `--verify` reloads frame 0 of each dataset via `FITS.Load` and hard-asserts
`Camera.PixelSize` / `Telescope.FocalLength` / `Camera.BinX` / `Image.ExposureTime` (closes R1).
Idempotent without `--overwrite`. Does **not** write `harness_settings.json`.

Library API for V1: `GenerateSweep(DatasetSpec, BootstrapParams{centerPosition, stepSize, offsetSteps,
exposureSeconds, afBinning, emitGoldens}, outDir)`, `seed = Mix(datasetSeed, scenarioId, round)`.

**G7**: `TestApp/SynthBank/synthetic-bank-spec.json` checked in (17 datasets per the design matrix),
SHA-256 recorded in metadata. Defaults: L filter, gain 100, bias 500, −10 °C, throughput 0.85,
offset 4, tier thresholds {20, 10, 5, 3.5}.

**G8b/c**: golden-policy edge cases (T1/T2 boundaries, dominance, tier cuts, edge clip, saturation,
binned transform) and FITS card/block format (pure). The `FITS.Load` round-trip stays in the runner
self-verify — the Tests project has no cfitsio natives.

## Task 5 — Smoke

`--dry-run` (read the catalog counts and kernel-guard table before trusting anything) → generate D06
with `--verify` → confirm `OptimizationRunDiscovery` finds it as one run with 9 positions → run
`golden eval --match centroid --match-radius <meta>` on it and read the recall/precision. **`--match
centroid` with the default radius 0 matches nothing** — always pass the radius explicitly.

## Task 6 — V-P1/2/3

`BankVerifyRunner.cs` (155 compute / 183 pass / 205 consume) and `GoldenEvalRunner.cs`
(`BuildEvalParams:294`): keep the profile value as fallback; per run, after the first frame loads, use
`HarnessSettingsStore.PixelScaleForFrame(meta, harnessSettings, out source)` when finite. For
`GoldenEvalRunner` that means moving `p.PixelScale` out of `BuildEvalParams` to after the first frame
load. Add `--pixel-scale header|profile` (**default header**) to both; bump the report schema to
`afbank-verify/3`; record per-run `pixelScale` + `pixelScaleSource`.

**Anchor guard before moving on**: `--pixel-scale profile` on the real bank's `cwhite_2026` must
reproduce precision 0.848, recall@≥12 = 0.181, sensor R² = 0.9933, 7/9 aligned. Then record the
header-mode real-bank delta once, in the results doc.

V-P2: both runners read `matchRadiusPx` from `synthetic_meta.json`; precedence per-run meta > CLI
`--match-radius` > 12; absent meta unchanged; effective radius reported per run.
V-P3: `bank-clean` keep-list += `synthetic_meta.json`.

## Task 7 — V1 convergence driver

`TestApp/SynthValidateRunner.cs` + `SynthValidationScenarios.cs` + `SynthValidationReport.cs`.
STA thread + pumped `DispatcherSynchronizationContext` **copied from `BankVerifyRunner:80-102`**
(`RunEvaluationData.EvaluateAndFitAsync` deadlocks on a non-pumping context; the thread-affine sensor
model fit hangs on the thread pool). No `ConfigureAwait(false)` in that path.

```
synth-validate --spec <json> --out <dir> [--datasets] [--scenarios] [--max-rounds 4] [--max-evals N]
```

`--out` defaults to `D:\SyntheticAutofocusBank-validation` and **must be outside the bank root**, or
discovery picks up scenario rounds as runs.

Per round: `GenerateSweep` → FITS in `<out>/<ds>/<scenario>/round_k/attempt01/` → load exactly as
`optimize` does (EXPTIME → `t_old`, step inferred from positions, `PixelScaleForFrame`) →
`RunEvaluationData.CreateEvaluator` → `StarDetectionOptimizer.OptimizeAsync` → extract
`StepSizeRecommender` on the winning fit, `ExposureRecommender` gated on `SensitivityIsAtFloor` (as
`BuildAggregateRow:792-796` does), `RecommendFromHfr(vertex HFR)` gated R² ≥ 0.9 → apply the update
policy → next round.

**Update policy**: binning first (defer exposure one round — SNRs are per-binned-pixel); else exposure
(only at-floor ∧ `IncreasesExposure`) + step; re-center on the fitted vertex; donut never toggled.

**Scenarios**: S0 control on every dataset (run first — the harness self-test); S1 step ×0.25; S2 step
×4; S3 exposure ×0.25 (plus D16 for `CappedByAbsoluteLimit`); S4 det-binning 1 where 2 expected;
S5 donut-off on obstructed datasets (PASS = degradation signature, standing FLAG); S6 step+exposure.

**Assertions A1–A7**, tri-state PASS/FLAG/FAIL, exit 1 only on FAIL: direction; monotone approach;
terminal band `[0.6,1.6]×` **`step_behavioral`** (the recommender's own fixed point on the truth curve;
`|step_behavioral − step_theory| > 25%` ⇒ FLAG referencing F18); cap semantics (`WasCapped` iff truth
half-width > `1.5 ×` sampled half-span — the `InFocusHfrDiagnosticTests` invariant); state sanity
(`StarFieldIsExhausted` only where plausible, nothing applied when gated, `OffsetSteps == 4`); fit gate
R² ≥ 0.95 per round; vertex tracking `|vertex − x0| ≤ max(2·step, 0.02·W_3x)`.

**F8 discipline: never assert landed optimizer knob values.** S3 at-floor miss = FLAG, not FAIL.

Report `synth-validate/1`: JSON + MD, per dataset × scenario × round, plus terminal
{converged, roundsUsed, deltas, flags}; flushed progress log.

## Task 8 — Fixed-point + cap unit tests

Extend the `InFocusHfrDiagnosticTests` area: a step fixed-point test (feed the recommender its own
recommendation on a truth curve and assert it stays put within band) and cap-semantics coverage
against `MaxHalfWidthSampledHalfSpanMultiple`.

## Tasks 10–12 — the long runs

All detached with flushed progress files.

- **10**: generate the 17 datasets (~30–60 min). Then regenerate 2 designated datasets from seeds into
  a temp root and diff frame SHA-256.
- **11**: S0 on all datasets → **CLI parity** (in-process round-0 must equal real
  `TestApp optimize --per-run` on the same folder) and `--max-evals` 120 vs 250 stability on one
  dataset → full matrix (~4–6 h).
- **12**: `optimize --per-run` (A) and `--per-run --donut` (B) prepasses (~4–8 h) → `bank-verify --runs
  D:\SyntheticAutofocusBank --nc-sweep 2,3,4 --opt-a … --opt-b … --commit <sha>` → check
  `docs/synthetic-af-bank-expectations.json` bands (W C0@nc2 recall@high ≥0.90/precision ≥0.95;
  M ≥0.97/≥0.95; L on config B ≥0.90/≥0.90; A precision ≥0.98 all non-donut; AF R² floors).
  Broken C0 on donut datasets is expected and recorded.

## Task 13 — Results + followups

`docs/synthetic-af-bank-baseline-results.md` in the style of `docs/af-bank-noiseclip-sweep-results.md`
(single `#` title with em-dash suffix; scope/provenance paragraph naming the harness command, plan and
PR; `##` Headline / caveat / Verdict / Per-run notes / Reproduce; pipe tables with `→` transitions and
**bold** on notable cells; a `| criterion | result |` PASS table; a fenced `Reproduce` block).

Triage every FLAG into `docs/followups.md` starting at **F19** (highest existing is F18), house format
`### F<N> — <sentence-case title>` / `**Status:** Open · <context>` / `**Evidence.**` /
`**Why it matters.**` / `**Next step.**`, filed under the right `##` category. Cross-link F18
(`step_behavioral` vs `step_theory` deltas), F1 (new "no product signal recommends donut" entry), and
quote the offending cell for every V2 band miss.

## Verification

1. **Unit** — null-sink render byte-identical; truth positions/HFR vs `DefocusModel`; golden-policy
   edge cases; FITS cards/blocks; step fixed-point.
2. **Runner self-verify** — `FITS.Load` round-trip asserts XPIXSZ/FOCALLEN/XBINNING/EXPTIME land
   (closes R1).
3. **S0 control** — the harness self-test; catches a wrong exposure band before it can masquerade as a
   product failure.
4. **CLI parity** — in-process round-0 == real `optimize --per-run` on the same folder.
5. **Anchor** — `cwhite_2026` exact under `--pixel-scale profile`.
6. **Determinism** — seed-regenerated datasets bit-identical (SHA-256 + metrics).
7. **Full suite** via Windows `dotnet.exe` at milestones + final gate.
