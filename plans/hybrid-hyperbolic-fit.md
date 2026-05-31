# Hybrid "Best-Fit" Hyperbolic Auto-Focus Model

## Context

The HocusFocus auto-focus engine fits one user-selected hyperbolic model (Symmetric, Uneven Blend Legacy, Tilted, Smooth Blend) to the HFR/focus curve. Empirically, across ~1000 real saved runs, no single model is best on every curve: the asymmetric models fit tighter (lower RMS/χ²) but Symmetric is often the most *stable* (lowest leave-one-out scatter), and Tilted is the best all-round asymmetric model. The user wants a **Hybrid** model that gives the best of both: render the curve live with Tilted during the sweep, then at run completion refit *all* hyperbolic models on the final point set and keep the one with the **least expected error for the best-focus position**. This makes the engine self-select the most trustworthy fit per run instead of forcing one global choice.

**Decisions (from the user):**
- **Selection metric:** σ(focus) = `MinimumStdError` (parametric standard error of the best-focus position) as primary; **leave-one-out (LOO) std as fallback** when σ(focus) is NaN.
- **Candidates:** **all four** concrete models compete. Uneven Blend (Legacy) has no σ(focus), so it can only win via the LOO fallback tier.
- **Default:** Hybrid becomes the **default** model for new profiles (existing profiles keep their saved setting).
- **Visibility:** show the actually-chosen model in the AF metrics panel (Hybrid runs only) **and** record it in the saved report JSON.

## Design summary

"Hybrid" is a *meta-model*, not a new fitting class. Live, it behaves exactly like `TiltedHyperbola`. At finalization a new selection step refits the candidate models on the final points, scores each, swaps the region's `HyperbolicFitting` to the winner, and records the chosen concrete model so everything downstream (final focus point, LOO, report, UI) uses the real model — never "Hybrid". Centralizing the scoring in a static, testable helper keeps it reusable and unit-testable without the full engine.

## Changes

### 1. Enum + factory
- `Interfaces/HyperbolicFitModel.cs`: append `[Description("Hybrid (Best Fit)")] Hybrid` **last** (preserves existing serialized ordinals).
- `StarDetection/AlglibHyperbolicFitting.cs` (`Create` switch, ~L88): add `case HyperbolicFitModel.Hybrid:` returning the **Tilted** fit (live rendering / defensive fallback for any caller that passes `Hybrid` directly). No recursion — `Create` never calls itself.

### 2. Centralized, testable selection helper
Add a static method to `AlglibHyperbolicFitting`:
```
public static HyperbolicFitModel SelectBestModel(
    IAlglibAPI api, IList<ScatterErrorPoint> points, int stepSize, bool useWeights,
    out AlglibHyperbolicFitting bestFit)
```
Algorithm (candidates = `{ Symmetric, UnevenBlendLegacy, TiltedHyperbola, SmoothBlend }`):
1. `Create(candidate).Solve()` each; drop fits that fail to solve or whose `Minimum.X` is non-finite / outside `[minX, maxX]` of `points`.
2. **Tier 1 (finite σ(focus)):** rank survivors with finite `MinimumStdError` ascending; smallest wins.
3. **Tier 2 (LOO fallback):** if no finite-σ candidate exists, score remaining survivors by `ComputeLeaveOneOutBestFocusStdError(candidate, …)` ascending (this is where Uneven Legacy can win). Computed lazily only when Tier 1 is empty — keeps the common path cheap (σ(focus) is already produced by `Solve()`).
4. **Final tiebreak:** `ReducedChiSquared` asc, then `RSquared` desc.
5. If nothing survives (all degenerate/out-of-range), return `TiltedHyperbola` with the already-solved Tilted fit so the live fit is preserved.

Reuses existing members only: `MinimumStdError`, `LeaveOneOutStdError`, `ReducedChiSquared`, `RSquared`, `Minimum`, and `ComputeLeaveOneOutBestFocusStdError`.

### 3. Record the chosen model on the fit container
- `Interfaces/IAutoFocusEngine.cs` — `AutoFocusFitting`: add `HyperbolicFitModel? SelectedHyperbolicFitModel { get; set; } = null;`; copy it in `Clone()` and null it in `Reset()`. Flows to VM/report automatically via `OnCompleted` (`Fittings = s.Fittings`).

