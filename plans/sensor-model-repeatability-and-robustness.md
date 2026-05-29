# Sensor Model & RANSAC Registration — Analysis and Improvement Plan

> Note: this plan was authored in plan mode at the harness-mandated path. Per `CLAUDE.md`,
> before execution it should be copied to `plans/sensor-model-repeatability-and-robustness.md`.

## Context

The aberration **Sensor Model** quantifies sensor tilt and field curvature. The user's
mental model (confirmed) of the pipeline is:

1. **One** autofocus run captures frames, each at a different focuser position.
2. Stars are **matched across frames** (RANSAC alignment + KdTree nearest-neighbor) so the
   same physical star is tracked through the focus sweep.
3. A **hyperbolic HFR-vs-focuser curve** is fit per matched star to find that star's optimal
   focuser position.
4. A **6-parameter tilted paraboloid** is fit to the (sensor-X, sensor-Y, optimal-focuser)
   points to model tilt + curvature.

The user's definition of **robustness is repeatability**: *"I can run the whole process
multiple times without changing anything and get repeatable, consistent behavior."* Today it
does **not** — there are several deliberate and accidental sources of non-determinism, plus a
math bug and modeling/perf weaknesses. The user asked to fix all four areas: correctness bugs,
registration robustness, model/optimizer quality, and performance.

### Where the code lives

| Concern | File |
|---|---|
| Pipeline orchestration, registration loop, KdTree matching, per-star fit | `Joko.NINA.Plugins.HocusFocus/Inspection/SensorModel.cs` |
| RANSAC, triangle matching, transforms | `Joko.NINA.Plugins.HocusFocus/Utility/RANSACRegistration.cs` |
| Paraboloid model + solver (Value/Gradient/bounds) | `Joko.NINA.Plugins.HocusFocus/Inspection/SensorParaboloidModel.cs` |
| Levenberg–Marquardt wrapper, winsorization, R²/RMS | `Joko.NINA.Plugins.HocusFocus/Utility/NonLinearLeastSquaresSolver.cs` |
| Fit-acceptance heuristics (pure, unit-tested) | `Joko.NINA.Plugins.HocusFocus/Inspection/SensorAberrationCalculator.cs` |
| Derived metrics (tilt°, curvature radius, mean elevation) | `Joko.NINA.Plugins.HocusFocus/Inspection/SensorModelAberrationResult.cs` |
| Existing tests | `Joko.NINA.Plugins.HocusFocus.Tests/Utility/RANSACRegistrationTests.cs` |

---

## Findings

### A. Sources of non-determinism (the headline problem)

1. **Unseeded shared RNG drives RANSAC.** `RANSACRegistration.cs:91` declares
   `private static Random RNG = new();`. It is used to shuffle candidate triplets in
   `EstimateAffineTransform` (`randIndices.OrderBy(x => RNG.Next()).Take(maxIterations)`,
   `:497`) and the analogous `EstimateSimilarityTransform` (`:420`). Different shuffles select
   different sample sets → different transform → different alignment → different KdTree matches
   → **different model every run.** This is the primary repeatability killer.

2. **Run results leak into the next run's starting point.** `RegisterStarsAndFit` reads
   `inspectorOptions.PreviousRunBrightnessDiff` as the seed for the brightness-tolerance search
   (`SensorModel.cs:401`) and **writes the winning value back** (`:519`). So "running the
   unchanged process again" starts from different state and can converge differently. By design,
   consecutive runs are *not* independent.

3. **Model acceptance depends on wall-clock time.** `SensorAberrationCalculator
   .TargetR2BasedOnTimeTaken` (`:30`) relaxes the accepted R² target from 0.9 → 0.6 based on how
   many seconds the iteration took. The same data accepts a different model on a busy vs idle
   machine. Used at `SensorModel.cs:440`.

