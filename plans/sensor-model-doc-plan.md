# Plan: Document how the sensor model is fit

## Context

The user-facing manual (MkDocs Material site under `documentation/docs/`) explains *what* the
Aberration Inspector measures but treats the **sensor model** as a black box. The page
`overview/tilt-aberration-inspector.md` says only that "a paraboloid is fit through the per-star
best-focus positions" and that "some star fits are rejected as outliers during modeling." None of
the math, the outlier handling, or the robustness machinery is documented, even though the code is
rich: a weighted Levenberg–Marquardt fit of a tilted paraboloid, MAD-based residual clipping, a
Grubbs per-star reject-and-refit, RANSAC frame registration, a covariance/delta-method error
budget, and a deliberate acceptance gate.

This plan adds a new, math-dense page that fills that gap, modeled on the existing
`overview/hyperbola-fitting.md` (the manual's precedent for an algorithm/"how it works" page that
pairs with a usage page). The outcome: a reader who wants to understand or trust the tilt/curvature
numbers can see the exact model, how bad data is detected and dropped, and every feature that makes
the fit robust.

Decisions confirmed with the user:
- **New dedicated page** `overview/sensor-model.md` (a math companion), cross-linked from the
  inspector page — not an in-place expansion.
- **Unified coverage**: recap the always-on 4-corner tilt plane, go deep on the paraboloid Sensor
  Curve Model, and relate them (the plane is the linear part of the paraboloid).
- **Reuse existing images + add 1–2 new diagrams** via the figure pipeline.

## Source of truth (verified against code)

The page must match these files exactly. Key references:

