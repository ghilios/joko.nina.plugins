# Offline Fit-Quality Harness over a Directory of Saved AF JSONs

## Context

We just added fit-quality metrics (R², reduced χ², σ(focus)=`MinimumStdError`, leave-one-out stability) and four
selectable hyperbolic fit models. To validate and tune these against **real** data, we need a headless tool that
sweeps a whole directory of saved auto-focus report JSONs, re-fits every curve with every model, and reports the
fit-quality metrics — so we can compare models across many real runs at once.

NINA (and HocusFocus) already writes one AF report JSON per run to `%LOCALAPPDATA%\NINA\AutoFocus`
(`Path.Combine(CoreUtil.APPLICATIONTEMPPATH, "AutoFocus")`, see `HocusFocusVM.ReportDirectory`,
`HocusFocusVM.cs:69`). Each report is a `HocusFocusReport`/`AutoFocusReport` with a `MeasurePoints` array of
`{Position, Value, Error}` (the focus curve), plus `Method`, `Fitting`, `Region`, `Timestamp`, `Filter`,
`RSquares`, and the stored `HyperbolicMinimumStdError`/`HyperbolicReducedChiSquared`/`HyperbolicLeaveOneOutStdError`
and `HocusFocusAutoFocusOptions.HyperbolicFitModel` (the model that was actually used). The intended outcome: point
the tool at that directory (default) **or** a zip of report JSONs, and get a per-curve-per-model CSV + an aggregate
summary.

## Approach: a `fit-quality` subcommand in TestApp

TestApp is the established headless diagnostic harness (see the `contamination` subcommand in CLAUDE.md). Add a
sibling subcommand. It needs **no NINA profile and no images** — it operates purely on the JSON `MeasurePoints`,
so it runs anywhere (including on a zip a user sends).

### CLI

