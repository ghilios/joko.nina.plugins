# Weight-Chain Hygiene (F5a/F5b/F6) — Design

Step 4 of `plans/star-detection-hfr-autofocus-accuracy-analysis.md` (§9 item 4, §10 row 4).
Branch: `ghilios/weight-chain-hygiene` → PR to `develop`.

## Problem

The AF curve-fitting weight chain has three degenerate paths (analysis findings F5a, F5b, F6):

1. **F5a — weight floor.** A per-point σ of ~0 (one detected star, or 2+ stars with near-identical
   HFR → MAD = 0) is floored to 0.001 at `AutoFocusEngine.cs:695`, then turned into weight
   `1/max(|ErrorY|, 1e-6)` = 1000 by every weighted fitter, vs typical weights 2–20. In squared
   terms that is 10⁴–10⁶× influence: one degenerate point steers the whole weighted fit. Huber
   IRLS and Grubbs cannot catch it because LM pins the curve to the point (its residual is
   minimized by construction).
2. **F5b — invalid-σ accumulation.** `CvImageUtility.AverageMeasurement` (CvImageUtility.cs:607-637)
   latches `invalidStdDev = true` on the first frame with σ ≤ 0/NaN and silently skips every
   *later* frame's variance, while the divisor still counts all frames → pooled σ underestimated.
   The flag itself is dead — never returned or surfaced.
3. **F6 — σ semantics.** Per-point ErrorY is the star-ensemble scatter (1.483·MAD), not the
   median's precision; multi-frame pooling is RMS (`√(Σσᵢ²/n)`), not SEM (no ÷√n), so
   FramesPerPoint does not reduce reported σ. Casualty: `ReducedChiSquared` runs ≪ 1 (scatter
   overstates per-point uncertainty by ~√N*), so the χ² > 5 rejection gate (when selected) almost
   never trips; it also scales ~1/N*, so no single threshold works across rich and sparse fields.

**New finding (verified during this design, NINA source):** NINA core's `Trendline` and
`QuadraticFitting` both weight by `1/ErrorY²` (Accord OLS/polynomial with explicit weights);
`GaussianFitting` is unweighted. The degenerate 0.001 point therefore also poisons the trendline
fit — which drives sweep termination, TRENDLINES/TRENDHYPERBOLIC final position, and the trendline
R² gate — and the PARABOLIC/TRENDPARABOLIC fit. Any fix confined to our four hyperbolic models
would leave those paths broken, and we cannot change the NINA-core classes; the regularization
must therefore be applied to the `ScatterErrorPoint`s they receive.

## Decisions taken (brainstorm 2026-06-12)

| Fork | Decision |
|---|---|
| σ semantics (F6) | **Modest**: keep ensemble scatter as the per-point σ proxy (documented as such); fix pooling to valid-σ-only + SEM. No star-count threading, no SEM-of-median. |
| F5a mechanism | **Shared fit-layer median floor**: regularize a *copy* of the points at fit entry; raw values continue to flow to reports/charts. |
| χ² gate | **Calibrate empirically** via `TestApp/FitQualityRunner.cs` over saved AF reports; document scatter-units semantics either way. |

Rationale for "modest" σ: the star-to-star spread is dominated by systematic field structure
(tilt/curvature) plus seeing; the true frame-to-frame repeatability of the median is not estimable
from a single frame, and SEM-of-median (scatter/√N*) would *understate* uncertainty (seeing
variance does not shrink with N*), over-trip the χ² gate, and skew relative weights toward
star-rich near-focus points. Scatter is a defensible *relative* weight across the sweep — which is
all the fit position needs (`MinimumStdError` is immune to uniform σ scaling; verified in the
analysis).

## Design

### 1. `WeightRegularization` — shared fit-entry regularizer (F5a + NINA-core protection)

New small static class in `NINA.Joko.Plugins.HocusFocus.StarDetection` (next to the fittings):

```
static List<ScatterErrorPoint> Regularize(IReadOnlyList<ScatterErrorPoint> points)
```

Rules, applied to ErrorY only (X/Y/ErrorX pass through):

- `m` = median of all finite, **positive** ErrorY in the list.
- If no such value exists → all ErrorY := 1.0 (the fit degenerates to unweighted).
- σᵢ non-finite or ≤ 0 → `m` — *unknown precision gets average weight, not maximum*.
- Otherwise → `max(σᵢ, k·m)` with `k = 0.2` (internal constant `MinErrorYFractionOfMedian`;
  no UI knob).

