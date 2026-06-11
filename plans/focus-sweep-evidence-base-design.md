# Focus-Sweep Evidence Base — Design Spec

## Context & goal

This is **step 1** ("Build the evidence base first") of
[`plans/star-detection-hfr-autofocus-accuracy-analysis.md`](star-detection-hfr-autofocus-accuracy-analysis.md).
That analysis confirmed several HFR/detection biases by *mechanism* (reading the code) but never
*measured their magnitude*. Before changing any production behavior (findings F1–F4), we want an
evidence base that turns "mechanism confirmed" into measured numbers.

The step has two independent deliverables:

- **Piece A — a synthetic test base**: a defocused/annular star generator plus unit tests that pin
  and quantify `MeasureStar`'s HFR behavior against known ground truth. This is where the HFR-bias
  magnitudes (F3 soft-threshold subtraction; F4 σ-mismatch noise inclusion) get measured.
- **Piece B — a `focus-sweep` diagnostic** in `TestApp`: replays a saved AutoFocus run (real data)
  through detection and reports star count, HFR, and rejection-reason histograms versus focuser
  position — the V-curve seen through the detector's eyes.

No production code in the plugin changes in this PR. We are building measurement tools and tests
only.

## Key decisions (settled during brainstorming)

1. **CI stays 100% synthetic, in-memory.** No Git LFS, no large binary assets in the repo, no CI
   data-retrieval step. This matches the existing test philosophy (all current tests synthesize
   their inputs).
2. **`focus-sweep` is a local manual diagnostic**, exactly like the existing `contamination`
   runner. `TestApp` is **not** run in CI (`.github/workflows/tests.yml` only runs the Tests
   project). The diagnostic consumes a saved AF-run folder on disk; real data never enters CI.
3. **Generator approach: parametric shapes with analytic/numerical ground truth** (chosen over a
   full physical defocus-PSF model, and over checked-in real cutouts). This cleanly separates
   *estimator bias* from *model error* and keeps ground truth tractable.

### Corrections to prior assumptions

- The analysis says "no synthetic-star `MeasureStar` test exists today." There is in fact one
  (`StarDetectorTests.MeasureStar_CircularAperture_HfrLowerThanRectangularBias`). This work
  **expands thin coverage**, it does not start from zero.
- `CLAUDE.md` says the Tests project "links shared source files directly." That is stale — the
  Tests project uses a `ProjectReference` to the plugin assembly. New generator/test code therefore
  drops into the Tests project normally and references plugin types directly.

---

## Piece A — Synthetic generator + `MeasureStar` characterization tests

Location: `Joko.NINA.Plugins.HocusFocus.Tests/Synthetic/` and `.../StarDetection/`.

### A.1 Generator API

Extend the existing synthetic-image helpers (new sibling file
`Synthetic/SyntheticDefocusedStarImage.cs`; keep `SyntheticGaussianStarImage` as-is). All
generators return an OpenCvSharp `Mat` of type `CV_32F`, matching the existing
`SyntheticGaussianStarImage.Create` contract.

Radially-symmetric profiles, evaluated by **supersampled** integration per output pixel (e.g. an
N×N sub-grid averaged) so the rendered pixel value equals the analytic mean over the pixel — this
keeps the rendered image faithful to the continuous profile the ground truth is computed from:

- `CreateDisk(width, height, cx, cy, radius, peak, background, edgeBlurSigma = 0)` — uniform filled
  disk; `edgeBlurSigma > 0` softens the rim (models a defocused refractor / filled donut).
- `CreateAnnulus(width, height, cx, cy, innerRadius, outerRadius, peak, background, edgeBlurSigma = 0)`
  — uniform annulus (donut with a dark center; models a central-obstruction defocus).
- Gaussian stars continue to come from the existing `SyntheticGaussianStarImage.Create`.

Optional, deterministic noise overlay (its own helper so it is reusable and seeded):

