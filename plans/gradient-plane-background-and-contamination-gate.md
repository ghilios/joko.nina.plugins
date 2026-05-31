# Gradient-Plane Local Background + Contamination Quality Gate

## Context

Investigation (M31, Pleiades) showed the sector-annulus contamination test flags stars sitting on a
smooth one-sided background (galaxy/nebula gradient) even with no neighbor. We added a gradient-robust
test that fits a robust plane `b0 + b1·dx + b2·dy` to the annulus, subtracts it, and flags only a
one-sided positive residual excess. The user now wants to:

1. **Remove the legacy opposite-sector-median test entirely** — no A/B, no dead code.
2. Make the gradient logic do double duty:
   - **Quality gate**: reject contaminated stars (not just flag).
   - **More accurate HFR + PSF**: use the fitted background **plane** (not a scalar median) as the
     local background for centroid/flux/HFR/PSF, removing gradient bias from every measurement.

The plane is a strict generalization of the scalar median (a flat plane == the median), so using it
universally — with a fallback to the median when the fit is unavailable (annulus too small / collinear)
— improves stars on gradients and is a no-op for stars on flat background.

## Key code flow (StarDetector.cs unless noted)

- `ComputeStarParameters`: `backgroundMedian` (median of annulus) is subtracted in centroid passes,
  flux accumulation, and the `backgroundThreshold`. Returns `StarCandidate.Background` (scalar).
- `MeasureStar` (HFR): subtracts scalar `star.Background` per sampled pixel (line ~697).
- PSF (`PSFModeler.Create`/`Solve`): feeds raw pixels; the scalar `detectedStar.Background` is the
  initial guess for the fitted background parameter `B`. PSF fits a single scalar B.
- `EvaluateStarCandidate`: rejection reasons each return null + increment a metric. Contamination is
  currently a **flag only** (`metrics.ContaminationSuspected++`, star kept).
- Options in `Interfaces/IStarDetector.cs` `StarDetectorParams`: `ContaminationSensitivity=5`,
  `CollectContaminationDiagnostics=false`.

## Design

### 1. Local background plane
- Extend `ComputeGradientContamination` to also return the fitted plane `(b0, b1, b2)` and validity.
- Add a small `LocalBackgroundPlane` (cx, cy origin + b0/b1/b2) with `double ValueAt(x, y)`; when the
  fit is unavailable, construct a **flat** plane from the annulus median (so callers are uniform).
- Carry it on `StarCandidate` and `Star`.

### 2. Use the plane everywhere the scalar median was used
- Centroid passes, flux accumulation, `backgroundThreshold`: subtract `plane.ValueAt(x,y)` per pixel.
- `Star.Background` scalar = `plane.ValueAt(center)` (keeps Saturated check etc. working).
- `MeasureStar`: subtract `plane.ValueAt(x,y)` per sampled pixel instead of the scalar.
- PSF: pre-subtract `plane.ValueAt(x,y)` from each output pixel fed to the modeler so the fit sees a
  flat background (better sigma/FWHM/eccentricity); report `PSFModel.Background` = plane-center value +
  fitted residual B. Initial B guess = 0.

### 3. Contamination as a quality gate
- New option `StarDetectorParams.RejectContaminatedStars` (+ mirror in `StarDetectionOptions`, UI
  control + tooltip per CLAUDE.md), **default ON** (decided). When on, a contaminated candidate is
  rejected (return null) and a new `StarDetectorMetrics.ContaminationRejected` counter increments;
  when off, keep the flag-only behavior (`StarContaminationSuspected`).
- Decisions: rejection gate **ON by default**; plane background used **always, with median fallback**
  when the fit is unavailable.
- Add the metric to the metrics panel in `AutoFocus/DataTemplates.xaml` (CLAUDE.md requirement).

### 4. Remove legacy
- Delete `IsContaminatedBySectors`, `BuildContaminationDiagnostics`, the `sectorPixels`/`sectorMedians`/
  `sectorCounts` machinery, and the opposite-pair fields on `ContaminationDiagnosticRecord`
  (`PairDiff/PairThreshold/PairRatio/PairSkipped/TrippingPairIndex/SectorMedians/SectorCounts/
  OppositePairSuspected`). Keep the residual/gradient fields + `ContaminationSuspected`.
- Update `StarDetectorTests.cs`: remove the `IsContaminatedBySectors` tests; add
  `ComputeGradientContamination` tests (gradient-only → clean; localized spike → flagged; edge deficit
  → clean; disabled/too-few → clean) and `LocalBackgroundPlane.ValueAt` tests.
- Strip the A/B from `ContaminationDiagnosticRunner` (no legacy to compare): drop `MaxRatio`/Pair*
  columns, `WriteAbComparison`, `contamination_annotated_ab.png`; keep the single-method CSV/summary,
  the flagged-vs-clean profile, the annotated image, and a gradient-robust-only sensitivity sweep.

## Verification
- `dotnet test` green (new + existing).
- Re-run the headless diagnostic on M31 and Pleiades; confirm flag set unchanged from the validated
  gradient-robust behavior, and that HFR/FWHM for gradient-region stars shift sensibly vs flat-region
  stars (spot check a few via CSV).
- Confirm flat-background stars' HFR is unchanged (plane ≈ median there).
</content>