```
TestApp fit-quality [--dir <path>] [--zip <path>] [--out <dir>] [--models <csv>] [--no-weights] [--min-points N]
```
- `--dir` — directory of report JSONs, scanned **recursively** (`*.json`). Default:
  `Path.Combine(CoreUtil.APPLICATIONTEMPPATH, "AutoFocus")` (NINA's AF report dir). Also catches saved-run
  `attemptXX/autofocus_report_RegionN.json` layouts via recursion.
- `--zip` — a .zip containing report JSONs; extracted to a temp dir (`System.IO.Compression.ZipFile.ExtractToDirectory`)
  then scanned recursively. Takes precedence over `--dir` when given.
- `--out` — output dir. Default `%LOCALAPPDATA%\NINA\Logs\hf-fitquality\<yyyyMMdd-HHmmss>` (mirror
  `ContaminationDiagnosticRunner` defaulting, `ContaminationDiagnosticRunner.cs:73-77`).
- `--models` — restrict to a subset (default: all four `HyperbolicFitModel` values).
- `--no-weights` — fit unweighted (default weighted, so reduced χ² is meaningful).
- `--min-points` — skip curves with fewer valid points (default 5; LOO needs ≥5 anyway).

## Files

**Create:** `Joko.NINA.Plugins/TestApp/FitQualityRunner.cs` — static `Run(string[] args)` + `RunImpl`, modeled on
`ContaminationDiagnosticRunner` (arg parsing via a local `GetArg`, out-dir defaulting, `new Application()` guard if
`Application.Current == null`, `Logger.SetLogLevel(TRACE)`, `double` formatting helper `F()`).

**Modify:** `Joko.NINA.Plugins/TestApp/Program.cs` (`Main`, lines 103-117) — extend dispatch: route
`args[0] == "fit-quality"` to `FitQualityRunner.Run(args)`; keep the existing contamination/`--image` branch.

**Reference / reuse (do not duplicate logic where avoidable):**
- `AlglibHyperbolicFitting.Create(alglibAPI, model, points, stepSize, useWeights)` then `.Solve()`
  (`StarDetection/AlglibHyperbolicFitting.cs`) — the single fit entry point.
- New metrics already on the fit object: `RSquared`, `ReducedChiSquared`, `ChiSquared`, `MinimumStdError`,
  and the static `AlglibHyperbolicFitting.ComputeLeaveOneOutBestFocusStdError(...)`.
- `MeasurePoints`→`ScatterErrorPoint` parsing: the exact pattern in
  `FocusCurveBenchmark.LoadMeasurePoints` (`Tests/StarDetection/FocusCurveBenchmark.cs:264-284`) — JObject parse,
  `Position`/`Value`/`Error`, keep `y > 0`, weight `= 1/Error` semantics via `ScatterErrorPoint(x,y,0,error)`.
  Reimplement this small helper in the runner (TestApp can't reference the Tests project).
- `new AlglibAPI()` for `IAlglibAPI` (`Utility/AlglibAPI.cs`), as `ContaminationDiagnosticRunner` does.

## Implementation notes

- **Per file:** parse with `JObject`. Pull metadata: `Timestamp`, `Filter`, `Method`, `Fitting`, `Region`,
  `RSquares.Hyperbolic`, stored `HyperbolicMinimumStdError`/`HyperbolicReducedChiSquared`/
  `HyperbolicLeaveOneOutStdError`, and `HocusFocusAutoFocusOptions.HyperbolicFitModel` (the saved model; may be
  absent in non-HocusFocus reports → "—"). Build the `ScatterErrorPoint` list from `MeasurePoints`.
- **Step size:** infer per curve as the median spacing of sorted unique `Position`s (the legacy uneven model uses
  it for transition width); fall back to 25 if it can't be inferred. More faithful than the benchmark's hardcoded 25.
- **Per model (default all four):** `Create(...).Solve()`; on success record `bestFocus = Minimum.X`, `RSquared`,
  `ReducedChiSquared`, `MinimumStdError`, `rmsResid` (√(mean residual²) — mirror `FocusCurveBenchmark.Rms`), and
  `LeaveOneOutStdError` via the static helper; on failure record `solveOk=false` and NaNs.

## Outputs (in `--out`)

- `fit_quality_runs.csv` — one row per (file, region, model):
  `file,timestamp,filter,region,savedMethod,savedFitting,savedModel,n,stepSize,model,solveOk,bestFocus,`
  `rSquared,reducedChiSquared,minStdErr,looStd,rmsResid,storedRSquared,storedMinStdErr,storedReducedChiSquared,storedLooStd`
  (the `stored*` columns are the report's own recorded values — repeated per row — so the recompute can be sanity-checked
  against what the plugin wrote for the model it actually used).
- `fit_quality_summary.txt` — counts (files found / parsed / skipped), and **per model** across all curves: solve
  success rate, median & mean of R², reduced χ², σ(focus) (+ how many were finite, since the legacy model yields
  NaN), LOO std, RMS residual; plus "win" counts (model with the lowest LOO and lowest RMS per curve) and a
  per-curve model-disagreement stat (max−min `bestFocus` across models) flagging curves where models diverge.
- Console: progress line per file and a final one-screen digest; verbose TRACE to the NINA log.

## Verification

- Build: `cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"` — clean.
- Default dir (uses real saved runs if present):
  `./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe fit-quality --out "C:\temp\hf-fq"`
  Confirm `fit_quality_runs.csv` has one row per curve×model and `fit_quality_summary.txt` ranks the models.
- Zip path: `TestApp.exe fit-quality --zip "C:\path\to\af-jsons.zip" --out "C:\temp\hf-fq"` — confirm it extracts,
  scans, and produces identical output structure.
- Spot-check: for a report whose `savedModel` is a covariance model (Symmetric/Tilted/Smooth), the recomputed
  `minStdErr` for that model ≈ the report's `storedMinStdErr`; for `UnevenBlendLegacy`, `minStdErr` is NaN while
  `looStd` is finite (matches the in-app behavior we just fixed).
- Sanity on a tiny hand-made zip of 2-3 JSONs to confirm parse/skip counts and CSV columns.
- Full suite still green: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` (no test changes
  expected, but confirm the TestApp addition doesn't break the solution build).
