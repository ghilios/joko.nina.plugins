# Camera Simulator Rendering Model

The [Camera Simulator](camera-simulator.md) page covers setting the simulator up and what to use it
for. This page is the supplement: how a frame is actually generated, stage by stage, and the
physical model behind each stage. Nothing here is needed to use the simulator; it is for
understanding why the frames behave like a real camera's.

Every exposure runs the same pipeline. The simulator queries the ASTAP catalog for stars in the
field of view, projects each star onto the sensor, computes its local defocus from the focuser
position and the aberration surface, renders a point-spread function of the right size, converts
the star's magnitude into photoelectrons, and stamps the result into an electron accumulator. A
uniform sky and dark-current background is added, and the accumulated electrons are then developed
into a 16-bit frame by applying photon noise, read noise, gain, and the bias pedestal, pixel by
pixel.

## From catalog to pixel positions

Stars are read from the ASTAP star database for a region covering the sensor's diagonal field of
view plus a 5% margin, down to the configured **Limiting Magnitude**. The database stores stars
sorted bright to faint, so the read stops early once the limit is reached. Coordinates are
Gaia-derived and treated as J2000; the mount's reported pointing is transformed to J2000 before
projection when needed.

Each star is projected with a gnomonic (TAN) tangent-plane projection, the standard projection for
a flat sensor behind an optic. The plate scale comes from the effective focal length and the
sensor's pixel size. Frame rotation is applied in the tangent plane: with a rotator connected the
frame renders at the rotator's mechanical angle plus the **Field Rotation** offset, otherwise at
**Field Rotation** directly. Stars slightly outside the frame are kept when their PSF could spill
onto the sensor, so a half-donut at the edge of a defocused frame looks the way it should.

## From magnitude to electrons

Star brightness comes from a radiometric exposure calculator. A star of magnitude \(m\)
delivers

\[
N_\star = F_0 \cdot 10^{-0.4\,m} \cdot A \cdot \Delta\lambda \cdot QE(\lambda_c) \cdot T \cdot t
\]

photoelectrons over its whole PSF, where:

- \(F_0 = 1.00\times10^{4}\) photons s⁻¹ cm⁻² nm⁻¹ is the zero-magnitude flux density,
  V-referenced (Bessell 1998).
- \(A = \tfrac{\pi}{4} D^2 (1 - \varepsilon^2)\) is the unobstructed collecting area in cm², from
  the aperture \(D\) and the central-obstruction fraction \(\varepsilon\).
- \(\Delta\lambda\) is the selected filter's bandwidth and \(\lambda_c\) its central wavelength.
- \(QE(\lambda_c)\) is the sensor's quantum efficiency, sampled from a piecewise-linear QE curve at
  the filter's central wavelength.
- \(T\) is the **Optical Throughput** setting and \(t\) the exposure time.

This is a deliberate gray-star approximation: every star uses its catalog magnitude with a single
V-referenced zero point, with no per-star color term and no atmospheric extinction. What the model
does capture is the part that matters for focus work: how star signal scales with aperture,
exposure time, filter bandwidth, and sensor QE.

The narrowband behavior falls straight out of the \(\Delta\lambda\) factor. A luminance filter
passes a 320 nm band; an Hα 3 nm filter passes about 1% of that, so the same star delivers about a
hundred times fewer electrons. That is why the simulator reproduces narrowband autofocus problems
(too few stars, noisy HFR) without any special-casing.

### Filters

| Filter | Central wavelength (nm) | Bandwidth (nm) |
|---|---|---|
| L | 540.0 | 320 |
| R | 635.0 | 100 |
| G | 530.0 | 100 |
| B | 465.0 | 100 |
| Hα 5nm / 3nm | 656.3 | 5 / 3 |
| OIII 5nm / 3nm | 500.7 | 5 / 3 |
| SII 5nm / 3nm | 672.4 | 5 / 3 |

The filter's central wavelength is also the wavelength used by the diffraction and defocus model
below, so switching from OIII to SII slightly changes the diffraction-limited PSF.

### Sensors

