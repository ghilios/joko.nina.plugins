# Quick Start

This guide takes you from a fresh install of Hocus Focus to a tuned star detector, a working autofocus
setup, and a first check of your sensor for tilt and backfocus errors. Each step links to the deeper
reference page if you want the full detail.

!!! note "Before you start"
    Install the plugin first. In NINA, open **Plugins → Available**, find **Hocus Focus**, and install
    it (you need NINA **3.2.0.2001** or newer). Then restart NINA.

## 1. Tell NINA about your rig

Several Hocus Focus features derive their starting points from your image scale, so it is worth getting
two things right first: your **pixel scale** and how far your **focuser moves per step**.

- **Pixel size** (microns) — NINA's **Options → Equipment → Camera** tab. Usually filled in by the
  camera driver; confirm it matches your sensor.
- **Focal length** (mm) — NINA's **Options → Equipment → Telescope** tab. Together with pixel size this
  gives your pixel scale (arcsec/px), which the detector and the Simple-mode [Pixel Scale
  preset](settings/advanced-debug.md#pixel-scale) use.
- **Focuser step size** — set **Microns per Focuser Step** in the
  [Aberration Inspector options](overview/tilt-aberration-inspector.md#inspector-options) (the inspector
  panel, not the main Options page). This tells the inspector how far the focuser moves per step, in
  microns, so it can report tilt and backfocus adjustments in microns rather than bare focuser steps.

## 2. Switch NINA over to Hocus Focus

Turn on the parts you want. Go to **Options → Imaging → Image Options** and, in the dropdowns, select
**Hocus Focus** for:

![The Image Options dropdowns set to Hocus Focus for Star Detector, Star Annotator, and Autofocus](assets/screenshots/image-options-all.png){ width=510 }

*Set Star Detector, Star Annotator, and Autofocus to Hocus Focus in Options, Imaging, Image Options.*

- **Star Detection** — the improved detector (see [Star Detection](overview/star-detection.md)).
- **Star Annotator** — the customizable overlay (see [Star Annotation](overview/star-annotation.md)).
- **Auto Focus** — the concurrent autofocus engine (see [Autofocus](overview/autofocus.md)).

!!! note "Some features need both"
    The autofocus engine and the Aberration Inspector require Hocus Focus to be selected for **both**
    Auto Focus **and** Star Detection. You can otherwise mix and match, for example keeping only the detector.

## 3. Optimize star detection

The detector ships with sensible defaults, but the [Optimization Wizard](optimization/index.md) tunes it
to *your* rig by replaying a saved autofocus run and searching for the settings that produce the
cleanest, most repeatable focus curve. Launch it from the top of the **Star Detection** options page.

You will choose **what to optimize for** on the start page:

- **Default (autofocus repeatability)** — the everyday choice. It tunes detection so your autofocus
  curves are tight and your best-focus position is repeatable run to run.
- **Optimize for Aberration Inspection** — tunes instead to **recover many more stars across the whole
  frame**, which is what the tilt/curvature model in step 4 needs, while keeping the focus curve usable.
  Use this when you are about to run a Detailed Analysis.

The wizard searches from the default settings and reports its improvement **relative to your current
settings**; it will never hand back a result worse than what you have today. You can feed it a
[saved autofocus run](overview/autofocus.md) (recommended; see step 6) or run a fresh one. After it
finishes you can **Continue optimizing** for another pass or accept the result.

→ Full detail: [Star Detection Optimization](optimization/index.md).

## 4. Check for tilt and backfocus

Open the **Aberration Inspector** dockable panel and run a **Detailed Analysis**. It performs an
autofocus across the center and corners of the sensor at once and reports **tilt**, **backfocus** (the
center-to-corner offset), and **field curvature**. A lighter **Simple Analysis** takes a single
exposure to show an FWHM contour map and an eccentricity vector field for a quick look.

Enable **Sensor Curve Model** if you want curvature and centering numbers (not just a corner-vs-center
tilt plane), for example when you intend to physically correct tilt. How that model is fit is covered
in [Sensor Model Fitting](overview/sensor-model.md).

→ Full detail: [Tilt &amp; Aberration Inspector](overview/tilt-aberration-inspector.md).

## 5. If you have a tilt adapter, calibrate it

The inspector can turn its tilt measurement into **concrete screw-turn (or stepper-step) guidance**,
but only after the **Tilt Adapter Wizard** has learned where each screw sits relative to your sensor and
how far a turn moves it.

1. Pick your adapter from the **Device** preset list (or **Manual** to enter the
   [hardware values](overview/tilt-adapter-wizard.md#hardware-model-and-device-presets)
   yourself: thread pitch, screw radius, screw count).
2. Run the guided **calibration loop** (a baseline measurement, then turning the screws by a known
   amount while the wizard re-measures) to map the screws into image space.

Once calibrated, the inspector's adjustment chart tells you exactly which screw to turn and by how much.

→ Full detail: [Tilt Adapter Wizard](overview/tilt-adapter-wizard.md).

## 6. Pro-tip: save an autofocus run, then replay it to tune during the day

You do not need clear skies to improve your settings. Save a real autofocus run once, then replay it
indoors as many times as you like with different settings.

1. **Save a run.** On the **Auto Focus** options page, turn on **Save** and set a **Save Path**. Every
   autofocus run then writes its images, star-detection results, and annotated frames to that folder.
2. **Replay it.** Point the [Optimization Wizard](optimization/index.md) (or the Aberration Inspector's
   **Replay** button) at a saved run. The engine re-fits the curve from the saved frames
   deterministically, so a re-fit reproduces the same per-position HFR every time. That lets you compare
   settings without touching the telescope.

This replay loop is the fastest way to tune detection: optimize, label any missed or false stars,
re-optimize, and repeat, all from your desk.

→ Full detail: [Autofocus](overview/autofocus.md) and [Star Detection Optimization](optimization/index.md).
