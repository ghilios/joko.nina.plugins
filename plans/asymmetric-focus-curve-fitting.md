# Auto-Focus Hyperbolic Fit: Asymmetric Models, Robust Outliers, Benchmark

> On execution, copy this plan to the repo `plans/` folder (project convention), e.g.
> `plans/asymmetric-focus-curve-fitting.md`, and do the work on a new branch
> `ghilios/asymmetric-focus-curve-fitting`.

## Context

The auto-focus engine fits HFR-vs-focuser-position curves with a hyperbola to find best
focus. Two problems motivate this work:

1. **The "uneven" (asymmetric) hyperbolic fit** (`HyperbolicUnevenFittingAlglib`) is a
   home-grown model that **blends two hyperbolas** with a hard clamp
   `t = clamp((x0 − x)/StepSize, 0, 1)`. It is only **C⁰ (kinked)** at `x0` and
   `x0 − StepSize`, the blend window is one-sided and tied to an externally supplied
   `StepSize` (not fitted), it has **no analytic Jacobian** (numeric differentiation over a
   kinked function), and it *assumes* the minimum is `(x0, a+y0)` rather than deriving it.
   The user wants a more mathematically sound asymmetric model (still ~5 parameters).
2. **Outlier handling is weak.** `MathUtility.RejectionTest` uses **mean + stddev** of
   residuals (non-robust — suffers masking/swamping), on **unweighted** residuals even when
   the fit is weighted, against an already-contaminated fit, and removes at most one point by
   default. The robust IRLS/Winsorized machinery in `NonLinearLeastSquaresSolver` is not
   reachable from the hyperbolic fitters.

**Decided direction (from user):** implement **both** new asymmetric models (tilted hyperbola
*and* smooth logistic blend) as **new, separately-selectable options** (keep the legacy uneven
fit), **benchmark them empirically** vs the current models on synthetic + real saved AF runs,
and do a **robust + integrated** outlier rework (robust scale, weighted residuals, in-fit Huber
IRLS). Outcome: more accurate/robust best-focus estimates, especially on asymmetric curves, with
data-driven evidence for which asymmetric model to prefer.

Also fix two confirmed bugs found during analysis (see step 1).

## Architecture decision

**Refactor the bespoke `AlglibHyperbolicFitting` into a template-method base** that hosts the
shared alglib boilerplate + Huber-IRLS loop + `JᵀWJ` covariance, with per-model hooks. Do **not**
migrate hyperbolic fitting onto `NonLinearLeastSquaresSolver<S,T,U>`.

Rationale: every consumer (`AutoFocusEngine`, `HocusFocusVM`, `HocusFocusReport`,
`AutoFocusFitting.HyperbolicFitting`) is typed against `AlglibHyperbolicFitting` and reads
`.Minimum`, `.RSquared`, `.MinimumStdError`, `.Fitting`, `.Expression`. The framework returns a
params object + stats via solver methods, so using it would require a wrapper subclass anyway.
The framework's `SolveIRLS` is L1 (`w=1/|r|`), not the Huber `δ=1.5σ` the user wants and that
`PSFModeler` already uses. The four models (symmetric, legacy-uneven, tilted, smooth-blend) share
~100 lines of identical alglib boilerplate today; pulling it into the base removes the
duplication and gives all models analytic-Jacobian + Huber-IRLS + covariance for free, while
preserving the exact public surface.

### New base hooks (`AlglibHyperbolicFitting`)
- `protected abstract int ParameterCount { get; }`
- `protected abstract bool TryComputeInitialState(out double[] guess, lower, upper, scale)`
  (encapsulates the seed math currently duplicated in each `Solve()`).
- `protected abstract double ModelValue(double[] p, double x)`
- `protected abstract void ModelGradient(double[] p, double x, double[] grad)` (unweighted row)
- `protected abstract DataPoint ComputeMinimum(double[] p)`
- `protected abstract string FormatExpression(double[] p)`
- `protected virtual bool UseJacobian => true;`
- `public bool HuberIrlsEnabled { get; set; } = true; public double HuberSigmaMultiplier = 1.5;`

Base `Solve()`: seed → (optional) Huber-IRLS wrap around the LM solve (recompute `δ = 1.5·σ`,
`σ = MAD` of current residuals via existing `MathUtility.MedianMAD`; per-point weight `1` if
`|r|≤δ` else `δ/|r|`, folded into the χ² weight; iterate ≤10 until `Σ|r|` stabilizes — mirror
`PSFModeler.SolveIRLS`) → populate `Fitting`, `Expression`, `Minimum`, weighted `RSquared`, and
`MinimumStdError` from the generalized `JᵀWJ` covariance. Keep OptGuard wiring. Compute reduced-χ²
inline for the benchmark.