- `AddGaussianNoise(Mat image, double sigma, int seed)` — adds zero-mean Gaussian noise of known
  σ using a **seeded** RNG so tests are reproducible and CI never flakes.

### A.2 Ground-truth HFR

`MeasureStar` computes a flux-weighted **mean** radius over a circular aperture
(`HFR = Σ w·v·d / Σ w·v`, with `v = pixel − plane − τ`). With `τ = 0`, a flat background `B`
subtracted, a fine sampling grid, and aperture radius `R` ≥ the star's support, this discretely
approximates the continuous flux-weighted mean radius of the background-subtracted profile `I(r)`:

```
HFR_true(R) = ∫₀ᴿ I(r) r² dr  /  ∫₀ᴿ I(r) r dr
```

Closed forms used as ground truth (with `R` ≥ outer radius):

- **Uniform disk** radius `a`:  `HFR_true = 2a/3`.
- **Uniform annulus** inner `b`, outer `a`:  `HFR_true = (2/3)·(a³ − b³)/(a² − b²)`.
- **Gaussian** σ, large aperture:  `HFR_true = σ·√(π/2) ≈ 1.2533·σ`
  (finite-aperture form via the incomplete gamma function, or numerical).

For edge-blurred shapes (no clean closed form), ground truth is computed by **high-resolution
numerical integration of the same defining profile** used to render the image. A small
`GroundTruthHfr(...)` helper centralizes these so tests read declaratively.

### A.3 Tests (`StarDetection/MeasureStarBiasTests.cs`)

All assertions are tolerance-based and use seeded noise. Bias tests assert **direction + bounded
magnitude** (ranges), never exact equality, so they document the measured effect without being
brittle. Where helpful, tests emit the measured magnitude via `TestContext.WriteLine` so the
numbers are visible in CI logs even when the assertion is a loose bound.

1. **Clean unbiasedness** — for disk, Gaussian, and annulus with `τ = 0` and no noise, `MeasureStar`
   matches `HFR_true` within a small tolerance (discretization + aperture truncation only). Pins
   that the estimator is correct in the ideal case and that the supersampled generator + ground
   truth agree.
2. **F3 — soft-threshold downward bias** — fix a shape, sweep the effective threshold `τ` (`τ` is
   set inside `MeasureStar` as `StarClippingMultiplier · noiseSigma`, so the test sweeps it via the
   `noiseSigma` argument with `StarClippingMultiplier` held fixed) and, separately, the peak
   amplitude. Assert `MeasureStar` decreases monotonically as `τ/peak` grows, is always
   `≤ HFR_true`, and the bias magnitude grows with `τ/peak`. This is the F3 magnitude curve.
3. **F4 — σ-mismatch noise inflation** — add seeded Gaussian noise of known `σ_sharp`, then run
   `MeasureStar` with the `noiseSigma` argument set to an understated value (mimicking the
   smoothed-image σ used in production, ~4–5× small) versus the true `σ_sharp`. Assert HFR is
   inflated in the understated-σ case, increasingly at low SNR, and bound the magnitude. This is the
   F4 magnitude curve.
4. **Donut/annulus sanity** — annulus HFR tracks the `(2/3)(a³−b³)/(a²−b²)` relation as inner/outer
   radii vary; a filled disk of the same outer radius reads a smaller HFR than the annulus
   (mass pushed outward), confirming behavior on genuinely non-Gaussian shapes.

These four pin the current behavior **and** produce the F3/F4 magnitude evidence the analysis asked
for, all without touching production code.

---

## Piece B — `focus-sweep` diagnostic (TestApp)

New file `TestApp/FocusSweepDiagnosticRunner.cs`, mirroring `ContaminationDiagnosticRunner` so it
inherits the same proven plumbing (profile load, image load, params build, output writing).

### B.1 CLI

Router entry in `Program.Main` (alongside `fit-quality` / `contamination`):

