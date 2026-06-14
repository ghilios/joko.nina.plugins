# Star Detection Optimization Wizard — Design

**Status:** Implemented (branch `ghilios/star-detection-optimization-wizard`)
**Implementation plan:** `plans/star-detection-optimization-wizard-plan.md`

## Context

HocusFocus exposes ~30 star-detection options. Tuning them well for a given optical train +
camera + sky is expert work, and the payoff that matters is **autofocus reliability**:
cleaner star measurements → a tighter HFR-vs-focuser curve → a more repeatable best-focus
position. Today users guess at these knobs.

This feature adds a **wizard** that automatically optimizes the star-detection options (and
recommends an autofocus step size) for the user's setup by running or replaying autofocus
sweeps and searching for the settings that produce the most reliable focus curve. The
optimized settings are saved **separately** and toggled with a single Simple-Mode option that
only appears once the wizard has succeeded.

## Goals

- One-click wizard launch from the top of the Star Detection options page.
- Optimize over a replayed saved AF run **or** a live run; choose **N runs** (default 1);
  balance a single setting across all N when N>1.
- Recommend an autofocus step size giving ~3–4 points per side of the optimum.
- Save optimized settings separately; expose a Simple-Mode toggle (hidden until a successful
  run); conclude with a summary of every changed parameter.
- A `TestApp` dev harness to verify the optimizer on real saved runs and to capture human
  ground truth on hard frames (dim/large defocused stars).

## Non-goals

- No changes to the autofocus engine (`CurveFittingResult.Calculate` stays private; we use the
  public `AlglibHyperbolicFitting.SelectBestModel`).
- The interactive two-pass review is a **TestApp dev tool only**, not shipped in the wizard.
- PSF-fit params are not tuned (PSF modeling is off during AF).

## Locked design decisions

- **Presentation:** a real **separate modal window** via NINA's `IWindowServiceFactory` (new
  pattern — no true window exists in the plugin today; the tilt-adapter "calibration wizard"
  is an `IDockableVM` dock panel and its only popup is the WinForms folder-picker on replay).
- **Objective:** a **composite score** blending focus-position uncertainty, usable star count,
  and curve consistency (+ optional human-label recall/precision); weights are tunable.
- **Step size:** **recommend, and apply on confirm** (write the profile only on Apply).
- **Scope:** optimized settings apply to **all** star detection (AF + inspector + sequence).
- **Toggle placement:** "Use Optimized Settings" is a **Simple-Mode-only** toggle, visible
  only after a successful run. When on, the three Simple presets (Noise Level / Pixel Scale /
  Focus Range) are hidden, and switching to **Advanced Mode** reveals the optimized values in
  the normal advanced controls (they are written into the live properties).

---

## Optimization Methodology (point 6 — the deliverable for review)

### Why this is *not* a clean convex problem

The "convex non-linear optimization over several variables" intuition is the right spirit, but
honestly:

- **Mixed-integer + boolean**, not continuous. Continuous (`BrightnessSensitivity`,
  `StarClippingMultiplier`, `NoiseClippingMultiplier`, `MinHFR`, `StarPeakResponse`,
  `MaxDistortion`), integer (`StructureLayers`, `NoiseReductionRadius`,
  `MinStarBoundingBoxSize`, dilation), boolean (`HotpixelThresholdingEnabled`).
- **Noisy / non-smooth objective.** Detection *counts* stars crossing thresholds, so the
  objective is piecewise-constant with jumps as stars enter/leave the accepted set. Gradients
  are undefined; a smooth solver (alglib `minbleic`, Nelder–Mead) would chase quantization
  noise.
- **Not provably unimodal globally** (a "few bright stars" basin vs. a "many faint stars"
  basin).

What *is* exploitable: each axis is approximately unimodal over its sensible range, the
dimensionality is low, and evaluation is cheap and cacheable → a **bounded, derivative-free
coordinate / pattern search** is the right tool (coordinate descent on near-monotone axes,
with a coarse seed to escape a bad starting basin).

### Objective J(θ) — composite, maximized, ∈ [0, 1]

Per candidate θ evaluated on one run:

1. Build `StarDetectorParams` = AF-base params (`BuildStarDetectorParams`, `ModelPSF=false`,
   Region/PixelScale set — exactly what `GetStarDetectorParams(..., isAutoFocus:true)`
   produces) with θ's curated values.
2. For each saved frame run the **detection delegate** (wizard:
   `IHocusFocusStarDetection.Detect`; harness: `StarDetector.Detect(Mat)`) → `AverageHFR`,
   `HFRStdDev`, `DetectedStars` (mirrors `AutoFocusEngine.cs:631`).
3. Pool frames at the same focuser position into `ScatterErrorPoint(focuserPos, pooledHFR, 0,
   pooledStdev)` (mirrors `AutoFocusEngine.cs:734`).