- Surface model + solver + covariance/std-errors: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Inspection/SensorParaboloidModel.cs`
- Orchestration (registration, per-star fits, brightness search, paraboloid fit): `.../Inspection/SensorModel.cs`
- Robust NLS driver (Winsorized loop, weights, R²/RMS/χ²/covariance): `.../Utility/NonLinearLeastSquaresSolver.cs`
- Acceptance rules + constants: `.../Inspection/SensorAberrationCalculator.cs`
- Physical-unit conversions + analysis rows: `.../Inspection/SensorModelAberrationResult.cs`
- Always-on tilt plane (Accord OLS): `.../AutoFocus/TiltModel.cs`
- Region layout, data gathering, backfocus, options: `.../AutoFocus/InspectorVM.cs`, `.../AutoFocus/InspectorOptions.cs`
- Per-star hyperbolic curve fit (already documented in `hyperbola-fitting.md`): `.../StarDetection/AlglibHyperbolicFitting.cs`

The exact facts/formulas to encode (do not paraphrase loosely):

- **Surface**: `z(x,y) = Gx·(x−X0) + Gy·(y−Y0) + Kx·(x−X0)² + Ky·(y−Y0)² + Z0`.
  Isotropic = 6 params (`Kx=Ky=K`); astigmatic = 7 params (independent `Kx,Ky`, one extra free param).
  Derived display quantities: `θ = atan(√(Gx²+Gy²))`, `φ = atan2(Gy,Gx)`, `K = (Kx+Ky)/2`,
  `C = sign(K)·√|K|`.
- **Data points**: each registered star → hyperbolic HFR-vs-focuser fit → best-focus position and its
  standard error `σ`. Point = `(x_µm, y_µm, focus_µm, R², σ_µm)` with `x_µm = (px − W/2)·pixelSize`,
  `focus_µm = fitMin·micronsPerStep`. Weight `w_i = 1/σ_i`; unknown `σ` imputed to the **median** σ.
- **Registration**: reference frame = most stars; per-frame brightness normalization; RANSAC
  similarity (or optional affine) alignment; KD-tree nearest-neighbor match (radius 10 px with
  RANSAC, 30 px without); each star must match in **≥5 frames**; per-star fit dropped if `R² < 0.90`;
  optional brightness-tolerance search (≤12 iterations).
- **Fit**: weighted LM via ALGLIB `minlm` with the **analytic Jacobian** (`minlmcreatevj` +
  `minlmsetacctype(state,1)`); minimizes `Σ w_i²(f(x_i)−y_i)²`; residual `f_i = w_i·(est−obs)` and the
  Jacobian carries the same weight. Box bounds: `X0,Y0 ∈ [−sensor/2, sensor/2]`, or **pinned to 0**
  when *Fixed Sensor Center*; `Z0`, gradients, curvature unbounded. Per-parameter scales
  `1,1,1,1e-3,1e-3,1e-6` (+`1e-6` for `Ky`). **Single solve** (signed `K` can cross zero).
- **Outlier handling, three layers** (call out clearly):
  1. *Surface — Winsorized residual loop* (`SolveWinsorizedResiduals`): after each solve compute the
     scaled MAD (`1.483 × median|·|`), **drop** points with `|residual| > 2.5·MAD`, re-solve, ≤10
     iterations, bail to the previous solution if R² does not improve. Note it *removes* rather than
     caps despite the "Winsorized" name.
  2. *Per-star curve — Grubbs two-tailed test* on median/MAD-scaled, 1/σ-weighted residuals
     (reject-and-refit, never below 5 points).
  3. *Per-star quality gate* — discard a star whose hyperbolic fit fails or scores `R² < 0.90`.
  Plus RANSAC rejecting registration outliers (implausible-scale transforms).
- **Robustness features**: χ² (1/σ) weighting + median-σ imputation; coordinate centering;
  per-parameter scaling; box constraints and the *Fixed Sensor Center* pin (which also keeps the
  covariance well-conditioned — a free center confounds tilt gradients with center offset, making the
  fit non-identifiable); minimum data (≥9 points to fit, ≥5 frames/star, warn under 10 stars);
  overfit guard (`1 − R² < 0.005` rejected); acceptance gate (reject **only** when `R² <
  AcceptableRSquaredMin` (0.05) **and** reduced `χ² > 5`); determinism (parallel per-star fits written
  to index-aligned slots); OptGuard analytic-gradient verification.
- **Fit-quality metrics**: weighted `R² = 1 − RSS/TSS` (weighted mean); unweighted RMS error
  (microns); `χ² = Σ(w(est−obs))²`, `dof = max(1, n_enabled − p)`, reduced `χ² = χ²/dof`, χ² p-value;
  parameter covariance `Cov ≈ s²(JᵀWJ)⁻¹`, `s² = weighted RSS/(n−f)` over **free** params only (null
  when singular/ill-conditioned), propagated by the **delta method** to `θ` std error and curvature
  radius std error.
- **Physical outputs (with units)**: tilt angle `θ` (deg) ± σ; `TiltEffectMicrons = (max−min corner
  tilt)/2`; curvature radius `mm = 1/(2·C²·1000) = 1/(2000|K|)` ± σ; `CurvatureEffectMicrons =
  K·r²` at the corner; `CriticalFocus = 2.44·F²·0.55` µm; center offsets `X0,Y0` (flagged ≥10 px);
  `SensorMeanElevation = Volume/Area` (closed-form surface integral), `SensorMeanPosition` (steps),
  `AutoFocusMeanOffset = SensorMeanPosition − finalFocusPosition`. Analysis-row thresholds: tilt
  acceptable if `|effect| < 0.25·CFZ`, curvature if `|effect| < 1.5·CFZ`.
- **Tilt plane (always on)**: 4 corner best-focus positions → Accord.NET OLS plane
  `Focus(x,y) = A·x + B·y + C` over normalized corner coords `(±0.5, ±0.5)`; no outlier rejection (4
  points); it is the linear part of the paraboloid and feeds per-corner screw guidance. **Backfocus**
  is separate: `mean(4 corners) − center`, scaled by *Microns per Focuser Step*, checked against CFZ.

## Deliverable

### 1. New page — `documentation/docs/overview/sensor-model.md`

Mirror `hyperbola-fitting.md`'s style: H1 title, `##`/`###` only (never deeper, `toc_depth: 3`),
LaTeX in `\(...\)` / `\[...\]` (never `$...$`), figures followed by an *italic caption*, admonitions
used sparingly, prose per `.claude/docs/documentation-style.md` (no em-dash overuse, no AI-writer
filler, quote in-app labels exactly). Proposed outline:

- **Intro** (2 short paras): what the sensor model is, what the page covers, and cross-links to
  `tilt-aberration-inspector.md` (usage/options), `hyperbola-fitting.md` (the per-star fit it
  consumes), and `autofocus.md` (per-region σ_focus / R² / reduced χ²). One notation-setup line
  (sensor coords in microns about the image center; focus in microns).
- **## Two models, one surface** — the always-on 4-corner tilt plane vs the optional paraboloid
  Sensor Curve Model, and how they relate (plane = linear part of the paraboloid). Reuse
  `assets/figures/tilt-heatmap.png`.
- **## The tilt plane** — OLS plane equation, normalized corner coords, per-corner Adjustment
  Required (steps/microns), and the separate backfocus computation. Keep brief; link to the inspector
  page for screw guidance.
