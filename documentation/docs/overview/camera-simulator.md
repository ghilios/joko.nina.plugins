# Camera Simulator

Hocus Focus includes a full camera simulator: a synthetic imaging camera that renders physically
realistic star fields for wherever the connected mount points. Select it in place of a real camera
and everything downstream runs unmodified, as it would under a real sky: star detection, autofocus,
the [Aberration Inspector](tilt-aberration-inspector.md), and the
[Tilt Adapter Wizard](tilt-adapter-wizard.md). The difference is that you choose the sky brightness,
the seeing, the filter, the focus error, and even the sensor tilt. That makes it a daytime test
bench: watch a complete autofocus V-curve, stress the detector with defocused donut stars, or
rehearse an entire tilt calibration without opening the roof.

The stars are real. The simulator reads an ASTAP star database, projects the catalog stars for the
mount's current pointing onto the sensor at your rig's pixel scale, and renders each one through a
physical exposure model: aperture, filter bandpass, sensor quantum efficiency, sky glow, seeing,
photon noise, and read noise. Defocused stars grow into donuts, a tilted sensor blurs one corner
before the other, and the HFR curve autofocus measures is the same hyperbola it fits on a real rig.
The full model is documented in the
[Camera Simulator Rendering Model](camera-simulator-rendering.md).

## Install ASTAP and a star database

The simulator's only external dependency is an ASTAP star database. If you already use ASTAP for
plate solving and installed a star database with it, there is nothing more to do: the simulator
looks in ASTAP's default installation folder, `C:\Program Files\astap`.

Otherwise, install both:

