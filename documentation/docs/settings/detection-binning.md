# Detection Binning

Star detection is calibrated for stars whose in-focus HFR is roughly **2 to 4 pixels**. Every pixel-based
setting in the detector assumes that: the minimum bounding box, the minimum HFR, the noise-reduction
radius, the number of structure layers, the background box expansion, the centering tolerance. At long focal
lengths an in-focus star is far bigger than that, so those defaults are out of range, detection has many more
pixels to chew through, and each pixel carries less signal than it needs to.

**Detection Binning** resamples the frame before detection so star sizes land back in that range. It applies
to detection only. The image NINA displays, and every HFR, FWHM and star position Hocus Focus reports, stay
at the resolution the camera captured.

## The setting

Find it in the **Star Detector** tab, below the other detection settings. It applies in both Simple and
Advanced mode.

> Software binning applied to the frame for star detection only. Star detection is calibrated for stars whose
> in-focus HFR is roughly 2 to 4 pixels. At long focal lengths stars are much larger than that, which puts
> every pixel-based setting out of range and leaves detection slower and noisier than it needs to be. Binning
> the frame for detection brings star size back into range and improves signal to noise per pixel. It does not
> change the image you see or the HFR values reported: those stay at the resolution the camera captured. The
> line below recommends a factor from your pixel scale, which already accounts for any camera binning; the
> Optimization Wizard recommends one from your measured focus curve, which is the better answer once you have
> run it. This is not the same as NINA's Auto Focus Binning, which changes how the camera captures the
> auto-focus frames and should match the binning you image at. The two multiply

**Default:** `1x1 (Off)` &nbsp;•&nbsp; **Range:** 1x1 (Off), 2x2, 3x3, 4x4.

Nothing chooses the factor for you. Under the dropdown, a line recommends one and shows the reasoning:

```
Recommended: 2x2 (currently 1x1) - 0.28"/px, est. in-focus HFR ~5.4 px -> ~2.7 px at 2x2
```

The line is dimmed while it agrees with your setting and plain when it does not, so a mismatch is visible
without being alarming. Disagreeing with it is a legitimate choice.

!!! note "Why there is no Auto"
    An automatic factor would change how frames are analyzed the moment the plugin updated, and every
    pixel-based setting you had already tuned would silently be measured against different pixels. The setting
    is explicit for the same reason the wizard makes you re-optimize after changing it: the factor and the
    settings tuned at it belong together.

## How the recommendation is derived

It reads your pixel scale from the profile's pixel size and focal length, including any camera binning, and
picks the integer factor that brings the estimated in-focus star size closest to 3 pixels:

```
estimated in-focus HFR = 3.0" / (2 x pixel scale)
factor = round(estimated HFR / 3.0), clamped to 1..4
```

The 3.0″ is a stand-in for a typical night's total FWHM (seeing, optics and guiding together); HFR is half the
FWHM, which is exact for a Gaussian star. On a 3.76 µm sensor that works out to:

| Focal length | Pixel scale | Est. in-focus HFR | Recommended | Binned HFR |
|---|---|---|---|---|
| 910 mm | 0.85″/px | 1.8 px | 1x1 | 1.8 px |
| 2800 mm | 0.28″/px | 5.4 px | 2x2 | 2.7 px |
| 3910 mm | 0.20″/px | 7.6 px | 3x3 | 2.5 px |
| 5600 mm | 0.14″/px | 10.8 px | 4x4 | 2.7 px |

This is an estimate from an assumed seeing figure, not a measurement. The
[Optimization Wizard](../optimization/index.md) recommends a factor from your own measured focus curve, which
is the better answer once you have run a sweep.

If focal length or pixel size is not set in NINA's options, the line reads *No recommendation* and the setting
stays wherever you put it.

## This is not NINA's Auto Focus Binning

NINA has its own **Auto Focus Binning** setting, in **Options → Focuser** (and per filter in the filter wheel
settings). The two are different things and they multiply.