## Models and math

Residual `r_i = w_i·(model(x_i) − y_i)`, `w_i = 1/max(|σ_i|,1e-6)` (existing convention); Jacobian
rows are `w_i·∂model/∂θ`.

### Symmetric (existing — wire the Jacobian, fix bug)
`f = (a/b)·√(u²+b²) + y0`, `u = x−x0`. Analytic gradient already exists
(`GetGradientForParameters`). Implement `FitResidualsJacobian` (currently throws) and set
`UseJacobian = true` (today it silently runs numeric). **Fix:** `upperBounds` is declared with
**5 elements for a 4-parameter model** (`HyperbolicFittingAlglib.cs:142`) — make it 4.

### Tilted hyperbola (NEW — `TiltedHyperbolicFittingAlglib`, 5 params {x0,y0,a,b,s})
`f = y0 + (a/b)·√(u²+b²) + s·u`. Let `R = √(u²+b²)`, `k = a/b`.
- `∂f/∂x0 = −[k·u/R + s]`,  `∂f/∂y0 = 1`,  `∂f/∂a = R/b`,
  `∂f/∂b = −a·u²/(b²·R)`,  `∂f/∂s = u`.
- **Minimum (closed form, required):** `f'(u) = k·u/R + s = 0` ⇒
  `u* = −sign(s)·|s|·b / √(k²−s²)`; report `Minimum = (x0+u*, f(x0+u*))`. (`u*=0` when `s=0`.)
- Reduces to symmetric at `s=0`. Asymptotic slopes `a/b ± s` (require `|s| < a/b`).
- **Bounds:** `x0∈[loX,hiX]`, `y0∈[−loY,loY]`, `a∈[0.001, 2·loY]`, `b∈[0.001,∞)`,
  `s∈[−0.9·k0, 0.9·k0]` with `k0 = initialA/initialB`. **Slope-bound risk:** a static box can't
  bind `s` to live `a,b`; additionally **saturate `√(k²−s²)`** with a floor (treat
  `k²−s² < (0.05k)²` as `(0.05k)²`) inside model/derivative so `u*`/`f'` stay finite (avoids
  `terminationtype=-8`). Initial guess starts symmetric `s=0`.

### Smooth logistic blend (NEW — `SmoothBlendHyperbolicFittingAlglib`, 5 params {x0,y0,a,b,c})
Replace the hard clamp with C-∞ logistic `t(x) = 1/(1+exp((x−x0)/w))`:
`f = y0 + t·(a/b)·√(u²+b²) + (1−t)·(a/c)·√(u²+c²)`.
- **`w` is derived from data spacing, not fitted** (`w = medianStepSpacing/4`). A fitted `w` is
  weakly identifiable (trades off against b,c, runs to bounds → unstable best-focus). Deriving it
  keeps 5 params (= legacy), sidesteps identifiability, makes the blend a clean drop-in. (Keep a
  `WidthFitted` hook, default off, not in UI.)
- Gradients with `Rb=√(u²+b²)`, `Rc=√(u²+c²)`, `dt/du = −t(1−t)/w`:
  `∂f/∂y0=1`; `∂f/∂a = t·Rb/b + (1−t)·Rc/c`; `∂f/∂b = t·(−a·u²/(b²Rb))`;
  `∂f/∂c = (1−t)·(−a·u²/(c²Rc))`; `∂f/∂x0 = −[t·(a/b)(u/Rb) + (1−t)(a/c)(u/Rc) +
  (dt/du)((a/b)Rb − (a/c)Rc)]`.
- **Minimum:** no clean closed form → **deterministic golden-section** minimization of `f(x)`
  over `[loX, hiX]` (~60 iters). Reduces to symmetric when `b=c`.

### Covariance for the shifted minimum (risk)
For symmetric, min-x = x0 so `Var(x0)` is direct. For tilted/smooth-blend, `x_min = x0 + u*(θ)` →
report `se(x_min) = √(∇θ x_min · Cov · ∇θ x_minᵀ)` (delta method; analytic gradient for tilted,
finite-difference of `ComputeMinimum` for smooth-blend). Guarded — any failure ⇒
`MinimumStdError = NaN` so the fit never breaks; fall back to `se(x0)` if unstable.

## Robust + integrated outlier detection

