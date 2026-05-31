# Fix Contamination Over-Flagging: Local Annulus Scatter Instead of Global Noise

## Context

The sector-annulus contamination test (`StarDetector.IsContaminatedBySectors`) flags ~**87%** of stars on
the `uneven` tilt sample — visually fine stars included — making the `ContaminationSuspected` metric and the
"Show Contaminated" overlay useless.

The headless diagnostic tool (`TestApp contamination --image ...`, added in commit `65cb5b8`) was used to
root-cause this **with data, not eyeballing**. Findings from `contamination_stars.csv` on
`11_Frame00_BitDepth16_Bayered0_Focuser56457.xisf` (2287 stars, 1983 flagged):

| Signal | Value | Reading |
|---|---|---|
| Sector pixel counts | ~30 each, **0 pairs skipped** | annulus binning is healthy; not a small-sample artifact |
| HFR median: flagged vs clean | **2.625 vs 2.605** | flagging is independent of star size — not bloated stars leaking flux |
| Tripping threshold | ~**1.26·σ** | with sensitivity 4 the bar sits at ~1.3σ |
| Actual tripping \|diff\| | ~**2.07·σ** | real opposite-sector median differences are ~2σ |
| Intra-star sector-median spread | median **2.96·σ**, p90 **4.44·σ** | annulus medians genuinely scatter far more than σ/√n predicts |

**Root cause:** the `noiseSigma` fed to the SE model is the **global structure-map K-σ estimate**
(`CvImageUtility.KappaSigmaNoiseEstimate`, ~5.2e-5 normalized). The SE model
`1.2533·σ·√(1/n_s+1/n_opp)` is the standard error of a sector median *assuming the sector pixels scatter
with σ*. The correct σ is the **scatter of those annulus pixels**, not the global image-noise floor. Near
bright stars and in gradients/nebulosity the local background roughness is several × the global noise, so
genuine ~2σ opposite-sector differences blow past a bar built on a too-small σ.

## Fix

Replace the global `noiseSigma` used by the contamination decision with a **per-star local background
scatter** estimated robustly from the annulus pixels themselves. Keep `IsContaminatedBySectors` pure and
unchanged — only the **σ the caller passes in** changes — so the existing 6 unit tests still hold.

Robust estimator (MAD, breakdown point 50%): one contaminated sector is only 1/8 (12.5%) of the annulus, so
a one-sided neighbor cannot inflate the MAD enough to hide itself, while smooth gradients/nebulosity (which
affect all sectors) are correctly absorbed into the bar. This is the minimal, well-motivated change the
diagnostic isolates.

### Changes

1. **`StarDetection/StarDetector.cs` — `ComputeStarParameters`**
   - The unsafe annulus walk already fills `surroundingPixels[0..surroundingPixelCount]` and sorts them to
     get `backgroundMedian`. After that sort, compute a robust local scatter:
     `localSigma = 1.4826 * MedianAbsoluteDeviation(surroundingPixels, backgroundMedian)`.
     (Reuse the existing sorted array; MAD = median of |x − median|. There is a `MedianMAD` helper in
     `Utility/MathUtility.cs` — prefer it if it operates on the needed type, else inline the MAD on the
     already-sorted `float[]`.)
   - Guard: if `surroundingPixelCount` is too small or `localSigma <= 0`, fall back to the global
     `noiseSigma` (pass-through current behavior).
   - Pass `localSigma` (not `noiseSigma`) into the existing
     `IsContaminatedBySectors(sectorMedians, sectorCounts, localSigma, p.ContaminationSensitivity, MinSectorPixels)`
     call, and into `BuildContaminationDiagnostics(...)` so the CSV's `NoiseSigma` column reflects the σ
     actually used. (Keep the global `noiseSigma` available for the fallback and for any other consumers.)

2. **Re-tune the default `ContaminationSensitivity`**
   - With local σ the natural default changes. Use the tool's `--sensitivity-sweep` to pick a value where
     clearly-clean stars are not flagged and genuine close-neighbor / hot-column cases still are. Update the
     default in three places that currently say `4.0`: `StarDetectorParams.ContaminationSensitivity`
     (`Interfaces/IStarDetector.cs`), `StarDetectionOptions` (`InitializeOptions` + `ResetDefaults`), and the
     option tooltip if it cites a number.

3. **Tests — `Tests/StarDetection/StarDetectorTests.cs`**
   - Keep the 6 pure `IsContaminatedBySectors_*` tests (signature unchanged).
   - Add a `ComputeStarParameters`-level (or a small extracted-helper) test proving the local-σ behavior: a
     noisy annulus with one-sided scatter *within* local roughness → **not** flagged; a clean annulus with a
     single bright sector well above local roughness → **flagged**. If `ComputeStarParameters` is hard to
     unit-test directly, extract the `localSigma` MAD computation into an `internal static` helper and test
     that plus the existing pure decision function together.

### Optional second pass (only if step 1 is insufficient on the sweep)
- Increase annulus standoff (`BackgroundBoxExpansion`, currently 3) or add a dedicated outer ring so the
  annulus catches fewer stellar wings — wings are the other plausible asymmetry source for off-axis
  (coma/tilt) stars in this dataset.
- Or subtract a fitted planar background gradient across the annulus before sector comparison.
- Decide based on whether residual false positives correlate with field position (corners) in the CSV.

## Verification

1. **Build/tests:** `cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo"` → all pass.
2. **Diagnostic re-run (the whole point of the tool):**
   `TestApp.exe contamination --image "C:\Workshop Data\Data\autofocus\uneven\final\11_Frame00_BitDepth16_Bayered0_Focuser56457.xisf" --out "C:\temp\hf-diag-fix"`
   → confirm flagged% drops from 86.7% to a small, plausible fraction; in
   `contamination_stars.csv` the `MaxRatio` median should fall near/below 1.0 for clean stars.
3. **Sweep to set the default:**
   `... --sensitivity-sweep 2,8,0.5` → inspect `sweep.csv`; pick the default where the flag rate stabilizes
   at the genuinely-contaminated population. Bake that into the three default sites above.
4. **Visual confirmation:** open `contamination_annotated.png` — magenta (flagged) markers should now sit
   only on stars with a real close neighbor / hot column, not on ordinary corner stars.
5. **In NINA (optional):** enable "Show Contaminated", run detection on the same image, confirm the overlay
   matches the tool and the **Contamination Sensitivity** slider still responds (0 disables).

## Notes
- `IsContaminatedBySectors` stays byte-for-byte pure — the fix is entirely in *which σ the caller supplies*.
- Run the headless tool before/after every tuning change rather than launching NINA; it reproduces the exact
  production decision (uses `BuildStarDetectorParams` + real profile) and emits per-star reasoning.
