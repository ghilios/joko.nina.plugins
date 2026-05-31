# Contamination Detection: Sector-Annulus Asymmetry

## Context

The contamination flag (`Star.StarContaminationSuspected`, surfaced as the `ContaminationSuspected`
metric and the "Show Contaminated" star-annotator overlay added earlier) is driven by
`StarDetector.CheckBackgroundContamination`, which compares the annulus-median background, the PSF-fit
background, and a derived clipping threshold. That test is broken:

1. Its second check `|annulusBg − thresholdBg| > 2σ` is a tautology — `thresholdBg = annulusBg +
   StarClippingMultiplier·σ`, so it reduces to `StarClippingMultiplier > 2` and is independent of the star.
2. Its first check `|annulusBg − psfBg| > 2σ` compares two *aggregate* estimates against a *per-pixel* σ
   tolerance (too tight by ~√N) and is biased by the systematic offset between a PSF-fit background and an
   annulus median (Moffat wings). Result: a vast majority of stars get flagged, so the metric is useless.

This replaces the detection with a **sector-annulus asymmetry** test that actually indicates a one-sided
neighbor, hot column, or steep local gradient, with a user-tunable sensitivity. Contaminated stars remain
**kept** (flag only) and inspectable via the existing annotator overlay (already implemented — unchanged).

Decisions (confirmed with user): sector-annulus asymmetry approach; expose a tunable sensitivity option
with UI.

## Algorithm

Split the existing background annulus into **8 angular sectors** about the star center and compare
**opposing sectors** (4 pairs). A neighbor/hot column brightens one side only; an *elongated* star (tilt/
coma) brightens two opposite sectors symmetrically and must NOT be flagged — comparing opposite sectors
(not each sector to the global median) achieves exactly that.

Pure, unit-testable decision function (new `internal static` in `StarDetection/StarDetector.cs`):

```
internal static bool IsContaminatedBySectors(double[] sectorMedians, int[] sectorCounts,
        double noiseSigma, double sensitivity, int minSectorPixels) {
    if (sensitivity <= 0 || noiseSigma <= 0) return false;        // sensitivity 0 disables
    int n = sectorMedians.Length;                                  // 8 (even)
    const double MedianSE = 1.2533;                                // sqrt(pi/2): SE of a median
    for (int s = 0; s < n / 2; ++s) {
        int opp = s + n / 2;
        if (sectorCounts[s] < minSectorPixels || sectorCounts[opp] < minSectorPixels) continue;
        double diff = Math.Abs(sectorMedians[s] - sectorMedians[opp]);
        double se = MedianSE * noiseSigma * Math.Sqrt(1.0 / sectorCounts[s] + 1.0 / sectorCounts[opp]);
        if (diff > sensitivity * se) return true;
    }
    return false;
}
```

Constants: `NumSectors = 8`, `MinSectorPixels = 8`. `sensitivity` is the σ-multiple `k`; **higher = fewer
flags**; default `4.0`; `0` disables.

## Changes

### 1. Bin the annulus into sectors and decide — `StarDetection/StarDetector.cs`
- In `ComputeStarParameters` (lines 788–847), during the existing unsafe annulus walk that fills
  `surroundingPixels`, also bin each background pixel into one of 8 sectors by its angle from the
  bounding-box center (`cx = starBounds.X + Width/2.0`, `cy = …`). Use a branch-based **octant**
  assignment from the signs/relative magnitudes of `dx,dy` (avoid per-pixel `atan2` — this runs per
  candidate). Collect into `List<float>[8]` (or fill the existing `surroundingPixels` plus a parallel
  sector-index array).
- After computing `backgroundMedian`, sort each sector list and compute `sectorMedians[8]` /
  `sectorCounts[8]`, then call `IsContaminatedBySectors(…, p.ContaminationSensitivity, noiseSigma,
  MinSectorPixels)`. Store the boolean on `StarCandidate` (add `public bool ContaminationSuspected`).
- In `EvaluateStarCandidate` (build site ~line 653), set
  `star.StarContaminationSuspected = starCandidate.ContaminationSuspected`, and increment
  `metrics.ContaminationSuspected` just before the accepted-star `return star` (~line 673) when the flag
  is set. (This now evaluates contamination for *all* accepted stars, not only PSF stars.)
- **Remove** the old `CheckBackgroundContamination` method (lines 408–426) and its call + counter in
  `ModelPSF` (~lines 371–373), leaving only `detectedStar.PSF = psf;` in the accepted branch.

### 2. New tunable option `ContaminationSensitivity`
Follow the existing numeric-option pattern (e.g. `Sensitivity`, `StarClippingMultiplier`).
- `Interfaces/IStarDetector.cs`: add `public double ContaminationSensitivity { get; set; } = 4.0;` to
  `StarDetectorParams` and include it in `ToString()`.
- `Interfaces/IStarDetectionOptions.cs`: add `double ContaminationSensitivity { get; set; }`.
- `StarDetection/StarDetectionOptions.cs`: backing field + property (persist via `SetValueDouble`),
  `InitializeOptions` (`GetValueDouble("ContaminationSensitivity", 4.0)`), `ResetDefaults` (`= 4.0`),
  setter validation `>= 0` (0 disables).
- `StarDetection/HocusFocusStarDetection.cs`: map
  `ContaminationSensitivity = starDetectionOptions.ContaminationSensitivity` in the `StarDetectorParams`
  initializer (near line 285).

### 3. Options UI — `Resources/OptionsDataTemplates.xaml`
- Add a `ContaminationSensitivity_Tooltip` `TextBlock` resource near the other star-detection tooltips
  ("How aggressively to flag stars whose background annulus is brighter on one side — a sign of a nearby
  star, hot column, or steep gradient. Value is the number of sigma of asymmetry required; higher = fewer
  flags; 0 disables.").
- Add a `ninactrl:UnitTextBox` bound to `StarDetectionOptions.ContaminationSensitivity` with a
  `DoubleRangeRule` (Min 0, Max ~20), in the star-detection options grid alongside the other PSF/detection
  numeric controls (same place the removed χ² control lived). Reuse a free grid row.

### 4. Tests — `Tests/StarDetection/StarDetectorTests.cs`
- Remove the four `CheckBackgroundContamination_*` unit tests (the method is gone).
- Add `IsContaminatedBySectors` unit tests: one-sided bright sector → true; symmetric (opposite sectors
  equally elevated, i.e. elongated-star analogue) → false; uniform → false; `sensitivity = 0` → false;
  sectors below `MinSectorPixels` skipped; larger sector counts tighten the threshold (scaling check).
- Keep the existing `ToDetectedStar_CopiesContaminationFlag` test.

The annotator overlay, the `ContaminationSuspected` metric, and the "Contaminated" results-pane row are
unchanged and automatically reflect the new detection. No `StarDetectorMetrics` field is added.

## Verification

1. **Build**: `cmd.exe /c "dotnet build Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo"` → 0 errors.
2. **Tests**: `cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo"` → all pass.
3. **Manual (NINA)** on `C:\Workshop Data\Data\autofocus\uneven\final\11_Frame00_BitDepth16_Bayered0_Focuser56457.xisf`:
   enable "Show Contaminated", run detection, and confirm that now only a small number of stars with a
   genuine close neighbor / hot column are flagged (no longer "a vast majority"), and that ordinary
   elongated stars in the corners are *not* flagged. Adjust **Contamination Sensitivity** (lower → more
   flags, higher → fewer) and confirm the count responds; set to 0 and confirm no stars are flagged.
