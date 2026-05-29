# Sensor Model — Configurable R²/χ² Fit-Quality Thresholds & Dual-Metric Display

## Context

The sensor model fits a tilted paraboloid to per-star best-focus positions. Two
goodness-of-fit statistics are already computed on `SensorParaboloidModel`
(`Inspection/SensorParaboloidModel.cs`, set in `EvaluateFit`):

- **`GoodnessOfFit`** — R² (variance explained). *Scale-dependent*: a near-flat but
  well-measured sensor scores low R² despite being a great fit; a strongly-tilted sensor
  with sloppy data can score high R². Intuitive and familiar, but wrong as a sole gate.
- **`ReducedChiSquared`** — χ²/dof against each star's propagated best-focus σ.
  *Magnitude-independent* (≈1 = fits within measurement error). The statistically correct
  fit-quality measure. **Caveat:** σ here is an approximate per-star standard error that
  does not capture model error, so reduced χ² tends to run above 1 even for good fits and
  its absolute calibration is rig-dependent — which is exactly why the threshold should be
  user-configurable and the bands generous.

Today both thresholds are **hardcoded constants** and only R² is surfaced:

| Concern | Current state |
|---|---|
| χ² acceptance cap | `SensorAberrationCalculator.AcceptableReducedChiSquaredMax = 5.0` (const) |
| χ² acceptance test | `SensorAberrationCalculator.IsReducedChiSquaredAcceptable(fit)` (uses the const) |
| Brightness-search stop | `SensorModel.cs:~476` — `StarsInModel < 10 || !IsReducedChiSquaredAcceptable(pfit)` |
| Final "modeling failed" throw | `SensorModel.cs:~566` — `bestPfit.GoodnessOfFit < 0.05 && !IsReducedChiSquaredAcceptable(bestPfit)` (AND) |
| Model Properties grid (UI) | `AutoFocus/DataTemplates.xaml:~2686` — shows **R² only** ("Model R²") |
| Analysis table below it | `AutoFocus/DataTemplates.xaml:~2925` — `ItemsSource = …AnalysisResults`; `SensorModelAberrationResult.AnalyzeSensorModelFit` adds one "Model Fit" R² row |
| Result populate method | `SensorModelAberrationResult.Update(...)`, called from `SensorModel.cs:~149` and `~1022` (both have `inspectorOptions` in scope) |

### Goals

1. Expose **both** rejection thresholds as configurable options.
2. Reject the model with a **combined rule** that keeps the χ² magnitude-independence
   benefit (flat sensors still accepted) while letting a low R² matter when the fit is
   also statistically questionable.
3. Display **both** R² and reduced χ² in the Model Properties grid **and** the analysis
   table below it.

### Decisions (settled during design)

- Threshold drives **both** acceptance and display (single source of truth).
- Two options, defaults preserving today's behavior: `AcceptableReducedChiSquared = 5.0`,
  `AcceptableRSquaredMin = 0.05`.
- Good/marginal χ² boundary is **derived** as `0.6 × AcceptableReducedChiSquared`
  (= 3.0 at default) — not a separate knob.
- **Combined rejection rule:** reject if
  `χ² ≥ maxChi` **OR** `(R² < minR² AND χ² ≥ 0.6·maxChi)`.
  Equivalently, accept iff `χ² < maxChi AND NOT(R² < minR² AND χ² ≥ 0.6·maxChi)`.
- YAGNI: no good/marginal knob, no p-value display, no low-χ² overfit flag.

> Execute on a fresh branch off the merged PR #43 result (or `develop` if merged).
> Run `/clear` first. Each phase ends green:
> `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`.
> Author/committer email `322725+ghilios@users.noreply.github.com`; never push to `develop`.

---

## Phase 1 — Acceptance logic in `SensorAberrationCalculator` (pure, test-first)

`SensorAberrationCalculator` is a pure, unit-tested helper — start here with TDD.