- **## The sensor surface model** — the paraboloid equation, parameter meanings, isotropic vs
  astigmatic mode. Reuse `assets/screenshots/inspector-sensor-model-3d.png`. **New figure**:
  surface = tilt plane + curvature decomposition.
- **## From stars to data points** — per-star hyperbolic best-focus + σ, cross-frame registration
  (RANSAC, KD-tree matching, ≥5 frames, brightness tolerance), pixel→micron and step→micron
  conversions, χ² weighting and median-σ imputation.
- **## Fitting the surface** — weighted LM (analytic Jacobian), objective `Σ w_i²(f−y)²`, the analytic
  gradient, bounds, parameter scaling, Fixed Sensor Center pinning + identifiability note, single-solve
  rationale.
- **## Detecting and rejecting outliers** — the three layers above. **New figure**: MAD-based
  residual clip (accepted vs rejected best-focus points / residual band).
- **## What makes the fit robust** — concise list of the robustness features (weighting, imputation,
  scaling, min-data, overfit guard, determinism, OptGuard) and the acceptance gate.
- **## Fit-quality metrics** — table (R² weighted, reduced χ², RMS, χ² p-value, θ/radius std errors)
  + the reduced-χ² and covariance/delta-method formulas. Mirror the hyperbola page's metrics table.
- **## From fit to physical numbers** — tilt angle/effect, curvature radius/effect, critical focus,
  centering, mean elevation / AF offset, with units and the 0.25×/1.5× CFZ acceptance thresholds.

### 2. Navigation — `mkdocs.yml`

Add under **Overview & Features**, immediately after `Tilt & Aberration Inspector` (math page follows
its usage page, matching AutoFocus → Hyperbolic Curve Fitting):

```yaml
- Sensor Model Fitting: overview/sensor-model.md
```

### 3. Cross-links

- In `overview/tilt-aberration-inspector.md` `### The full sensor surface (optional)`: add a sentence
  linking to the new page for the full derivation (the way `autofocus.md` points to `hyperbola-fitting.md`).
- In `overview/index.md` Tilt & Aberration Inspector blurb: link the new page.
- Optionally in `quick-start.md` step 4.
- New page back-links to the inspector options table and to `hyperbola-fitting.md` / `autofocus.md`.

### 4. New figures — `documentation/figures/generate_figures.py`

Add two deterministic `fig_*` builders (seed their own RNG; style per existing `fig_tilt_heatmap`),
register them in the `FIGURES` dict, then run the generator to produce committed PNGs under
`documentation/docs/assets/figures/`:

- `sensor-surface-decomposition` — three small panels (tilt plane, curvature bowl, summed paraboloid)
  of best-focus offset, reusing the meshgrid approach in `fig_tilt_heatmap`.
- `sensor-outlier-rejection` — scattered per-star best-focus points along a field cross-section with
  the fitted surface, MAD-clipped outliers in pink (rejected) vs accepted in green and the ±2.5·MAD
  band, illustrating the Winsorized loop.

CI (`.github/workflows/docs.yml`) regenerates figures fresh and builds `--strict`, so the new figures
must be registered and the committed PNGs must match (`--check`).

## Execution approach

Given the depth, draft then verify:
1. Draft the page from the verified facts above.
2. Add the two figure builders + register + generate PNGs.
3. Wire nav + cross-links.
4. Have a subagent acting as a professional technical writer review the page for (a) factual accuracy
   against the cited source files and (b) AI-writer tells / house-voice per `documentation-style.md`;
   apply fixes.

Per project convention (`CLAUDE.md` Specs & Plans Workflow), copy this plan to
`plans/sensor-model-doc-plan.md` in the repo when execution begins, and do the work on a feature
branch `ghilios/sensor-model-docs` (never push to `develop`; open a PR; commit with the privacy
email).

## Verification

```bash
python -m pip install -r requirements-docs.txt
# generate just the new figures first, then everything, then confirm no drift
python documentation/figures/generate_figures.py --only sensor-surface-decomposition sensor-outlier-rejection
python documentation/figures/generate_figures.py
python documentation/figures/generate_figures.py --check
python -m mkdocs build --strict
```

- `mkdocs build --strict` must pass (catches broken internal links and missing images).
- Confirm all math uses `\(...\)` / `\[...\]` (never `$...$`); spot-check rendering with
  `python -m mkdocs serve` and eyeball the new page (formulas, figures, cross-links resolve).
- Re-read each formula against its source file to confirm exactness (no doc/code drift).
- No code changes to the plugin, so the .NET test suite is unaffected; no `dotnet test` needed for a
  docs-only change.
