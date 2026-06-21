# TestApp CLI — Headless Diagnostics & Optimizer Harnesses

Read this when you need to run TestApp to diagnose or tune the star detector / optimizer **without launching NINA** — the `contamination`, `optimize`, `review`, and `diagnose-labels` subcommands. For the contamination algorithm itself, see `star-detection-internals.md`.

All subcommands load the user's real NINA profile and build params through `HocusFocusStarDetection.BuildStarDetectorParams` (the single options→params source of truth). They are **read-only with respect to the profile/options** (they never call a settings setter or touch the options accessor — NINA auto-saves the active profile, so mutating options would silently rewrite the user's settings).

Build first:
```bash
cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"
```

## Contamination diagnostic

`TestApp` doubles as a self-contained, headless diagnostic for the contamination test. It runs detection with per-star diagnostics enabled and `RejectContaminatedStars=false` (so contaminated stars are retained for analysis).

**Run it** (WSL interop runs the Windows `.exe` directly, so paths with spaces quote cleanly):

```bash
./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe \
  contamination --image "C:\path\to\image.xisf" --out "C:\temp\hf-diag"
```

- Args: `--image <path>` (req; `.xisf`/`.fits`/`.tif`), `--profile-id <guid>` (default: active profile),
  `--out <dir>` (default `%LOCALAPPDATA%\NINA\Logs\hf-diag\<timestamp>`), `--sensitivity <double>` (override),
  `--sensitivity-sweep <a,b,step>` (per-value CSVs → `sweep.csv`).
- No `--image`/`contamination` arg ⇒ TestApp launches its normal WPF GUI instead.

**Outputs** (in `--out`): `contamination_stars.csv` (one row per accepted star — center, HFR, background
(plane value at center), σ used, `ContaminationSuspected`, gradient-robust fields `GradientSlope`/
`LocalSigmaResidual`/`MaxSectorResidualOverSE`/`ResidualTrippingSector`, per-octant `resid*`/`residCount*`,
and per-star shape/proximity `Eccentricity`/`FWHMx`/`FWHMy`/`FWHMPixels`/`ThetaDeg`/`NearestNeighborDist`/
`NearestNeighborOverHfr`/`HasCloseNeighbor`/`PsfFitOk`); `contamination_summary.txt` (settings + flag rate +
a flagged-vs-clean profile + `MaxSectorResidualOverSE` distribution); `contamination_annotated.png` (green =
clean, magenta = flagged-with-close-neighbor, cyan = flagged-isolated); `gr_sweep.csv` (flag rate + flagged
set's median gradient slope/eccentricity vs sensitivity, from a single run); plus verbose TRACE in
`%LOCALAPPDATA%\NINA\Logs`.

**How the diagnostics hook works (off by default, zero overhead):** set
`StarDetectorParams.CollectContaminationDiagnostics = true` and read
`HocusFocusStarDetectorResult.ContaminationDiagnostics` (a `List<ContaminationDiagnosticRecord>`). When the
flag is false the detector fills no per-sector residual arrays and skips the diagnostic record (the plane fit
+ decision always run, since they are the production background/contamination path).

## Star Detection Optimizer harnesses (`optimize` / `review` / `diagnose-labels`)

The Star Detection Optimization Wizard ships with `TestApp` subcommands that drive the **same**
`StarDetectionOptimizer` the live wizard uses, so the optimizer can be exercised/tuned offline. They load the
user's real NINA profile and, mirroring the wizard, build two param bundles (each with the AF overrides
`ModelPSF=false`, `Region=Full`, `SaveIntermediateFilesPath=""`, `PixelScale` from the profile × binning=1 for raw
Mats): the optimizer **seed** = fully-default params (`BuildDefaultStarDetectorParams`, the wizard's
`LoadedRun.Seed`), and the **baseline** = the user's current settings (`BuildStarDetectorParams`, the wizard's
`LoadedRun.Baseline`). The search starts from the default seed; improvement (J, σ_focus, curated-param deltas, the
`optimized_settings.json` `BaselineJ`) is reported **vs the current-settings baseline** — exactly the wizard's
"vs current" display.