Bounds: one point can exceed the median weight by at most 1/k = 5× (25× squared influence),
preserving legitimate ~3× near-focus/wing weight ratios while killing the 1000× path.
Implementation finding (Task 5 investigation, noise-free synthetic sweep): the 5× cap bounds weights
as designed, but it does not let Huber IRLS recover a *displaced* degenerate-σ point — any point whose
capped weight ratio exceeds ~1.25× self-masks (the weighted fit nearly interpolates it, so its
residual stays under the Huber threshold while its clean neighbors get downweighted; measured recovery
cliff at k ≥ 0.8). Fit-level damage from one max-leverage wing outlier saturates (~56–61 steps)
almost independently of weight ratio (5× vs 1000×). The regularization contract is therefore the
weight bound itself; robust rejection of high-weight displaced points would need a structural IRLS
change (e.g. judging residuals against an unweighted reference fit) — out of scope, recorded for the
step-5 small-fixes batch.

Call sites (regularize once per fit entry, on the copy handed to fitters):

- `CurveFittingResult.Calculate` (AutoFocusEngine.cs:97) — covers `TrendlineFitting`,
  `QuadraticFitting`, `GaussianFitting` (harmless no-op), the four hyperbolic models via
  `AlglibHyperbolicFitting.Create`, and Grubbs via `BuildResidualWeights` in the rejection loop.
  Note: `RejectedPoints` recorded from this path (AutoFocusEngine.cs:229,294) will carry
  regularized σ — acceptable cosmetic difference, noted in code.
- `AutoFocusRegionState.SelectBestHyperbolicModel` → `SelectBestModel` path.
- `ComputeLeaveOneOutStability` → `ComputeLeaveOneOutBestFocusStdError`.
- `HocusFocusVM.SetCurveFittings` — NINA core's saved-chart reload (`AutoFocusToolVM.LoadChart`)
  rebuilds `FocusPoints` from a saved report's raw `Error` values and re-fits through this method
  (NINA-core trendline/quadratic + our weighted hyperbolics). Found in Task 4 code review; without
  it, a reloaded report with `Error: 0` resurrects the degenerate-weight path after Task 5.
- `FitQualityRunner` re-fit path (TestApp) — so harness numbers match production behavior, and old
  saved reports containing fabricated 0.001 values are regularized identically (0.001 → k·m).

The models' internal `1/max(|σ|, 1e-6)` guards stay as numerical backstops. `BuildResidualWeights`
keeps its formula (its inputs are now regularized).

### 2. Honest point construction (display/report layer)

`AutoFocusEngine.cs:695` changes from `Math.Max(0.001, σ)` to a finite-guarded raw value via the
shared helper `AutoFocusEngine.SafeDisplayError`: `double.IsFinite(σ) ? Math.Max(0.0, σ) : 0.0`
(catches ±Inf as well as NaN). Applied at all three construction sites: the engine's per-point
construction (~line 695), `HocusFocusVM` chart points, and `InspectorVM` chart points.
ErrorY = 0 renders as "no error bar" and is treated as *unknown* by the regularizer (→ median σ).
Saved reports (`HocusFocusReport` MeasurePoints.Error) carry measured values, never fabricated floors.
Within the plugin, report and charts only display raw ErrorY; all plugin fitters go through §1
(including the saved-report reload path via `SetCurveFittings`).

### 3. `AverageMeasurement` rewrite (F5b + F6 pooling)

`CvImageUtility.AverageMeasurement` becomes:

- `Measure` = mean over frames with `Measure > 0` — unchanged.
- Variance pooling over **valid-σ frames only** (finite and > 0); no latch — each frame's σ is
  judged independently. Fixes F5b.
- **SEM, not RMS**: `Stdev = √(Σ_valid σᵢ² / n_valid) / √total`, where `total` = frames in the
  mean. Equivalent to the §9 sketch's ÷√FramesPerPoint; exact no-op for FramesPerPoint = 1 (the
  common default). Rationale: the mean of n frames genuinely has ~√n smaller uncertainty; treating
  invalid-σ frames as having typical σ (RMS of the valid ones) is the least-surprise estimate.
- All σ invalid (but measures present) → `Stdev = NaN` — the previously dead `invalidStdDev`
  signal becomes real and flows to §2's NaN guard (display: no bar) and §1's unknown rule
  (fit: median weight).
- `total == 0` → `{Measure = 0, Stdev = NaN}` — unchanged.

Also used by the InitialHFR/FinalHFR paths (AutoFocusEngine.cs:716-735): `Measure` semantics there
are unchanged; their σ becomes honest for multi-frame users.
The `MeasurementPointCompleted` event now carries the pooled measurement rather than the last
sub-frame's (found in Task 3 review; fixed in this PR), so charts, the NINA broadcast point, and
saved-report MeasurePoints agree with the fit inputs when FramesPerPoint > 1.

Per-frame σ stays the ensemble scatter (1.483·MAD) — documented in the XML doc of
`AverageMeasurement` and in `MeasureAndError` usage comments as a *relative* precision proxy.

### 4. χ² gate: empirical calibration (F6 casualty)

After §§1–3 land:

1. Run `FitQualityRunner` over the saved AF report corpus (directory or zip), which re-fits every
   curve with every model through the regularized path, and collect the χ²_red distribution for
   known-good runs (plus any known-bad ones).
