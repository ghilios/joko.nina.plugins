# Star Detection Optimization Wizard — Implementation Plan

**Design / methodology:** `docs/star-detection-optimization-wizard-design.md` (read first).
**Branch:** `ghilios/star-detection-optimization-wizard` (never push `develop`; PR to merge).
**Commit identity:** `George Hilios <322725+ghilios@users.noreply.github.com>` (author +
committer), per `CLAUDE.md`.

This plan is decomposed into independently-reviewable tasks for subagent-driven execution.
Each task lists its files, the existing code to reuse, and its own verification gate. Build
after every task; run `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
and fix root causes before moving on.

## Architecture

### New files (plugin, under `Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/`)

| File | Responsibility |
|---|---|
| `OptimizerVariable.cs` | Descriptor per tunable knob: name, get/set on `StarDetectorParams`, type (continuous/int/bool), bounds, initial step. |
| `OptimizationObjective.cs` | Composite `J_run` (S_focus/S_stars/S_fit + optional label recall/precision) from a fit + metrics + labels; tunable-constants block; multi-run `J_total`. |
| `OptimizedStarDetectionSettings.cs` | Serializable DTO snapshot (curated values) + metadata (date, #runs, baseline/final J, recommended step size/offset). |
| `RunEvaluationData.cs` | Loaded-once per-run frames (focuser pos + image) + the matching detection delegate. Wizard: `IRenderedImage`; harness: float `Mat`. |
| `StarDetectionOptimizer.cs` | Search engine: coarse seed + compass/pattern search, caching, multi-run aggregation. Depends only on a **detection delegate** `Func<frame, StarDetectorParams, CancellationToken, Task<HocusFocusStarDetectionResult>>`, `IAlglibAPI`, AF fit options. No WPF/mediator deps. |
| `StarDetectionOptimizerWizardVM.cs` | Wizard VM (`BaseINPC`, not `DockableVM`), enum step state, async commands, progress, cancel, summary, Apply. Dual MEF/test constructors. |
| `DataTemplates.xaml` (+ `.xaml.cs`) | Window content template, key `NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.StarDetectionOptimizerWizardVM`; data-source / acquire / optimize / summary panels. |

### Modified files (plugin)

| File | Change |
|---|---|
| `Interfaces/IStarDetectionOptions.cs` | Add `bool HasOptimizedSettings { get; }`, `bool UseOptimizedSettings { get; set; }`, `OptimizedStarDetectionSettings GetOptimizedSettings()`, `void ApplyOptimizedSettings(OptimizedStarDetectionSettings)`. |
| `StarDetection/StarDetectionOptions.cs` | Persist snapshot as JSON string + `UseOptimizedSettings` bool (`HasOptimizedSettings` derived); load in `InitializeOptions`, clear in `ResetDefaults`; add `UseOptimizedSettings` to `SimplePropertyNames`; `ConfigureSimpleSettings()` optimized branch (before preset derivation); `ApplyOptimizedSettings()` per design. |
| `StarDetection/HocusFocusStarDetection.cs` | **No change** to `BuildStarDetectorParams` (reads live properties). |
| `Resources/OptionsDataTemplates.xaml` | Top-of-page "Optimize Star Detection…" button; Simple-Mode "Use Optimized Settings" checkbox (visible only when `HasOptimizedSettings`); collapse the 3 preset rows when `UseOptimizedSettings`; tooltips. |
| `HocusFocusPlugin.cs` | `OptimizeStarDetectionCommand` (`RelayCommand`) builds the wizard VM and shows it via `IWindowServiceFactory`. |

### New files (TestApp)

| File | Responsibility |
|---|---|
| `TestApp/OptimizationDiagnosticRunner.cs` | Headless `optimize` subcommand (mirrors `ContaminationDiagnosticRunner.cs`); harness detection delegate via `StarDetector.Detect(Mat)`; reports + annotated PNGs. |
| `TestApp/StarReview/StarReviewRunner.cs` | `review` arg parse, load runs, build review queue, persist labels, optional re-optimize. |
| `TestApp/StarReview/StarReviewWindow.xaml(.cs)`, `StarReviewVM.cs`, `StarReviewLabels.cs` | Interactive two-pass click UI + label model. |
| `TestApp/Program.cs` (modify) | Add `optimize` and `review` dispatch branches. |

### Reuse (do not reinvent)

- `AlglibHyperbolicFitting.SelectBestModel` / `ComputeLeaveOneOutBestFocusStdError`.
- `HocusFocusStarDetection.BuildStarDetectorParams` / `GetStarDetectorParams` / `ToHocusFocusParams`; `IHocusFocusStarDetection.Detect`.
- `IAutoFocusEngine.LoadSavedAutoFocusAttempt` / `GetOptions(savedAttempt)` / `Run`; `AutoFocusEngineFactory`.
- Wizard image load like `InspectorVM.LoadSavedFile` (`imageDataFactory.CreateFromFile` + `imagingMediator.PrepareImage`).
- `ScatterErrorPoint`, `MeasureAndError`, `SafeDisplayError`.
- TestApp: `DiagnosticUtil.LoadFloatMat`, `ProfileService`, `StarDetector.Detect(Mat,…)`, `CvImageUtility` MTF stretch + `BuildStretchedBgr` pattern, `StarDetector.ComputeCacheKey`.
- `ProgressFactory.Create`, `AsyncRelayCommand`, `CancellationTokenSource`, `PluginOptionsAccessor`.

---

## Task breakdown (ordered; note dependencies)

**T1 — Options model & toggle** (no deps). `OptimizedStarDetectionSettings.cs`; modify
`IStarDetectionOptions.cs` + `StarDetectionOptions.cs` per design (snapshot persistence,
`ConfigureSimpleSettings` optimized branch, `ApplyOptimizedSettings`, `SimplePropertyNames`,
`ResetDefaults`). **Verify (tests):** toggle off → preset-derived live values; on → snapshot
values (and `BuildStarDetectorParams` reflects them); JSON round-trip; `HasOptimizedSettings`
false→true; `ResetDefaults` clears; on→off restores presets.

**T2 — Optimizer core** (no deps; pure). `OptimizerVariable.cs`, `OptimizationObjective.cs`,
`StarDetectionOptimizer.cs` with the detection-delegate abstraction. **Verify (tests):**
objective monotonicity + hard-fails; pattern search reaches a synthetic optimum within budget,
never worse than seed, deterministic; multi-run β behavior; label recall/precision terms shift
θ and keep weights summing to 1.

**T3 — Run evaluation + fit + step size** (deps T2). `RunEvaluationData.cs`; wizard-side
loader (saved attempt → frames); per-frame detect → pool → `ScatterErrorPoint` → `SelectBestModel`
→ σ_focus/R²/reducedχ²; step-size/offset recommendation. **Verify:** synthetic fit of known
width → ~3–4 points/side; a real saved run loads and scores end-to-end (exercised by T6).

**T4 — Wizard VM + window** (deps T1–T3). `StarDetectionOptimizerWizardVM.cs`,
`Optimization/DataTemplates.xaml(.cs)`; step flow (data source / acquire / optimize / summary),
STARHFR guard, live progress + cancel, summary table, Apply → `ApplyOptimizedSettings` +
profile step-size write on confirm. **Verify:** unit-test the VM step transitions / summary
model; manual window smoke later.

**T5 — Options UI + launch** (deps T1, T4). `Resources/OptionsDataTemplates.xaml` button +
toggle + preset hiding + tooltips; `HocusFocusPlugin.cs` `OptimizeStarDetectionCommand` via
`IWindowServiceFactory`. **Verify:** builds; manual smoke in NINA later.

**T6 — TestApp headless `optimize`** (deps T2, T3). `OptimizationDiagnosticRunner.cs` +
`Program.cs` branch; harness delegate via `StarDetector.Detect(Mat)`; summary/CSV + stretched
annotated PNGs. **Verify:** run on a real folder of AF runs; `n_min ≥ N_hard`; inspect PNGs.

**T7 — TestApp interactive `review`** (deps T6). `StarReview/*` + `Program.cs` branch; two-pass
clicking, selective queue, JSON labels; `optimize --labels` consumes them. **Verify:** label a
frame, confirm JSON persists and re-`optimize --labels` activates the recall/precision term.

**T8 — Docs, CLAUDE.md, PR** (deps all). Ensure `docs/` design is current; document `optimize`
+ `review` in `CLAUDE.md` by the contamination-runner section; final full build + tests; open
PR to `develop`.

---

## Global verification

1. `cmd.exe /c "dotnet build Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo"` and
   `... TestApp\TestApp.csproj ...` — both clean.
2. `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` — all green.
3. **Harness** `TestApp optimize --runs <folder>` on the user-supplied AF runs: completes,
   valid stars on every frame incl. defocused extremes; review annotated PNGs for misses.
4. **Interactive** `TestApp review --runs <folder>`: low-count/uncertain frames queued; pass 1
   (missed) + pass 2 (should-reject); labels persist; re-`optimize --labels` recovers flagged
   misses (or makes the gap explicit).
5. NINA: Star Detection options shows the button; toggle absent until a run. Wizard (separate
   window) replays N=1 → summary (changed params, before/after σ_focus, recommended step).
   Apply → Simple Mode + toggle on + presets hidden; Advanced Mode shows optimized values;
   snapshot persists across reload/profile switch; step size/offset written. Toggle off/on
   reverts/reapplies. N=2 → single balanced setting (cross-check harness combined result).

> The user will supply a path to a folder of several autofocus runs for steps 3–4.