### 4. Engine wiring (`AutoFocus/AutoFocusEngine.cs`)
- `AutoFocusRegionState`: add `private HyperbolicFitModel? selectedHyperbolicModel;` and method `SelectBestHyperbolicModel()`:
  - Guard: only when method = STARHFR, fitting ∈ {HYPERBOLIC, TRENDHYPERBOLIC}, `State.Options.HyperbolicFitModel == Hybrid`, and `lastValidFocusPoints` has usable points.
  - Call `AlglibHyperbolicFitting.SelectBestModel(...)` on `lastValidFocusPoints.Where(Y>0)` **outside** the lock (CPU-heavy); then under `lock (SubMeasurementsLock)` set `Fittings.HyperbolicFitting = bestFit`, `Fittings.SelectedHyperbolicFitModel = best`, and `selectedHyperbolicModel = best` (mirrors `CalculateCurveFittings` lock discipline).
- Insert the call in the finalization block (~L1182) **before** `CalculateFinalFocusPoint()`:
  ```
  autoFocusRegionState.SelectBestHyperbolicModel();   // NEW
  autoFocusRegionState.CalculateFinalFocusPoint();
  autoFocusRegionState.ComputeLeaveOneOutStability();
  ```
- `ComputeLeaveOneOutStability` (~L251): use the resolved model so LOO matches the chosen curve and never recurses through Hybrid:
  `var modelForLoo = selectedHyperbolicModel ?? State.Options.HyperbolicFitModel;` (null for non-Hybrid runs → existing behavior preserved).

