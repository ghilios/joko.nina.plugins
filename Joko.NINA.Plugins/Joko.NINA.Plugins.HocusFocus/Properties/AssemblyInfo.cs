#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Allow the test project to access internal types
[assembly: InternalsVisibleTo("Joko.NINA.Plugins.HocusFocus.Tests")]

// Allow the headless diagnostic harness to call internal helpers (BuildStarDetectorParams, the
// StarDetectionOptions ctor) so it reproduces production behavior without drift
[assembly: InternalsVisibleTo("TestApp")]

// [MANDATORY] The following GUID is used as a unique identifier of the plugin
[assembly: Guid("0f1d10b6-d306-4168-b751-d454cbac9670")]

// [MANDATORY] The assembly versioning
//Should be incremented for each new release build of a plugin
[assembly: AssemblyVersion("4.0.0.4")]
[assembly: AssemblyFileVersion("4.0.0.4")]

// [MANDATORY] The name of your plugin
[assembly: AssemblyTitle("Hocus Focus")]
// [MANDATORY] A short description of your plugin
[assembly: AssemblyDescription("Improved Star Detection, Star Annotation, Auto Focus, and Tilt Correction for NINA")]

// The following attributes are not required for the plugin per se, but are required by the official manifest meta data

// Your name
[assembly: AssemblyCompany("George Hilios (jokogeo)")]
// The product name that this plugin is part of
[assembly: AssemblyProduct("Hocus Focus")]
[assembly: AssemblyCopyright("Copyright ©  2026")]

// The minimum Version of N.I.N.A. that this plugin is compatible with
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.2.0.2001")]

// The license your plugin code is using
[assembly: AssemblyMetadata("License", "MPL-2.0")]
// The url to the license
[assembly: AssemblyMetadata("LicenseURL", "https://www.mozilla.org/en-US/MPL/2.0/")]
// The repository where your pluggin is hosted
[assembly: AssemblyMetadata("Repository", "https://github.com/ghilios/hocus-focus")]

// The following attributes are optional for the official manifest meta data

//[Optional] Your plugin homepage URL - omit if not applicaple
[assembly: AssemblyMetadata("Homepage", "https://ghilios.github.io/hocus-focus/")]

//[Optional] Common tags that quickly describe your plugin
[assembly: AssemblyMetadata("Tags", "StarDetection,AutoFocus,Tilt,Aberration,BackFocus,Sensor,Curvature")]

//[Optional] A link that will show a log of all changes in between your plugin's versions
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/ghilios/hocus-focus/commits/develop")]