| | NINA Auto Focus Binning | Hocus Focus Detection Binning |
|---|---|---|
| What it changes | How the camera captures the auto-focus frames | How the frame is analyzed, after capture |
| Set it to | The binning you image at, so focus is found for the frames you actually shoot | Whatever brings star size into the detector's range |
| Effect on the displayed frame | Smaller frame, larger pixels | None |
| Effect on reported HFR | Reported in the captured (binned) pixels | None; still reported in captured pixels |

Set Auto Focus Binning to match your imaging binning, and set Detection Binning from the recommendation. If
you raise Detection Binning while Auto Focus Binning is above 1x1, Hocus Focus explains the difference and
offers to set the NINA setting back to 1x1, listing the global value and any per-filter overrides it would
clear. Answering **No** keeps both, and the two factors stack.

The recommendation already accounts for camera binning, because the pixel scale it reads includes it. At
2800 mm with the camera at 2x2 it recommends 1x1 rather than 2x2.

## Changing it in the Optimization Wizard

The [Optimization Wizard](../optimization/index.md) shows **Detection binning** next to **Capture binning** on
its confirmation panel, and it is settable there: the whole search is tuned at whatever factor is in effect
when the run starts, so that is the last useful moment to choose it. It is the same setting as the one on the
Star Detector tab, edited in place.

After a run, the summary reports the factor your measured focus curve calls for. If it differs, the only
action offered is **Optimize again at NxN**, which repeats the search on the frames already captured at the
new factor. Nothing is saved until you accept that result, and then the factor and the settings tuned at it
are applied together. There is no way to apply the factor on its own, because a factor without settings
measured at it is a combination the optimizer never evaluated.

## What is and is not rescaled

Detection runs on binned pixels; everything it returns is converted back to captured pixels before it leaves
the detector. So HFR, star positions, bounding boxes, PSF sigma and the rejected-candidate boxes drawn in the
annotation overlay are all in the frame's own coordinates, and the auto-focus curve means the same thing at
any factor.

Two consequences worth knowing:

- **Star centers are coarser.** A star's position in captured pixels is quantized to roughly the binning
  factor. That is the trade for the signal-to-noise gain. Auto-focus depends on HFR, not on sub-pixel
  centroids, so this does not affect focus accuracy.
- **HFR is not identical between factors.** Binning raises the signal-to-noise per pixel, so more of each
  star's outer flux clears the measurement threshold and the measured half-flux radius creeps up: on
  synthetic frames, about 5% at 2x and 15% at 3x. That is a scale factor across the whole focus curve, so it
  does not move best focus, but HFR numbers from different factors are not directly comparable. Changing the
  factor is a good moment to re-run the Optimization Wizard.

Detection also crops the frame to a whole multiple of the factor first, so up to three rows and columns at the
right and bottom edge are dropped. Stars there would be rejected by the border gate anyway.

## When to change it

Follow the recommendation unless you have a reason not to, and re-check it if you change optics or camera.
Reasons to override:

- **Stay at 1x1** if you want detection to see every pixel — chasing a specific detection problem, or
  comparing against an older run's HFR values.
- **Go higher than recommended** if your seeing is consistently worse than 3″, or if the Optimization
  Wizard's measured recommendation says so.

Whichever you pick, re-run the [Optimization Wizard](../optimization/index.md) afterwards. Every pixel-based
setting is measured in binned pixels, so a factor change makes the tuned values mean something different.

**Feedback:** in the [Star Detection Results panel](index.md#reading-the-results-the-star-detection-results-panel),
a factor that suits your rig raises **Total Detected** (fainter stars clear the threshold) and cuts detection
time, without a jump in the **Too Small** or **Too Distorted** counts.

## Where it sits in the pipeline

Detection binning is an **EARLY** parameter: it resamples the frame before candidate finding, so changing it
forces a full re-detect and the Optimization Wizard cannot reuse a cached context across factors. Hot-pixel
filtering deliberately runs *before* the resample, at full resolution, because a hot pixel averaged into its
block is no longer the isolated outlier the filter looks for.