4a. **R² is the wrong goodness-of-fit statistic for acceptance.** R² = 1 − RSS/TSS is
   *scale-dependent* (inflates with large tilt/curvature signal, collapses on near-flat sensors)
   and ignores per-point measurement uncertainty and DOF. Its denominator TSS depends on *which*
   stars matched, so the value jitters run-to-run with the non-deterministic registration —
   directly undermining repeatability. The project already does the right thing where noise is
   known: `PSFModel.ReducedChiSquared = rss / (nPixels · noiseSigma²)`
   (`StarDetection/PSFModel.cs:74`), keeping R² only as a secondary intuitive number. The sensor
   pipeline does **not** carry per-point uncertainties: the per-star hyperbolic points are built
   with zero error (`SensorModel.cs:576`, also why weighted-fit is a no-op, finding #12), and the
   paraboloid fit weights every star equally despite very heterogeneous best-focus-position
   uncertainties.

4. **Greedy, order-dependent triangle matching.** `GeneratePutativeMatchesUsingSimilarTriangles`
   marks triangles matched as it scans (`MarkAsMatched`, `:375`) and depends on reference-triangle
   iteration order; combined with (1) this compounds variance.

### B. Correctness bugs

5. **`Volume()` formula bug.** `SensorParaboloidModel.cs:159`:
   `part1 = C2 * w * h * (X0 * X0 + Y0 + Y0)` — the `Y0 + Y0` term must be `Y0 * Y0`. This feeds
   `SensorMeanElevation` in `SensorModelAberrationResult`, so the reported mean elevation is wrong
   whenever the curvature center is offset in Y.

6. **6-DOF affine where physics allows only a similarity.** Frames in one AF run differ by small
   translation (drift/flexure), maybe a tiny rotation, negligible scale. `AlignStarsWithRANSAC`
   uses `EstimateAffineTransform` (`SensorModel.cs:749`), which permits shear + anisotropic scale.
   Those extra DOFs let RANSAC "explain" mismatches by warping the field, displacing star
   positions that the paraboloid then treats as truth. A `SimilarityTransform` (4-DOF) already
   exists in `RANSACRegistration.cs:61` but is **never used**.

### C. Registration robustness

7. **Reference-frame-only star registry.** `MatchStarsUsingKdTree` seeds the global registry
   *only* from the reference frame (the frame with the most detected stars, `SensorModel.cs:361`,
   `:814`) and never adds stars seen in other frames but absent there. Stars not detectable at the
   sharpest frame can never be registered, reducing spatial coverage and biasing the paraboloid.

8. **Weak triangle descriptor + brittle threshold.** Matching uses cosine similarity of a
   3-vector of normalized squared side-lengths (`AsShapeMatrix`, `:208`) with thresholds
   `0.999999` / `0.99999` (`SensorModel.cs:652`). Cosine similarity of side-length ratios is a
   poor shape metric (insensitive to the right things, ignores handedness), and the near-1
   threshold is fragile to seeing/centroiding noise.

### D. Model / optimizer quality

9. **Curvature requires two full solves.** Because curvature is parameterized as
   `sign(c)·c²` with `|c| ≥ 1e-4` (cannot cross zero), `FitParaboloidModel` solves once with
   `PositiveCurvature=true`, again with `false`, and keeps the lower-RMS result
   (`SensorModel.cs:196–206`). Doubles optimizer work and adds a discontinuity at flat fields.

10. **Tilt parameterization has a built-in singularity.** Tilt uses `(theta, phi)` with
    `theta ≥ 1e-5` forced (`SensorParaboloidModel.cs:286`) precisely to dodge the
    `phi`-unidentifiability and local minimum at `theta=0`. The tilt plane is otherwise *linear*;
    the `(theta, phi)` form injects trig nonlinearity and a trap for no benefit.

11. **Isotropic curvature only.** `C²·(x'²+y'²)` cannot represent astigmatic/saddle field
    curvature. (Lower priority — a modeling enhancement, not a bug.)

12. **Per-star hyperbolic fit weighting is a no-op.** Points are built as
    `new ScatterErrorPoint(focuser, HFR, 0.0, 0.0)` (`SensorModel.cs:576`); the zero error means
    `WeightedHyperbolicFitEnabled` weights nothing. The hard `R² ≥ 0.90` gate (`:608`) silently
    discards stars with no SNR-aware weighting.

### E. Performance

13. **Triangle build is O(n²) with an O(n) dup check inside the loop.** Non-`onePerPoint` mode
    builds every C(k,3) triangle per neighborhood and runs `triangles.Count(tri =>
    tri.SameTriangle(triangle)) == 0` for each (`RANSACRegistration.cs:319`).

14. **All triplets materialized before sampling.** `EstimateAffineTransform` enumerates the full
    C(n,3) triplet list, then shuffles and takes 10,000 (`:489–497`) — O(n³) memory/time before a
    single model is tested.

15. **Brightness-tolerance retry loop** (`SensorModel.cs:416–515`) re-runs the entire
    match+fit+paraboloid pipeline up to many times per analysis; interacts badly with (2) and (3).

---

## Improvement Plan

Ordered so the repeatability fixes (the user's stated goal) land first and are independently
verifiable. Each phase ends green (`dotnet test`).

### Phase 1 — Determinism / repeatability (highest priority)

- **Inject a seeded RNG into RANSAC.** Replace the static `RNG` (`RANSACRegistration.cs:91`) with
  an instance seeded from a fixed constant (or a value derived deterministically from the input
  star set, e.g., a hash of reference-star coordinates). Thread it through
  `EstimateAffineTransform` / `EstimateSimilarityTransform` so a given input always yields the
  same sampling. Expose the seed as an `InspectorOptions` value (default fixed) only if a user-
  facing knob is wanted; otherwise keep it internal.
- **Stop persisting search state across runs.** Make the brightness-tolerance search start from a
  fixed, configured value each run instead of `PreviousRunBrightnessDiff`
  (`SensorModel.cs:401`, `:519`). Either remove the write-back or gate it behind an explicit
  "adaptive" option that defaults off, so the default path is stateless and repeatable.
- **Remove wall-clock from model acceptance.** Replace `TargetR2BasedOnTimeTaken`
  (`SensorAberrationCalculator.cs:30`, used at `SensorModel.cs:440`) with a fixed, uncertainty-aware
  acceptance criterion (reduced-χ² / p-value — see Phase 4) and a fixed max-iteration budget. This
  is a pure function already under test — update `SensorAberrationCalculatorTests` accordingly.
- **Make tie-breaking deterministic.** Where RANSAC compares models with equal inlier counts it
  already falls back to average inlier distance (`:530`); ensure any remaining ordering
  (triangle iteration, dictionary enumeration) is stable and documented.

### Phase 2 — Correctness bug fixes

- **Fix `Volume()`**: `Y0 + Y0` → `Y0 * Y0` (`SensorParaboloidModel.cs:159`). Add a unit test that
  pins `Volume`/`SensorMeanElevation` against an analytically integrated value for a known model.

### Phase 3 — Registration robustness

- **Switch alignment to the similarity transform.** Use the existing `SimilarityTransform` /
  `EstimateSimilarityTransform` in `AlignStarsWithRANSAC` (`SensorModel.cs:749`) instead of the
  affine path, optionally keeping affine behind an option for diagnostics. Reuses
  `FitSimilarityTransform` (`RANSACRegistration.cs:550`) — already covered by transform tests.
- **Seed the registry from a frame union (or symmetric scheme).** In `MatchStarsUsingKdTree`,
  allow stars matched across frames but absent from the reference to be added to the global
  registry, improving coverage. Guard against double-counting via the existing one-to-one
  bipartite constraint (`matchedGlobalStars` / `matchedSourceStars`, `:836`).
- **Strengthen the triangle descriptor** (if matching still proves fragile after the above): match
  triangles in a proper invariant feature space (sorted side ratios) via a KdTree with a tolerance,
  replacing the cosine-similarity scan and the `0.999999` thresholds.

### Phase 4 — Model / optimizer quality

- **Single-solve, linear curvature.** Reparameterize curvature as one signed coefficient
  `k = sign(c)·c²` so `curvature = k·(x'²+y'²)`, `k ∈ ℝ`. Update `Value`, `Gradient`, bounds, and
  initial guess in `SensorParaboloidSolver` and remove the positive/negative double solve in
  `FitParaboloidModel` (`SensorModel.cs:196–206`). Eliminates the discontinuity and halves
  optimizer work.
- **Reparameterize tilt as linear gradients.** Replace `(theta, phi)` with `(gx, gy)` where
  `tilt = gx·x' + gy·y'`; derive display `theta = atan(hypot(gx,gy))`, `phi = atan2(gy,gx)` in
  `SensorModelAberrationResult`. Removes the `theta ≥ 1e-5` hack (`SensorParaboloidModel.cs:286`)
  and the local-minimum trap, and makes most of the model linear (cleaner Jacobian, more
  repeatable convergence).
- **Adopt χ² (weighted least squares) end-to-end with real uncertainties.** This is the
  statistically correct replacement for R² as the *acceptance* metric, following the
  `PSFModel.ReducedChiSquared` precedent (`StarDetection/PSFModel.cs:74`):
  - **Per-star hyperbolic fit:** assign each HFR point a σ estimated from the star's SNR (instead
    of the current zero error at `SensorModel.cs:576`). This simultaneously makes
    `WeightedHyperbolicFitEnabled` meaningful (finding #12) and lets the per-star gate use a real
    reduced-χ² / p-value instead of the hard `R² ≥ 0.90` (`:608`).
  - **Paraboloid fit:** propagate each star's best-focus-position uncertainty from its hyperbolic
    fit (standard error of the hyperbola minimum, from the LM covariance) into the
    `SensorParaboloidDataPoint`, and have `NonLinearLeastSquaresSolver` weight by `1/σ²`. Add a
    `ReducedChiSquared` (and p-value) alongside the existing `GoodnessOfFit`/`RMSErrorMicrons` in
    `SensorParaboloidModel.EvaluateFit`, and use reduced-χ² ≈ 1 (within a band) as the acceptance
    criterion in `IsBetterFit` / the brightness loop — replacing `IsFitTooGood` (R²>0.995) and the
    wall-clock target. Keep reporting R² in the UI as an intuitive secondary number.
  - Keep the robust MAD/winsorization outlier step regardless (χ² assumes ~Gaussian errors, which
    holds for high-SNR stars but not faint ones).
- **(Optional) Astigmatic curvature** `kx·x'² + ky·y'²` behind an option, for saddle-shaped fields.

### Phase 5 — Performance

- **Lazy triplet sampling** in `EstimateAffineTransform`/`EstimateSimilarityTransform`: draw
  random index combinations on demand with the seeded RNG instead of enumerating all C(n,3)
  (`RANSACRegistration.cs:489`).
- **De-dup triangles via a hash set** keyed on ordered point identities instead of the O(n)
  `SameTriangle` scan (`:319`).
- Re-measure the brightness retry loop after Phase 1; with stateless start + similarity transform
  it should converge in fewer iterations.

---

## Verification

- **Repeatability harness (new test).** Add a test in
  `Joko.NINA.Plugins.HocusFocus.Tests` that runs `SensorModel.UpdateModel` (or the
  `RegisterStarsAndFit` path) twice on a fixed synthetic star set spanning several focuser
  positions and asserts the resulting model parameters (`Theta/Phi` or `gx/gy`, `C/k`, `Z0`,
  `StarsInModel`, `GoodnessOfFit`) are bit-for-bit (or within 1e-9) identical. This is the direct
  proof of the user's robustness goal and should fail on `develop` before Phase 1.
- **Transform tests.** Extend `RANSACRegistrationTests` to confirm `EstimateSimilarityTransform`
  recovers a known translation+rotation+scale from noisy correspondences, and that a fixed seed
  yields identical inlier sets across repeated calls.
- **`Volume`/mean-elevation test** as described in Phase 2.
- **Solver tests.** Add a test fitting a synthetic tilted paraboloid (known `gx, gy, k, z0`) and
  asserting recovery within tolerance, plus a flat-field (`k≈0`) case to prove the single-solve
  reparameterization handles zero curvature without the old double solve.
- **χ² tests.** Verify reduced-χ² ≈ 1 when synthetic points are perturbed by Gaussian noise of the
  declared σ, that it scales correctly when σ is mis-stated (×2 → ÷4), and that the new acceptance
  criterion is *invariant to overall signal magnitude* (a near-flat sensor with good residuals
  accepts, where R² would have rejected it) — the magnitude-independence that motivated the change.
- **Full suite:** `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` after
  every phase.
- **Manual smoke (optional):** run the Aberration Inspector on a real/saved AF run twice and
  confirm identical reported tilt/curvature and a stable `RegistrationAndFitReport`.
