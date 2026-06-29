# bobp_m101 Star-Detection Recall Investigation — Results

**Run:** `D:\Autofocus Bank\bobp_m101\AutoFocus_20260626_225408` (added 2026-06-26). 10-position AF sweep,
Bayered XISF, excellent AF curve (hyperbolic R²=0.9997, HFR best 1.90 @ 5682, wings 7.61 @ 5562 / 6.24 @ 5778 —
moderate defocus, not full donuts).

**Question:** the run produces a tight AF curve but few stars survive at the wings (low recall, "most rejected
due to sensitivity"). Can 1–2 optimization passes recover recall, and what is precision/recall before vs after?

## TL;DR

- The run's **stored** optimized settings pinned **two** star-shedding knobs to extremes — `BrightnessSensitivity
  = 50` (the search ceiling) and `StarClippingMultiplier = 9.5` (near ceiling). On the golden set these give
  **recall@SNR≥12 = 0.126 at precision 1.000**: 59% of the missed stars are rejected by the late
  `LowSensitivity` gate and a further 35% by the `Degenerate` guard (the high `StarClippingMultiplier` clips the
  thin rings of defocused stars — see `StarDetector.cs:116`).
- The root cause is **not** rig-specific: the standard optimizer objective is **bistable** in the
  (sensitivity, star-clip) plane. Both the star-shedding corner (sens 50 → recall 0.13) and the star-flooding
  corner (sens 0 → recall 0.87) score **J ≈ 0.999**, so which one the search lands on depends on pixel scale +
  seed, not AF quality. bobp_m101 unluckily landed on the shedding corner on its capture rig; the bobp **sibling**
  run (same imager, one night earlier) landed on the flooding corner (`BrightnessSensitivity = 0`).
- **A single fresh `optimize` pass recovers recall**: on the available profile it lands at sens 0 / starClip 0.375,
  **recall@SNR≥12 0.126 → 0.867 while σ_focus tightens 0.478 → 0.205** (more stars → better per-frame HFR → a
  *tighter* curve, the opposite of the "tight curve needs high sensitivity" intuition). But that corner over-shoots
  on precision (0.572 vs the golden). The Pareto-clean operating point is in between (see the sweep).
- **Fix (separate, Phase 2):** make the objective stop preferring the star-shedding corner so it never lands where
  bobp_m101 did, bounded so it does not crater precision. Tracked in
  `docs/optimizer-sensitivity-pinning-design.md`.

## Method

- **Golden set** (detector-independent ground truth): `bank-donut-meta` → `donutAware=false` (mild defocus);
  `export-linear` (debayer XISF → mono linear FITS); `snr_ref` (no `--donut`) + bounded LLM montage QA (chunk=4,
  35 montages, **0** rate-limit failures); auto-confirm SNR≥12. **2375 golden stars** over 10 frames, **1365** in
  the high (SNR≥12) tier. Per frame golden / high: 5562 123/17 · 5586 151/29 · 5610 154/54 · 5634 208/100 · 5658
  403/305 · 5682 495/450 · 5706 382/246 · 5730 199/93 · 5754 145/44 · 5778 115/27.
- **Scoring:** `TestApp golden eval --match centroid --match-radius 12`, reporting overall + per-SNR-tier recall,
  precision, and false-negative gate attribution.
- **Caveats.** (1) Scored on the **Default** profile (`b10b1d6d`, current sens 33.58); the capturing rig's profile
  is not on this box, so absolute pixel scale (→ the arcsec MinHFR gate) is approximate — but every config is
  scored on the *same* profile, so config-to-config comparisons are valid. (2) On the heavily-defocused wing
  frames the LLM QA confirmed ~100% of the uncertain tier, so the wing golden denominator is **generous**;
  **recall@SNR≥12 (auto-confirmed, QA-independent) is the firm headline**, and precision at the low-sensitivity
  configs is a **lower bound** (real faint stars absent from the golden count as FPs). (3) Single region (Global)
  — no corner breakdown for this run.

## Before / after matrix

`recall@high` = recall over the SNR≥12 golden tier (the firm number). σ_focus / R² from `optimize` where available.

| # | Config | sens | starClip | recall@high | recall@all | precision | σ_focus | notes |
|---|--------|------|----------|-------------|------------|-----------|---------|-------|
| a | **BEFORE** (stored optimized) | 50 | 9.5 | **0.126** | 0.072 | **1.000** | — | FN gates: LowSensitivity 1302, Degenerate 762, NoCandidate 8 |
| b | shipped defaults | 2 | 2 | 0.741 | 0.655 | 0.693 | — | all knobs default (NC 2, SL 4) |
| f | sens sweep (BEFORE knobs) | 20 | 9.5 | 0.262 | 0.150 | 1.000 | — | LowSens 1001, Degenerate 762 |
| f | " | 10 | 9.5 | 0.351 | 0.208 | 1.000 | — | LowSens 496, Degenerate 762 |
| f | " | 5 | 9.5 | 0.351 | 0.208 | 1.000 | — | LowSens 0, **Degenerate 762 (wall)** |
| f | " | 2 | 9.5 | 0.351 | 0.208 | 1.000 | — | Degenerate 762 (wall) |
| c1 | **fresh `optimize`** (1 pass) | 0 | 0.375 | **0.867** | 0.748 | 0.572 | **0.205** (←0.478) | R² 0.9998; per-frame stars 6→128 … 7→174 |
| c2 | `optimize --continue-rounds 2` (3 passes) | 0 | 0.375 | 0.871 | 0.752 | 0.566 | 0.205 | identical basin to c1 |
| d1 | `optimize --inspection` | 0 | 0.375 | 0.867 | 0.748 | 0.572 | 0.205 | **same corner** — raised knees + fit-guard don't find a moderate point |

**All three optimize variants land at the identical corner (sens 0 / starClip 0.375).** 1 vs 3 passes makes no
difference; the inspection objective (star-favoring, σ bounded to 1.5× current) reaches the *same* flooding corner,
not an interior point. So on this profile the optimizer reliably recovers recall (0.126 → 0.87) and tightens the
curve (σ 0.478 → 0.205) — but cannot, on its own, locate the precision-clean middle.

### What the numbers say

1. **Two independent star-shedding knobs, not one.** Lowering only sensitivity (sweep f, BEFORE's other knobs
   held) doubles-then-saturates recall@high at **0.351** by sens 5 — all at precision 1.000 — and then hits a hard
   wall: **Degenerate = 762**, unchanged at every sensitivity. That wall is the high `StarClippingMultiplier = 9.5`
   clipping the thin rings of defocused stars until too few pixels survive (`StarDetector.cs:116-124`,
   *"…often pushed high by the optimizer…starves the surviving-pixel count → the Degenerate guard fires"*).
   Recovering it needs a lower star-clip (defaults / c1 do this) — so the recall deficit is caused by **both**
   pinned knobs.
2. **Lowering sensitivity is "free" up to the wall** — recall@high 0.126 → 0.351 with precision staying exactly
   1.000 and (per c1) σ_focus *improving*. There is no focus cost to recovering the LowSensitivity-rejected stars.
3. **Re-optimizing already recovers recall** (c1): recall@high 0.126 → 0.867 and σ_focus 0.478 → 0.205. So the
   answer to "can 1–2 passes improve recall?" is **yes** — but the unconstrained optimum overshoots to the
   precision-loose corner (0.572 vs golden; true precision higher because the golden under-counts faint stars).
4. **The sweet spot is interior.** A moderate sensitivity (≈5–10) with a *lower* star-clip would clear both the
   LowSensitivity rejections and the Degenerate wall while keeping precision high — the operating point the
   objective fails to locate on its own (it lacks a precision signal when unlabeled, so it wanders to an extreme).

## Recommendation (per run)

A small 2-knob sweep (sensitivity × star-clip, other BEFORE knobs held) locates the precision-clean interior:

| sens | starClip | recall@high | recall@all | precision |
|------|----------|-------------|------------|-----------|
| 8 | 2 | 0.677 | 0.551 | 0.977 |
| **8** | **1** | **0.763** | **0.617** | **0.972** |
| 8 | 0.5 | 0.780 | 0.630 | 0.969 |
| 5 | 2 | 0.691 | 0.619 | 0.708 ⚠ |
| 10 | 2 | 0.644 | 0.450 | 0.998 |

**Recommended operating point: `BrightnessSensitivity = 8`, `StarClippingMultiplier = 1.0`** (keep the run's other
knobs). That is **recall@SNR≥12 0.126 → 0.763 (6×)** at **precision 0.972** — it clears the LowSensitivity gate
*and* the Degenerate wall while staying clean. Sensitivity is the precision lever (sens 5 craters to 0.71; sens 10
leaves recall on the table at 0.64); star-clip 1.0 is the recall knee (2.0 → 1.0 lifts recall@high 0.68 → 0.76 for
~0.5% precision). σ_focus is not directly emitted per fixed config, but more stars tighten the curve here (σ
0.478 → 0.205 at the extreme), so this point keeps a tight curve.

The durable fix (Phase 2, `docs/optimizer-sensitivity-pinning-design.md`) makes the optimizer reach a star-rich
operating point on its own, regardless of rig, so it never lands at the recall-starved corner again.

## Provenance

Goldens: `<frame>.xisf.golden.json` beside the frames (detector-independent, reused across all configs above).
Eval/optimize artifacts: scratchpad `bobp_recall/`. Commit: see branch `ghilios/optimizer-sensitivity-pinning`.