- **In-fit Huber IRLS** in the base `Solve()` (above) — resists outliers *before* hard rejection.
- **`MathUtility.RejectionTest`** (`Utility/MathUtility.cs:133`): switch center/scale from
  mean+stddev to **median + MAD·1.4826** (reuse `MedianMAD`); add an optional
  `Func<double,double> weights = null` param so callers pass the fit's per-point weight and the
  residuals are weighted consistently with the fit. Keep the Grubbs limit and the iterative
  re-fit-after-removal loop in `AutoFocusEngine`. Guard `madScale == 0` like the existing
  `stddev == 0` branch.
- **Callers** (`AutoFocusEngine.cs:123,137`, and `HocusFocusVM` if applicable): pass
  `x => 1/max(|errorY(x)|,1e-6)` when `WeightedHyperbolicFitEnabled`, else `null`.

## Options & UI

New enum `Interfaces/HyperbolicFitModel.cs` with `[Description]` attributes:
`Symmetric`, `UnevenBlendLegacy`, `TiltedHyperbola`, `SmoothBlend`. It **supersedes the
`UnevenHyperbolicFitEnabled` boolean** (mutually exclusive choices belong in an enum).

`AutoFocus/AutoFocusOptions.cs`:
- Add persisted `HyperbolicFitModel` via `GetValueEnum`/`SetValueEnum` (pattern:
  `InspectorOptions.cs`). **Default `UnevenBlendLegacy`** (current default is uneven=true → no
  behavior change for existing users).
- **Migration** in `InitializeOptions`: if the new enum key is absent but the legacy
  `UnevenHyperbolicFitEnabled` key exists, seed from it (`true→UnevenBlendLegacy`,
  `false→Symmetric`) and persist.
- Keep `UnevenHyperbolicFitEnabled` as a thin shim over the enum (getter `==UnevenBlendLegacy`,
  setter maps) to minimize churn / keep `AutoFocusOptionsTests` intact. Reset in `ResetDefaults`.
- `Interfaces/IAutoFocusOptions.cs`: add `HyperbolicFitModel HyperbolicFitModel { get; set; }`.

**Selection sites** — switch on `state.Options.HyperbolicFitModel` (a refinement of the existing
`HYPERBOLIC`/`TRENDHYPERBOLIC` `AFCurveFittingEnum`, not a new top-level curve type):
- `AutoFocus/AutoFocusEngine.cs:126-139`
- `AutoFocus/HocusFocusVM.cs:415-423`
Map each enum value to `HyperbolicFittingAlglib` / `HyperbolicUnevenFittingAlglib` /
`TiltedHyperbolicFittingAlglib` / `SmoothBlendHyperbolicFittingAlglib`.

**XAML** (`Resources/OptionsDataTemplates.xaml`): replace the Uneven `CheckBox` with a `ComboBox`
bound to `AutoFocusOptions.HyperbolicFitModel` using `util:EnumBindingSource` +
`HF_EnumStaticDescriptionValueConverter` (copy the PSFFitType ComboBox pattern). Add a
`HyperbolicFitModel_Tooltip` TextBlock. Keep the Weighted checkbox.

## Benchmark runner (empirical comparison)

`TestApp/FocusCurveBenchmarkRunner.cs` + a `focus-benchmark` branch in `TestApp/Program.cs`
(pattern: `ContaminationDiagnosticRunner`). Usage:
`TestApp.exe focus-benchmark --runs <af-json-dir> --out <dir> [--synthetic]`.
- **Real runs:** saved AF runs are `AutoFocusReport` JSON whose `MeasurePoints` are
  `FocusPoint { Position, Value, Error }` (`HocusFocusReport.cs:85`). Deserialize with
  Newtonsoft, map to `new ScatterErrorPoint(Position, Value, 0, Error)`. (No engine rerun /
  images needed.)
- **Synthetic:** extend `SyntheticFocusCurveSamples` with tilted + smooth-blend generators
  (known true minimum), fixed RNG seeds, with/without injected outliers.
- For each curve × each of the 4 models: `Create(new AlglibAPI(), points, useWeights:true)`,
  `Solve()`, record best-focus `Minimum.X`, `RSquared`, reduced-χ², residual RMS,
  `MinimumStdError`, and a **leave-one-out stability** metric (std of `Minimum.X` across LOO
  refits). Emit `focus_benchmark.csv` (one row per curve×model) + a stdout summary (mean abs
  best-focus error and mean LOO-std per model) so tilted vs smooth-blend vs legacy is decidable.

## Tests (NUnit, deterministic seeds)

