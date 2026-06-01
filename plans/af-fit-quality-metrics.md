# Auto-Focus Fit-Quality Metrics: se(x_min), Reduced χ², Selectable Rejection, LOO

> Branch off `ghilios/asymmetric-focus-curve-fitting` (PR #45), not `develop` — this work depends on
> `AlglibHyperbolicFitting.MinimumStdError`, the template-method base, and the `HyperbolicFitModel` enum
> added there. Suggested branch: `ghilios/af-fit-quality-metrics` (stacked PR). Bump version 3.0.0.29 → 3.0.0.30.

## Context

The auto-focus hyperbola fit currently surfaces only R² to the user, and the whole-fit rejection gate
(`AutoFocusEngine.ValidateCalculatedFocusPosition`) rejects a run solely on `R² < FocuserSettings.RSquaredThreshold`.
R² is the weakest measure for a focus curve (it is inflated by the defocused wings and is non-discriminating),
while the quantity that actually matters — the uncertainty of the best-focus position — is already computed
(`MinimumStdError`) but never shown. This change surfaces the more meaningful metrics and lets the user pick a
better rejection criterion:

1. Display **se(x_min)** (best-focus position standard error, already computed as `MinimumStdError`).
2. Compute and display **reduced χ²** for the hyperbolic fit.
3. Let the user **choose the rejection criterion: R² (default, unchanged) or reduced χ²**.
4. Add **leave-one-out (LOO) best-focus stability** as a displayed diagnostic.

The sensor-aberration model already implements the reduced-χ² / configurable-threshold / dual-metric-display
pattern; we mirror it.

## 1. Reduced χ² on the hyperbolic fit (compute)

In `StarDetection/AlglibHyperbolicFitting.cs`:
- Add properties `public double ChiSquared`, `public int DegreesOfFreedom`, `public double ReducedChiSquared` (default NaN).
- Compute in `Solve()` for **all** models (independent of `SupportsCovariance`, so the legacy model gets it too),
  reusing the same weighted-residual sum already formed in `ComputeMinimumStdError`:
  `χ² = Σ (Weights[i]·(ModelValue(solution,xᵢ) − Outputs[i]))²`; `dof = max(1, n − ParameterCount)`;
  `ReducedChiSquared = χ²/dof`. Mirror the formula in `Utility/NonLinearLeastSquaresSolver.cs`
  (`ChiSquared`/`DegreesOfFreedom`/`ReducedChiSquared`, lines ~398–423).
- **Caveat to document in the tooltip:** reduced χ² is a true χ² only when `WeightedHyperbolicFitEnabled` (Weights = 1/σ
  from `ErrorY`); unweighted it degenerates to RSS/dof (scale-dependent). The χ² rejection option is meaningful only
  with weighted fits — note this in the option tooltip.

## 2. Leave-one-out best-focus stability (compute, diagnostic only)

- Add a static helper `AlglibHyperbolicFitting.ComputeLeaveOneOutBestFocusStdError(IAlglibAPI, HyperbolicFitModel, IList<ScatterErrorPoint>, int stepSize, bool useWeights)` that mirrors the benchmark's `LeaveOneOutStd`
  (`Tests/StarDetection/FocusCurveBenchmark.cs`): refit dropping each point via the existing
  `AlglibHyperbolicFitting.Create(...)`, collect `Minimum.X`, return the sample std (NaN if < 2 valid refits or < ~5 points).
- Add a settable `public double LeaveOneOutStdError { get; set; } = double.NaN;` on `AlglibHyperbolicFitting`.
- In `AutoFocus/AutoFocusEngine.cs`, compute it **once at run completion** for the final hyperbolic fit per region
  (in/after `CalculateFinalFocusPoint`, guarded to STARHFR + hyperbolic + enough points), and assign it onto that
  fit object so the panel and report can read it. Do NOT compute it on every live `MeasurementPointCompleted`.
- It is **not** a rejection gate (see Recommendation below).

## 3. Selectable rejection criterion (R² or reduced χ²)

Mirror the `HyperbolicFitModel` plumbing exactly.
- New enum `Interfaces/FitRejectionCriterion.cs`: `[Description("R²")] RSquared`, `[Description("Reduced χ²")] ReducedChiSquared`
  (with `[TypeConverter(typeof(EnumStaticDescriptionConverter))]`).
- `AutoFocus/AutoFocusOptions.cs` + `Interfaces/IAutoFocusOptions.cs`: add persisted
  `FitRejectionCriterion FitRejectionCriterion` (default `RSquared`, `GetValueEnum`/`SetValueEnum`) and
  `double ReducedChiSquaredRejectionThreshold` (default **5.0**, `GetValueDouble`, validate `> 0`); reset in `ResetDefaults`.
- Thread both onto the engine struct: `Interfaces/IAutoFocusEngine.cs` (`AutoFocusEngineOptions`) + copy in
  `AutoFocusEngine.GetOptions` (the block that already copies `HyperbolicFitModel`, ~line 1593).
- Extract a small testable decision helper (parallels `SensorAberrationCalculator.IsModelAcceptable`):
  `static bool IsHyperbolicFitAcceptable(FitRejectionCriterion criterion, double rSquared, double reducedChiSquared, double rSquaredThreshold, double reducedChiSquaredThreshold)`
  — RSquared: reject when `rSquaredThreshold > 0 && rSquared < rSquaredThreshold` (today's rule); ReducedChiSquared:
  reject when `threshold > 0 && finite(reducedChiSquared) && reducedChiSquared > threshold` (NaN/Inf never rejects).
- In `AutoFocusEngine.ValidateCalculatedFocusPosition` (lines 1083–1117): for the **hyperbolic** branch
  (`HYPERBOLIC`/`TRENDHYPERBOLIC`), replace the inline `hyperbolicBad` R² test with the helper using
  `autoFocusState.Options.FitRejectionCriterion`, casting `fittings.HyperbolicFitting as AlglibHyperbolicFitting`
  for `ReducedChiSquared`. **Quadratic/trendline keep the R² test** (no per-point σ). Default criterion = RSquared
  ⇒ zero behavior change. Update the failure log/notification to name the criterion and show the failing value.

## 4. Display (AF panel + Options UI + saved report)

**Dockable panel** — `AutoFocus/DataTemplates.xaml`, immediately after the Hyperbolic R² `UniformGrid` (line 435):
add three `UniformGrid Columns="2"` rows copying that block's structure and its `HF_AutoFocusFittingToVisibilityConverter`
MultiBinding (keyed on `"HyperbolicFitting"`):
- "Hyperbolic se(focus)" → `{Binding HyperbolicFitting.MinimumStdError, StringFormat=± {0:0.#} steps}`
- "Hyperbolic Reduced χ²" → `{Binding HyperbolicFitting.ReducedChiSquared, StringFormat={0:0.00}}`
- "Best-focus stability (LOO)" → `{Binding HyperbolicFitting.LeaveOneOutStdError, StringFormat=± {0:0.#} steps}`
WPF binds against the runtime type, so `MinimumStdError`/`ReducedChiSquared`/`LeaveOneOutStdError` resolve even though
`HocusFocusVM.HyperbolicFitting` is statically the NINA `HyperbolicFitting` base. Use the existing
`HF_ZeroToDoubleDashConverter`/NaN handling so empty values render as "—".

**Options UI** — `Resources/OptionsDataTemplates.xaml`, next to the existing "Hyperbolic Fit Model" dropdown:
a `ComboBox` bound to `AutoFocusOptions.FitRejectionCriterion` (copy the `EnumBindingSource` +
`HF_EnumStaticDescriptionValueConverter` pattern) and a `ninactrl:UnitTextBox` for
`AutoFocusOptions.ReducedChiSquaredRejectionThreshold` with a `DoubleRangeRule` (min 0). Add `*_Tooltip` TextBlocks,
reusing the sensor-model reduced-χ² wording (the σ-is-approximate caveat).

**Saved report** — `AutoFocus/HocusFocusReport.cs`: add HocusFocus-specific fields populated from the hyperbolic fit
alongside the existing `RSquares.Hyperbolic` (line ~100): `HyperbolicMinimumStdError`, `HyperbolicReducedChiSquared`,
`HyperbolicLeaveOneOutStdError` (from `hyperbolicFitting as AlglibHyperbolicFitting`, NaN-safe).

## Recommendation: LOO error (task 4 answer)

- **Yes, it is displayable**: LOO best-focus *stability* = std of the predicted `Minimum.X` across the N
  drop-one refits, in focuser steps — the same unit as se(x_min) — shown as "± N steps".
- **How it should be used:**
  - **As a diagnostic, displayed next to se(x_min)** — that is what this plan implements. se(x_min) is the
    parametric covariance estimate (assumes the model is correct and errors Gaussian); LOO std is a
    non-parametric robustness estimate (how much the answer moves if any single point is dropped). When the two
    diverge, it flags model misspecification or a single high-leverage point.
  - **Not as a hard rejection gate** by default — it is a stability indicator, not a pass/fail. (A soft warning
    when LOO std ≫ step size could be a later addition.)
  - **For model selection** (4-param symmetric vs 5-param asymmetric), the principled use is LOO *predictive
    error* (sum of squared drop-one residuals) or AICc, which penalize overfitting — but that is an offline
    comparison (already available via `FocusCurveBenchmark`), not a per-run live feature. Keep it offline.

## Tests (NUnit)

- `AlglibHyperbolicFittingTests`: reduced χ² ≈ 1 when per-point σ matches the injected noise; equals RSS/dof when
  unweighted; `DegreesOfFreedom = n − p`; `ReducedChiSquared` is NaN-safe with too few points.
- LOO helper: finite/positive on noisy data, small on near-noiseless data, NaN under ~5 points (deterministic seeds).
- `IsHyperbolicFitAcceptable`: R² mode reproduces today's behavior; χ² mode rejects high reduced χ² and accepts low;
  NaN/Inf reduced χ² never rejects; thresholds ≤ 0 disable the gate.
- `AutoFocusOptionsTests`: defaults (`FitRejectionCriterion == RSquared`, threshold 5.0), persistence, reset,
  PropertyChanged, and the threshold validation rule.

## Files

**Create:** `Interfaces/FitRejectionCriterion.cs`; test files above.
**Modify:** `StarDetection/AlglibHyperbolicFitting.cs` (reduced χ², LOO helper, LeaveOneOutStdError),
`AutoFocus/AutoFocusEngine.cs` (criterion-aware rejection + LOO at completion + GetOptions copy),
`Interfaces/IAutoFocusEngine.cs`, `AutoFocus/AutoFocusOptions.cs`, `Interfaces/IAutoFocusOptions.cs`,
`AutoFocus/DataTemplates.xaml` (panel rows), `Resources/OptionsDataTemplates.xaml` (options + tooltips),
`AutoFocus/HocusFocusReport.cs`, `Properties/AssemblyInfo.cs` (→ 3.0.0.30), and the test doubles that implement
`IAutoFocusOptions` (`Tests/Inspection/FakeSensorModelOptions.cs`).

## Verification

- `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` (Windows `dotnet.exe`) — all green.
- Manual: load a saved AF run; confirm the panel shows se(focus), reduced χ², and LOO stability next to R²; switch
  the rejection criterion to Reduced χ² and confirm a deliberately poor fit is rejected with a χ²-worded message,
  while R² mode is unchanged. Confirm the three new fields appear in the saved report JSON.
- Sanity: with a clean symmetric curve, se(x_min) and LOO std are both small and comparable; inject one outlier and
  confirm LOO std grows (robustness divergence visible).