4. Fit with the user's actual AF fit settings (`engine.GetOptions(savedAttempt)`):
   `AlglibHyperbolicFitting.SelectBestModel(alglibAPI, points, stepSize, weighted,
   maxOutlierRejections, rejectionConfidence, out bestFit, out _)` →
   `σ_focus = bestFit.MinimumStdError`, `R²`, `reducedχ²`, fallback
   `ComputeLeaveOneOutBestFocusStdError(...)`.

Sub-scores (each ∈ [0,1], higher better):

- **S_focus:** `ρ = σ_focus/stepSize`; `S_focus = 1/(1 + (ρ/ρ_ref)²)`, `ρ_ref=0.25`. NaN
  σ_focus → fall back to LOO std; both NaN → **hard fail** `J_run=0`.
- **S_stars:** `n_min` = min star count over frames (curve weakest at defocused extremes),
  `n_med` = median; `S_stars = 0.6·clamp(n_min/N_floor) + 0.4·clamp(n_med/N_target)`,
  `N_floor=8`, `N_target=20`.
- **S_fit:** `clamp(R²)·penalty(reducedχ²)`; `penalty=1` for `reducedχ² ≤ τ`, decays above
  (reducedχ² runs ≪1 for star-rich fields, so penalize only the high side).

Composite (unlabeled frame): `J_run = w_f·S_focus + w_s·S_stars + w_c·S_fit`, defaults
**w_f=0.55, w_s=0.20, w_c=0.25** (one named constants block). Hard-constraint violation
(NaN focus, or `n_min < N_hard=3` on more than k frames) forces `J_run=0`.

### Optional human-label terms — recall / precision

When a frame has labels from the interactive harness, add a fourth sub-score (turns the count
proxy into measured precision/recall against ground truth — decisive for dim/large defocused
stars):

- **Recall** = fraction of labeled **missed** stars now covered by an accepted star within
  `R` px. **Precision** = fraction of labeled **should-reject** stars now excluded.
- `S_label = 0.5·recall + 0.5·precision`; labeled-frame composite
  `J_run = w_f·S_focus + w_s·S_stars + w_c·S_fit + w_l·S_label`, weights rescaled to sum to 1
  (default `w_l=0.25`). `R` defaults to ~max(2 px, star-size-derived), stored with labels.
  Recall is weighted (not a hard fail) to stay feasible; a strong-penalty mode is available.

### Multi-run balancing (point 5)

`J_total = (1−β)·mean_r(J_run,r) + β·min_r(J_run,r)`, `β=0.5`. The `min` term enforces balance
— θ must be good on average **and** not bad on any single run.

### Search algorithm (derivative-free, bounded, deterministic)

- Variables: curated set, each `{type, lower, upper, initialStep}`.
- **Seed at today's settings** θ0 and record `J(θ0)` — never silently regress below today.
- **Phase A — coarse seed:** 3–5-level grid over the 2 highest-impact axes
  (`BrightnessSensitivity` × `StarClippingMultiplier`), others at θ0; take the best.
- **Phase B — compass/pattern search:** ±step per variable (integer steps for ints, flip for
  bools), accept best improving move; on a no-improvement sweep, halve continuous steps until
  the step floor.
- **Caching:** memoize detection by `(frameId, StarDetector.ComputeCacheKey(θParams))`.
- **Determinism:** detection + `SelectBestModel` are deterministic; fix evaluation order; cap
  total evals (~400) with progress + cancel.

### Curated variables and exclusions

**Optimized:** `BrightnessSensitivity`, `StarClippingMultiplier`, `NoiseClippingMultiplier`,
`StarPeakResponse`, `MaxDistortion`, `MinHFR`, `StarCenterTolerance` (continuous);
`StructureLayers`, `NoiseReductionRadius`, `MinStarBoundingBoxSize` (integer);
`HotpixelThresholdingEnabled` (+ `HotpixelThreshold`). Bounds from the validation ranges in
`StarDetectionOptions.cs`.

**Excluded (reason):** all PSF params (PSF off during AF — can't be tuned from AF data, follow
Simple-mode defaults); `SaturationThreshold` + contamination (safety / separate concern);
debug/intermediate-save flags, parallelism knobs, `PixelSampleSize` (not quality knobs).

**Transfer caveat:** AF frames are defocused, so params (esp. `StructureLayers`) could skew
large for sharp in-focus stars. Mitigation: the objective spans the whole sweep (incl.
near-focus frames), biasing toward settings that work in both regimes; the summary surfaces
every change and the toggle makes it reversible.

### Step-size recommendation (point 7)

Derived (not part of θ). From the winning fit, estimate the focus-sensitive half-width `W`
(offset from the minimum where modeled HFR ≈ 2× the minimum HFR); then
`recommendedStepSize ≈ round(W/3.5)` (~3–4 points/side), `recommendedOffsetSteps ≈ 4`,
clamped to focuser limits. **Limitation:** a replay can't re-sample at a new spacing — this is
a recommendation for future runs; written to `profile.FocuserSettings.AutoFocusStepSize` /
`AutoFocusInitialOffsetSteps` only on Apply.

