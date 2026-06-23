# Plan: repeatable optimization / autofocus / sensor-modeling verification across the AF bank

## Context

We lowered the default `NoiseClippingMultiplier` 4→2 (recall fix from the golden-set audit). We now need a
robust, **repeatable, regression-comparable** verification of **optimization, autofocus, and sensor modeling**
(NOT tilt calibration) across the whole bank of saved AF runs, producing a timestamped report future runs can be
diffed against.

Bank root: **`D:\Autofocus Bank`**. Runs discovered with `OptimizationRunDiscovery` (attempt-anchored, ≥3
focuser positions). Each run is treated as its **own optical train** — optimize/evaluate **per run**, never
jointly. Camera/pixel-scale read per-run from the FITS header (the bank mixes setups).

The harness was **validated end-to-end on the cwhite run** (see `D:\Tilt Calibration Bank\cwhite\verification_*.{md,json}`);
this plan is the full-bank generalization with the dry-run learnings baked in.

## Validated harness (concrete commands)

| Output | Tool (validated) |
|---|---|
| Per-frame precision/recall vs golden (shared by AF + sensor model — report once per config) | `TestApp golden eval --match centroid --match-radius 12 [--params default \| --opt-results <dir>]` |
| Autofocus fit tightness (σ_focus, R², reduced-χ²) | `TestApp optimize` (optimized configs); `optimize --max-evals 0` for the as-default config's seed σ_focus |
| Sensor-model fit tightness (paraboloid R², RMS µm, reduced-χ², stars-in-model, tilt θ, framesAligned) | `TestApp inspect-align --opt-results <dir>` (extended this session to inject settings + report the SensorModel fit) |
| Golden reference (cached per image) | `tools/golden/`: `snr_ref.py` (SNR + `--donut` matched filter + sat masking) → `qa_montage.py` + `qa_workflow.js` (LLM QA) → `build_goldens.py` |

Small harness additions still needed for a clean full run: a `--params default` selector on `inspect-align`
(so the as-default config's sensor model runs without relying on the live profile — today it has `--opt-results`/
`--noise-clip` only), and the `bank-clean` / `bank-donut-meta` / `bank-verify` orchestrators below.

## Step 1 — Bank cleanup (idempotent, dry-run-first)

Per run folder, **keep** the AF frame images, `autofocus_report_Region*.json` (run config + region geometry +
step size), `labels/`, and our `*.golden.json` / `run_meta.json`. **Delete** stale clutter:
`*_star_detection_result*.json`, annotated/diagnostic PNGs, stray `optimize`/`eval`/`contamination` outputs and
`optimized_settings.json` handoffs (regenerated per config). `TestApp bank-clean --runs "D:\Autofocus Bank"`
lists first; `--apply` deletes, logging every removal. (cwhite had nothing stale — common for fresh runs.)

## Step 2 — Golden set per image (established approach, cached, parallelized)

For every frame in every run, write the per-image `<image>.golden.json` sidecar once (skip runs already
complete — idempotent). Pipeline: `snr_ref.py` (per-run camera params from the FITS header; `--donut`+sat-mask
on defocused runs) → montage QA → `build_goldens.py` (SNR→confidence tiers). **This is the long pole** — drive
the QA fan-out with a `Workflow` over (run × montage) so the bank's montages QA concurrently; commit sidecars as
each run completes. Goldens are detector-independent, so they only regenerate if the *reference method* changes.

## Step 3 — Per-run donut metadata (persisted; refined heuristic)

For each run, decide whether donut-aware detection is warranted and persist `run_meta.json`
(`{donutAware, reason, extremeFocusers, donutSignal, detectedAtUtc}`); later runs read it (`--refresh` to recompute).
**Refined rule (from the dry-run):** the donut/peak local-maxima fraction alone over-flags mildly-defocused runs
(cwhite frac≈1.5 but donuts were marginal). Require **both** a high donut fraction **and** heavy defocus on the
extreme frames (e.g., extreme-frame median HFR above a threshold and/or donut bbox size large) before setting
`donutAware=true`. `TestApp bank-donut-meta --runs "D:\Autofocus Bank"`.

## Step 4 — Verification matrix per run (THREE configs)

The dry-run showed the **optimizer trades recall for σ_focus** (it kept NC=2.0 but raised `BrightnessSensitivity`
~16, so optimized recall 0.15–0.18 was *below* the plain NC=2.0 default ~0.46), and that the **best config
differs by operation** (donut-aware tightened AF + raised recall, but loosened the sensor-model paraboloid fit).
So report **three** configs per run:

- **C0 — as-default** (shipped NC=2.0, no optimization): the honest recall reference. AF σ_focus via
  `optimize --max-evals 0` (seed = default); precision/recall via `golden eval --params default`; sensor model
  via `inspect-align --params default`.
- **A — optimized, donut OFF:** `optimize` → `golden eval --opt-results A` + `inspect-align --opt-results A`.
- **B — optimized, donut ON:** `optimize --donut` → `golden eval --opt-results B` + `inspect-align --opt-results B`.

Per run × config record: optimized knobs (esp. NoiseClip + Sensitivity), **precision/recall** (overall +
per-region + per-SNR-tier, computed once — identical for AF and sensor modeling), **AF fit** (σ_focus, R²,
reduced-χ²), **sensor-model fit** (paraboloid R², RMS µm, reduced-χ², stars-in-model, tilt θ, **framesAligned**;
expect a couple of extreme-defocus frames not to align). Do not pick a global winner — surface the per-operation
tradeoff.

## Step 5 — Summary report (timestamped, regression-ready)

Aggregate all runs × 3 configs into JSON + a Markdown summary, written to a **timestamped file at the bank
root** (`D:\Autofocus Bank\verification_<UTC>.{json,md}`). Header records the **detector commit/version** and the
NoiseClip default so a future run can be diffed against this baseline. Per-run rows for all three configs +
bank-level aggregates (median recall@SNR≥12, median AF σ_focus, median sensor R², donutAware count, % runs where
donut-aware helped AF vs hurt sensor fit). Mirror the cwhite report schema (`afbank-verify/1`).

## Orchestration / scale

- `TestApp bank-verify --runs "D:\Autofocus Bank" --out <bankroot>` drives Steps 4–5 per run (clean/golden/meta
  assumed done). Golden generation (Step 2) is the costly one-time stage — parallelize via `Workflow` and cache.
- Per-run, not joint (mixed optical trains). Read camera/pixel-scale per run from the FITS header so `snr_ref`
  donut radii / saturation level and detection PixelScale are correct.
- Determinism: two `bank-verify` runs on an unchanged bank + fixed goldens must produce identical metrics so
  future diffs reflect real changes only (optimizer search is seeded/deterministic; confirm).

## Verification of the harness itself
- Idempotency: re-running clean/golden/donut-meta on a processed run is a no-op (sidecars/meta reused).
- Spot-check: cwhite numbers reproduce the dry-run report; a heavy-donut run (e.g., a mufti run) flags
  `donutAware=true` and shows donut-aware helping recall/AF.
- Full `dotnet test` green for the new `--params default` on inspect-align + the bank orchestrators.

## Notes / decisions to confirm before executing
- Cleanup aggressiveness (keep vs delete `autofocus_report_Region*.json`) — default: keep (needed for region
  geometry + step size).
- Golden cost across the full bank is large; run run-by-run, commit sidecars as they complete.
- "Donut-aware ON" pairs with the lowered NoiseClip default (both levers) on genuinely defocused runs.
