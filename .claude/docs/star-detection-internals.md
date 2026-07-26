# Star Detection Internals

Read this when working on the star detector itself: the gradient-robust contamination test, the local background plane, or any `StarDetectorMetrics` field.

**Invariant:** every new field added to `StarDetectorMetrics` (rejection counts, flags, etc.) **must** also be displayed in the star detection metrics panel in `AutoFocus/DataTemplates.xaml` (see "Star Detection Metrics UI Requirement" below).

## Gradient-Robust Contamination Test + Local Background Plane

The star detector's contamination test is **gradient-robust** (`StarDetector.ComputeGradientContamination`):
for each star it fits a robust plane `b0 + b1·dx + b2·dy` to the background-annulus pixels via IRLS (Huber)
to model a smooth one-sided background (galaxy/nebula gradient), subtracts it, then flags a star **only** when
a single octant shows a one-sided **positive** residual excess above `ContaminationSensitivity` sigma — a
contaminant adds light, so this ignores both smooth gradients (removed by the fit) and edge-clip deficits
(negative). The fitted plane (`LocalBackgroundPlane`, carried on `Star.BackgroundPlane`) doubles as the
**local background** used per-pixel for centroid, flux, HFR, and PSF, so a gradient no longer biases any
measurement; for flat fields the plane equals the annulus median (no change). The PSF fit has the gradient
*tilt* removed before fitting (zero at the star center, so the amplitude and fitted background `B` are
unaffected) for cleaner sigma/FWHM/eccentricity.

- **Quality gate**: `StarDetectorParams.RejectContaminatedStars` (option `StarDetectionOptions.
  RejectContaminatedStars`, **default ON**) rejects contaminated stars (metric `ContaminationRejected`);
  when off they are kept and only flagged (`Star.StarContaminationSuspected`, metric `ContaminationSuspected`).
- The legacy opposite-sector-median test was removed (it tripped on smooth gradients). Validated on M31 +
  Pleiades: the gradient-robust test drops smooth-gradient/edge false positives and recovers real faint
  companions that an opposing gradient had masked.

For the headless TestApp diagnostic that exercises this test, see `testapp-cli.md`.

## Software Detection Binning

`StarDetectorParams.DetectionBinning` (int, 1 = off) resamples the frame at the top of
`BuildDetectionContextInternal`, so **the whole pipeline runs in binned pixels** and every pixel-unit knob
stays in its calibrated range (in-focus HFR ~2-4 px) regardless of the rig's pixel scale. Non-obvious rules:

- **It is an EARLY param** (`EarlyCacheKeyProperties`) — it changes candidate formation, so an early context
  can never be reused across factors.
- **Hotpixel filtering is hoisted above the resample** (native resolution). A hot pixel averaged into its
  block is no longer the isolated outlier the filter looks for.
- **Everything pixel-valued is scaled back to SOURCE pixels** at the end of `GateAndMeasureInternal`, BEFORE
  the ROI offset (the ROI is cropped at native resolution, so its offset is already in source pixels):
  `Star.ScaleToSourcePixels`, `StarDetectorMetrics.ScaleBounds`, `PSFModel.ScaledToSourcePixels`. Centers
  pick up a `(b-1)/2` half-block shift; intensities are unchanged (mean binning preserves level);
  `PSF.FWHMArcsecs` is NOT rescaled because `StarDetectorParams.PixelScale` already carries the factor.
  `HocusFocusStarDetection.SourcePixelScale` divides it back out for anything user-facing.
- `DetectionBinningEnum.Auto` is resolved in exactly one place —
  `HocusFocusStarDetection.ApplyDetectionImageContext`, against the captured frame's pixel scale — by
  `Utility/DetectionBinningResolver`, the single source of truth for the rule.
- **Reported HFR drifts with the factor** (+5% at 2x, +16% at 3x on the capstone frames): higher per-pixel
  SNR admits more outer flux into the measurement. Harmless for best-focus, but HFR is not comparable across
  factors — don't "fix" a test that observes this.

## Star Detection Metrics UI Requirement

Every new field added to `StarDetectorMetrics` (rejection counts, flags, etc.) **must** also be displayed in the star detection metrics panel in `AutoFocus/DataTemplates.xaml`. The metrics panel uses a `UniformGrid Columns="2"` with `StackPanel` pairs. Add new entries using the `HF_ZeroToDoubleDashConverter` pattern:

```xaml
<StackPanel Orientation="Horizontal">
    <TextBlock Width="120" VerticalAlignment="Center" Text="My Metric" />
    <TextBlock Width="70" HorizontalAlignment="Center" VerticalAlignment="Center"
        Text="{Binding Metrics.MyMetricField, Converter={StaticResource HF_ZeroToDoubleDashConverter}}" />
</StackPanel>
```
