# Star-detection recall/precision audit — cwhite tilt-calibration run

**Run audited:** `D:\Tilt Calibration Bank\cwhite\TiltCalibration_20260621_214304\01_Baseline\AutoFocus_20260621_214312\attempt01`
(ZWO ASI6200MM Pro, 9576×6388 mono, IR filter, 3 s subs; 9-frame focus sweep, focuser 2613→2789 step 22,
best focus ≈ 2713). Goal: find where the HocusFocus detector **misses real stars** (recall), to judge whether
per-region star yield is robust enough for tilt-adapter calibration, and to identify how to improve it.

## Method (and why the first method was abandoned)

Ground truth must be **independent of HocusFocus**. Two approaches were tried:

1. **Pure LLM-vision labeling (ABANDONED).** Having an LLM mark every star in tiled stretched images is
   unreliable on this faint data: subagents receive each tile **downscaled to a ~256px thumbnail** (they
   self-report scaling coordinates up from ~256px), giving ~15–170px localization that is **inconsistent**
   frame-to-frame, and the LLM **over-marks background noise** while **missing obvious bright stars**. Validated
   failure: an LLM de-novo golden was essentially uncorrelated with the detector's (correct) stars — only ~5 of
   145 detector stars had any golden mark within 25px, and no zoom/model/prompt fixed it. Lesson: use the LLM for
   **classification** (is this crop a star?), never **localization**.

2. **Independent SNR reference + LLM montage QA (USED).**
   - **Reference detector** (`tools/golden/snr_ref.py`): local-background + per-pixel SNR + connected components
     on the LINEAR FITS. Independent of HF's gates (no contamination/distortion/centering/sensitivity/PSF gates,
     no wavelet structure stage), so its finds that HF rejects are HF's recall gaps. Validated: **every** HF
     star matches an SNR candidate within 10px (the reference is a precise superset of HF), and SNR≥8/area≥3
     components cannot arise from noise.
   - **LLM montage QA** (`tools/golden/qa_montage.py` + `qa_workflow.js`): per-candidate crops in labelled
     grids; an LLM confirms/rejects each cell as a real centered star (robust classification). QA confirmed
     **~79%** of SNR≥5 candidates as real (uniform across SNR tiers) — the rest are hot-pixel/saturation/edge
     artifacts removed from the golden.
   - **Golden** = QA-confirmed candidates, with **SNR as an objective confidence tier** (≥12 high, 8–12 medium,
     5–8 low), stored per image as `<image>.golden.json`. Scored with `TestApp golden eval --match centroid
     --match-radius 12`.

**Caveat:** the per-pixel-SNR reference under-counts heavily-defocused **donuts** (low surface brightness spread
below the per-pixel threshold), so reference counts fall toward the sweep extremes (447 @ 2613, 399 @ 2789 vs
1485 @ 2701). Near-focus and mid-defocus frames are reliable; extreme-defocus donut recall needs a matched-filter
reference (future work). The QA flagged most extreme-frame candidates as donuts, consistent with this.

## Reference (golden) size per frame — QA-confirmed real stars

| Focuser | Δ from best | SNR candidates | golden (QA-confirmed) | high (SNR≥12) |
|---|---|---|---|---|
| 2613 | −100 (defocus) | 530 | 447 | 94 |
| 2635 | −78 | 683 | 590 | 157 |
| 2657 | −56 | 904 | 653 | 220 |
| 2679 | −34 | 1653 | 1327 | 581 |
| 2701 | −12 (near best) | 1883 | 1485 | 659 |
| 2723 | +10 | 1561 | 1335 | 559 |
| 2745 | +32 | 1263 | 1102 | 477 |
| 2767 | +54 | 810 | 537 | 185 |
| 2789 | +76 (defocus) | 532 | 399 | 100 |
| **total** | | | **7875** | |

## Results — settings matrix vs the golden (all 9 frames, golden = 7875 real / 3032 SNR≥12)

| Settings | HF stars (TP+FP) | recall@SNR≥12 | recall@all | precision | vs default |
|---|---|---|---|---|---|
| **O3 `--inspection`** | 970 | **0.266** (806/3032) | 0.102 | 0.83 | **+40%** |
| Mdef manual defocus-aware on default | 911 | 0.254 | 0.098 | 0.84 | +34% |
| O6 `--inspection --donut --continue-rounds 2` | 876 | 0.246 | 0.095 | 0.85 | +30% |
| O5 `--inspection --donut` | 860 | 0.241 | 0.093 | 0.85 | +28% |
| **M1 default (as-run baseline)** | 675 | 0.189 (573/3032) | 0.073 | 0.85 | — |
| O7 `--inspection --donut --start-from-current --continue-rounds 2` | 578 | 0.163 | 0.063 | 0.85 | −14% |
| **O1 standard `optimize`** | 256 | 0.072 | 0.028 | 0.85 | **−62%** |
| User's live profile (Sens≈34) | ~150 | ~0.05 | — | 1.0 | far worse |

Two striking results: the **standard optimizer objective (O1) HALVES recall vs default** (it minimizes σ_focus /
favors a tight clean set, shedding stars), while **`--inspection` (O3) is the best at recall@SNR≥12 = 0.266**, a
**+40% relative** gain over default — but still **missing ~73% of bright real stars**. `--donut`/multi-round/
`--start-from-current` did not beat plain `--inspection` on this train. Precision stays ~0.85 throughout (against
the QA-cleaned golden; raw-reference precision is ~1.0 — HF essentially never accepts a non-star).

### Per-region recall (aggregated over frames) — the corners (tilt leverage) are most starved

| Region | default recall@all | O3 recall@all |
|---|---|---|
| Global | 0.073 | 0.102 |
| Center | 0.072 | 0.100 |
| Corner TL | 0.082 | 0.134 |
| Corner TR | 0.052 | 0.083 |
| Corner BL | 0.061 | 0.100 |
| Corner BR | 0.054 | 0.065 |