The four sensor models carry datasheet-derived parameters. All share a Sony
back-illuminated QE curve anchored to a published IMX455 measurement (about 80% peak QE, falling
toward the red: roughly 0.75 at 530 nm, 0.50 at Hα, 0.46 at SII).

| Model | Resolution | Pixel (µm) | Bit depth | Full well (e⁻) | Read noise (e⁻, low → high conversion gain) |
|---|---|---|---|---|---|
| IMX455 | 9576 × 6388 | 3.76 | 16 | 50000 | 3.5 → 1.5 |
| IMX571 | 6248 × 4176 | 3.76 | 16 | 50000 | 2.8 → 1.5 |
| IMX533 | 3008 × 3008 | 3.76 | 14 | 50000 | 3.8 → 1.5 |
| IMX294 | 4144 × 2822 | 4.63 | 14 | 66000 | 7.0 → 1.3 |

### Sky and dark current

The sky background applies the same radiometric formula to the **Sky Brightness** surface
brightness \(\mu\) (mag/arcsec²), multiplied by the solid angle one pixel subtends:

\[
N_{\text{sky}} = F_0 \cdot 10^{-0.4\,\mu} \cdot A \cdot \Delta\lambda \cdot QE(\lambda_c) \cdot T
\cdot t \cdot \Omega_{\text{px}} ,
\]

with \(\Omega_{\text{px}}\) the pixel area in arcsec². Because the sky is broadband too, a 3 nm
filter suppresses it by the same factor it suppresses stars, which is exactly why long narrowband
subs tolerate bright sky.

Dark current follows the usual doubling law: the sensor's datasheet dark current at its reference
temperature, doubled for every 6.5 °C above it (and halved below), times the exposure time. The
**Sensor Temperature** setting feeds this directly.

## The point-spread function

The PSF is a defocused annulus convolved with a Gaussian blur. In focus, the annulus collapses and
the profile reduces to a pure Gaussian whose width combines seeing and diffraction:

\[
\sigma_{\min} = \sqrt{\sigma_{\text{seeing}}^2 + \sigma_{\text{diff}}^2} .
\]

The seeing term converts the **Seeing** FWHM from arcseconds to pixels through the plate scale.
The diffraction term uses the FWHM of an annular-aperture Airy core,

\[
\text{FWHM}_{\text{diff}} = \left(1.0290 - 0.5673\,\varepsilon^2 + 0.3919\,\varepsilon^4\right)
\frac{\lambda\,N}{p} \ \text{px},
\]

for wavelength \(\lambda\), focal ratio \(N\), pixel size \(p\), and obstruction
\(\varepsilon\). The minimum HFR follows as \(\text{HFR}_{\min} = \sqrt{\pi/2}\,\sigma_{\min}
\approx 1.2533\,\sigma_{\min}\).

Kernels are generated numerically from the analytic radial profile, rasterized at 4× oversampling
into sixteen sub-pixel phase variants, and normalized so each integrates to exactly one. Stamping a
star picks the phase kernel nearest its fractional pixel position, so sub-pixel centroids survive
into the rendered frame. Diffraction rings are not modeled; the analytic profile is HFR-exact,
which is what star detection and focus measurement respond to.