Extend `SyntheticFocusCurveSamples` with `TiltedHyperbolaPoints` and `SmoothBlendPoints`
(returning the true minimum). Then:
- **Symmetric:** `Solve_WithJacobian_NoOptGuardViolation` (OptGuard on); `UpperBounds_HasFourElements`
  (regression for the 5-element bug).
- **Tilted:** recovers true (shifted) minimum on skewed data; reduces to symmetric at `s=0`; beats
  symmetric (lower residual RMS) on asymmetric data; respects slope bound (no NAN/INF); Jacobian
  OptGuard clean.
- **Smooth blend:** recovers true minimum; reduces to symmetric when `b=c`; continuity across `x0`
  (bounded finite-difference 1st/2nd derivative, vs legacy kink); beats legacy uneven on smooth
  asymmetric data; Jacobian OptGuard clean.
- **Huber IRLS:** injected gross outlier → `Minimum.X` near clean fit and closer than with
  `HuberIrlsEnabled=false`.
- **RejectionTest:** masking/swamping case where old mean/std fails but median/MAD detects the true
  outlier; weighted-residuals case (low-weight off-curve point not rejected, high-weight is);
  preserve perfect-fit and <4-points guards.
- **Options:** default is `UnevenBlendLegacy`; migration from legacy boolean true/false; setter
  persists enum.

## Ordered steps (TDD; commit per step; author/committer `322725+ghilios@users.noreply.github.com`)

1. Fix symmetric bugs: 4-element `upperBounds`; implement `FitResidualsJacobian` + `UseJacobian=true`
   (tests first).
2. Robustify `MathUtility.RejectionTest` (median/MAD + optional weighted residuals); update callers
   to pass `null` (no behavior change yet) (tests first).
3. Refactor `AlglibHyperbolicFitting` into the template-method base; reduce `HyperbolicFittingAlglib`
   and `HyperbolicUnevenFittingAlglib` to hooks — existing tests stay green (pure refactor).
4. Add Huber IRLS to the base (default on) + injected-outlier test.
5. Implement `TiltedHyperbolicFittingAlglib` + tests.
6. Implement `SmoothBlendHyperbolicFittingAlglib` + tests.
7. Add `HyperbolicFitModel` enum, options property + migration + interface, both selection sites,
   XAML ComboBox + tooltip, options tests; wire weighted residuals into `RejectionTest` calls.
8. Add `FocusCurveBenchmarkRunner` + `Program.cs` dispatch; run on synthetic + a real saved-run dir.
9. Bump `Properties/AssemblyInfo.cs` `3.0.0.28 → 3.0.0.29`.

## Files

**Create:** `StarDetection/TiltedHyperbolicFittingAlglib.cs`,
`StarDetection/SmoothBlendHyperbolicFittingAlglib.cs`, `Interfaces/HyperbolicFitModel.cs`,
`TestApp/FocusCurveBenchmarkRunner.cs`, and the test files above.

**Modify:** `StarDetection/AlglibHyperbolicFitting.cs`, `StarDetection/HyperbolicFittingAlglib.cs`,
`StarDetection/HyperbolicUnevenFittingAlglib.cs`, `Utility/MathUtility.cs`,
`AutoFocus/AutoFocusOptions.cs`, `Interfaces/IAutoFocusOptions.cs`, `AutoFocus/AutoFocusEngine.cs`,
`AutoFocus/HocusFocusVM.cs`, `Resources/OptionsDataTemplates.xaml`, `TestApp/Program.cs`,
`Properties/AssemblyInfo.cs`.

## Risks
- `|s| < a/b` (tilted): static box can't bind to live a,b → mitigated by init-derived bound +
  in-model `√(k²−s²)` saturation; verified by slope-bound test + OptGuard.
- Logistic-width identifiability (smooth-blend): mitigated by deriving `w` from spacing; verified
  by LOO-std in benchmark ≈ or better than legacy.
- Covariance for shifted minimum: delta method may be unstable → guarded fallback to `se(x0)`/NaN.
- Default kept `UnevenBlendLegacy` + migration → no silent behavior change for existing users.

## Verification
- `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` (Windows `dotnet.exe`) —
  all green.
- Build TestApp and run
  `TestApp.exe focus-benchmark --runs "%LOCALAPPDATA%\NINA\AutoFocus" --out C:\temp\focus-bench`;
  inspect `focus_benchmark.csv` to confirm tilted/smooth-blend reduce best-focus abs error and
  LOO-std on asymmetric/real runs vs legacy, and decide which asymmetric model to recommend.
