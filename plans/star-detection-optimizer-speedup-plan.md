# Star Detection Optimizer — Wall-Clock Reduction (executed)

**Status: EXECUTED.** Full empirical findings + data tables: `docs/star-detection-optimizer-speedup-results.md`.
This file records what was planned and what each lever turned out to be when measured against the whole
`D:\Autofocus Bank` (48-core box, via `TestApp optimize`). Branch: `ghilios/optimizer-speedup-heuristics`.

## Context

A prior round (`docs/star-detection-optimizer-performance-design.md`, "T14") already shipped the large wins
(early/late split + per-(frame,early-key) cache + bounded per-frame parallelism, ~10–13×, bit-identical), leaving
per-eval cost near-optimal. This round tested the remaining proposed levers (parallel candidate evaluation,
heuristic field conversion to shrink the search) and the result-changing eval-budget headroom, **validating each
case-by-case before any bank-wide conclusion**, per the brief.

## Outcomes per lever

| Lever | Plan | Outcome | Shipped? |
|---|---|---|---|
| **Eval-budget reduction** | Investigate; cut if validated | **The real lever.** Search self-terminates at convergence; capping at 250 costs ≤0.011% J bank-wide. Default `MaxEvaluations` 400 → **250**. | **Yes** |
| **Wizard "start from current settings" toggle** | (user request) | CheckBox on the start page (off by default); seeds the search from current settings (Baseline) instead of defaults, to refine an already-tuned setup | **Yes** |
| **Trajectory instrumentation (0c)** | `optimize_trajectory.csv` (bestJ vs eval#) | Backs the eval-budget analysis | **Yes** |
| **Parallel candidate evaluation (WI1)** | Bit-identical concurrent LATE/grid evals | Bit-identical (unit-proven) but **no speedup** — 0–130% slower; cores already saturated by frames×per-star, sweeps have ≤16 candidates | **No** (reverted) |
| **Heuristic field conversion (WI2)** | Derive StructureLayers + MinBBox from pixel scale, drop from search | StructureLayers **collapses J on defocus runs** (Panos 0.985→0.62); MinBBox J-neutral, no wall-clock gain | **No** (reverted; findings documented) |
| **Pure preset extraction (0a)** | Scaffolding for the (reverted) random-preset seeding | Behavior-preserving refactor, but orphaned once WI2 was dropped | **No** (reverted) |
| **Early-stage internal parallelism (I1)** | Investigation | Not done; would speed EARLY builds bit-identically but its value shrinks once the budget is lowered | Future |

## Key numbers (see results doc for the full tables)

- Bank convergence at max-evals 400: worst-case J shortfall if budget capped at 250 = 0.011%, at 200 = 0.34%,
  at 120 = 0.51% (Panos binding). Full-bank before/after (400 vs 250) in the results doc.
- WI1 muggsie A/B: bit-identical, 12.2 s (seq) vs 12.4–28.4 s (parallel, every config).
- WI2 muggsie helped (J↑, 30% faster) but Panos collapsed (J 0.985→0.62) — not generalizable.

## Files

- Shipped: `StarDetection/Optimization/StarDetectionOptimizer.cs` (`MaxEvaluations` 250),
  `StarDetectionOptimizerWizardVM.cs` + `Optimization/DataTemplates.xaml` (the toggle),
  `TestApp/OptimizationDiagnosticRunner.cs` (`optimize_trajectory.csv`), wizard toggle tests.
- Reverted: WI1 (`StarDetectionOptimizer` candidate parallelism), WI2 (`HeuristicSeedDerivation`,
  `CreateCuratedSet(opts)`, `--heuristic-seed`), 0a (`DerivePreset`/`DerivedPresetValues`).

## Verification

`dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` — 1292 pass. Bank runs reproduced
deterministically (Panos re-run reproduced bestJ=0.990235, evals=339 exactly).