> **Perf:** detection is split into a cacheable EARLY context (`BuildDetectionContext`) + a cheap LATE
> `GateAndMeasure`; `RunEvaluationData` caches the early context per (frame, early-key) and reuses it across
> late-only candidate moves, plus bounded parallel per-frame detection. **Bit-identical, ~10–13× faster**, and
> it benefits BOTH the live wizard and replay (both converge on `RunEvaluationLoader` → `RunEvaluationData` →
> `HocusFocusSplitFrameDetector`). See `docs/star-detection-optimizer-performance-design.md`.

### `TestApp.exe optimize`

Headless driver of the optimizer. Args:
`--runs <folder>` (required), `--per-run` (flag), `--profile-id <guid>` (default active), `--out <dir>`
(default `%LOCALAPPDATA%\NINA\Logs\hf-diag\optimize\<timestamp>`), `--max-evals <int>` (override the
optimizer budget; wizard default 250), `--annotate extremes|all` (default `extremes` = min/max-focuser frames
only), `--labels <dir>` (label JSON dir; activates the recall/precision objective term), `--verbose` (restore
TRACE logging; default INFO). `optimize` has **no** `--defocus-*` switches — the combined `DefocusAwareGates`
flag is in the optimizer's curated search set (`OptimizerVariable.CreateCuratedSet`), so the optimizer explores
the relaxation itself (guarded by the objective's `SDefocusPrecision` near-focus penalty); to force the gates
on for diagnosis, use `diagnose-labels`/`contamination`. It writes
`optimized_settings.json` into **each focus run's source folder** (the review handoff) **and** the `--out`
dir (or each per-run subfolder).

- **Run discovery is attempt-anchored** (pure logic in `OptimizationRunDiscovery`): it recursively finds
  `attempt<NN>` folders (1–4 levels under `--runs`) that contain ≥3 distinct focuser positions, mirroring
  `RunEvaluationData.MinPositionsForFit = 3`. Single-frame `final`/`initial` validation folders and frameless
  `attempt` folders are skipped (recorded with a reason, not silently dropped). Back-compat: pointing `--runs`
  directly at an `attempt01` folder works. Fallback: if no `attempt*` folders exist anywhere, each immediate
  subfolder with ≥3 positions is a run. Frame filenames match `0_Frame1_BitDepth16_Bayered0_Focuser5000.fits`.
- **Default = joint** optimization: all discovered runs are optimized together (N=1 reduces to a single run;
  N>1 is the balanced blend). **Only group runs from the SAME optical setup** — a joint objective across
  different cameras/scopes is meaningless. **`--per-run`** optimizes each discovered run independently, writing
  one subfolder per run plus a top-level `aggregate_summary.txt` (one scannable row per run: load OK/failed,
  hard-floor PASS/FAIL with min star count, seed→best J, σ_focus, recommended step, changed params). Use
  `--per-run` to verify across a bank of many different setups in one command.
- Detection deliberately scores the **full accepted-star set** (NumberOfAFStars=0 — no brightest-N trim),
  matching the wizard's `RunEvaluationLoader` (HFR aggregation at the `HocusFocusDetectionParams` defaults,
  high=4.0 / low=3.0; this is the loader path, NOT the live AF path). The whole-frame detection also keeps the
  harness useful for sensor-modeling work.
- Outputs (in `--out`, or per-run subfolders): `optimize_summary.txt` (seed→optimized `J`, per-run σ_focus /
  R² / reducedχ², recommended step size, curated params old→new with `*` markers, hard-floor check, per-frame
  star counts), `optimize_result.csv`, `optimize_trajectory.csv` (bestJ vs eval# — one row per accepted move; the
  convergence curve for eval-budget analysis), and stretched annotated PNG(s) (accepted = green circle + HFR; rejected
  color-coded by reason from `StarDetectorMetrics.*Bounds`; **a real star with no marker = missed entirely**);
  `--per-run` also writes `aggregate_summary.txt`. Verbose TRACE in `%LOCALAPPDATA%\NINA\Logs`.

```bash
./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe \
  optimize --runs "C:\Users\me\AppData\Local\NINA\AutoFocus" --out "C:\temp\hf-opt" --per-run
```

### `TestApp.exe review`

