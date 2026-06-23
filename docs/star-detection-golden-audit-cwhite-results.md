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

## Candidate-formation sweep (the recall lever)

Sweeping the candidate-formation knobs directly against the golden (via `golden eval --noise-clip /
--structure-layers / --min-box`, all 9 frames, vs the as-run default) isolates the fix:

| NoiseClippingMultiplier | recall@SNR≥12 | recall@all | precision | NO-CANDIDATE |
|---|---|---|---|---|
| 4.0 (default) | 0.189 | 0.073 | 0.85 | 6191 |
| 2.5 | 0.358 | 0.140 | 0.82 | 4712 |
| 2.0 | 0.459 | 0.186 | 0.81 | 3556 |
| 1.5 | **0.596** | 0.263 | 0.80 | 1757 |

- **`NoiseClippingMultiplier` (the structure-map binarize threshold) is THE recall lever.** Lowering it 4→1.5
  triples recall@SNR≥12 (0.19→0.60) at modest precision cost (0.85→0.80) and collapses `NO CANDIDATE`
  6191→1757 — i.e., the wavelet structure stage *can* form these candidates; the default 4σ binarize was just
  too strict. NC=1.5 alone beats the best optimizer combo (0.27) by 2.2×.
- `StructureLayers` (5/6) and `MinimumStarBoundingBoxSize` (3/2) **do not help** recall (StructureLayers slightly
  *raises* NO-CANDIDATE; MinBox had no effect in isolation).
- The optimizer leaves NC at 4.0 because its objective rewards labeled-recall/star-count balanced against
  σ_focus — not recovery of unlabeled real stars — so it never pushes the binarize threshold down.

## Donut / defocus validation (mufti frame, focuser 2325, heavy donuts + a saturated spiked star)

A separate heavily-defocused frame (same ASI6200MM rig) with obvious donuts and an oversaturated diffraction-
spiked star, scored against an SNR+matched-filter reference (donut detection + saturation masking):

| Setting | HF accepted | recall@SNR≥12 | NO-CANDIDATE | TooDistorted (rejected) |
|---|---|---|---|---|
| default | **7** | 0.007 | 1085 | 191 |
| defocus-aware (gates+donut+structure) | 84 | 0.114 | 896 | 214 |
| NC=2.0 alone | 21 | 0.028 | 801 | **436** |
| **NC=2.0 + defocus-aware** | **161** | **0.157** | 648 | 203 |

- **HF default detects almost nothing on a donut frame (7 stars)** — donuts are either `NO CANDIDATE` or
  rejected as `TooDistorted` (low fill-ratio rings).
- **The defocus-aware gates genuinely recover donuts** (7→84, 16×) — they relax `TooDistorted`/centering so
  rings pass. This validates that HF feature.
- **For donuts you need BOTH levers:** NC=2.0 alone forms more candidates (NO-CAND 1085→801) but they pile up
  in `TooDistorted` (191→**436**); NC + defocus-aware together gives the most recovery (161 stars, 22× default)
  at high precision (0.93).
- **Faint pure-ring donuts (no core) remain noise-limited** for everyone — HF catches the brighter cored donuts
  and misses the faint rings; a naive matched filter over-detects them. Honest extreme-defocus donut recall is
  therefore bounded by SNR, not just by the detector.
- **HF's precision around the saturated star is excellent** — it rejects the diffraction-spike/bloom fragments
  (where a naive SNR matched filter floods). HF's problem on defocused frames is purely *recall*, not spike FPs.

## Recommendations to improve the detector

- **Lower `NoiseClippingMultiplier` — this is THE recall lever (confirmed by the sweep).** The structure-map
  binarize threshold at 4σ is what produces the `NO CANDIDATE` misses. Dropping it to ~**2.0–2.5** roughly
  doubles–triples recall@SNR≥12 (0.19→0.46–0.36) at small precision cost (~0.85→0.81); 1.5 maximizes recall
  (0.60) but is the most aggressive. `StructureLayers`/`MinBox` are not useful levers. Recommended production
  setpoint: **NC≈2.0–2.5**, pending the HFR-scatter check below.
- **For defocused / donut frames, lower NC AND enable the defocus-aware gates together.** Neither alone is
  enough: NC forms the donut candidates but they're then rejected as `TooDistorted`; the defocus-aware gates
  relax that. Combined they gave 22× the donut recall of default at 0.93 precision.
- **Fix the optimizer so it actually pursues recall.** The candidate-formation knobs are already in its curated
  set, but the objective leaves `NoiseClippingMultiplier` at 4.0. Either reweight/extend the objective to reward
  recovery of real (e.g., SNR-reference) stars, or seed/penalize so the search drives the binarize threshold
  down. Today even `--inspection` (the best objective) only tunes late gates.
- **Validate the precision/HFR-accuracy cost before shipping (the gating step for a production change).** More
  (fainter) stars can add HFR scatter that hurts the AF curve / per-region tilt fit. Measure per-region HFR
  scatter and σ_focus at NC∈{2.5,2.0,1.5} vs default before changing the production default. This is the subject
  of the B-phase-2 plan (`plans/star-detection-candidate-formation-recall-plan.md`).
- **For robust tilt calibration *now*:** use NC≈2.0 (+ defocus-aware on defocused sweeps) — far more per-region
  stars than default at unchanged precision; this most helps the recall-starved corner ROIs.
- **Faint pure-ring donuts are SNR-limited**, not a tractable detector deficiency on a single sub — don't chase
  them; they carry little usable HFR signal when that defocused.

## Tooling produced (reusable)

- `TestApp golden tiles` / `golden eval` (+ `GoldenStarSet.cs`, `GoldenGeometry.cs`, unit tests) — render tiles
  and score detection vs per-image `golden.json` (recall/precision overall + per-region + per-SNR-tier + FN gate
  attribution; centroid/IoU/center match modes).
- `tools/golden/` — `snr_ref.py` (independent reference detector), `qa_montage.py` + `qa_workflow.js` (LLM
  montage QA), `build_goldens.py` (assemble per-image goldens). Method documented in
  `.claude/docs/golden-star-set.md`.
