# Tilt &amp; Aberration Inspector

The **Aberration Inspector** is a dockable panel that drives an auto-focus run (or a single snapshot)
and turns the result into a quantitative picture of how your sensor sits in the optical train. It
answers the questions a single center-of-frame focus number cannot: *Is one corner sharper than the
other? Is the field bowed? Is my backfocus right?* Backfocus is the spacing between the
corrector/flattener and the sensor. And, when paired with a tilt-adapter calibration, the inspector
translates those numbers into concrete screw-turn guidance.

## Sensor tilt, in one paragraph

A camera sensor should sit perfectly perpendicular to the optical axis. When it does not, best focus
is reached at a *different* focuser position on each side of the frame: stars on one edge are tight
while the opposite edge is bloated, no matter where you park the focuser. That asymmetry is **tilt**.
A separate but related defect, **field curvature**, is when the best-focus surface is not flat but
bowl- or dome-shaped, so the corners and the center focus at different positions even with zero tilt.
The inspector measures both, plus the axial **backfocus** offset between the center and the corners.

![Sensor tilt and field-curvature best-focus offset map](../assets/figures/tilt-heatmap.png){ width=620 }

*A best-focus offset map: tilt shows up as a smooth gradient across the sensor, curvature as a
center-to-corner bowl. The inspector separates the two.*

## What the inspector measures

Running a **Detailed Analysis** performs an auto-focus across several regions of the frame at once.
Per the tooltip, this is used to "detect various aberrations, such as tilt, incorrect backfocus, and
field curvature." The panel reports, among others:

![The Aberration Inspector model properties and the model-analysis checklist with pass/fail results](../assets/screenshots/inspector-measurements.png){ width=620 }

*The inspector reports tilt, curvature, and backfocus, then grades each against critical focus.*

| Measurement | What it means (from the panel tooltips) |
|---|---|
| **Tilt** | "The angle the sensor is tilted. 0 indicates no tilt, and values here are typically very small." |
| **Tilt effect** | "The maximum nominal distance from the corners of the sensor to the center, due purely to the modeled tilt." |
| **Curvature effect** | "The nominal distance from the corner of the sensor to the center, due purely to the modeled curvature. Curvature effect is more exaggerated on larger sensors." |
| **Curvature radius** | "Near focus, the base of the sensor parabola is close to a sphere. This value represents the radius of that sphere. Larger values indicate flatter curves, which are better." |
| **Critical focus** | "The focuser distance a pixel can be from optimal focus before the effects can be noticed. This value increases with larger (slower) F/ratios, which can tolerate more curvature before affecting image quality." |
| **Mean focuser position** | "The weighted mean focuser position under the sensor curve … a focus point where pixels throughout the sensor have minimum absolute distance from optimal focus." |
| **AutoFocus offset** | "The number of focuser steps between the AutoFocus position and the Sensor Mean Focuser Position." |

A lighter-weight **Simple Analysis** "[takes] a single exposure to produce a FWHM contour map and
Eccentricity vector field," useful for a quick look at off-axis aberrations without a full sweep.

![Corner PSFs showing off-axis aberration](../assets/figures/aberration-corners.png){ width=620 }

*Off-axis aberration grows toward the corners. The eccentricity vector field and FWHM contour from a
Simple Analysis make this pattern visible at a glance.*

## How it measures it

The Detailed Analysis fits a focus curve **per region** rather than just at the center. Internally it
evaluates the full field plus five named regions (center, top-left, top-right, bottom-left, and
bottom-right) and records each region's estimated best-focus position and its goodness-of-fit
(\(R^2\)).

### The tilt plane

The four corner positions are fed to an ordinary-least-squares fit of a plane over the sensor in
normalized image coordinates that run from \(-0.5\) to \(+0.5\) on each axis:

\[
\text{Focus}(x, y) = A\,x + B\,y + C
\]

\(A\) and \(B\) are the tilt slopes in the horizontal and vertical directions (in focuser steps per
normalized image unit), and \(C\) is the mean focus plane. Each corner's **Adjustment Required** is
its best-focus position minus the mean, reported in focuser steps (and in microns, if you have set
*Microns per Focuser Step*).

### The full sensor surface (optional)