The corner ROIs — which give the tilt-plane fit its leverage — have the **lowest** recall (default ~0.05–0.08).
`--inspection` improves them but they remain low (~0.07–0.13).

### The decisive finding: candidate formation is a hard ceiling

False-negative attribution is **identical** in the `NO CANDIDATE` bucket between default and O3:

| Miss reason | default | O3 inspection |
|---|---|---|
| **NO CANDIDATE (structure-detection gap)** | **6191** | **6191** (unchanged) |
| REJECTED: TooSmall | 563 | 194 |
| REJECTED: TooDistorted | 258 | 24 |
| REJECTED: NotCentered | 263 | 570 |
| REJECTED: Contaminated | 22 | 52 |

**6191 of 7875 real stars (79%) never form a candidate at all** — and that number does not move when the
optimizer changes the late gates. O3's recall gain comes entirely from relaxing late gates (TooSmall 563→194,
TooDistorted 258→24), i.e., rescuing stars from the small rejected-candidate pool (~1100). The 6191 NO-CANDIDATE
stars are unreachable by any late-gate tuning — only candidate-formation changes can recover them.

**Single-frame anchor (f2701, near best focus, golden = 1485 real / 659 SNR≥12):**

| Settings | HF stars | precision | recall@SNR≥12 | recall@all | dominant miss mode |
|---|---|---|---|---|---|
| Default (as-run) | 145 | ~1.0* | 0.178 (117/659) | 0.079 | NO CANDIDATE 1151/1368 |
| Optimized +inspection (O3) | 170 | ~1.0* | 0.203 | 0.090 | NO CANDIDATE 1151 |
| Optimized +inspection+donut (O5) | 160 | ~1.0* | 0.196 | 0.087 | NO CANDIDATE 1120 |
| User's live profile (Sens≈34) | 28 | 1.0 | 0.014 | — | (extremely strict) |

\* Raw-reference precision is 1.0 (all 145 HF stars are SNR sources); against the QA-cleaned golden it reads
~0.8 because the QA over-rejects ~20% of real stars, not because HF has false positives.

## Key findings

1. **HocusFocus has excellent precision but poor recall on this train.** It detects only ~19% of real (SNR≥12)
   stars at the as-run default (573 / 3032 bright; 675 / 7875 total across the sweep), at precision ~0.85–1.0.
2. **The recall bottleneck is candidate formation, not the late gates — and it is a hard ceiling.** 79% of all
   real stars (6191 / 7875) are **`NO CANDIDATE`**: HF's wavelet structure-detection stage never proposes them.
   This count is **identical** under default and the best optimizer settings — the optimizer (which tunes late
   gates) cannot move it. Late-gate rejections are a much smaller pool (~1100: TooSmall, TooDistorted,
   NotCentered, Contaminated).
3. **`--inspection` is the best settings lever (+40% recall@high, 0.189→0.266) but standard `optimize` HURTS
   recall (−62%).** The default optimizer objective favors a tight low-σ set and sheds stars (O1: 256 vs 675);
   `--inspection` instead rewards star count, recovering stars from the rejected-candidate pool (TooSmall 563→194,
   TooDistorted 258→24). Neither touches the `NO CANDIDATE` ceiling. `--donut`/multi-round/`--start-from-current`
   did not beat plain `--inspection` here.
4. **The corner ROIs that drive tilt are the most recall-starved** (default ~0.05–0.08; `--inspection` ~0.07–0.13)
   — so per-region star yield for the sensor/tilt fit is weakest exactly where it matters most. With default
   settings the corners get only ~20–25 stars each across the sweep; `--inspection` ~30–40. Substantially higher,
   robust per-region yield requires improving candidate formation, not more late-gate tuning.

## Recommendations to improve the detector

- **Make candidate formation more sensitive (the actual recall lever).** The wavelet/à-trous structure stage
  with `StructureLayers=4` + `NoiseClippingMultiplier=4` is the gate that produces the `NO CANDIDATE` misses.
  Options: lower the structure-map binarize threshold (smaller `NoiseClippingMultiplier`), add/boost structure
  layers, reduce `MinimumStarBoundingBoxSize`, and/or add a complementary per-pixel-SNR candidate path (like the
  reference) so faint compact stars that the wavelet residual erases still form candidates. These should be
  swept on this run and validated against the golden.
- **Add the structure-stage knobs to the optimizer's search.** Today the optimizer mostly tunes late gates, so
  it cannot recover `NO CANDIDATE` stars; include candidate-formation params (`NoiseClippingMultiplier`,
  `StructureLayers`, structure-noise threshold) so it can actually move recall.
- **For robust tilt calibration now:** prefer the `--inspection` optimized settings (modest recall gain at
  unchanged precision) and ensure enough stars per corner ROI; if corners are starved, the candidate-formation
  changes above are required.
- **Defocus/donut recall** could not be fully quantified here (reference under-counts donuts); add a
  matched-filter reference pass to audit whether the defocus-aware/donut settings recover the extreme-defocus
  rings.

## Tooling produced (reusable)

- `TestApp golden tiles` / `golden eval` (+ `GoldenStarSet.cs`, `GoldenGeometry.cs`, unit tests) — render tiles
  and score detection vs per-image `golden.json` (recall/precision overall + per-region + per-SNR-tier + FN gate
  attribution; centroid/IoU/center match modes).
- `tools/golden/` — `snr_ref.py` (independent reference detector), `qa_montage.py` + `qa_workflow.js` (LLM
  montage QA), `build_goldens.py` (assemble per-image goldens). Method documented in
  `.claude/docs/golden-star-set.md`.