1. **[ASTAP](https://www.hnsky.org/astap.htm)**: the installer is linked at the top of the ASTAP
   home page.
2. **A star database**: from the
   [ASTAP star database downloads](https://sourceforge.net/projects/astap-program/files/star_databases/).
   **D50** (the general-purpose database ASTAP recommends for most users) is a good choice; **D80**
   is denser and gives the simulator more faint stars to draw. The wide-field **W08** database is
   too shallow for typical imaging fields of view.

Any ASTAP star database works. The simulator auto-detects whatever database is in the folder, in
both the current 1476-cell format (`.1476` cell files) and the older 290-cell format (`.290`), and
prefers the finer 1476-cell partitioning when both are present. If ASTAP is installed somewhere
else, point **ASTAP Catalog Path** on the plugin's **Camera Sim** options tab at the database
folder. The path is re-read for every exposure, so a change takes effect on the next frame without
reconnecting the camera.

If the database is missing, exposures fail with an error naming the folder it searched, and the
Camera Sim tab's path field is where to fix it.

## Connect it

The simulator appears in NINA's normal equipment chooser:

1. Connect a **focuser** and a **mount** first. NINA's built-in simulator focuser and simulator
   telescope are fine. The camera reads the focuser position to compute defocus and the mount
   pointing to select catalog stars, so an exposure without them fails with an error saying which
   device is missing.
2. In **Equipment → Camera**, select **Hocus Focus Simulator** (listed under **Hocus Focus**) and
   connect.
3. Optionally connect a **rotator**: the frame then renders at the rotator's mechanical angle, with
   the **Field Rotation** setting acting as an offset. Without a rotator, **Field Rotation** sets
   the frame rotation directly.

The camera is monochrome, 1×1 binning only, with a gain slider and no cooler control (its
**Temperature** readout reports the configured sensor temperature). There is no live view.

## Configure the rig

Click the gear icon next to the camera in **Equipment → Camera** to open the **Hocus Focus
Simulator Setup** dialog. This is the physical rig, set once:

| Setting | Default | What it does |
|---|---|---|
| **Sensor Model** | IMX455 (ASI6200MM / QHY600M) | Chooses the sensor: IMX455, IMX571 (ASI2600MM / QHY268M), IMX533 (ASI533MM), or IMX294 (ASI294MM). Each brings its real resolution, pixel size, bit depth, full-well capacity, read-noise curve, and dark current. Change it while disconnected: NINA latches resolution and pixel size when the camera connects. |
| **Aperture** (mm) | blank | Leave blank to infer from your NINA telescope settings; the greyed hint shows the value in effect. |
| **Focal Length** (mm) | blank | Leave blank to take the focal length from **Options → Equipment → Telescope**. A fresh profile falls back to 980 mm at f/7. |
| **Central Obstruction** | on | Whether the optic has a central obstruction. This is what puts the hole in defocused donut stars. |
| **Obstruction Fraction** | 0.3 | Obstruction diameter as a fraction of the aperture. Typical Newtonian/SCT values are 0.3–0.5. |
| **Optical Throughput** | 0.85 | Light loss through the optical train. |

Everything else lives on the plugin's **Camera Sim** options tab (NINA's options → **Plugins** →
**Hocus Focus**) and is meant to change per session. Every value there is read when an exposure
starts, so edits apply to the next frame.

| Setting | Default | What it does |
|---|---|---|
| **Optimal Focuser Position** | 5000 steps | The focuser position at which stars are sharpest. Move the focuser away from it and stars defocus. |
| **Focuser Step Size** | unset | Microns of defocus per focuser step. This is the same setting as the Aberration Inspector's **Focuser Step Size**; editing either changes both. Blank renders at 2 µm/step. |
| **Gain** | 100 | Camera gain, on a ZWO-style scale. Affects e⁻/ADU and read noise, including the high-conversion-gain step. |
| **Bias Pedestal** | 500 ADU | Offset added to every pixel. |
| **Sensor Temperature** | −10 °C | Sets dark current, which doubles every 6.5 °C. |
| **Filter** | L (Luminance) | L, R, G, B, or narrowband: Hα, OIII, SII in 5 nm and 3 nm bandwidths. Narrowband filters pass proportionally less light, so faint-star autofocus problems reproduce faithfully. |
| **Sky Brightness** | 20.5 mag/arcsec² | Sky background level. Around 21–22 is a dark site, 18–19 heavy light pollution. |
| **Seeing** | 2.5 arcsec | Atmospheric seeing, as a FWHM. |
| **ASTAP Catalog Path** | `C:\Program Files\astap` | Folder containing the ASTAP star database. |
| **Limiting Magnitude** | 16 | Faintest catalog stars rendered. |
| **Field Rotation** | 0° | Frame rotation; an offset to the rotator's mechanical angle when one is connected. |
| **Noise Seed** | 42 | Base seed for the noise. A fixed sequence of exposures taken after connecting replays identically; repeated exposures at one position still differ, as on a real camera. |

The tab also shows the rig values (aperture, focal length, obstruction) read-only, so you can
confirm what the next exposure will use without opening the setup dialog.

## Autofocus against the simulator

With the pieces connected, run an autofocus exactly as in the
[Quick Start](../quick-start.md): the sweep produces defocused donuts at the wings, a V-shaped HFR
curve, and a fitted minimum at **Optimal Focuser Position**. Because the simulator's
HFR-versus-focuser-position curve is the same hyperbola the [autofocus model](hyperbola-fitting.md)
fits, it is a clean environment for practicing step-size tuning, trying the
[Optimization Wizard](../optimization/index.md), or reproducing a detection problem with known
ground truth. Nothing needs to be dark, tracked, or in focus first.

## Rehearse a tilt calibration in the daytime

The simulator can inject a known sensor tilt and expose a **simulated tilt adapter** whose screws
you turn with buttons. The Aberration Inspector measures the injected tilt exactly, so the whole
correction loop (measure, turn the screw the guidance names, measure again) can be walked through
at your desk before you ever touch the real adapter.

1. On the **Camera Sim** tab, turn on **Enable Aberrations** and inject a tilt: set **Tilt Angle**
   (the direction, as an azimuth in the inspector's convention), **Tilt Amount** (the
   center-to-corner focus swing in microns; 20–50 µm is a clearly visible tilt), and optionally a
   **Backfocus Error**.
2. Under **Tilt Adapter**, configure the simulated adapter's geometry: screw count, thread pitch,
   screw radius, and screw 1 angle. If you have already set up a real adapter in the
   [Tilt Adapter Wizard](tilt-adapter-wizard.md), press **Copy from adapter settings** so the
   simulated adapter matches it; a badge shows whether the two agree. Then turn on **Show tilt
   adapter panel in Imaging** (the panel appears after a NINA restart the first time).
3. Connect the simulator focuser, telescope, and the **Hocus Focus Simulator** camera. Hocus Focus
   must be selected for both **Star Detector** and **Auto Focus** under
   **Options → Imaging → Image Options**.
4. In the Imaging tab, run the Aberration Inspector's **Detailed Analysis**. It reports the tilt
   you injected, along with per-screw guidance such as `0.75 ⟳` for each screw.
5. Open the **Simulator Tilt Adapter** panel next to the inspector. For each screw the guidance
   names, set **Amount per click** to the printed magnitude and click that screw's ⟳ or ⟲ button.
   The panel shows the resulting plane and the net position of every screw.
6. Re-run **Detailed Analysis** and repeat. Applied correctly, the guidance converges: the panel
   reads **✓ ≈ flat** and the inspector's tilt drops within tolerance.

This is the same loop you run on the real rig at night, with the same guidance text and the same
conventions, so mistakes are free and the workflow is familiar before real screws are involved.
The panel can also stand in for real screws when the Tilt Adapter Wizard prompts for calibration
turns, though the inspector guidance loop above is the flow the simulator is built around.

!!! note "Match the simulated adapter to the real settings"
    The inspector computes its guidance from the *real* tilt-adapter settings, while the simulated
    adapter obeys its own geometry. If the two differ, the guidance will not converge in the
    simulator. The panel's badge (**matches adapter ✓** / **⚠ differs from adapter settings**)
    makes the mismatch visible, and **Copy from adapter settings** resolves it in one click.
