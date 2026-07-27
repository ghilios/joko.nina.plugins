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

> Software binning applied to the frame for star detection only. Star detection is calibrated for in-focus
> stars of roughly 2 to 4 pixels; at long focal lengths stars are much larger than that, which puts the
> pixel-based settings out of range. Binning the frame for detection brings star size back into range and
> improves signal to noise per pixel. The image you see and the HFR values reported stay at the resolution the
> camera captured. This is not NINA's Auto Focus Binning, which changes how the camera captures the auto-focus
> frames and should match the binning you image at; the two multiply

**Default:** `1x1 (Off)` &nbsp;•&nbsp; **Range:** 1x1 (Off), 2x2, 3x3, 4x4.

Nothing chooses the factor for you. A line appears under the dropdown only when there is something to act on:
no measurement yet, or a factor change:

```
Run an auto-focus to get a recommendation
Measured in-focus HFR 6.1 px - 2x2 recommended
```

Once your setting matches what the measurement calls for, the line disappears: there is nothing to act on.
Hover it for the reasoning, including when the measurement was taken. In the
[Optimization Wizard](../optimization/index.md) the same line sits to the right of its dropdown, under the
same rule, and its summary offers to re-run the search at the recommended factor.

The recommendation is advisory; nothing enforces it.

!!! note "Why there is no Auto"
    An automatic factor would change how frames are analyzed the moment the plugin updated, and every
    pixel-based setting you had already tuned would silently be measured against different pixels. The setting
    is explicit for the same reason the wizard makes you re-optimize after changing it: the factor and the
    settings tuned at it belong together.

## Where the recommendation comes from

It is **measured, not estimated**. The number is the final HFR of your last auto-focus run: a real exposure
taken at the focuser position the run settled on. Accepting a live sweep in the
[Optimization Wizard](../optimization/index.md) updates it too, from the sweep's fitted curve. The factor is
then whichever brings that measurement closest to 3 px:

| Measured in-focus HFR | Recommended |
|---|---|
| below 4.5 px | 1x1 (off) |
| 4.5 to 7.5 px | 2x2 |
| 7.5 to 10.5 px | 3x3 |
| 10.5 px and above | 4x4 |

!!! warning "Pixel scale cannot answer this"
    The recommendation is never derived from pixel scale and an assumed seeing figure. Plausible seeing spans
    roughly 1.5&Prime; to 4&Prime;, a factor of 2.7, which is wider than the whole 1x1-versus-2x2 decision
    margin: on a 0.28&Prime;/px rig the answer flips at about 2.3&Prime; of seeing. An assumed figure would
    decide the recommendation, not your rig.

Re-run an auto-focus after changing optics, cameras or filters, so the recommendation reflects the current
rig rather than the previous one. The tooltip shows the measurement date for exactly this reason.

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

The recommendation accounts for camera binning automatically, because the HFR it reads was measured on
frames the camera had already binned. On one simulated 5600 mm rig it asks for 3x3 unbinned and 2x2 with the
camera at 2x2.

## Changing it in the Optimization Wizard

The [Optimization Wizard](../optimization/index.md) shows **Detection binning** next to **Capture binning** on
its confirmation panel, and it is settable there: the whole search is tuned at whatever factor is in effect
when the run starts, so that is the last useful moment to choose it. It is the same setting as the one on the
Star Detector tab, edited in place.

After a run, the summary reports the factor your measured focus curve calls for. If it differs, the only
action offered is **Optimize again at NxN**, which repeats the search on the frames already captured at the
new factor. Nothing is saved until you **Accept** that result; the factor and the settings tuned at it
are then applied together. The factor is never applied on its own, because settings tuned at one factor are
not valid at another.

## What is and is not rescaled

Detection runs on binned pixels; everything it returns is converted back to captured pixels before it leaves
the detector. So HFR, star positions, bounding boxes, PSF sigma and the rejected-candidate boxes drawn in the
annotation overlay are all in the frame's own coordinates, and the auto-focus curve means the same thing at
any factor.

Two consequences:

- **Star centers are coarser.** A star's position in captured pixels is quantized to roughly the binning
  factor. That is the trade for the signal-to-noise gain. Auto-focus depends on HFR, not on sub-pixel
  centroids, so this does not affect focus accuracy.
- **HFR is not identical between factors.** Binning raises the signal-to-noise per pixel, so more of each
  star's outer flux clears the measurement threshold and the measured half-flux radius creeps up: on
  synthetic frames, about 5% at 2x and 21% at 3x. That is a scale factor across the whole focus curve, so it
  does not move best focus, but HFR numbers from different factors are not directly comparable. Changing the
  factor is a good moment to re-run the Optimization Wizard.

Detection also crops the frame to a whole multiple of the factor first, so up to three rows and columns at the
right and bottom edge are dropped. Stars there would be rejected by the border gate anyway.

## When to change it

Follow the recommendation unless you have a reason not to, and re-run an auto-focus after changing optics so
it stays current. Reasons to override:

- **Stay at 1x1** if you want detection to see every pixel — chasing a specific detection problem, or
  comparing against an older run's HFR values.
- **Go higher than recommended** if you routinely image in worse seeing than the night the measurement was
  taken, since the measurement is one night's answer rather than a long-run average.

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