Enabling **Sensor Curve Model** goes further. Per its tooltip, it will "in addition to analyzing
AutoFocus curves for the corners and center, create a paraboloid model of the sensor by creating
focus curves for every star. This enables calculation of centering error and curvature, and is
similar to the type of analysis done by CCD Inspector." Stars are matched across frames (the panel
notes "[each] star needs to be matched across at least 5 of the frames, and some star fits are
rejected as outliers during modeling"), optionally after RANSAC alignment, and a paraboloid is fit
through the per-star best-focus positions.

![The 3D sensor curve model surface showing tilt and field curvature](../assets/screenshots/inspector-sensor-model-3d.png){ width=620 }

*The sensor curve model: a 3D surface of best-focus offset across the sensor (telescope up, sensor down).*

!!! tip "When the surface model helps"
    Enable **Sensor Curve Model** when you intend to physically correct tilt or want curvature and
    centering numbers, not just a corner-vs-center plane. It is heavier (it fits a curve for every
    matched star) and needs enough matched stars to be meaningful, so leave it off for a quick tilt
    check.

!!! note "Frame alignment is robust to heavy defocus"
    Matching stars across frames is hardest at the defocused extremes of the sweep, where stars are
    bloated and sparse. The matcher chains alignment through neighboring frames and retries against a
    denser reference rather than giving up, so the "*N frames failed to align*" condition is now rare.
    If you still hit it, collect more stars on the weak frames (a wider exposure, or the
    [donut-recovery settings](../settings/acceptance-gates.md#recover-out-of-focus-donut-stars)) and
    re-run.

## Guiding tilt-adapter screw adjustments

A measured tilt plane tells you which corners need to move and by how much, but turning a screw moves
the sensor along that screw's own axis. The inspector must therefore know **where each screw sits
relative to the sensor**. That mapping is established once by the **Tilt Adapter Wizard**.

![The Tilt Adapter Guidance table giving the direction and number of turns for each screw](../assets/screenshots/inspector-tilt-guidance.png){ width=620 }

*Tilt Adapter Guidance turns the model into concrete per-screw adjustments: direction and number of turns.*

### The screw-angle convention

Screw orientations are stored as angles measured **clockwise from straight up (12 o'clock)**: `0°` is
the top of the sensor, `90°` is to the right, `180°` is the bottom, `270°` is to the left. The wizard
stores one angle per screw (`Screw1AngleDegrees` … `Screw4AngleDegrees`).

!!! warning "Orientation is tracked in image space, not physically"
    A star diagonal, a mirror, or a rotator can flip the sensor's orientation inside the camera body.
    As the wizard's own code comments note, "image mirroring causes screws numbered clockwise on the
    physical adapter to appear counter-clockwise in the sensor image." The wizard therefore **derives
    the winding direction from the actual measurements** rather than assuming clockwise. Do not
    reason about screw numbers from the physical adapter; trust the calibrated image-space angles.

### 3-screw vs 4-screw adapters

The adapter type is set by **Screw Count** (default `3`).

- **3-screw adapter**: screws are independent and spaced about `120°` apart. The wizard labels
  Screw 1 at the top (12 o'clock) and numbers the rest clockwise, then solves for the three angles
  with an equal-spacing constraint, splitting measurement error evenly between them.
- **4-screw adapter**: screws are spaced about `90°` apart and **opposite screws are mechanically
  coupled**, so adjustments are made in pairs (turn one in while the opposite turns out). The wizard
  exploits this: "opposite screws are always 180° apart regardless of mirroring," so it measures two
  screws and places the other two 180° across.

### The calibration loop

The wizard establishes the screw-to-tilt mapping empirically. You take a **baseline** measurement,
then follow on-screen instructions to turn screws by a known amount (the wizard prompts, e.g., "turn
ALL screws INWARD exactly 1 full turn each," then per-screw steps), re-measuring after each. From the
change in the tilt vector \((\Delta A, \Delta B)\) it computes each screw's angle and the sign of its
effect (`ScrewInwardCurvatureSign`: whether turning a screw inward pushes that side away from or
toward the telescope). To average out seeing, set **Measurement Average Count** above 1; the wizard
flags inconsistent repeats so you can re-run.

!!! note "Set Microns per Focuser Step for the best guidance"
    Per its tooltip, *Microns per Focuser Step* is "how much the focuser moves per step, in microns.
    If this is set, the adjustment chart will include adjustments in microns." Without it, adjustments
    are still reported in focuser steps, and the tilt-angle calculation falls back to the connected
    focuser's reported step size when available.

!!! tip "Tilt vs. curvature: which can a tilt adapter fix?"
    A tilt adapter corrects the **linear** part of the focus surface, the tilt plane. It cannot
    flatten genuine **field curvature** (the bowl/dome residual after the plane is removed); that is
    an optical property of your flattener/corrector and focal ratio. Use the *Tilt effect* and
    *Curvature effect* numbers to tell which problem dominates before reaching for the screwdriver.

### Absolute screw-turn (or stepper-step) guidance

The calibration above tells the inspector *which* screws to move and the focus deviation to remove.
To turn that into a concrete instruction (*"turn this screw inward ¼ turn"* rather than a number of
focuser steps), the wizard needs the adapter's **physical hardware model**:

| Hardware field | Meaning |
|---|---|
| **Adjustment Type** | Whether the adapter is adjusted by **Screws** (reported in turns) or **Stepper Motors** (reported in steps). |
| **Thread Pitch** | Axial microns the sensor moves per full screw turn; used when Adjustment Type is Screws. |
| **Stepper Step Size** | Axial microns per stepper step; used when Adjustment Type is Stepper Motors. |
| **Screw Radius** | Distance of each screw from the sensor center, in millimeters; converts a tilt *angle* into an axial movement at the screw. |

**Device presets.** Rather than entering those numbers by hand, pick your adapter from the **Device**
list. Choosing a preset pre-fills and locks the hardware fields to the manufacturer's values; the
**"Manual"** entry leaves every field editable for an adapter that is not in the list. (The built-in
list grows over time; if yours is missing, use Manual and enter its thread pitch and screw radius.)

!!! note "The wizard cross-checks the hardware values it measures"
    A calibration run also *measures* an effective thread pitch / stepper step size from how far the
    sensor actually moved. The wizard remembers the last measured value and **warns you if it diverges
    from the configured value**, which usually means the wrong preset is selected or a number was
    mistyped. Without a valid hardware model, adjustments are still reported in focuser steps (and, if
    *Microns per Focuser Step* is set, in microns); you just do not get the turn/step figure.

### Saving and replaying a calibration run

Calibration involves several focus sweeps, which are expensive to repeat. Turn on **"Save Calibration
for Replay"** and the wizard writes each step's autofocus run (plus a `metadata.json`) into the folder
you choose, so a calibration can be re-analyzed later without going back to the telescope. Two replay
buttons sit next to **Calibrate**:

- **Replay**: re-runs the analysis on a saved folder using the **settings stored in that run's
  `metadata.json`** (your current profile settings are restored afterward), reproducing the original
  calibration exactly.
- **Replay Current Settings**: re-runs the same saved frames with your **current** star-detection and
  tilt-calibration settings instead, so you can see how a settings change (for example, after running
  the [Optimization Wizard](../optimization/index.md)) would have affected the result. Your profile
  settings are left unchanged.

!!! tip "Tuning detection against a saved calibration"
    Replay-current is the daytime tuning loop for the inspector: save a calibration once at the scope,
    then iterate on detection settings indoors and replay to compare. It is the same idea as
    [replaying an autofocus run](autofocus.md).

## Inspector options

These settings live in the inspector panel (not the main Options page). Defaults and ranges are taken
from the option definitions; descriptions quote the in-app tooltips where one exists.

![The Aberration Inspector options section](../assets/screenshots/inspector-options-empty.png){ width=519 }

*The inspector's Options section sets the analysis grid, exposures, and sensor-model behavior.*

| Setting | Default | Range | What it does |
|---|---|---|---|
| **Num Regions Wide** | 7 | odd, positive | "How many cells wide to divide the sensor pixels when generating a grid of eccentricity vectors … the height will be calculated proportionally." |
| **Microns per Focuser Step** | -1 (auto) | -1 or &gt;0 | "How much the focuser moves per step, in microns. If this is set, the adjustment chart will include adjustments in microns." |
| **Sensor ROI** | 1.0 | 0.1–1.0 | "Uses only a centered portion of the full sensor when evaluating aberration. This is useful if you have a flattener that cannot produce a flat field for your sensor." |
| **Corners ROI** | 1.0 | 0.1–1.0 | "Reduces the size of the corner regions when performing corners analysis … evaluate only stars closer to the corners than the full 1/9th region. This can be combined with Sensor ROI." |
| **Sensor Curve Model** | off | on/off | "Create a paraboloid model of the sensor by creating focus curves for every star. This enables calculation of centering error and curvature … similar to … CCD Inspector." |
| **Show Sensor Model** | on | on/off | Displays the 3D surface model in the panel. |
| **Fixed Sensor Center** | on | on/off | "Assume the sensor is perfectly centered in the optical train. If this option is off, the sensor location will be modeled along with the other model parameters." |
| **Astigmatic field curvature** | off | on/off | "When off (default), field curvature is modeled as rotationally symmetric … Enable to fit independent X and Y curvature, representing the saddle-shaped field of an astigmatic optical train. Adds one free parameter." |
| **Use RANSAC** | on | on/off | Align frames with RANSAC before matching stars, improving registration robustness. |
| **Use Affine Alignment** | off | on/off | Use a 6-DOF affine transform (adds shear) instead of similarity; only enabled when RANSAC is on. |
| **Reject Badly Fitting Matches** | on | on/off | Drop matched stars whose per-star hyperbolic fit is too poor. |
| **Reject Bad Brightness Matches** | off | on/off | Enable an adaptive search that rejects matched stars whose brightness differs too much. |
| **Starting Brightness Diff** | -1 (auto) | -1 or &ge;0.01 | Starting brightness tolerance for the adaptive match search; -1 starts from the previous run's value. |
| **Acceptable R² Min** | 0.05 | 0–1 | Minimum model \(R^2\) for acceptance; only triggers rejection when reduced \(\chi^2\) also fails. |
| **Max Stars Per Region** | -1 (unlimited) | -1 or &gt;0 | Cap on the number of (brightest) stars used per region. |
| **Eccentricity Color Map** | on | on/off | "Enable color on the eccentricity map." |
| **Mouse on Charts** | on | on/off | "Enable mouse events on charts to scroll, pan, and zoom. Disable this if you don't want the charts to intercept mouse actions." |
| **Step Count** | -1 (auto) | -1 or &gt;0 | "The minimum number of data points needed on each side of the AutoFocus curve minimum. Uses the value set for AutoFocus if blank." |
| **Step Size** | -1 (auto) | -1 or &gt;0 | "How many focuser steps in between each data point … Uses the value set for AutoFocus if blank." |
| **Frames Per Point** | -1 (auto) | -1 or &ge;1 | "How many exposures to average together for each focuser point. Uses the value set for AutoFocus if blank." |
| **Timeout (s)** | -1 (auto) | -1 or &gt;0 | "How long, in seconds, after which AutoFocus should time out and fail. Uses the value set for AutoFocus if blank." |
| **Simple Exposure (s)** | -1 (auto) | -1 or &gt;0 | "How long of an exposure to take for analysis. Defaults to the Auto Focus exposure duration if not set." Sets the exposure for the single-frame Simple Analysis. |
| **Detailed Analysis Exposure (s)** | -1 (auto) | -1 or &gt;0 | Per-frame exposure for a Detailed Analysis sweep; defaults to the AutoFocus exposure duration when blank. Unlike Simple Exposure (the single-frame Simple Analysis), this sets the per-frame exposure for the multi-frame Detailed Analysis sweep. |
| **Looping Exposure Analysis** | off | on/off | "If enabled, repeatedly take and analyze exposures." |
| **Save Images on Reruns** | off | on/off | Save registered/alignment images when reanalyzing saved runs. |
| **Save Alignment Images** | off | on/off | Also save the pre-alignment star-detection images. |

!!! tip "When Sensor ROI / Corners ROI help"
    Reach for **Sensor ROI** when your flattener cannot deliver a flat field all the way to the sensor
    edges. Restricting analysis to the well-corrected center keeps a bad corner from polluting the
    tilt fit. Use **Corners ROI** when you want the corner regions sampled closer to the actual
    corners than the default outer one-ninth of the frame.

## Running it from a sequence

The **Run Aberration Inspector** sequence instruction performs a Detailed Analysis unattended. It
validates that the camera and focuser are connected before running and fails the instruction if the
analysis does not complete. Its estimated duration is built from the configured step count, exposure
time, and a focuser settle allowance (the focuser-settle setting plus two seconds), scaled by the
number of auto-focus attempts and capped to guard against unreasonable estimates, so a long sequence
plan can budget time for the run.