- Replace the `AcceptableReducedChiSquaredMax = 5.0` const with default constants:
  - `public const double DefaultAcceptableReducedChiSquared = 5.0;`
  - `public const double DefaultAcceptableRSquaredMin = 0.05;`
  - `private const double GoodReducedChiSquaredFraction = 0.6;` (good/marginal boundary).
- Parameterize the χ² test:
  ```csharp
  public static bool IsReducedChiSquaredAcceptable(SensorParaboloidModel fit, double maxReducedChiSquared) {
      if (fit == null) return false;
      var rcs = fit.ReducedChiSquared;
      return !double.IsNaN(rcs) && !double.IsInfinity(rcs) && rcs <= maxReducedChiSquared;
  }
  ```
- Add the combined acceptance rule:
  ```csharp
  public static bool IsModelAcceptable(SensorParaboloidModel fit, double maxReducedChiSquared, double minRSquared) {
      if (fit == null) return false;
      if (!IsReducedChiSquaredAcceptable(fit, maxReducedChiSquared)) return false; // χ² rejected outright
      var goodBand = GoodReducedChiSquaredFraction * maxReducedChiSquared;
      var chiElevated = fit.ReducedChiSquared >= goodBand;
      if (fit.GoodnessOfFit < minRSquared && chiElevated) return false;            // low R² only when χ² also elevated
      return true;
  }
  ```
- Add a display-band classifier:
  ```csharp
  public enum FitQualityBand { Good, Marginal, Rejected }
  public static FitQualityBand ClassifyReducedChiSquared(double reducedChiSquared, double maxReducedChiSquared) {
      if (double.IsNaN(reducedChiSquared) || double.IsInfinity(reducedChiSquared) || reducedChiSquared >= maxReducedChiSquared)
          return FitQualityBand.Rejected;
      if (reducedChiSquared < GoodReducedChiSquaredFraction * maxReducedChiSquared)
          return FitQualityBand.Good;
      return FitQualityBand.Marginal;
  }
  ```

**Tests** (`SensorAberrationCalculatorTests`): truth table for `IsModelAcceptable` —
- good χ² + low R² → **accept** (flat sensor);
- marginal χ² (≥ 0.6·max) + low R² → **reject**;
- marginal χ² + adequate R² → **accept**;
- χ² ≥ max → **reject** regardless of R²;
- null / NaN / Inf → reject.
Band classifier boundaries (just below/above `0.6·max` and `max`). Update existing
`IsReducedChiSquaredAcceptable` tests to the new signature. Verify defaults reproduce
today's numbers (5.0, 0.05).

---

## Phase 2 — Options plumbing

- **`IInspectorOptions`**: add `double AcceptableReducedChiSquared { get; set; }` and
  `double AcceptableRSquaredMin { get; set; }`.
- **`InspectorOptions`** (follow the existing option pattern): backing fields, `InitializeOptions`
  load (`GetValueDouble(nameof(...), 5.0 / 0.05)`), property setters with
  `SetValueDouble` + `RaisePropertyChanged`, and `ResetDefaults` (5.0 / 0.05).
- **`FakeInspectorOptions`** (test double): add both auto-properties, defaulting to 5.0 / 0.05.

**Tests:** an `InspectorOptions` round-trip/reset test for both new options (mirroring any
existing options test), or extend the fake-backed options test if present.

---

## Phase 3 — Wire acceptance through `SensorModel`

Replace both hardcoded uses with the configurable combined rule:

- Brightness-search stop (`~:476`): retry while
  `pfit == null || pfit.StarsInModel < 10 || !SensorAberrationCalculator.IsModelAcceptable(pfit, inspectorOptions.AcceptableReducedChiSquared, inspectorOptions.AcceptableRSquaredMin)`.
  Update the debug log to print both thresholds.