---

## Persistence & Toggle (point 2)

Mental model: in **Simple Mode** the live advanced properties are *driven* by a single source
— normally the 3 presets via `ConfigureSimpleSettings()`; "Use Optimized Settings" makes that
source the **optimized snapshot** instead.

- Snapshot stored separately as one JSON string key in `StarDetectionOptions` (never
  overwrites the presets). `HasOptimizedSettings` is derived = "a snapshot exists".
- `UseOptimizedSettings` (persisted bool, default false) added to `SimplePropertyNames`. In
  `ConfigureSimpleSettings()` (runs only in Simple Mode), **before** preset derivation:
  `if (UseOptimizedSettings && HasOptimizedSettings) { ApplyOptimizedSnapshotToLiveProperties(); return; }`.
  Off → existing preset derivation runs.
- Because optimized values land in the **live** properties, `BuildStarDetectorParams` needs
  **no overlay**; Advanced Mode shows them in normal controls.
- UI: toggle shown only when `!UseAdvanced && HasOptimizedSettings`; when on, the 3 preset rows
  collapse. `ApplyOptimizedSettings(snapshot)` stores it, sets `UseAdvanced=false` +
  `UseOptimizedSettings=true`, re-runs `ConfigureSimpleSettings()`. `ResetDefaults` clears it.

---

## Verification harness (`TestApp`) — two tools

TestApp already loads the real NINA profile, builds params via `BuildStarDetectorParams`, and
has headless subcommands + a WPF GUI path. Reuse `DiagnosticUtil.LoadFloatMat`,
`ProfileService.TryLoad`, `StarDetector.Detect`, and the MTF stretch
(`CvImageUtility.CreateMTFLookup`/`ApplyLUT`). Accepted stars from `StarList`; rejected
candidates from `StarDetectorMetrics.*Bounds` (populated every run — no flag needed).

### A. Headless `optimize`

`TestApp optimize --runs <folder> [--profile-id] [--out] [--max-evals] [--annotate
extremes|all] [--labels <dir>]`. Loads each run's frames as float `Mat`s, drives the **same**
`StarDetectionOptimizer` (single + N>1 balanced), asserts valid stars on every frame incl.
defocused extremes (`n_min ≥ N_hard`). Outputs: `optimize_summary.txt` (baseline vs optimized
`J`, `σ_focus`, recommended step size, param old→new, per-frame star-count table),
`optimize_result.csv`, and a **stretched annotated PNG per frame** (accepted = green + HFR;
rejected = color-coded by reason from `*Bounds`; **a real star with no marker = missed
entirely**).

> **As implemented:** run discovery is **attempt-anchored** — it finds `attempt<NN>` folders
> (recursively, ≤4 deep) with ≥3 distinct focuser positions and skips the single-frame
> `final`/`initial` validation captures (pure logic in `OptimizationRunDiscovery`). A
> **`--per-run`** flag optimizes each discovered run independently into its own subfolder plus a
> top-level `aggregate_summary.txt`, for verifying across many *different* optical setups in one
> command (the default joint objective only applies within a single setup). Scoring deliberately
> uses the **full accepted-star set** (NumberOfAFStars=0, no brightest-N trim) — both for
> objective stability and so the same detection is useful for whole-frame sensor modeling.

### B. Interactive two-pass `review` (dev tool only)

`TestApp review --runs <folder> [--labels <dir>] [--params optimized|current] [--review
low|uncertain|all]`. New `Program.cs` branch shows `StarReview/StarReviewWindow` (+ VM). Frames
are queued only when accepted count `< N_review` **or** the optimizer flags uncertainty.
MTF-stretched, zoom/pan display with a `Canvas` overlay (accepted + rejected-by-reason).
Two passes (mode toggle; click→pixel via `e.GetPosition(canvas) ÷ scale`): **Mark Missed**
(false negatives) and **Mark Should-Reject** (false positives). Labels persist to JSON
(`runId` + focuser position, missed/should-reject points, `R`), reloaded incrementally, and
consumed by `optimize --labels <dir>` to activate the recall/precision term. Loop:
`optimize` → `review --labels L` → `optimize --labels L`.

### Dim/large/out-of-focus star recall

Long-focal-length rigs often miss faint, bloated donut stars *entirely* (not even a structure
candidate) — a structure-detection gap the count proxy barely penalizes. A user-marked missed
star activates the recall term, pressuring the structure axes (`StructureLayers`,
`NoiseReductionRadius`, `NoiseClippingMultiplier`, `MinStarBoundingBoxSize`) to recover it. If
settings alone can't, the labels prove it's detector-algorithm work (e.g. multi-scale /
large-structure handling) — the point where the author may ask for help.
