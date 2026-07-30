# Golden Star Sets — measuring star-detection recall/precision against an independent reference

Read this when you need to measure how many real stars the HocusFocus detector **misses** (recall) and how
clean its detections are (precision) on a real optical train — e.g., before trusting per-region star
measurements for tilt-adapter calibration. Tooling: `TestApp golden tiles` / `golden eval` (in the repo) plus
the reference-detector + QA scripts described below.

## Hard-won lesson: pure LLM-vision star labeling does NOT work here

The first approach was to have an LLM visually mark every star in tiled, stretched images. **It failed and the
audit must not use it.** Measured against the (correct) detector positions on these faint IR/3 s subs:

- Subagents receive each image **downscaled to a ~256px thumbnail** regardless of how large the tile is
  rendered, then *guess* coordinates and scale up — so localization is ~15–170 px and **inconsistent** tile to
  tile (some tiles fine, some 170 px off).
- The LLM **over-marks background noise** as faint stars at any stretch aggressive enough to reveal real faint
  stars, and at gentle stretches **misses obvious bright stars**.
- Net: an LLM de-novo "golden" was essentially uncorrelated with real stars (only ~5/145 detector stars had any
  golden within 25 px). Zooming/upscaling tiles, stronger models (opus), and tiered-confidence prompts did not
  fix it — it's an intake-resolution + localization-consistency limit, not a prompt problem.

**Conclusion:** use the LLM for *classification* (is this crop a star? — robust even at thumbnail res), NOT for
*localization*. Get precise positions from code.

## The method that works: independent SNR reference + LLM montage QA

0. **The LINEAR FITS the reference reads.** `TestApp export-linear --runs <bank>` writes `<frame>.linear.fits`
   beside each frame (`snr_ref.py` only parses mono FITS, so this is how XISF and **bayered** runs are covered).
   "Linear" means no MTF/auto-stretch — it does **not** mean no debayer: a bayered frame is debayered to
   luminance, because a reference computed on the Bayer MOSAIC while HocusFocus detects on luminance scores every
   mosaic-only find as an HF recall gap HF could never close. It is deliberately **not** CFA hot-pixel filtered
   (unlike the detector's own OSC path): the reference's independent blind spots are the point, and hot-pixel
   rejection is step 2's job. Sidecars written before 2026-07 were exported from the mosaic for the four bayered
   runs (`SorenVance`, `bobp`, `bobp_m101`, `timmer`) — regenerate those with `--overwrite` before rebuilding
   their goldens. See `docs/headless-detection-parity-design.md`.
1. **Independent reference detector (code, on the LINEAR FITS).** A simple local-background + per-pixel SNR +
   connected-component detector (`scratchpad`/`tools`: `snr_ref.py`). It is INDEPENDENT of HocusFocus's gates
   (no contamination/distortion/centering/sensitivity/PSF gates, no wavelet structure stage), so stars it finds
   that HF rejects are exactly HF's recall gaps. Steps: coarse-grid local background (median) + noise
   (1.4826·MAD); threshold `(img-bg) > k·σ` (k≈5); light binary-close (reconnect donut arcs); label components;
   per component emit intensity-weighted centroid, bbox, peak, **SNR = peak/σ_local**, area; filter by area.
   Validated: every HF-accepted star matches an SNR candidate within 10 px (the reference is a precise superset
   of HF), and SNR≥8 components are real (noise can't reach 8σ over area≥3).
2. **LLM montage QA (vision classification).** Build montages of small per-candidate crops, each centered on a
   candidate with a reticle (`qa_montage.py` → grid PNGs). An LLM agent per montage returns which cells hold a
   real centered star (`qa_workflow`); this strips noise/hot-pixel/saturation-fragment candidates. Classification
   is robust to the thumbnail downscale because it needs only "is there a concentrated source under the reticle",
   not coordinates. Use sonnet + low effort (cheap).
3. **Golden = QA-confirmed candidates**, each carrying its **SNR as the confidence tier** (objective:
   SNR≥12 high, 8–12 medium, 5–8 low) — better than subjective LLM tiers.
4. **Score** with `TestApp golden eval --params <current|default|optimized> --match centroid --match-radius ~12`
   (centroid match absorbs the few-px reference/HF offset). It reports recall (overall / per-region / per-SNR-tier
   / per-frame) + precision + **FN gate attribution** (`NO CANDIDATE` = structure/candidate-formation gap vs
   `REJECTED:<gate>` = a tunable late gate vs `ACCEPTED-elsewhere`).

**Donut caveat:** the per-pixel-SNR reference UNDER-counts heavily defocused donuts (their surface brightness is
spread below the per-pixel threshold), so candidate counts fall toward the focus-sweep extremes. For donut-recall
on the most-defocused frames, extend the reference with a matched filter (convolve `(img-bg)/σ` with a disk/annulus
kernel) before thresholding. Near-focus and moderately-defocused frames are reliable as-is.

## Data format — per-image `golden.json` sidecar

One golden file PER IMAGE, named `<imageFileName>.golden.json`, beside the FITS (e.g.
`05_..._Focuser2701.fits.golden.json`). One `GoldenFrame`: `imageFile`, `focuserPosition`, `stars[]` (box =
top-left `{x,y,w,h}` full-frame px + tiered `confidence`), optional `coveredTiles`, optional `method`. Same box
semantics as the review label boxes; consumed by `TestApp golden eval` (`GoldenStarSet.cs` POCOs).

## `golden tiles` (still useful)

`TestApp golden tiles --runs <run> --tile 512 --overlap 96 --upscale 2 [--black-clip -2.0 --midtone 0.25
--gamma 0.8]` renders MTF-stretched tiles for visual inspection/overlays and writes `tiles_manifest.json`. Good
for *viewing* detections/overlays; not a reliable source of de-novo star positions (see the lesson above).

## What the audit found (cwhite TiltCalibration_20260621 run)

HF has ~perfect precision but **low recall** — it detects only ~15–20% of real (SNR≥12) stars near focus, and
**~84% of the misses are `NO CANDIDATE`** (the wavelet structure-detection / candidate-formation stage never
proposes them). The optimizer's `--inspection`/`--donut` settings recover only modestly and don't touch the
structure gap — so the recall bottleneck is **candidate formation**, not the late gates. See
`docs/star-detection-golden-audit-cwhite-results.md`.