//[Optional] The url to a featured logo that will be displayed in the plugin list next to the name
[assembly: AssemblyMetadata("FeaturedImageURL", "https://github.com/ghilios/hocus-focus/releases/download/downloads/HocusFocus.jpg")]
//[Optional] A url to an example screenshot of your plugin in action
[assembly: AssemblyMetadata("ScreenshotURL", "")]
//[Optional] An additional url to an example example screenshot of your plugin in action
[assembly: AssemblyMetadata("AltScreenshotURL", "")]
//[Optional] An in-depth description of your plugin
[assembly: AssemblyMetadata("LongDescription", @"This plugin improves Star Detection, Star Annotation, and Auto Focus for NINA. It also includes an Aberration Inspector that measures backfocus and sensor tilt errors, and an optimizer that auto-tunes star detection to your specific rig.

*Special thanks to Frank Freestar8n, Ph.D. Optical Sciences, for his guidance and expertise creating the Sensor Model in the Aberration Inspector*
*Special thanks as well to Steve Smith for his work implementing RANSAC star registration in Aberration Inspection to improve robustness of modeling*

Check out his website at [https://www.smallstarspot.com](https://www.smallstarspot.com)

**Features**

*AutoFocus*

* Concurrent auto-focus engine that analyzes each exposure while the next focus point is still being captured, making it faster than NINA's built-in auto focuser - especially with the more resource-intensive Hocus Focus detector.
* Multiple curve-fitting models: symmetric and asymmetric (tilted) hyperbolas, smooth-blend and uneven-blend variants, plus parabolic and trendline fits, so the model matches how stars actually defocus on each side of focus.
* A ""Hybrid"" mode (the recommended default) refits every model at the end of a run and automatically keeps the one with the least expected error at the best-focus position.
* Weighted fitting that incorporates each point's own measurement error, so noisy frames count for less and a single star-starved frame cannot dominate the fit.
* Robust outlier rejection (iterative two-tailed Grubbs test plus an in-fit Huber down-weighting loop) keeps clouds, satellites, and bad frames from skewing focus.
* A goodness-of-fit accept/reject gate (R-squared or reduced chi-squared) plus an optional before/after HFR-improvement check with automatic retries, so a bad curve never silently sets focus.
* Per-run uncertainty reporting: the standard error of the best-focus position (drawn as an error band on a live V-curve chart) plus a leave-one-out cross-validation of focus stability.
* Save full auto-focus runs (images, star-detection results, annotated frames) and replay them later with different settings. Refits are deterministic, reproducing the same per-position HFR every time, so you can tune indoors without clear skies or touching the telescope.

*Improved Star Detection*

* A purpose-built, gradient-aware star detector that measures each star's HFR empirically from a robust local-background fit, for lower HFR scatter across a frame and cleaner, more repeatable measurements.
* Nebula and gradient removal via a-trous wavelet structure detection, and a per-star tilted background plane (robust Huber least-squares) so one-sided gradients from galaxies, nebulae, or bright neighbors don't bias each star's centroid, flux, or HFR.
* Gradient-robust contamination rejection: an eight-sector residual test flags (and optionally removes) a neighbor bleeding into one side of a star's measurement annulus, while ignoring smooth gradients and edge clipping.
* Optional PSF modeling (Gaussian or Moffat, with the beta exponent fixed or fit per star) yielding per-star FWHM and eccentricity; stars that fail the goodness-of-fit gate fall back to their empirical HFR.
* Out-of-focus donut star recovery: opt-in defocus-aware modes add wavelet layers and relax the distortion and centering gates so bloated donut stars survive at sweep extremes, plus a dedicated mode that reconnects fragmented hollow rings for scopes with a central obstruction (Newtonians/SCTs).
* Saturated-star handling (clipped cores masked during the PSF fit rather than discarding the star), hot-pixel filtering, optional noise reduction, and robust median/MAD frame-level aggregation.
* Multiple independent acceptance gates (clipped, distorted, off-center, flat, dim, contaminated), each tracked as a named rejection count you can inspect to see exactly why candidates were dropped.
* Simple mode driven by three plain-language presets (Noise Level, Pixel Scale, Focus Range) with image-scale-aware defaults, plus an Advanced mode that exposes the individual detection, gating, and PSF parameters for hand-tuning unusual setups.

*Star Detection Optimizer*

* A guided Optimization Wizard that auto-tunes the detection settings for your exact optical train, camera, and sky by replaying your saved auto-focus runs and searching for the parameters that produce the cleanest, most repeatable focus curve.
* Results are safe and reversible: optimized settings are stored separately and activated by a single toggle that appears only after a successful run, and the wizard never hands back a result worse than your current settings.
* Improvement is reported honestly against what your rig does today, the wizard can seed from defaults or from your current settings, and it recommends an auto-focus step size tailored to your focal ratio, pixel scale, and focuser.
* Selectable objectives: default auto-focus repeatability, an ""Optimize for Aberration Inspection"" objective that recovers far more stars across the frame, and a ""Recover out-of-focus donut stars"" mode for defocus-aware tuning.
* ""Continue optimizing"" runs successive refinement passes, and optional hand-drawn bounding-box labels add a recall/precision term that pushes the optimizer toward your own judgment on hard frames.

*Aberration Inspector & Tilt Correction*

* A dockable inspector that runs a multi-region auto-focus across the center and four corners at once to measure sensor tilt, backfocus, and field curvature, grading each against the critical focus zone computed from your focal ratio.
* Backfocus is measured even when tilt is present, reported in microns, so you can tell a too-far sensor apart from a tilt problem.
* A fast 4-Corners tilt-plane model (always computed) for a quick corner-vs-center check, plus an opt-in full Sensor Curve Model (a tilted-paraboloid surface fit, similar in spirit to CCD Inspector) that separates tilt, field curvature, and sensor centering with one-sigma uncertainties on every readout.
* RANSAC cross-frame star registration aligns every frame to a reference before matching stars, staying reliable even at the bloated, sparse, defocused ends of the sweep (with an optional 6-DOF affine transform and automatic fallback for hard frames).
* Robust surface fitting (per-star hyperbolic curves, inverse-variance weighting, iterative MAD-based outlier rejection) plus optional astigmatic (independent X/Y) curvature and free sensor-centering estimation.
* Visualizations: an FWHM contour map, an eccentricity vector field, and an interactive 3D sensor-tilt surface, with scroll/pan/zoom charts. A lighter single-exposure Simple Analysis gives a quick visual read without a full sweep.
* Tilt Adapter Wizard: a guided calibration that learns where each adapter screw sits relative to the sensor (deriving winding direction from the data, so mirrors and rotators don't fool it), supporting both 3-screw and 4-screw adapters.
* Concrete per-screw guidance: which screw to turn, which direction, and how far - as focuser steps, microns, or actual screw turns / stepper steps using your adapter's hardware model (pick a Device preset or enter values manually), with a cross-check warning when measured movement diverges from the configured pitch.
* Save and replay calibration runs (re-analyze an at-the-scope calibration indoors later), and a ""Run Aberration Inspector"" sequence instruction for unattended analysis inside a NINA sequence.

*Customizable Star Annotation*

* A fully customizable overlay drawing detected-star markers, per-star labels, center reticules, and the ROI box directly on the image NINA shows you, with configurable bounds shape (box, ellipse, or fitted PSF ellipse), fonts, point size, and ARGB colors for every element.
* Choose what each star is labeled with: HFR, FWHM (arcseconds or pixels, including per-axis), eccentricity, PSF rotation, background, PSF peak, or Moffat beta.
* Color-coded per-reason rejection diagnostics show exactly why candidates were thrown out, and changing any annotation option re-renders the overlay immediately without re-running detection. On dense fields you can label only the brightest N stars.

The plugin is deliberately modular: use the new star detector or the new annotator independently and keep whichever pieces you like, leaving the rest on NINA's defaults.

To enable these features, go to Options -> Imaging -> Image Options. After this plugin is installed, there will be Star Detection, Star Annotator, and Auto Focus dropdown boxes you can select ""Hocus Focus"" for. The concurrent auto-focus engine and the Aberration Inspector both require ""Hocus Focus"" to be selected for both Auto Focus and Star Detection.

# Getting Help #

If you have questions, come ask in the **#plugin-discussions** channel on the NINA [Discord chat server](https://discord.com/invite/rWRbVbw).
* Hocus Focus is provided 'as is' under the terms of the [Mozilla Public License 2.0](https://www.mozilla.org/en-US/MPL/2.0/)
* Source code for this plugin is available at this plugin's [source code repository](https://github.com/ghilios/hocus-focus)
")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]
// [Unused]
[assembly: AssemblyConfiguration("")]
// [Unused]
[assembly: AssemblyTrademark("")]
// [Unused]
[assembly: AssemblyCulture("")]