- Final throw (`~:566`): replace
  `if (bestPfit.GoodnessOfFit < 0.05 && !IsReducedChiSquaredAcceptable(bestPfit))`
  with `if (!SensorAberrationCalculator.IsModelAcceptable(bestPfit, …AcceptableReducedChiSquared, …AcceptableRSquaredMin))`.
  Update the exception message to report both R² and reduced χ² and both thresholds.

At the default 5.0 / 0.05 the *only* behavioral change versus today is the intended one:
the rule is now the combined OR-with-guard instead of the old AND (a marginal-χ²-and-low-R²
fit that the AND would have accepted is now rejected; a flat good-χ² fit still accepts).

**Verification:** full suite green; the `SensorModelRepeatabilityTests` harness still passes
(determinism unaffected). Add an integration-style assertion only if cheap; the rule itself
is covered by Phase 1 unit tests.

---

## Phase 4 — Display both metrics

### 4a. Model Properties grid (`AutoFocus/DataTemplates.xaml:~2686`)
R² already appears ("Model R²" → `Model.GoodnessOfFit`). Add a **"Reduced χ²"** label +
value bound to `SensorModel.SensorModelResult.Model.ReducedChiSquared`
(`StringFormat={0:0.00}`) in a free cell of the 5-row × 6-col grid (add a row if needed).
Add a tooltip resource explaining reduced χ² and the σ-calibration caveat.

### 4b. Analysis table below (`SensorModelAberrationResult` + `:~2925`)
- `Update(...)` gains `acceptableReducedChiSquared` and `acceptableRSquaredMin` params;
  pass `inspectorOptions.AcceptableReducedChiSquared` / `.AcceptableRSquaredMin` from both
  call sites in `SensorModel` (`~:149`, `~:1022`).
- `AnalyzeSensorModelFit` produces **two** rows:
  - **"Model Fit (R²)"** — value `R²`; `Acceptable = !(R² < minR² && χ² ≥ 0.6·maxChi)`
    (red only when R² actually contributes to rejection); details adapt ("low R² is
    expected for a near-flat sensor and is fine while χ² is good").
  - **"Reduced χ²"** — value `χ²`; `Acceptable = χ² < maxChi`; details by
    `ClassifyReducedChiSquared`: Good = "fits within measurement uncertainty",
    Marginal = "acceptable but elevated — model error or under-estimated σ",
    Rejected = "exceeds the acceptable limit; model rejected".
- Existing `AnalysisResults` binding renders the extra row automatically (no XAML change
  needed for 4b beyond what already iterates the collection).

**Verification:** build (XAML compiles); full suite green. If `AnalyzeSensorModelFit` is
reachable in tests, assert the two rows and their `Acceptable` flags for representative
fits (flat/good, marginal/low-R², rejected).

---

## Phase 5 — Options UI

In the inspector's **Experimental** options group (`AutoFocus/DataTemplates.xaml`), add two
numeric inputs (follow the existing `ninactrl:UnitTextBox` / `HintTextBox` + `DoubleRangeRule`
pattern used by "Starting Brightness Tolerance"):

- **"Max reduced χ² (rejection)"** → `InspectorOptions.AcceptableReducedChiSquared`,
  range e.g. (0, 1000].
- **"Min R² (rejection)"** → `InspectorOptions.AcceptableRSquaredMin`, range [0, 1).

Each with an inline tooltip (matching the section's style) explaining the metric, the
combined rule, and that χ² runs above 1 even for good fits (tune per rig). Place them in
free cells / new rows of the Experimental grid.

**Verification:** build; full suite green; manually confirm the controls bind and persist.

---

## Verification (whole feature)

- `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` green after each phase.
- Defaults (5.0 / 0.05) reproduce current acceptance except the intended AND→combined change.
- Manual smoke: run the Aberration Inspector; confirm Model Properties shows R² **and**
  reduced χ², the analysis table shows both rows with sensible Good/Marginal/Rejected status,
  and changing either threshold in options changes acceptance + the displayed bands.
