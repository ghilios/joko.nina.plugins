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

---

# Re-check after the headless-detection parity fix — 2026-07-29

**Everything above was measured on a Bayer mosaic.** The Phase 1 parity fix
(`docs/headless-detection-parity-design.md`) changed what the headless detector sees for bayered runs — raw mosaic
→ CFA-hotpixel-filtered debayered luminance — and also fixed `ExportLinearRunner`, which feeds the `snr_ref`
reference. Both the detector *and* the golden it was scored against were therefore measuring a different image
than the app uses. This section re-measures the run on the corrected pipeline. It supersedes the numbers above;
the original section is kept because it is the record of what the earlier decision rested on.

## The conclusion holds, and the deficit is worse than reported

**The recall deficit was real, not an artifact of scoring a luminance detector against a mosaic reference.**
Correcting the reference *deepens* it. Holding the detector and its params fixed and varying only the golden:

| params | golden | TP | FP | precision | **recall@SNR≥12** | recall@all |
|---|---|---|---|---|---|---|
| sens 50 / clip 9.5 (row **a** reconstruction) | old (mosaic) | 247 | 0 | 1.000 | 0.181 (247/1365) | 0.104 |
| sens 50 / clip 9.5 | **new (luminance)** | 247 | 0 | 1.000 | **0.117 (247/2107)** | 0.080 |
| sens 0 / clip 0.25 (today's optimizer landing) | old (mosaic) | 1401 | 330 | 0.809 | 0.681 (930/1365) | 0.590 |
| sens 0 / clip 0.25 | **new (luminance)** | 1671 | 60 | **0.965** | 0.617 (1299/2107) | 0.543 |
| shipped defaults, NC 2 | old (mosaic) | 1415 | 56 | 0.962 | 0.720 (983/1365) | 0.596 |
| shipped defaults, NC 2 | **new (luminance)** | 1457 | 14 | **0.990** | 0.637 (1343/2107) | 0.473 |

At the shedding corner the **true positives are identical either way (247)** — the detector finds exactly the same
stars, and only the denominator moves, 1365 → 2107. That is the cleanest possible demonstration that the golden,
not the detector, changed: recall@SNR≥12 falls **0.181 → 0.117**.

The mechanism is the one Task 1 measured: the CFA checkerboard inflated `snr_ref`'s block-MAD σ by 3.0–3.25×
(17.8–19.3 ADU vs 5.93), so the mosaic reference was ~3× *less* sensitive. It was a strict **subset** of the new
one — 1365/1365 of its high-tier finds reproduce, with **zero** mosaic-only finds — not a set polluted with
mosaic artifacts. So there were never any phantom recall gaps to remove.

**Two consistent second-order effects**, visible across all three param sets:

- **recall@SNR≥12 always falls** with the corrected golden (0.181→0.117, 0.681→0.617, 0.720→0.637): the added
  reference stars are fainter than anything the mosaic reference could resolve, and HF misses them.
- **precision always rises** (1.000→1.000, 0.809→0.965, 0.962→0.990; FPs 330→60 and 56→14). Much of what the
  mosaic reference scored as HF false positives were **real stars it was too insensitive to see**. The old
  precision figures for star-rich configs were biased low.

## The golden set is also stricter, not just larger

| | old (mosaic) | new (luminance) |
|---|---|---|
| `snr_ref` candidates | 2,427 | 4,410 |
| high tier, auto-confirmed (SNR≥12) | 1,365 | **2,107** (+54%) |
| uncertain candidates | 1,062 | 2,303 |
| uncertain QA-confirmed | 1,010 (**95.1%**) | 972 (**42.2%**) |
| **golden total** | **2,375** | **3,079** |

Per-frame golden (old → new): 5562 123→157 · 5586 151→187 · 5610 154→233 · 5634 208→308 · 5658 403→444 ·
5682 495→583 · 5706 382→479 · 5730 199→303 · 5754 145→225 · 5778 115→160.

**Caveat (2) of the original Method section is now measured and largely retired.** That caveat warned the wing
golden denominator was "generous" because the LLM QA confirmed ~100% of the uncertain tier there. On the corrected
frames the QA is far more selective, and its confirm rate tracks **defocus, not SNR**:

| focuser | 5682 (best focus) | 5658 | 5706 | 5778 | 5562 | 5586 | 5754 | 5634 | 5730 | 5610 |
|---|---|---|---|---|---|---|---|---|---|---|
| uncertain confirm rate | **1.7%** | 12.0% | 28.5% | 47.9% | 54.9% | 63.3% | 59.3% | 59.2% | 60.5% | **69.1%** |

At best focus every real star is already above SNR 12, so the uncertain tier there is genuinely noise and is
rejected almost entirely; at defocus real light spreads out and drops below SNR 12, so the uncertain tier there
holds real stars. By SNR tier alone the confirm rate is non-monotone (47.9% at 10–12, 33.0% at 8–10, 40.1% at
6.5–8, 52.3% at 5–6.5), which is a composition artifact of that split — the faintest bucket is dominated by
defocused frames. Coverage was complete: all 2,303 uncertain candidates were rendered and QA'd (67 montages,
chunk=4, **0** rate-limit failures), so the new golden is not budget-truncated.

## The star-shedding characterisation survives, but "94% from two knobs" does not

FN gate attribution at the shedding corner, same detector, both goldens:

