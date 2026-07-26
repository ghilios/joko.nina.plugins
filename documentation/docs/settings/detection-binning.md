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
> change the image you see or the HFR values reported: those stay at the resolution the camera captured. Auto
> picks a factor from your pixel scale, and already accounts for any camera binning. This is not the same as
> NINA's Auto Focus Binning, which changes the capture itself and should match the binning you image at

**Default:** `Auto` &nbsp;•&nbsp; **Range:** Auto, 1x1 (Off), 2x2, 3x3, 4x4.

Under the dropdown, a line reports what the setting resolves to and why, for example:

```
Resolved: 2x - 0.28"/px, est. in-focus HFR ~5.4 px -> ~2.7 px binned
```

## How Auto chooses

Auto reads your pixel scale from the profile's pixel size and focal length, including any camera binning,
and picks the integer factor that brings the estimated in-focus star size closest to 3 pixels:

```
estimated in-focus HFR = 3.0" / (2 x pixel scale)
factor = round(estimated HFR / 3.0), clamped to 1..4
```

The 3.0″ is a stand-in for a typical night's total FWHM (seeing, optics and guiding together); HFR is half the
FWHM, which is exact for a Gaussian star. On a 3.76 µm sensor that works out to:

| Focal length | Pixel scale | Est. in-focus HFR | Factor | Binned HFR |
|---|---|---|---|---|
| 910 mm | 0.85″/px | 1.8 px | 1x | 1.8 px |
| 2800 mm | 0.28″/px | 5.4 px | 2x | 2.7 px |
| 3910 mm | 0.20″/px | 7.6 px | 3x | 2.5 px |
| 5600 mm | 0.14″/px | 10.8 px | 4x | 2.7 px |

Auto is a starting point derived from an assumed seeing figure, not a measurement. If you want a specific
factor, pick it from the dropdown. The [Optimization Wizard](../optimization/index.md) recommends one from
your own measured focus curve, which is the better answer once you have run a sweep.

If focal length or pixel size is not set in NINA's options, Auto reports that it cannot compute a pixel scale
and stays at 1x1.

## This is not NINA's Auto Focus Binning

NINA has its own **Auto Focus Binning** setting, in **Options → Focuser** (and per filter in the filter wheel
settings). The two are different things and they multiply.

| | NINA Auto Focus Binning | Hocus Focus Detection Binning |
|---|---|---|
| What it changes | How the camera captures the auto-focus frames | How the frame is analyzed, after capture |
| Set it to | The binning you image at, so focus is found for the frames you actually shoot | Whatever brings star size into the detector's range |
| Effect on the displayed frame | Smaller frame, larger pixels | None |
| Effect on reported HFR | Reported in the captured (binned) pixels | None; still reported in captured pixels |

Set Auto Focus Binning to match your imaging binning, and leave Detection Binning on Auto. If you raise
Detection Binning while Auto Focus Binning is above 1x1, Hocus Focus explains the difference and offers to
set the NINA setting back to 1x1, listing the global value and any per-filter overrides it would clear.
Answering **No** keeps both, and the two factors stack.

Auto already accounts for camera binning, because the pixel scale it reads includes it. At 2800 mm with the
camera at 2x2, Auto resolves to 1x rather than 2x.

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

Leave it on **Auto**. Reasons to override:

- **Force 1x1** if you want detection to see every pixel — chasing a specific detection problem, comparing
  against an older run's HFR values, or working at a pixel scale where Auto's assumed seeing is wrong.
- **Force a higher factor** than Auto picks if your seeing is consistently worse than 3″, or if the
  Optimization Wizard's measured recommendation says so.

**Feedback:** in the [Star Detection Results panel](index.md#reading-the-results-the-star-detection-results-panel),
a factor that suits your rig raises **Total Detected** (fainter stars clear the threshold) and cuts detection
time, without a jump in the **Too Small** or **Too Distorted** counts.

## Where it sits in the pipeline

Detection binning is an **EARLY** parameter: it resamples the frame before candidate finding, so changing it
forces a full re-detect and the Optimization Wizard cannot reuse a cached context across factors. Hot-pixel
filtering deliberately runs *before* the resample, at full resolution, because a hot pixel averaged into its
block is no longer the isolated outlier the filter looks for.
