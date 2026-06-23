# Plan: raise star-detection recall via candidate formation (B-phase-2)

## Context

The golden-set audit (`docs/star-detection-golden-audit-cwhite-results.md`) found that HocusFocus detects only
~19% of real (SNR≥12) stars at default settings, and **79% of all real stars never form a candidate**
(`NO CANDIDATE` — the wavelet structure-detection stage). The candidate-formation sweep proved the cause and
the lever: lowering **`NoiseClippingMultiplier`** (the structure-map binarize threshold) from 4 to ~2.0–2.5
roughly doubles–triples recall@SNR≥12 (0.19→0.46–0.36) at small precision cost (~0.85→0.81), collapsing
`NO CANDIDATE`. `StructureLayers`/`MinBox` are not useful levers. The optimizer leaves NC at 4.0 because its
objective rewards labeled-recall/star-count balanced against σ_focus, not recovery of unlabeled real stars.

This plan decides and implements a production change. It is **gated on a cost check**: more (fainter) stars can
add HFR scatter that hurts the AF curve / per-region tilt fit, so we must confirm recall gains don't degrade
focus accuracy before changing any default.

## Step 1 — Cost analysis (the gate; do this first, no production change)

Measure, across the AF bank (not just cwhite — at least mufti + a couple others, both near-focus and
defocused), at `NoiseClippingMultiplier ∈ {4.0 (default), 2.5, 2.0, 1.5}`:
- **Recall** vs the SNR+QA golden (already have the harness: `TestApp golden eval --noise-clip <v>`).
- **σ_focus / R² / reduced-χ²** of the AF curve fit (use the optimizer/fit harness `RunEvaluationData` or
  `TestApp optimize`/`af-fit`) — does looser candidate formation tighten or loosen the focus fit?
- **Per-region HFR scatter** (std of accepted-star HFR within each region) — the direct "noise added" signal.
  Add this to `golden eval`'s per-region report (it already has accepted stars + HFR).

Decision criterion: pick the largest recall gain whose σ_focus does not worsen and whose per-region HFR scatter
stays acceptable. Expectation from the audit: NC≈2.0–2.5 is the sweet spot.

## Step 2 — Production change (choose based on Step 1)

Options, smallest-first:
1. **Re-default `NoiseClippingMultiplier`** (and document) if Step 1 shows a clean win at a fixed value. Touches
   `StarDetectionOptions.ResetDefaults` + `BuildDefaultStarDetectorParams` (keep them in lockstep — there is a
   test asserting this) + the options UI default. Smallest, safest.
2. **Fix the optimizer objective to pursue recall** so per-train tuning finds the right NC automatically: reweight
   `OptimizationObjective` to reward recovery of real stars (e.g., an SNR-reference / richer label term), or
   add a candidate-formation-aware term, so the search drives the binarize threshold down. The knobs are already
   in `OptimizerVariable.CreateCuratedSet`; the objective is what must change.
3. **Add a complementary per-pixel-SNR candidate path** (like `tools/golden/snr_ref.py`) in
   `StarDetector` so faint compact stars the wavelet residual erases still form candidates. Largest change,
   biggest potential recall, most validation; only if Steps 1–2 leave a meaningful gap.

For defocused/donut frames, ensure the recommendation pairs NC with the existing defocus-aware gates (the audit
showed both are needed: NC forms donut candidates, defocus-aware gates stop them being rejected as `TooDistorted`).

## Step 3 — Tests + validation

- Unit tests for any new option/default (lockstep `ResetDefaults` ↔ `BuildDefaultStarDetectorParams`; objective
  changes get focused tests). Full suite green.
- Re-run `golden eval` across the bank to confirm the recall gain holds and precision/σ_focus are acceptable.
- Re-run the AF-fit/optimizer harness across the bank to confirm no σ_focus / AF-accuracy regression.
- Sanity-check the saturated-star case (mufti frame): precision around bright/spiked stars must stay high (HF
  already handles this well — don't regress it).

## Critical files
- `Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetector.cs` (structure stage / candidate formation; optional
  per-pixel-SNR path), `HocusFocusStarDetection.cs` (`BuildStarDetectorParams`/`BuildDefaultStarDetectorParams`),
  `StarDetectionOptions.cs` (`ResetDefaults`, UI default), `Optimization/OptimizationObjective.cs` (objective),
  `TestApp/GoldenEvalRunner.cs` (add per-region HFR-scatter to the report).
- Harness: `tools/golden/` (reference + QA), `docs/star-detection-golden-audit-cwhite-results.md` (findings).

## Verification
`dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` green; `golden eval` recall up at
unchanged precision; optimizer/af-fit harness shows no σ_focus regression across the bank; mufti saturated-star
precision unchanged.
