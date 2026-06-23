# Golden reference + QA tools (star-detection recall/precision audit)

Independent reference-detector + LLM-QA pipeline for auditing how many real stars HocusFocus misses. See
`.claude/docs/golden-star-set.md` for the full method and the "pure LLM vision doesn't work" lesson. Requires a
Python env with `numpy` + `scipy` + `Pillow` (no astropy — `snr_ref.py` reads FITS directly).

Pipeline:

1. **`snr_ref.py <frame.fits> <k> <out.json>`** — independent SNR/connected-component star detector on the
   linear FITS (local background + `(img-bg)>k·σ` + connected components → centroid/bbox/peak/SNR/area). It is
   independent of HocusFocus's gates, so candidates it finds that HF rejects are HF's recall gaps. `k≈5`.
   *Caveat:* per-pixel SNR under-counts heavily-defocused donuts; add a matched filter (disk/annulus convolution)
   for donut-recall on the most defocused frames.

2. **`qa_montage.py <frame.fits> <candidates.json> <out_dir>`** — builds 6×6 montages of stretched per-candidate
   crops (each centered, reticle-marked, indexed) for LLM vision QA.

3. **`qa_workflow.js`** — a Claude Code `Workflow` script: one agent per montage classifies which cells hold a
   real centered star (robust classification, not localization). Edit its `CFG.frames` for your run. Returns the
   confirmed candidate indices per frame.

4. **`build_goldens.py`** — writes per-image `<image>.golden.json` sidecars = QA-confirmed candidates, with the
   SNR value mapped to a `confidence` tier (≥12 high, 8–12 medium, 5–8 low).

Then score with `TestApp golden eval --runs <run> --params <current|default|optimized> --match centroid
--match-radius 12` (recall/precision overall + per-region + per-SNR-tier + false-negative gate attribution).

These were authored for the cwhite `TiltCalibration_20260621_214304` audit; paths inside are examples — adjust
for other runs.
