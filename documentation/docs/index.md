# Hocus Focus

**Hocus Focus** is a plugin for [NINA](https://nighttime-imaging.eu/) (Nighttime Imaging 'N' Astronomy)
that provides improved **star detection**, customizable **star annotation**, a concurrent
**autofocus** engine, and a **tilt / aberration inspector** that measures backfocus (the spacing
between the corrector/flattener and the sensor) and sensor-tilt errors. It measures HFR with a robust,
gradient-aware star detector and fits PSF models to stars for eccentricity and FWHM measurements; lets
you swap in just the star detector or just the annotator without taking both; and builds a full sensor
tilt-and-curvature model so you can quantify and correct aberrations across the field.

!!! tip "New here? Start with the Quick Start"
    The **[Quick Start](quick-start.md)** walks you from a fresh install through setting up detection,
    autofocus, and annotation, optimizing star detection, and checking your rig for tilt and backfocus
    errors, linking to the deeper reference pages as you go.

![Star field with accepted stars circled in green and rejected detections in pink, each accepted star labeled with its measured HFR](assets/figures/annotation-overlay.png){ width=620 }

*Hocus Focus star detection: accepted stars (green) carry an HFR label, while rejected candidates (pink) are marked by the gate that excluded them.*

## Key features

- **[Improved star detection](overview/star-detection.md)** — accurate, gradient-aware HFR measurement
  plus optional Gaussian and Moffat-4 PSF fitting for eccentricity and FWHM, with simple defaults and an
  advanced mode for fine tuning.
- **[Customizable star annotation](overview/star-annotation.md)** — configurable colors and fonts, with
  dynamic reloading of annotations without re-running detection.
- **[Concurrent autofocus](overview/autofocus.md)** — analyzes exposures while the next focus points are
  still being captured, and can save and replay AF runs with different settings.
- **[Tilt / aberration inspector](overview/tilt-aberration-inspector.md)** — generates a sensor tilt and
  curvature model, measuring backfocus error even in the presence of tilt, plus FWHM contour maps and
  eccentricity vector fields.

!!! note "Mix and match"
    You can use the new star detector or the new annotator independently; keep whichever you like.
    To enable the autofocus and aberration-inspector features, Hocus Focus must be selected for both
    Auto Focus and Star Detection.

## Installation

Hocus Focus is published through NINA's in-app plugin manager: open **Plugins → Available**, find
**Hocus Focus**, and install it. To turn on the star detector and annotator afterward, go to
**Options → Imaging → Image Options** and select **Hocus Focus** in the Star Detection, Star Annotator,
and Auto Focus dropdowns.

!!! tip "Minimum NINA version"
    Hocus Focus requires NINA **3.2.0.2001** or newer.

## How this documentation is organized

- **[Quick Start](quick-start.md)** — the recommended first-run path: set up detection, autofocus, and
  annotation, optimize, then check for tilt and backfocus.
- **[Overview](overview/index.md)** — a tour of star detection, annotation, autofocus, and the tilt /
  aberration inspector, with the concepts and figures behind each.
- **[Star Detection Settings](settings/index.md)** — a complete reference for every star-detection
  setting: what it does, when it helps, and when it can hurt.
- **[Star Detection Optimization](optimization/index.md)** — a technical deep-dive into the optimization
  approach: the objective function, the search algorithm, and how each setting factors in.

## License and source

Hocus Focus is provided 'as is' under the terms of the
[Mozilla Public License 2.0](https://www.mozilla.org/en-US/MPL/2.0/) by George Hilios (jokogeo). The
source code lives at [github.com/ghilios/hocus-focus](https://github.com/ghilios/hocus-focus).

!!! tip "Getting help"
    Questions are welcome in the **#plugin-discussions** channel on the NINA
    [Discord server](https://discord.com/invite/rWRbVbw).