Interactive box-based labeling dev tool (WPF; produces labels only;
**read-only on the profile**). Args: `--runs <folder>` (required), `--labels <dir>` (default `<runs>\labels`;
read+written, feeds `optimize --labels`), `--params current|optimized` (default `current`), `--opt-results
<dir>` (folder holding `optimized_settings.json`), `--review low|uncertain|all` (default `low` = frames with
`< N_review` accepted stars; `uncertain` adds the defocused extremes; `all`), `--profile-id <guid>`.

- **`--params optimized` load priority:** (1) explicit `--opt-results <dir>/optimized_settings.json`, then (2)
  auto-discover `<runFolder>/optimized_settings.json` (where `optimize` wrote it), then (3) the profile
  snapshot `options.GetOptimizedSettings()`, else (4) fall back to `current`.
- Detects every queued frame once, shows MTF-stretched frames with accepted/rejected-by-reason overlays (colors
  match `optimize`'s PNG legend), zoom/pan, and **thick, zoom-invariant** markers. **Three box-based label
  categories:** **missed** (false negative → recall) = drag a box on a real star with no marker;
  **should-reject** (false positive → precision) = click an **accepted** box; **wrongly-rejected** (recall) =
  click a **rejected** box. Labels reload incrementally (re-running merges).
- The label JSON is consumed by `optimize --labels <dir>` to activate the objective's recall/precision term
  (recall/precision are scored by **box containment**). One file per run (`<runId>.json`, or any `*.json` whose
  embedded `runId` matches a discovered run); each labeled box carries a bounding box (legacy point-only files
  auto-load with a default box).

### `TestApp.exe diagnose-labels`

Classifies each labeled box against a fresh detection of the same frame to
find **which gate rejects** flagged stars. Per box: **ACCEPTED** (overlaps an accepted star) /
**REJECTED:<reason>** (overlaps a rejected candidate — reports the gate: TooDistorted, NotCentered, TooFlat,
LowSensitivity, Saturated, Degenerate, Contaminated) / **NO CANDIDATE** (no candidate formed there = a true
structure-detection gap, only fixable by detector-algorithm work). Args: `--runs` (required), `--labels`
(default `<runs>\labels`), `--params current|optimized`, `--opt-results <dir>` (same load priority as
`review`), `--profile-id`, `--out <dir>` (writes `diagnose_labels.txt`), plus opt-in defocus switches
(`--defocus-distortion` / `--defocus-centering` / `--defocus-size-ref` / `--defocus-min-factor`
/ `--defocus-center-factor`) that force the gates ON on the built params (`contamination` has the same set).
Read-only on the profile.

### The labeling loop

`optimize` (baseline) → `review --labels L` (drag-box misses / click false positives / click
wrongly-rejected) → `optimize --labels L` (re-optimize with the box-containment recall/precision term
active). Use `diagnose-labels` to attribute each labeled miss to a specific gate vs. a structure gap.

## Defocus-aware gates (Advanced options)

A single `StarDetectionOptions.DefocusAwareGates` option
(**opt-in, default OFF**, Advanced-only CheckBox + tooltip in `OptionsDataTemplates.xaml`) drives **both** the
distortion and centering relaxations together (it maps to the two `StarDetectorParams` fields
`DefocusAwareDistortion` + `DefocusAwareCentering` in `BuildStarDetectorParams`; TestApp keeps the two as
separate `--defocus-distortion`/`--defocus-centering` CLI flags). They relax the distortion / centering gates
for large candidates (large size = defocus proxy) to recover bloated/donut defocused stars. Default-OFF returns
the gates verbatim, keeping detection **bit-identical**. The three numeric knobs are now Advanced options too
(UnitTextBox + DoubleRangeRule + tooltip): `DefocusDistortionSizeReference` (30 px), `DefocusDistortionMinFactor`
(0.25), `DefocusCenteringToleranceFactor` (2.0). The combined gate is also a curated optimizer variable
(`DefocusAwareGates`), guarded by the objective's `SDefocusPrecision` near-focus precision penalty (multiplicative,
= 1.0 when no star is relaxation-admitted ⇒ objective bit-identical when off). See
`docs/star-detection-optimization-wizard-results.md` (F2/F3 + cache-health notes).
