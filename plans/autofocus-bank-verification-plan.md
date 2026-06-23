# Plan: repeatable optimization / autofocus / sensor-modeling verification across the AF bank

## Context

We lowered the default `NoiseClippingMultiplier` 4→2 (recall fix from the golden-set audit) and need a robust,
**repeatable, regression-comparable** verification of optimization, autofocus, and sensor-modeling performance
across a whole bank of saved AF runs — not just the single cwhite run. The output is a timestamped report that
future runs can be diffed against, so detector/optimizer changes can be regression-checked over time.

Bank root: **`D:\Autofocus Bank`** (memory: attempt01-anchored runs; some have Panos labels). Runs are
discovered with the existing `OptimizationRunDiscovery` (attempt-anchored, ≥3 focuser positions). Reuse the
golden method in `.claude/docs/golden-star-set.md` and the harnesses in `tools/golden/` + `TestApp`.

This is a **build-the-harness-then-run** plan; it is sizable (golden generation across the bank is the costly
part, but goldens are cached as sidecars so it is one-time per run).

## Step 1 — Bank cleanup (idempotent, careful)

For each discovered run folder, **keep**: the AF sweep frame images (`0Y_FrameXX_..._Focuser####.fits/.xisf`),
the `autofocus_report_Region*.json` (run config + region geometry — needed for per-region scoring and step
size), any existing `labels/`, and our generated `*.golden.json` / `run_meta.json`. **Delete** stale clutter:
`*_star_detection_result*.json`, annotated/diagnostic PNGs (`*annotated*.png`, contamination/optimize/eval
outputs), and stray harness output dirs. Implement as a dry-run-first cleanup (`--apply` to actually delete),
logging every removal, so it is safe and repeatable. (Note: the spec says "keep only the run images"; we retain
the small `autofocus_report_Region*.json` because per-region scoring + step size need them — call this out so it
can be tightened if undesired.)

## Step 2 — Golden set per image (established approach, cached)

For every frame in every run, produce the detector-independent reference and store it as the per-image
`<image>.golden.json` sidecar (so it is never recomputed):
1. `tools/golden/snr_ref.py` — SNR/connected-component reference (+ `--donut` matched filter + saturation
   masking for defocused/spiked frames) on the linear FITS.
2. `tools/golden/qa_montage.py` + `qa_workflow.js` — LLM montage QA (confirm/reject candidates).
3. `tools/golden/build_goldens.py` — write QA-confirmed candidates with SNR confidence tiers.
Driver: a bank-walker that runs this per run, skipping runs that already have complete sidecars (idempotent).
Cost note: log per-run candidate/QA counts; this is the expensive one-time step.

## Step 3 — Per-run donut metadata (persisted, computed once)

For each run, inspect the **most-defocused frames** (min/max focuser) for donuts and decide whether
donut-aware detection should be used. Heuristic: run `snr_ref --donut` + compare donut-matched local-maxima
count / ring-eccentricity / extreme-frame HFR against thresholds (and/or a small LLM montage check on the
extreme frames). Write a persisted `run_meta.json` in the run folder:
`{ donutAware: bool, reason, extremeFocusers:[...], donutRadiiPx, detectedAtUtc }`. Subsequent verification
runs READ this file instead of recomputing. Include a `--refresh` to recompute.

## Step 4 — Verification matrix per run (two configs)

For each run, run **two configs** and collect the same metrics:
- **Config A — donut-aware OFF:** `TestApp optimize` from **default settings** (no `--donut`) → `optimized_settings.json`.
- **Config B — donut-aware ON:** `TestApp optimize --donut` (only meaningfully different for runs flagged
  `donutAware=true`; still run for all so the report is uniform).

For each config, with its optimized settings, run BOTH:
1. **Autofocus** — the AF HFR-vs-focuser curve fit (global/AF region) → **fit tightness**: σ_focus, R²,
   reduced-χ², LOO std (the optimizer/`af-fit` harness already computes these).
2. **Sensor modeling** — the multi-region tilt/sensor-curve fit (the 6-region layout) → **fit tightness**:
   per-region focus-position uncertainty + tilt-plane residual / sensor-model fit-quality metrics.

**Precision/recall** of detected stars per frame (vs the golden) is **identical for AF and sensor modeling**
(same detection), so compute it once per config via `golden eval` and report it once.

Report per run × config: per-frame precision/recall (+ overall + per-region + per-SNR-tier), AF fit tightness,
sensor-model fit tightness, and the chosen optimized knob values.

## Step 5 — Summary report (timestamped, regression-ready)

Aggregate all runs × both configs into one machine- + human-readable report (JSON + a Markdown summary),
written to a **timestamped file in the bank root** (`D:\Autofocus Bank\verification_<UTC-timestamp>.{json,md}`).
Include: per-run rows (recall@SNR≥12, precision, AF σ_focus, sensor-model residual, donutAware flag, optimized
knobs) for both configs, plus bank-level aggregates and a header recording the detector version / commit so a
future run can be diffed against this baseline for regression.

## New tooling needed (reuse first)

- **Reuse:** `OptimizationRunDiscovery`, `TestApp optimize` (`--donut`, `--inspection`, `--per-run`),
  `TestApp golden eval`, `tools/golden/*` (reference + QA + build), `TestApp af-fit`/`focus-sweep` (AF fit),
  `TestApp tilt` / `InspectorVM` sensor model.
- **Build:**
  1. `TestApp bank-clean` (Step 1, dry-run + `--apply`).
  2. A bank golden driver (Step 2) — orchestrates the python + QA workflow per run, idempotent.
  3. `TestApp bank-donut-meta` (Step 3) — donut decision + `run_meta.json`.
  4. A headless **sensor-model fit-quality** evaluator if one does not already exist (Step 4.2): drive
     `SensorModel`/`InspectorVM` region fit on a run with given settings and emit per-region fit tightness.
     (AF fit tightness already exists via `af-fit`/optimizer; confirm it can run at supplied optimized settings.)
  5. `TestApp bank-verify` (Steps 4–5) — the orchestrator producing the timestamped report.
- Add per-region **HFR-scatter** to `golden eval`'s report (started in the B-phase-2 cost gate) since it is a
  useful fit-quality covariate.

## Verification of the harness itself
- Idempotency: re-running cleanup/golden/donut-meta on an already-processed run is a no-op (sidecars/meta reused).
- Spot-check one run end-to-end (cwhite + mufti) and confirm the report numbers match the manual audit
  (recall@SNR≥12, σ_focus). Full `dotnet test` green for any new TestApp code.
- Confirm two consecutive `bank-verify` runs on an unchanged bank produce identical metrics (determinism), so
  future diffs reflect real changes only.

## Notes / decisions to confirm before executing
- Cleanup aggressiveness (keep vs delete `autofocus_report_Region*.json`) — default: keep.
- Golden cost across the full bank may be large; consider running it run-by-run and committing sidecars as they
  complete. Goldens are detector-independent so they only need regenerating if the *reference method* changes.
- "Donut-aware ON" pairs with the lowered NoiseClip default (both levers) for the defocused frames, per the
  golden-audit donut finding.