| gate | old golden | new golden |
|---|---|---|
| `LowSensitivity` | 1491 | 1236 |
| `Degenerate` | 164 | 595 |
| NO CANDIDATE (structure gap) | 197 | 446 |
| `TooSmall` | 187 | 311 |
| `TooDistorted` | 71 | 228 |
| other (Contaminated/OnBorder/TooFlat/accepted-elsewhere) | 18 | 16 |
| **total FN** | **2128** | **2832** |

`LowSensitivity` + `Degenerate` still dominate and the two-pinned-knob story stands. But they now account for
**1831/2832 = 65%** of the misses, not the **94%** the original section claimed. The corrected reference adds
fainter stars that fail *earlier* in the pipeline — the structure gap (446) and `TooSmall` (311) are now
material, and were previously invisible because the mosaic reference could not see those stars at all.

Note the two goldens are not nested outside the high tier (their uncertain tiers come from independent `snr_ref`
runs and independent QA), so the per-gate deltas mix a denominator change with a composition change and should
not be read as a per-star migration.

## Does `Wtie = 0.02` still look like the right call? Yes — the evidence is stronger

Per the plan, no objective constant was changed in this branch. On the evidence:

- The **motivation is stronger.** The shedding corner is worse than believed: recall@SNR≥12 0.117, not 0.126.
- The **counter-argument is weaker.** The bistability table in `docs/optimizer-sensitivity-pinning-design.md`
  justifies caution about the flooding corner with "precision 0.572". On the corrected pipeline the star-rich
  corner scores **precision 0.965** at recall@≥12 0.617. It is not a precision disaster.
- So the star-rich corner now **dominates** the shedding corner on both axes (0.617 @ 0.965 vs 0.117 @ 1.000),
  and a tie-breaker that pushes the optimizer away from shedding is more clearly correct, not less.
- Consistent with this, a fresh `optimize` on the corrected representation lands at **sens 0 / clip 0.25** — the
  star-rich corner — and `--params optimized` reproduces the config-F row above exactly.

**Caveat on the precision comparison:** row (c)'s 0.572 was measured at sens 0 / **clip 0.375** on the mosaic, and
the 0.965 here is at sens 0 / **clip 0.25** on luminance. They are not the same operating point, so treat
0.572 → 0.965 as "the flooding corner is far cleaner than recorded", not as a like-for-like delta. The direction
is unambiguous — both the detector fix and the golden fix push precision the same way.

Also unchanged: the evidence that **shipped** `Wtie = 0.02` never rested on this run. Per
`docs/optimizer-sensitivity-pinning-design.md`, the real-data demonstration is **`muggsie`**, a **mono** run that
Phase 1 provably does not touch. bobp_m101 supplied the motivating narrative, and that narrative survives.

## Method (this section)

- Goldens rebuilt per `generating-af-golden-data`: `bank-donut-meta --refresh` re-confirmed `donutAware=false` on
  the new representation (donut peak fraction rose 11.5 → 39.0, but extreme-frame HFR 6.9 < 9.0 and donut bbox
  14 px < 24 px keep it below the gate), so `snr_ref` ran without `--donut`, as before.
- `golden_prep --budget-montages 20` (not the usual 4) so the **entire** uncertain tier was QA'd, matching the
  original run's effectively-full coverage; anything less would have truncated the new golden's faint tier and
  made the old-vs-new comparison unfair.
- Scoring: `TestApp golden eval --runs "D:\Autofocus Bank\bobp_m101\AutoFocus_20260626_225408" --profile-id
  b10b1d6d-80eb-4dc4-9d89-f1fbf64c2b34 --params default --sensitivity <s> --star-clip <c> --match centroid
  --match-radius 12`, with `--golden <dir>` pointed at the archived mosaic goldens for the "old" rows. Same
  profile caveat as the original section (caveat 1) applies unchanged.
- Row (a) is a **reconstruction**: the run's `optimized_settings.json` was overwritten on 2026-07-29T05:24Z by a
  fresh optimize on the corrected pipeline, so the original stored param set is no longer on disk. Only its
  `sensitivity = 50` / `starClip = 9.5` are recorded (above), and they are reapplied here on top of `--params
  default`. Its other knobs match the current file exactly (NC 4, PeakResponse 0.75, MaxDistortion 0.5,
  StarCenterTol 0.3, StructureLayers 4, MinHFR 1.2, MinBox 5), so the reconstruction is close but not certified
  identical — which is why the reconstructed old-golden number is 0.181 rather than the originally reported 0.126.
  **The old-vs-new comparison does not depend on this**, because both rows use the same reconstructed params.
- Harness note: `GoldenEvalRunner.cs` prints a hardcoded `match: center-in-box OR IoU` header regardless of
  `--match`. The mode *is* applied (verified: `--match centroid --match-radius 0` → TP=0; radius 12 → TP=26);
  only the label was wrong. Fixed in this branch.

**Provenance.** Old mosaic goldens archived at `D:\Autofocus Bank\_prior_reports\bobp_m101_goldens_mosaic_prephase1\`;
old mosaic linear exports at `…\bobp_m101_linear_mosaic_prephase1\`; the new reference, QA confirmations and
goldens at `…\bobp_m101_goldens_luminance_phase2\`. Branch `ghilios/bank-rebaseline-bayered`.
