# Sensor Model — Code-Review Follow-Ups

Deferred items from the thorough code review of branch
`ghilios/sensor-model-repeatability-and-robustness` (PR #43). The high-severity
correctness bugs and the safe robustness/cleanup fixes from that review were already
applied on the branch (commits `03b5ce7` and `5381eb3`). This plan captures the items
that were intentionally **not** done in that PR — two low-impact correctness/clarity
issues and two larger structural refactors that carried regression risk too close to
merge.

> Execute against `develop` (or the merged result of PR #43) on a fresh feature
> branch `ghilios/sensor-model-review-followups`. Run `/clear` before executing.
> Each phase must end green: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`.

## Context

The sensor model fits a tilted paraboloid (linear tilt gradients `Gx, Gy` + curvature
`K`, or independent `Kx, Ky` in optional astigmatic mode) to per-star best-focus
positions recovered from a focus sweep. Stars are registered across frames via a
seeded RANSAC similarity transform plus a KdTree triangle matcher, and the paraboloid
fit is a weighted (1/σ²) χ² least-squares solve.

| Concern | File |
|---|---|
| Per-star fit, registration, brightness search | `Joko.NINA.Plugins.HocusFocus/Inspection/SensorModel.cs` |
| Paraboloid model + solver (isotropic/astigmatic) | `Joko.NINA.Plugins.HocusFocus/Inspection/SensorParaboloidModel.cs` |
| Derived display metrics (tilt°, curvature radius) | `Joko.NINA.Plugins.HocusFocus/Inspection/SensorModelAberrationResult.cs` |
| RANSAC transforms, triangle matcher | `Joko.NINA.Plugins.HocusFocus/Utility/RANSACRegistration.cs` |
| LM solver wrapper, χ²/R²/winsorization | `Joko.NINA.Plugins.HocusFocus/Utility/NonLinearLeastSquaresSolver.cs` |

---

## Phase 1 — Interpolated-grid points carry a real σ (low impact)

**Problem.** In `SensorModel.FitImages`, when `inspectorOptions.InterpolationEnabled` is
on, the per-star best-focus points are replaced by an interpolated grid via
`ToInterpolatedGrid(...)`. Those grid points are constructed with the default
`focuserPositionStdDev = 1.0` (see `ToInterpolatedGrid`, which builds
`new SensorParaboloidDataPoint(x, y, focuser, rSquared: 1.0)` with no σ), whereas the
non-interpolated path now passes a real per-point σ. So the paraboloid fit's weighting
and the reduced-χ² acceptance gate behave inconsistently depending on whether
interpolation is enabled. `InterpolationEnabled` is currently an experimental option
whose UI is commented out, which is why this is low priority.

**Approach.**
- Propagate a representative σ into the interpolated points. Simplest defensible choice:
  carry the median of the input points' `FocuserPositionStdDev` into every grid point
  (interpolation smooths the surface, so a single representative uncertainty is
  reasonable), or interpolate σ alongside the value if cheap.
- Update `ToInterpolatedGrid` to accept/produce the σ and set it on each
  `SensorParaboloidDataPoint`.

**Alternative.** If interpolation is considered dead, delete the `InterpolationEnabled`
path and `ToInterpolatedGrid` entirely rather than fixing its weighting. Decide with the
maintainer before removing.

**Verification.** Unit test: build interpolated grid from points with known σ and assert
the grid points' `FocuserPositionStdDev` equals the intended representative value (not
1.0). Full suite green.

---

## Phase 2 — Astigmatic curvature reporting (low impact, off by default)

**Problem.** `SensorModelAberrationResult` computes
`CurvatureRadiusMillimeters = 1.0 / (2 · C² · 1000)` where `C = sign(K)·√|K|` and
`K = ½(Kx + Ky)` is the **mean** curvature. In astigmatic mode (`AstigmaticCurvatureEnabled`,
off by default) `Kx ≠ Ky`, so a single radius derived from the mean is a physically
meaningless blend — and for a saddle field (`Kx`, `Ky` opposite signs) the mean can be
~0 while real curvature is large, making the reported radius blow up.

**Approach.**
- When `sensorModel.Astigmatic` is true, report the two principal curvature radii
  (from `Kx` and `Ky`) instead of one, or label the single value clearly as a mean / mark
  it N/A. Add an astigmatism indicator to the analysis result.
- Keep the isotropic path (the default) exactly as-is.

**Verification.** Unit test on `SensorModelAberrationResult` with an astigmatic model:
assert the reported curvature reflects `Kx`/`Ky` rather than only the mean, and that a
saddle field does not produce a single misleading radius. Full suite green.

---

## Phase 3 — Extract a shared generic RANSAC core (refactor, no behavior change)

**Problem.** `RANSACRegistration.EstimateSimilarityTransform` and
`EstimateAffineTransform` are now near-verbatim copies: same deterministic-seed setup
(`ComputeDeterministicSeed`), same lazy sampler, same inlier-count / `aveInlierDistance`
/ best-model loop, same refine-or-throw tail. They differ only in sample arity
(pair vs triplet) and the `Fit*` call. Any future change to scoring or refinement must be
applied in two places and will drift.

**Approach.**
- Extract a private generic core, e.g.
  `EstimateTransform(srcPoints, dstPoints, sampleSize, sampler, fitFn, refineFn, status, progress)`,
  parameterized by a sampler delegate (`SamplePairs`/`SampleTriplets`) and a fit/refine
  delegate. The two public methods become thin wrappers.
- Preserve current behavior **exactly**, including the deterministic seed and the
  `aveInlierDistance` tie-break (already guarded against 0/0).

**Verification.** This is behavior-preserving: the existing
`EstimateSimilarityTransform_*` / `EstimateAffineTransform_*` tests (recovery + fixed-seed
bit-for-bit determinism) must continue to pass unchanged. Full suite green. Do not add
new behavior in this phase.

---

## Phase 4 — Single source of truth for the isotropic/astigmatic mode (refactor)

**Problem.** Whether the paraboloid is isotropic (6 params) or astigmatic (7 params) is
currently encoded three ways that must be kept in sync: `parameters.Length` (inferred in
`SensorParaboloidModel.FromArray`/`ToArray` and `SensorParaboloidSolver.Value`/`Gradient`),
the `SensorParaboloidModel.Astigmatic` bool, and the `SensorParaboloidSolver.astigmatic`
field — each with its own `Length == 7 ? … : …` / `if (astigmatic)` branch scattered
across `FromArray`, `ToArray`, `Value`, `Gradient`, `SetInitialGuess`, `SetBounds`,
`SetScale`. Adding a parameter or mode means hunting down 6+ branches; missing one (e.g.
`SetScale` forgetting index 6) silently mis-scales the fit.

**Approach.**
- Make `NumParameters` the single source of truth: a `bool IsAstigmatic => NumParameters == 7`
  helper on the solver, and drive every solver branch from it. On the model, keep `Astigmatic`
  derived consistently from `FromArray`'s array length (already the case) and centralize the
  param-packing/unpacking so `ToArray`/`FromArray`/`Value`/`Gradient` share one notion of layout.
- Consider a small private layout description (indices for z0/gx/gy/kx/ky) so bounds, scale,
  initial guess, value, and gradient all read from it.

**Verification.** Behavior-preserving. All existing `SensorParaboloidModelTests`
(isotropic recovery, flat field, astigmatic Kx/Ky recovery, 6-/7-element round-trip,
weighted-Jacobian OptGuard) must pass unchanged. Full suite green.

---

## Phase 5 (optional) — Build the triangle KdTree once across strict/relaxed passes

**Problem.** `SensorModel.AlignStarsWithRANSAC` calls
`GeneratePutativeMatchesUsingSimilarTriangles` twice (strict tolerance, then a relaxed
retry when too few matches), and each call rebuilds the image-triangle KdTree from
scratch. Caching `ShapeDescriptor` (done in PR #43) already removed the dominant
recompute cost, so this is now minor.

**Approach.**
- Add an overload of `GeneratePutativeMatchesUsingSimilarTriangles` that accepts a
  prebuilt `KdTree<double, StarTriangle>`; build the tree once in the caller and reuse it
  for both passes (the same `StarTriangle` instances are reused, and `ResetMatch` already
  clears their matched flags between passes, so the tree need not be rebuilt).
- Keep the existing parameterless-tree wrapper for other callers/tests.

**Verification.** Behavior-preserving: the matcher tests
(`GeneratePutativeMatches_*`) and the end-to-end `SensorModelRepeatabilityTests` must pass
unchanged. Full suite green. Skip this phase if the maintainer considers the win too small.

---

## Notes

- Phases 1–2 are independent and small; Phases 3–4 are independent refactors; Phase 5 is
  optional. They can be done in any order or as separate PRs.
- None of these should change the model fit results on real data except Phase 1 (which
  only affects the experimental interpolation path) and Phase 2 (display only).
- Keep author/committer email `322725+ghilios@users.noreply.github.com` per `CLAUDE.md`,
  branch off and PR into the appropriate base, and never push to `develop` directly.
