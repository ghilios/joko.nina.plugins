# Software Binning for Star Detection — Design

## Problem

Hocus Focus has no software binning. The only binning in the codebase is NINA's hardware/driver
binning:

- `AutoFocusEngine.TakeExposure` sets `CaptureSequence.Binning` from `FilterInfo.AutoFocusBinning`
  (per-filter) or `FocuserSettings.AutoFocusBinning` (global).
- `HocusFocusStarDetection.ApplyDetectionImageContext` reads the resulting
  `MetaData.Camera.BinX` back, purely to scale `PixelScale` (arcsec/px).

Nothing ever resamples the image, which leaves long-focal-length rigs badly served. Every pixel-unit
detector knob — `MinHFR`, `MinimumStarBoundingBoxSize`, `NoiseReductionRadius`, `StructureLayers`,
`BackgroundBoxExpansion`, `StarCenterTolerance`, `DefocusDistortionSizeReference` — is calibrated for
in-focus HFR in the **2–4 px** range. At 0.2″/px an in-focus star has HFR around 7 px, so the defaults
are off by 2–3×, detection processes far more pixels than it needs to, and per-pixel SNR is worse than
it has to be.

The current mitigation is the Simple-mode `PixelScaleEnum.LongFocalLength` preset, which nudges four
knobs (`StructureLayers`, `MinStarBoundingBoxSize`, `BrightnessSensitivity`, and indirectly
`PixelSampleSize`). That is a workaround for a missing feature: the frame itself is still analyzed at a
resolution the detector was never tuned for.

## Approach

Resample the frame by an integer factor **inside star detection only**, so the detector always works
near its calibrated HFR range, then scale every pixel-space output back before the result leaves
`StarDetector`. Nothing downstream — auto-focus HFR, star annotations, the aberration inspector, the
optimizer, saved runs — can tell the difference, except that detection got better.

NINA's Auto Focus Binning keeps its own job: matching the binning used for imaging, so focus is found
for the frames actually being shot. The two compose, and Hocus Focus explains the distinction when a
user stacks them by accident.

### The unit contract

Detection runs in **binned pixel space**. Every result crosses back to **capture pixel space** (the
resolution NINA delivered the frame at, i.e. after NINA AF binning) at the end of the detector. A
user's tuned parameters therefore stay in binned units — that is the entire point — while
`AverageHFR`, star centers, bounding boxes and the annotated image keep their existing meaning.

| Quantity | Transform on the way out (factor `b`) |
|---|---|
| `Star.Center` | `c·b + (b−1)/2` (a block-mean sample sits at the block center) |
| `Star.StarBoundingBox`, `StarDetectorMetrics.*Bounds`, `DebugData.DetectionROI` | `X, Y, W, H → ·b` |
| `Star.HFR`, `PSF.Sigma`, `PSF.FWHMx`, `PSF.FWHMy` (pixels) | `·b` |
| `PSF.FWHMArcsecs` | unchanged — `PixelScale` was already scaled by `b`, so this is already physical |
| `Star.Background`, `MeanBrightness`, `PeakBrightness` | unchanged — mean binning preserves normalized level, and with it saturation semantics |
| `Star.BackgroundPlane` | `OriginX`, `OriginY → ·b`; `B1`, `B2 → /b` (the slope is per original pixel) |

Mean binning is required rather than summing: the detector works on `[0, 1]`-normalized floats and
`SaturationThreshold` defaults to 0.99, so summing would clip. Mean binning keeps the level, divides
uncorrelated noise by `b`, and is exactly what `Cv2.Resize(..., InterpolationFlags.Area)` computes on
an exact integer downscale.

### Where the resample happens

At the top of `StarDetector.BuildDetectionContextInternal`, before the ROI crop, so the ROI rectangle
and the star centers share one coordinate space and a single uniform scale at the end of
`GateAndMeasureInternal` converts everything at once.