With [field astigmatism](#field-astigmatism) enabled the annulus becomes an **ellipse** — the same
profile, with its two semi-axes set independently and its long axis pointed along or across the
field radius. That case is rasterized differently, as an antialiased elliptical-annulus mask
convolved with the seeing Gaussian, but it collapses back to the circular path exactly whenever the
two semi-axes coincide, so a star on the optical axis and a frame with astigmatism switched off
render precisely as they always did.

## Defocus and donuts

Defocus is driven by the focuser. With a step size of \(k\) µm of focus travel per step (the
shared **Focuser Step Size** setting), the sensor sits

\[
\Delta = k \,(\text{steps} - x_0)
\]

microns from perfect focus, where \(x_0\) is **Optimal Focuser Position**. Geometric optics turns
that defocus into an annulus: outer radius \(r_{\text{out}} = |\Delta| / 2N\) at the focal plane,
inner radius \(\varepsilon \cdot r_{\text{out}}\). With a central obstruction enabled the
defocused star is a donut with a dark hole; without one it is a filled disk.

The resulting HFR-versus-position curve is

\[
\text{HFR}(\text{steps}) = \sqrt{\text{HFR}_{\min}^2 + \bigl(\kappa\,(\text{steps} -
x_0)\bigr)^2},
\qquad
\kappa = \frac{k\,(1 + \varepsilon + \varepsilon^2)}{3\,N\,p\,(1 + \varepsilon)} ,
\]

which is the same hyperbolic family the [autofocus curve fit](hyperbola-fitting.md) assumes. This
is by construction, not coincidence: the PSF kernels are sized from this model, so the V-curve that
autofocus measures against the simulator agrees with the V-curve the model predicts.

To keep rendering fast, defocus is quantized so that the donut's outer radius changes by at most a
quarter pixel per level, and one PSF kernel is built and cached per level per exposure. A smooth
tilt across the sensor therefore collapses onto a small set of kernels instead of one per star.
With astigmatism enabled a kernel is identified by both of its defocuses and by its orientation,
which is quantized on the same quarter-pixel budget; a 61-megapixel frame with tilt and backfocus
injected typically builds a few hundred kernels rather than one.

## Tilt and field curvature

With **Enable Aberrations** on, best focus is no longer one focuser position for the whole sensor.
The simulator models the best-focus surface as a tilted paraboloid over sensor coordinates, the
exact surface family the Aberration Inspector's
[sensor model](sensor-model.md) fits: a tilt plane (gradients \(G_x, G_y\)), an isotropic curvature
term \(K\), and an optical-axis offset. Each star's local defocus is the gap between the focuser's
current focus plane and this surface at the star's position, fed into the defocus model above. The
aberrations are a local defocus surface: no coma is modeled, and off-axis astigmatism is a separate
opt-in described [below](#field-astigmatism).

The options map onto the surface in the inspector's own reporting convention, so the values you
inject are the values the inspector reports back:

- **Tilt Angle** is the tilt azimuth, \(\operatorname{atan2}(G_y, G_x)\).
- **Tilt Amount** is the inspector's *Tilt Effect*: the worst-case center-to-corner focus swing
  caused by the tilt plane, in microns.
- **Backfocus Error** is the inspector's *Curvature Effect*: the corner-versus-center offset caused
  by the curvature term, in microns.

Inject 30 µm of tilt at 45° and a Detailed Analysis measures 30 µm at 45°, up to the noise of the
measurement itself. One deliberate subtlety: with a rotator connected the star field rotates, but
the tilt stays fixed to the sensor, because on a real camera the tilted sensor rotates along with
the rotator.

## Field astigmatism

A corrector at the wrong spacing does not simply defocus the corners — it splits focus in two. Rays
in the plane containing the optical axis and the star come to a focus at a different distance from
rays in the perpendicular plane, so instead of one best-focus surface there are two, and a sensor
placed between them sees stars stretched into short lines. **Model Astigmatism** renders that, which
is what makes simulated stars go *eccentric* rather than merely soft.

The two surfaces straddle the surface above, separated by a half-split \(A\):

\[
z_T = z + A, \qquad z_S = z - A, \qquad
A(x,y) = 
ho \, c_m \, e(x,y) \, r'^2 ,
\]

where \(r'\) is the distance from the optical axis and \(e(x,y)\) is the **local** axial spacing
error — the centre spacing error plus the tilt plane, because the tilt plane is literally how far
that patch of sensor has moved along the axis. A star's blur is then an ellipse: its radial extent
comes from \(\lvert \Delta - A 
vert\) and its tangential extent from \(\lvert \Delta + A 
vert\),
where \(\Delta\) is the local defocus from the surface above.

That crossing is the whole behaviour, and it reduces to one rule:

> Stars stretch **radially** where the local defocus and the astigmatism disagree in sign,
> **tangentially** where they agree, and stay round wherever either is zero.

Which way stars point is set by the **sign of the Astigmatism Ratio** — a property of the corrector,
not of which way the spacing is off. Positive gives radially elongated stars, negative tangential
ones. Tilt is what makes the two appear in the same frame: it drives the local defocus positive on
one side of the sensor and negative on the other, so a sensor tilted enough for that to outweigh the
field curvature shows stars pointing at the corners on one edge and lying across the radius on the
opposite edge — the pattern the [Aberration Inspector](tilt-aberration-inspector.md)'s eccentricity
vector field is built to reveal. Where the surface crosses focus the two extents are equal and the
star is round again, but not a point: that is the circle of least confusion, and it is why a corner
on a badly spaced rig never comes to a sharp focus at any focuser position.

One consequence worth knowing before you go looking for it: **reversing the backfocus error renders
an identical frame.** It flips the local defocus and the astigmatism together, and the star's two
extents are unchanged by that pair of flips. Too much and too little spacing are not distinguishable
from star shapes in a single image — you tell them apart by refocusing, because the corners come to
focus on opposite sides of the centre. That is the optics of a first-order model, not a limitation
of the renderer.

Three options control it, inside the Field Aberrations group:

- **Model Astigmatism** turns it on. It is on by default, and does nothing at all until there is a
  spacing error for it to work from.
- **Backfocus Spacing Error** is how far the sensor sits from the corrector's design spacing, in
  microns — the *cause*, where Backfocus Error above is the corner curvature it *produces*. Enter a
  magnitude; the direction comes from the sign of Backfocus Error. Leave it blank and it is inferred
  from Backfocus Error, assuming a nominal flattener where 1 mm of spacing error gives 50 µm of
  corner curvature on a full-frame sensor.
- **Astigmatism Ratio** is how much astigmatism accompanies the field curvature, and its **sign**
  chooses the direction: positive elongates stars radially, negative tangentially. Both terms grow
  together with spacing error on a real corrector and are comparable in size, so a magnitude near 1
  is the physical starting point; the default is 0.7, and 0 disables the effect without touching the
  toggle.

Two things this deliberately does not change. The **inspector still recovers exactly what you
inject**: the two surfaces' mean is the surface it fits, and reversing the defocus swaps the two
extents — which rotates the star 90° without resizing it — so HFR stays symmetric about the same
best focus and the tilt and curvature it reports are unmoved. And with astigmatism switched off, or
its ratio at zero, frames render bit-for-bit as they did before the model existed.

## Noise and readout

The stamped stars plus sky and dark current give each pixel an expected electron count \(\lambda\).
Development converts that to ADU with the full noise chain, per pixel:

1. **Shot noise**: a Poisson draw with mean \(\lambda\) (an exact Poisson sampler below 40
   electrons, a Gaussian approximation above, where the distributions agree to within a percent).
2. **Full-well clamp**: electrons saturate at the sensor's full-well capacity.
3. **Read noise**: a Gaussian draw whose width follows the sensor's gain-dependent read-noise
   curve, including the step down at the dual-conversion-gain threshold (gain 100 on the IMX
   sensors here, 120 on the IMX294).
4. **Digitization**: electrons divide by the e⁻/ADU gain, the **Bias Pedestal** is added, and the
   result clamps to the sensor's bit depth. The gain law is ZWO-style, 0.1 dB per gain unit:
   \(g_{e^-/\text{ADU}} = (\text{full well} / 2^{\text{bits}}) \cdot 10^{-\text{gain}/200}\).

The result is a mono 16-bit frame whose statistics behave like a real camera's: background
standard deviation set by sky shot noise and read noise, faint stars emerging from the noise floor
as exposure grows, and bright stars saturating and flattening.

## Determinism

Every frame is reproducible. The per-exposure noise seed mixes the **Noise Seed** option with the
focuser position and an exposure counter that resets when the camera connects, so repeated
exposures at one position differ (as on a real camera) while a fixed sequence of exposures after
connecting replays identically. Development is partitioned into 64 fixed stripes, each with its own
seeded generator, so the output is bit-identical regardless of how many CPU cores render it.

Rendering starts when the exposure starts and is collected at download, mirroring a real camera's
exposure-then-download rhythm; its parallel loops share the plugin's CPU governor so a render
cannot starve star detection running concurrently during an autofocus. If the catalog is missing
or the field is empty, the simulator still delivers a starless frame of sky, dark current, and
noise rather than failing the exposure.