### 5. Report (`AutoFocus/HocusFocusReport.cs`)
- Add `[JsonProperty] public HyperbolicFitModel? HyperbolicFitModelChosen { get; set; } = null;`
- In `GenerateReport`, set it from `fittings.SelectedHyperbolicFitModel` (null when the option isn't Hybrid → no change to existing reports). Distinct from `HocusFocusAutoFocusOptions.HyperbolicFitModel`, which still records the *option* ("Hybrid").

### 6. Default = Hybrid (`AutoFocus/AutoFocusOptions.cs`)
- Change the new-profile default: `GetValueEnum(nameof(HyperbolicFitModel), HyperbolicFitModel.Hybrid)` (was `UnevenBlendLegacy`) and `ResetDefaults()` → `Hybrid`.
- Keep the legacy boolean→enum migration (false→Symmetric, true→UnevenBlendLegacy) unchanged: upgrading users keep their effective behavior; only brand-new profiles get Hybrid.
- Update the two affected option tests in `Tests/AutoFocus/AutoFocusOptionsTests.cs` (`…DefaultsToUnevenBlendLegacy` → Hybrid; `…ResetRestoresUnevenBlendLegacy` → Hybrid). Migration tests stay as-is.

### 7. UI
- `Resources/OptionsDataTemplates.xaml`: the model dropdown auto-discovers `Hybrid` via `EnumBindingSource` + `EnumStaticDescriptionConverter` (no binding change). Update the `HyperbolicFitModel_Tooltip` text (~L416) to describe Hybrid as the recommended/default best-fit option.
- AF panel `AutoFocus/DataTemplates.xaml` (the σ(focus)/χ²/LOO block, ~L491-536): add a "Hyperbolic model" row, shown only for Hybrid runs. Surface the value as a new `HocusFocusVM.SelectedHyperbolicFitModel` property set in `AutoFocusEngine_CompletedNoReport` from `firstRegion.Fittings.SelectedHyperbolicFitModel`; bind text via `HF_EnumStaticDescriptionValueConverter` and gate row visibility with a null→collapsed converter (reuse an existing converter; add one only if none fits).

### 7b. SensorModel + saved-run replay also honor Hybrid (added during execution)
The aberration inspector and the saved-run display both refit hyperbolic curves directly with the option model, so they must resolve Hybrid the same way:
- `Inspection/SensorModel.cs` `FitImages()` — when the option is `Hybrid`, resolve the **best concrete model per star** via `AlglibHyperbolicFitting.SelectBestModel(...)` on that star's points *before* the outlier-rejection loop, then fit with the resolved model. Each star self-selects its most trustworthy model; non-Hybrid options pass straight through. Rejection loop / χ² gate / paraboloid feed unchanged.
- `AutoFocus/HocusFocusVM.cs` `SetCurveFittings()` — when the option is `Hybrid`, use `SelectBestModel(...)` to pick the model (and reuse its already-solved fit) so a reloaded Hybrid run shows the concrete chosen model and computes LOO against it instead of falling back to Tilted.

### 8. Offline fit-quality tool (`TestApp/FitQualityRunner.cs`) — minor
- Read the new `HyperbolicFitModelChosen` field; when present, show it in the `savedModel` column (so Hybrid runs display the concrete pick, not just "Hybrid"). Do **not** add `Hybrid` to `AllModels` (the runner already evaluates every concrete model per curve). Optionally add an informational `hybridPick` column by reusing `SelectBestModel`.

## Files
- `Interfaces/HyperbolicFitModel.cs` — enum value
- `StarDetection/AlglibHyperbolicFitting.cs` — `Create` case + `SelectBestModel` helper
- `Interfaces/IAutoFocusEngine.cs` — `AutoFocusFitting.SelectedHyperbolicFitModel`
- `AutoFocus/AutoFocusEngine.cs` — `SelectBestHyperbolicModel`, call site, LOO model
- `AutoFocus/HocusFocusReport.cs` — `HyperbolicFitModelChosen`
- `AutoFocus/AutoFocusOptions.cs` — default = Hybrid
- `AutoFocus/HocusFocusVM.cs` + `AutoFocus/DataTemplates.xaml` — panel display + saved-run replay Hybrid
- `Resources/OptionsDataTemplates.xaml` — tooltip + converter registration
- `Converters/NullToVisibilityCollapsedConverter.cs` — new null→collapsed converter for the panel row
- `Inspection/SensorModel.cs` — per-star Hybrid best-fit selection
- `TestApp/FitQualityRunner.cs` — read chosen-model field
- Tests: new `Tests/StarDetection/HybridModelSelectionTests.cs`; update `Tests/AutoFocus/AutoFocusOptionsTests.cs`

## Test strategy (test-first)
New `HybridModelSelectionTests` (reuse `AsymmetricFitRobustnessTests` fixtures + `SyntheticFocusCurveSamples`):
1. **Picks lowest expected error:** on a synthetic curve where one model has clearly smaller σ(focus), `SelectBestModel` returns that model and `bestFit` is its concrete type.
2. **LOO fallback tier:** when all σ(focus)-capable candidates are degenerate (σ NaN) — e.g. `MonotonicRamp_NoVertexInRange` — selection falls back to LOO, returns a concrete model, never throws, and the minimum is finite/in-range (regression vs the 1e14 blowup).
3. **Records chosen model:** a Hybrid run's report has `HyperbolicFitModelChosen == <winner>` while `HocusFocusAutoFocusOptions.HyperbolicFitModel == Hybrid`.
4. **LOO uses chosen model:** finalized `HyperbolicFitting.LeaveOneOutStdError` equals `ComputeLeaveOneOutBestFocusStdError(chosenModel, …)` and is finite/bounded.
5. **< 5 points:** selection works on σ(focus)/χ² alone (LOO NaN), no exception, chosen model non-null.
6. **Non-Hybrid unchanged:** option = Tilted → `SelectBestHyperbolicModel` no-ops, `SelectedHyperbolicFitModel` null, LOO still uses the option model.
7. **Default:** new profile → `AutoFocusOptions.HyperbolicFitModel == Hybrid`; reset → Hybrid.

Follow the repo TDD loop: write each test, watch it fail, implement, green. Run the full suite after each step: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`.

## Verification
- **Build:** `cmd.exe /c "dotnet build Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo"` — clean.
- **Unit suite:** full `dotnet test` green, including the new Hybrid tests.
- **Offline end-to-end:** rebuild TestApp; the existing `fit-quality` tool already scores every model per curve, so confirm that, per curve, the Hybrid winner equals the concrete model with the lowest σ(focus) (or LOO when σ is NaN) on the shared UC108/CS200 sets — i.e. the engine's pick matches the offline ranking. Confirm `HyperbolicFitModelChosen` round-trips by generating a report (or hand-checking serialization).
- **Manual (NINA, optional, can't be driven headless here):** run an AF with the model set to Hybrid; confirm the curve renders live (Tilted), the panel shows the chosen model after completion, the saved report JSON contains `HyperbolicFitModelChosen`, and a saved-run re-run reproduces the same pick.