One ordering constraint: the hotpixel filter must run at **native** resolution. A hot pixel that is
averaged into its block first is no longer a single-pixel outlier at a recognizable amplitude, so when
binning is active the existing `ApplyHotpixelFilter` call is hoisted above the resample and Step 1 is
told it has already run. Bayered frames already get their CFA hotpixel pass at native resolution in
`PrepareSrcImageFromRenderedImage`, so both paths behave the same way.

`DetectionBinning` changes candidate formation, so it belongs in
`StarDetector.EarlyCacheKeyProperties`: an early `DetectionContext` built at one factor can never be
reused at another. It reaches the full cache key automatically, since `ToCanonicalCacheString`
reflects over the public properties of `StarDetectorParams`.

Placing the resample in `StarDetector` rather than in `HocusFocusStarDetection` means the ten or so
TestApp runners that call `detector.Detect(Mat, params, …)` directly get binning, and correct
coordinates, for free.

### Choosing the factor: recommend from measurement, never resolve

The factor is **always explicit**. There is no Auto mode, and the plugin never changes the setting on its
own. A factor that resolved itself from the profile would change how frames are analyzed the moment the
plugin updated, silently invalidating every pixel-unit setting the user had already tuned.

The recommendation is built from a **measurement**, in one shared function (`DetectionBinningResolver`) the
options page, the optimization wizard and the documentation all cite:

```
recommended = clamp(round(measuredInFocusHfrPixels / TargetHfrPixels), 1, 4)   // TargetHfrPixels = 3.0
```

The measurement is the auto-focus run's **final HFR** — a real exposure at the position the run settled on —
recorded per profile in `InFocusHfrRecord` and refreshed by every auto-focus run and by a live wizard sweep.
It is in captured pixels at every binning factor (the detector scales its outputs back), so the rule is
independent of the factor the measurement was taken at, and camera binning is accounted for automatically
because the frames were already binned when it was measured.

| Measured in-focus HFR | Recommended |
|---|---|
| < 4.5 px | 1×1 |
| 4.5–7.5 px | 2×2 |
| 7.5–10.5 px | 3×3 |
| ≥ 10.5 px | 4×4 |

#### Showing it

The recommendation is shown **only when it asks for something**: a first auto-focus, or a factor change. Once
the setting matches the measurement it is hidden entirely — on the options page its grid row collapses to zero
height. A line that merely confirms the current setting costs space to say nothing, and trains the eye to skip
the line that matters.

Placement differs by surface because the constraints do. On the Star Detector options page it sits on its own
row under the dropdown, where a longer line has room and the collapsing row keeps it free at rest; it cannot
share the dropdown's row, because that column is `Auto` with a `SharedSizeGroup` and a long string there would
widen the shared value column and shift every other row in the pane. In the optimization wizard's confirmation
panel it sits to the right of the dropdown, which is a plain horizontal row with no such constraint.

Emphasis comes from the theme's own text brushes — `SecondaryBrush` for the no-measurement prompt,
`PrimaryBrush` when the factor disagrees — and explicitly NOT from `Opacity`. Dimming toward the background is
illegible on NINA's dark theme. It is not a warning color either: overriding the recommendation is a
legitimate choice, not an error.

#### Why not estimate from pixel scale

The first version did: `HFR ≈ assumedFwhm / (2 · pixelScale)` with `assumedFwhm = 3.0″`. That cannot work.
Plausible seeing spans roughly 1.5″–4″, a factor of 2.7, which is **wider than the entire 1×1-vs-2×2 decision
margin**. On a 0.28″/px rig the answer flips at about 2.3″ of seeing — inside ordinary night-to-night
variation — so the assumed figure decided the recommendation rather than the rig did. It told a user whose
stars measured 3.6 px to bin 2×2, when 3.6 px was already inside the calibrated band.

The simulator confirms the same inflation further out: at 4500 mm the estimate claimed 8.7 px and asked for
3×3, while the detector actually measures 6.92 px there, which is 2×2.

#### Why not the fitted curve minimum

The wizard's measured recommendation reads the fitted focus curve's vertex, and that is only trustworthy while
the fit is. Measured on the simulator at 2800 mm across sweep widths:

| Sweep (±4 steps of) | Fit R² | Fitted vertex | Optics truth |
|---|---|---|---|
| 10 | 0.9984 | 4.71 px | 4.87 px |
| 40 | 0.9992 | 4.73 px | 4.87 px |
| 80 | 0.9999 | 4.65 px | 4.87 px |
| 160 | −0.21 | 5.95 px | 4.87 px |
| 320 | −0.34 | **2.16 px** | 4.87 px |

Past roughly 22 px of defocus the detector stops resolving the donuts and reports a handful of compact ~2 px
noise blobs instead; those points drag the vertex far off. No estimator survives that — the smallest measured
point and the point nearest best focus were equally wrong — so the wizard **gates its recommendation on fit
quality** (R² ≥ 0.9) and says nothing when the curve cannot support one. The options page is unaffected: it
reads the auto-focus final HFR, which is a direct measurement rather than a fit.

### Interaction with NINA's Auto Focus Binning

The two settings answer different questions, and stacking them by accident is the failure mode worth
guarding against. When a user raises Hocus Focus detection binning above 1 while NINA's AF binning is
also above 1, a prompt explains that NINA's Auto Focus Binning should match the binning used for
imaging, that Hocus Focus detection binning is a detection-only resample, that the two multiply, and
offers to set NINA's back to 1 — the global `FocuserSettings.AutoFocusBinning` and every
`FilterInfo.AutoFocusBinning` row above 1, since a leftover filter row would silently keep capturing
binned for that filter.

The prompt fires only on a user edit. Profile load, settings import, auto-focus replay, per-filter
copy and per-filter buffered edits all suppress it. The conflict enumeration and its message are a
pure function so they can be tested without a dialog.

### Optimization

The Optimization Wizard holds binning at whatever the user configured, so the settings it produces are
tuned for the resolution actually used. It is not an optimizer search variable: changing the factor
mid-search would shift the units of every HFR-based objective term, making candidate scores
incomparable.

Instead the wizard reports what it will use next to the existing camera-binning readout, and once the
run has been evaluated at the baseline settings it computes a recommendation from the measured median
HFR of the frames nearest best focus — the same `TargetHfrPixels` rule, but with real data instead of
an assumed seeing figure. Applying it is a separate, explicit action, and the wizard says plainly that
the settings it just produced were tuned at the old factor.

## What this does not change

- The rendered and annotated image stays at the binning NINA captured at.
- Reported HFR, FWHM, eccentricity and star positions stay in capture pixels.
- With the factor at 1 the pipeline is bit-identical to today.
- `InspectorVM.TakeAndAnalyzeExposureImpl` builds its own `CaptureSequence` and never sets `.Binning`,
  so the Inspector's own "take exposure" ignores `AutoFocusBinning` entirely. That is a pre-existing
  inconsistency, unrelated to this feature, and is left alone.

## Trade-offs

- **Trailing crop.** The frame is cropped to a multiple of the factor before binning, so up to `b − 1`
  rows and columns at the right and bottom edge are dropped. Stars there would be border-rejected
  anyway.
- **Sub-pixel centroids.** Centroid precision in capture pixels is coarser by roughly `b`, which is the
  price of the SNR gain. For auto-focus, HFR precision is what matters and it improves.
- **HFR is not identical between factors** (measured, not predicted). On the capstone frames the reported
  in-focus HFR creeps up with the factor: +5% at 2× (4.77 → 5.03 px) and +21% at 3× (7.46 → 9.02 px). The
  cause is the SNR gain itself — more of each star's outer flux clears the measurement threshold, so the
  flux-weighted mean radius grows. It is a scale factor across the whole focus curve, so it does not move
  best focus, but HFR values from different factors are not directly comparable and the capstone tolerance
  (20%) says so explicitly rather than hiding it. This is why applying the wizard's binning recommendation is
  a separate action that tells the user to re-run the search.
- **Assumed seeing.** The options-page recommendation uses a fixed 3.0″ FWHM. On an unusually good or bad
  night it may be one step off; the wizard's measured recommendation exists precisely to correct that.