2. **Pre-registered decision rule**: if the good-run χ²_red distribution is tight enough to
   discriminate (≲ 2 orders of magnitude spread), set a new default
   `ReducedChiSquaredRejectionThreshold` from a robust upper fence (e.g. median × 10 or ~P99 with
   margin). If it sprawls — expected, since χ²_red scales ~1/N* and, post-SEM, ×FramesPerPoint —
   keep 5.0 and document the gate as advisory in scatter units.
3. Either way: update XML docs on `ChiSquared`/`ReducedChiSquared`
   (AlglibHyperbolicFitting.cs:58-73) and the options tooltip for the χ² threshold in
   `Resources/OptionsDataTemplates.xaml` with the scatter-units caveat (χ²_red ≪ 1 is normal for
   star-rich fields; scales with FramesPerPoint).

Default-change mechanics: constant in `AutoFocusOptions.ResetDefaults`/initial value only.
Existing persisted profiles keep their stored threshold; no migration (users who explicitly chose
the χ² criterion likely tuned it). Default criterion remains R² — unaffected users see no change.

## Testing

Unit (NUnit, Tests project):

- `AverageMeasurement`: all-valid SEM scaling (÷√n); **latch regression** — invalid frame first,
  valid frames after → their variances are counted; all-invalid → Stdev NaN, Measure intact;
  `total == 0` unchanged; FramesPerPoint = 1 passthrough (Stdev == frame σ).
- `WeightRegularization`: floor at k·median; ≤ 0/NaN → median; all-degenerate → all 1.0; single
  point; X/Y/ErrorX preserved; empty list.
- Integration-style: synthetic hyperbolic sweep + one zero-σ off-curve point → fitted minimum
  within tolerance of ground truth through the regularized path (pin-release regression test).
- `BuildResidualWeights` consistency with regularized inputs (no 1e6 weights reachable).
- Existing `IsHyperbolicFitAcceptableTests` unchanged (gate logic itself is untouched).

Harness validation:

- `FitQualityRunner` before/after over the saved-report corpus: fitted-minimum shifts, σ(focus),
  LOO stability. Expectation: only degenerate-σ-affected runs change; everything else within
  noise.
- Full suite green (`rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`)
  before completion.

## Scope

**In**: §§1–4 above; χ² calibration run + resulting threshold/doc decision; roadmap §10 update in
`plans/star-detection-hfr-autofocus-accuracy-analysis.md` — row 3 → ✅ Done (PR #48, merged),
row 4 → 🟡 In progress (this design + plan).

**Out** (deliberately):

- F7 (TRENDHYPERBOLIC 50/50 averaging) and F12 (parabolic Grubbs unweighted) → step 5 small fixes.
- Star-count threading / SEM-of-median (rejected — see Decisions).
- Empirical frame-to-frame σ for FramesPerPoint ≥ 3 (mixed semantics; revisit only if harness data
  demands it).
- UI knob for `k`; persisted-threshold migration.
- Contrast-method (`GaussianFitting`) behavior changes — it is unweighted; pooling fixes apply
  harmlessly.

## Risks / notes

- Relative weights within a sweep change slightly wherever σ < k·median occurred naturally —
  expected to be rare for healthy fields (typical weights 2–20 sit well inside the 5× cap).
- Multi-frame users (FramesPerPoint > 1) get √n-smaller σ → χ²_red grows ×n for them; the
  calibration step must bucket by FramesPerPoint when reading the corpus.
- Saved-report Error values written by the new code are raw (0 possible); FitQualityRunner and any
  external consumer must tolerate 0/absent error — regularization handles this uniformly.
- HF-written reports live in the shared NINA AutoFocus report directory. If the user switches the
  AF behavior back to NINA's built-in VM and reloads an HF-written report whose `Error` is 0,
  NINA core's own `SetCurveFittings` computes 1/0² unguarded (NaN trendline) — outside plugin
  control, display-only, rare (degenerate run + behavior switch + reload). Accepted.
- Latent pre-existing bug found during the Task 5 investigation: `AlglibHyperbolicFitting.SolveHuberIrls`
  returns the FAILED solve's parameters when a mid-loop `SolveOnce` fails (the `out` solution is
  overwritten before the termination check), despite intending to keep the previous good solution.
  Unexercised today; fix belongs in the step-5 small-fixes batch, together with the related nit that
  the Huber threshold compares uncentered |r| against a median-centered MAD.
- Sibling hazard (out of scope, found in Task 5 review): the Inspection module's per-star weighted
  hyperbolic fits (`SensorModel.cs` ~664/692) do not route through `WeightRegularization`. Their σ
  comes from a different estimator (`EstimateHfrStdDev`, HFR/max(SNR,1) floored at 1e-3), but has
  the same structural hazard — a high-SNR frame hitting the 1e-3 floor gets a ~1000× weight within
  one star's sweep. Recorded for the step-5 small-fixes batch.