```
TestApp focus-sweep --af-run <dir> [--profile-id <guid>] [--out <dir>]
```

- `--af-run <dir>` (required): a saved AutoFocus run folder. Parsed with the existing
  `AutoFocusEngine.LoadSavedAutoFocusAttempt` and the focuser-position filename regex
  (`IMAGE_FILE_REGEX`), so it reads the user's real saved runs as-is.
- `--profile-id <guid>` (optional): NINA profile to source detection options from; defaults to the
  active profile (same convention as the contamination runner).
- `--out <dir>` (optional): output directory; defaults under `%LOCALAPPDATA%\NINA\Logs\hf-diag\…`
  like the contamination runner.

### B.2 Pipeline (reuse)

For each `SavedAutoFocusImage` in the attempt: load the frame via the existing `LoadFloatMat`
(handles FITS/XISF/TIF), build params once via
`HocusFocusStarDetection.BuildStarDetectorParams(options)` (the single options→params source of
truth), run `StarDetector.Detect` with metrics enabled, and bucket the result by
`FocuserPosition`. No production code changes; this is pure reuse.

### B.3 Outputs (mirroring the contamination runner)

- `focus_sweep.csv` — one row per focuser position: position, accepted star count, HFR median and
  robust spread (`1.483·MAD`), and the per-reason rejection counts from `StarDetectorMetrics`
  (too-small, on-border, too-distorted, low-sensitivity, too-flat, HFR-failure, min-HFR,
  contamination, …).
- `focus_sweep_summary.txt` — run settings, frame/position counts, and the position with minimum
  median HFR (a crude detector's-eye focus estimate).
- `focus_sweep_hfr.png` — HFR-vs-focuser-position V-curve (median with MAD whiskers), drawn with the
  same OpenCV plotting approach the contamination annotated output already uses.

### B.4 Validation

Validated by a **manual local run** of `TestApp focus-sweep` against a small synthetic AF-run
folder: a handful of `.tif` frames (Piece A generators, star widening toward the sweep extremes)
written to a temp directory with the `NN_FrameNN_BitDepth…_Bayered…_Focuser…` filename convention
that `LoadSavedAutoFocusAttempt`/`IMAGE_FILE_REGEX` expects (the loader keys off the filename, so
`.tif` works and `LoadFloatMat` reads it). Confirm the CSV/PNG/summary are produced and the V-curve
is sane. The exact frame-generation mechanism (small dev helper vs. throwaway) is an implementation
detail for the plan. This needs **no real data**. If an example real saved run is provided, it is
used only as an extra manual eyeball — never required, never committed.

---

## Non-goals (YAGNI)

- No production behavior changes (F1–F14 fixes are later steps).
- No physical defocus-PSF model (diffraction, true obstruction optics) — Approach 1 only.
- No real image data in the repo or CI.
- No new automated CI test that depends on `TestApp` (it stays a manual diagnostic).
- The `focus-sweep` diagnostic is descriptive (reports numbers); it does not fit curves or judge
  focus quality.

## Branch, PR, verification

- Branch: `ghilios/focus-sweep-evidence-base` off `develop` (already created).
- Commits use the required identity (`322725+ghilios@users.noreply.github.com` for both author and
  committer).
- Before reporting done: `TestApp` builds, and the full test suite is green
  (`dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug`).
- Open a PR into `develop` at the end (never push to `develop` directly).

## Risks & mitigations

- **Flaky bias tests** → seeded RNG everywhere; assert bounded ranges and monotonic direction, not
  exact values; emit measured magnitudes to the log.
- **Generator/ground-truth disagreement** → supersample pixel rendering so the rendered image
  matches the analytic profile; the "clean unbiasedness" test is the cross-check that catches any
  mismatch.
- **TestApp drift** → it is not in CI, so a separate local build step is part of verification; the
  synthetic AF-folder smoke path keeps it exercised without real data.
