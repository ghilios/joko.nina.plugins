# Synthetic Star-Field Camera — Design

## Problem & goal

HocusFocus (autofocus, star detection, sensor/tilt aberration inspection) has no way to produce **repeatable,
physically-known** test frames. Improvements to any of those subsystems are validated against real sky or a small
bank of captured runs, with no ground truth for star positions, HFR-vs-focuser behavior, or injected sensor tilt.

This adds a **synthetic monochrome camera device** for NINA, provided by the HocusFocus plugin. On each exposure it
reads the connected focuser (defocus) and mount (pointing), projects real catalog stars onto a chosen sensor,
renders a physically-correct PSF (aperture diffraction + central-obstruction donut + seeing + defocus), applies a
physically-correct photon→electron→ADU noise model, and returns a 16/14-bit frame. Outcome: a demo/teaching camera
**and** a regression harness — render a frame (or a stepped focuser run), feed it to HocusFocus's own detector /
autofocus / aberration inspector, and assert recovery of the known truth.

## Key decisions

- **Star source — real ASTAP catalog.** Project real star RA/Dec/magnitude from the mount pointing onto the sensor.
- **ASTAP vs PixInsight GAIA DR3 (the consideration-3a question).** The ASTAP DB binary format **is** fully
  documented — the author publishes a byte-level spec and ships an MPL-2.0 reference reader (`unit_star_database.pas`)
  with trivial fixed-width, uncompressed records — so no clarification / GAIA fallback is needed. ASTAP is also the
  better fit regardless: ~100 MB–1 GB on disk (vs 12–100 GB for GAIA XPSD), usually already installed by NINA users
  for plate solving, permissive license, and a ~100-line reader. PixInsight GAIA XPSD would require porting a
  quadtree + LZ4/Zstd-compressed C++ reader under a non-OSI license for zero simulator benefit. **→ Build a small
  C# ASTAP reader.**
- **PSF — full physical.** Aperture diffraction + configurable central obstruction (toggle + fraction) + defocus, so
  the out-of-focus donut and its central hole are accurate.
- **Defocus sizing — real focuser travel.** The donut/HFR growth is driven by focuser step size (µm/step) and the
  physical optics, **not** by a configured "±range → 4× HFR". HFR at best focus comes from diffraction ⊕ seeing.
  (Deliberately supersedes the original consideration-2 "focuser range = 1000" idea; optimal focuser position is
  still configured.)
- **Noise — physically correct.** Read-noise-limited at short exposures, shot/sky-limited at long; star SNR improves
  with exposure even though absolute background noise rises with √t.
- **Sensors — Sony CMOS set:** IMX455, IMX571, IMX533, IMX294. The chosen sensor fixes resolution, pixel size, QE
  curve, read-noise/gain behavior, dark current, full well, and bit depth. **Monochrome only**, with a clean seam to
  add a Bayer CFA later.
- **Filters:** L, R, G, B, and narrowband Hα/OIII/SII at 3 nm and 5 nm. Wider bandpass passes more light → shorter
  exposure for equal signal.
- **Field aberrations — modeled now**, behind an Enable toggle, with **tilt angle**, **tilt amount**, and
  **backfocus**, parameterized to match HocusFocus's own best-focus-surface model so the aberration inspector
  recovers what the simulator injects (and a future tilt-adapter model can drive the knobs).

All required dependencies already ship with the plugin (**OpenCvSharp4** `Cv2.Dft`, **MathNet.Numerics** Poisson/Normal/erf,
**alglib**) — no new NuGet references.

---

## Math foundations

Everything is computed in **electrons (e⁻)** until the final ADC step, through one shared photon→electron→ADU
pipeline, so each mean and its variance use the same constants.

### Shared geometry

- Aperture net collecting area: `A = (π/4)·D²·(1 − ε²)` cm² (D in cm, ε = central-obstruction fraction).
- Focal ratio `N = f/D`. Plate scale `arcsec/px = 206.265·p/f` (p = pixel µm, f = focal length mm).
- Pixel solid angle `Ω_px = (arcsec/px)²` arcsec².

### Radiometry — star brightness vs exposure (consideration 8)

Photometric zero point, V-referenced (Bessell 1998: `F_λ(0,V) = 3.631×10⁻⁹ erg/s/cm²/Å`, ÷ `E_ph = hc/λ`):

```
F0 = 1.00×10⁴ ph·s⁻¹·cm⁻²·nm⁻¹    (magnitude 0)
Φ(m) = F0 · 10^(−0.4·m)            ph·s⁻¹·cm⁻²·nm⁻¹
```

Star electrons collected:

```
Ne_star = F0 · 10^(−0.4·m) · A · Δλ · QE(λc) · T · t        [electrons]
```

m = magnitude (V-referenced, gray-star approximation), Δλ = filter bandwidth (nm), QE(λc) = sensor QE at the filter
center (narrowband: at the exact line), T = optical throughput (default 0.85), t = exposure (s). Ne is then
distributed over the normalized PSF kernel (Σ = 1): `N_star(i,j) = Ne_star · kernel_ij`.

Bandwidth drives exposure: L (Δλ ≈ 320 nm) collects ≈ 93× the electrons of a 5 nm Hα filter at equal QE.
Exposure-parity ratios (t needed to match L's electrons for the same star):

| Filter | λc (nm) | Δλ (nm) | QE(λc) | t / t_L |
|---|---|---|---|---|
| L | 540 | 320 | 0.730 | 1.00 |
| B | 465 | 100 | 0.808 | 2.89 |
| G | 530 | 100 | 0.750 | 3.11 |
| R | 635 | 100 | 0.542 | 4.31 |
| OIII 5 nm | 500.7 | 5 | 0.780 | 59.9 |
| Hα 5 nm | 656.3 | 5 | 0.500 | 93.4 |
| SII 5 nm | 672.4 | 5 | 0.460 | 101.6 |
| OIII 3 nm | 500.7 | 3 | 0.780 | 99.8 |
| Hα 3 nm | 656.3 | 3 | 0.500 | 155.7 |
| SII 3 nm | 672.4 | 3 | 0.460 | 169.3 |

### Sky background (feeds brightness realism + noise)

```
Ne_sky = F0 · 10^(−0.4·μ_sky) · A · Δλ · QE(λc) · T · t · Ω_px    [e⁻/px]
```

μ_sky = sky surface-brightness density (mag/arcsec², V-referenced). Narrowband sky ∝ Δλ falls out automatically
(64× less through 5 nm than L at the same density), which is why NB tolerates long subs. Defaults (mag/arcsec²):

| Filter class | Dark (Bortle 1–3) | Suburban (5–6) | Urban (8–9) |
|---|---|---|---|
| L | 21.5 | 19.5 | 18.0 |
| R | 21.0 | 19.5 | 18.2 |
| G | 21.9 | 20.0 | 18.5 |
| B | 22.3 | 20.3 | 18.7 |
| NB (all) | 21.5 | 21.0 | 20.5 |

(A single `SkyBrightnessMagPerArcsec2` config, default 20.5, drives all filters via the Δλ scaling; the per-filter
table is an optional refinement.)

### Noise model & sampling (consideration 7)

Per-pixel electron budget: `λ_e = N_star + Ne_sky + N_dark`, with `N_dark = I_dark(T_sensor)·t` and dark-current
temperature scaling `I_dark(T) = I_ref · 2^((T − T_ref)/6.5)` (6.5 °C doubling for these CMOS; 5.8 °C for the
KAF-8300 if ever added).

```
ne  = Poisson(λ_e)               if λ_e < 1000       # exact shot noise
    = round(Normal(λ_e, √λ_e))   otherwise            # CLT approximation
ne  = min(ne, FullWell)                              # analog full-well clip
e   = ne + Normal(0, σ_read(gain))                   # read noise (gain/HCG dependent)
adu = round(e / g_e(gain)) + pedestal                # e⁻ → ADU + bias offset
ADU = clamp(adu, 0, 2^bits − 1)                      # digital clip (enforces N_sat)
```

- Gain law (ZWO-style slider, g in 0.1 dB units): `g_e(g) = (FullWell / 2^bits) · 10^(−g/200)` e⁻/ADU. Digital
  saturation `N_sat(g) = min(FullWell, g_e(g)·(2^bits − 1))` — at high gain the digital clip is tighter than the well.
- Read noise `σ_read(g)`: per-sensor piecewise-linear with a step down at the HCG threshold (dual conversion gain).
- Bias pedestal default **500 ADU (16-bit) / 125 ADU (14-bit)** so read noise never clips at zero. This yields
  calibratable bias/dark/flat frames for free.
- SNR = `N_star / √(N_star + N_sky + N_dark + σ_read²)`: ∝ t when read-noise-limited (short), ∝ √t when shot/sky
  limited (long). Sky-limited crossover `t_c = σ_read² / (R_sky + R_dark)` (≈ 6.5 s for IMX455 / L / gain 100 /
  dark site → ~60 s subs are sky-limited, matching field practice; ≈ 154 s for 5 nm Hα → multi-minute NB subs).
- Photon-transfer validation invariant: variance-vs-mean (flat, in ADU) has slope `1/g_e`, intercept `(σ_read/g_e)²`.

**Saturation is physical, not avoided.** At D=100 mm f/8, gain 100, a mag-10 star saturates in ~6 s; in a 60 s L sub
it renders as a saturated core with correct unsaturated wings. The "strong but unsaturated" exemplar at those
settings is ~mag 13 (peak ≈ 42 kADU, SNR ≈ 100). The simulator reproduces saturation.

### Optics — PSF / HFR / defocus (consideration 4)

**In-focus size.** Annular-Airy FWHM (obstruction shrinks the core):

```
FWHM_diff(ε) = (1.0290 − 0.5673·ε² + 0.3919·ε⁴) · (λ/D)   [rad]   (max error 3e-4 for ε ≤ 0.5)
```

In pixels `FWHM_diff,px = FWHM_diff(ε) · λ_µm·N / p`; `σ_diff = FWHM_diff,px / 2.3548`. Seeing (FWHM s arcsec) as a
Gaussian `σ_see = (s / 2.3548) / (arcsec/px)`. Combined in-focus:

```
σ_min = √(σ_diff² + σ_see²)        HFR_min = 1.2533 · σ_min      [px]
```

(The flux-weighted radius of a bare Airy pattern diverges logarithmically; the FWHM-matched Gaussian is the correct
practical model and matches what detectors measure.)

**Defocused PSF.** Pupil = annulus `[εD/2, D/2]` with defocus phase `exp(i·(2π/λ)·W20·(ρ/(D/2))²)`;
`PSF = |F{pupil·phase}|²`. Near focus → Airy-like core with rings; W20 ≳ 1–2λ → near-uniform **donut** whose
inner/outer radius ratio → ε (the obstruction directly sets the hole). Geometric-optics limit for focus error Δ (µm
of sensor displacement):

```
r_out = |Δ| / (2N)      r_in = ε · r_out      (focal-plane µm; ÷ p for px)
annulus HFR = g(ε) · r_out,   g(ε) = (2/3)·(1 + ε + ε²)/(1 + ε)     [g(0)=2/3, g(0.3)=0.713, g(1)=1]
```

**Focuser → defocus (physical-travel mode).** `Δ = k·(x − x0)` with **k = FocuserStepSizeMicrons**;
`W20 = Δ/(8N²)` (paraxial; ≤ 1.2 % high at f/4). The resulting HFR-vs-position curve is a hyperbola matching the
autofocus fit within ~3 %:

```
HFR(x) ≈ √( HFR_min² + (κ·(x − x0))² )      κ = k·(1 + ε + ε²) / (3·N·p·(1 + ε))   [px/step]
```

So the physical PSF automatically produces a correct autofocus V-curve. The old "±range → 4× HFR" is now a computed
readout `range = √15·HFR_min/κ` (√15 from 4² − 1²), not an input. It maps to the codebase hyperbola
`HFR = (a/b)√((x−x0)²+b²)+y0` as `a = HFR_min, b = range/√15, y0 = 0`.

**Kernel rendering (recommended: analytic annulus ⊛ Gaussian).** Radial profile (σ = σ_min·p µm):

```
I(r) = 1/(π(r_out²−r_in²)σ²) · ∫_{r_in}^{r_out} R·e^{−(r−R)²/(2σ²)}·[e^{−rR/σ²} I₀(rR/σ²)] dR
```

Recipe: per exposure build a ~512-entry radial LUT (Simpson, ~100 R-samples), rasterize on a 4× oversampled grid,
box-bin S×S into sub-pixel-phase kernels; normalize Σ=1; stamp `kernel·flux` per star. Kernel HFR validated in closed
form via the Rice distribution:

```
E[r|R] = σ√(π/2)·e^{−t/2}[(1+t)I₀(t/2) + t·I₁(t/2)],  t = R²/(2σ²)
HFR_kernel = ∫_{r_in}^{r_out} R·E[r|R] dR / ((r_out²−r_in²)/2)
```

**FFT path (optional high-fidelity toggle).** `|Cv2.Dft(pupil·phase)|²` on an M×M grid with K samples across D:
image sampling `δx = λN·K/M`, need `K ≥ 16·W20/λ + margin` and oversample ≥2×; multiply by the seeing OTF. Adds real
diffraction rings + Fresnel donut-edge ripple (HFR differs from analytic by ≲3–8 % near W20 ≈ 1–2λ). Use for
|W20| ≤ ~3λ and offline validation; analytic is default (HFR-exact, ~100× cheaper).

**Sanity check** (D=100 mm, f=800 mm/N=8, ε=0.3, λ=550 nm, p=3.76 µm, s=2.5″): plate scale 0.969″/px, HFR_min 1.50 px,
at 4× HFR_min the donut OD ≈ 16.3 px with focus error Δ ≈ 491 µm ⇒ k ≈ 0.49 µm/step, W20 ≈ 1.74λ — consistent, and
matches the small donuts seen on obstructed scopes at 4× HFR.

### Field-aberration surface (tilt / backfocus)

Mirror the inspector's fitted **tilted-paraboloid best-focus surface** (`Inspection/SensorParaboloidModel.cs`) so
inject ⇄ recover is exact. For a star at pixel (px,py), centered microns `x=(px−W/2)·pixelSize`, `y=(py−H/2)·pixelSize`:

```
zBestFocus(x,y) = Gx·(x−X0) + Gy·(y−Y0) + Kx·(x−X0)² + Ky·(y−Y0)² + Z0     [µm of focuser travel]
localDefocusMicrons = currentFocuserMicrons − zBestFocus(x,y)
```

Render the PSF (above) at `localDefocusMicrons` (÷ k for steps). It is a **local-defocus model** — no coma —
which is precisely what makes it the inspector's inverse. Config knobs → surface:

- **Tilt angle** = azimuth φ (deg, inspector convention CW from "up"); **tilt amount** → gradient magnitude
  `|G| = tan(θ)`: `Gx = |G|·cos φ`, `Gy = |G|·sin φ`. Inspector reports `Theta = atan|G|` (deg) and `TiltEffectMicrons`.
- **Backfocus** = isotropic field-curvature `K = Kx = Ky` (1/µm): corner effect
  `CurvatureEffectMicrons = K·(halfW² + halfH²)`, radius `R_mm = 1/(2000·|K|)`; sign = inward/outward spacing.
- `X0, Y0` (optical-axis offset) default 0.

**Off-axis astigmatism** was added later, on top of this surface rather than in place of it: the surface splits
into a tangential/sagittal pair straddling the one above, so stars render elliptical (radial in one corner,
tangential in the opposite) while the mean — and therefore everything the inspector fits — is unchanged. See
[`camera-simulator-astigmatism-design.md`](camera-simulator-astigmatism-design.md) for the model, the sign
convention, and the elliptical rasterizer; [`camera-simulator-astigmatism-results.md`](camera-simulator-astigmatism-results.md)
for its measured cost.

Because the camera reads the live focuser position each exposure, a stepped autofocus/inspector run naturally
recovers the injected surface: each region's HFR-vs-focuser minimum lands at `zBestFocus(region)`.

---

## Sensor reference (datasheet-derived; hard-coded in `SensorRegistry`)

Model peak absolute QE for the Sony BSI family at **~80 %** (vendor 90–91 % are optimistic relative-response
figures; IMX455 measured ~80 % in arXiv:2302.03700). Shared QE curve anchors (interpolate, clamp outside):

| λ (nm) | 450 | 475 | 500 | 530 | 656 | 672 |
|---|---|---|---|---|---|---|
| QE | 0.82 | 0.80 | 0.78 | 0.75 | 0.50 | 0.46 |

Hα/SII are ~55–65 % of the green peak — the deep-sky NB penalty is real.

| Sensor | Camera(s) | W×H (px) | Pixel µm | Bits | Full well e⁻ | Read noise e⁻ (gain0 / HCG@thr / min) | Dark e⁻/px/s | Gain0 e⁻/ADU |
|---|---|---|---|---|---|---|---|---|
| IMX455 | ASI6200MM, QHY600M | 9576×6388 | 3.76 | 16 | ~50 k | 3.5 / 1.5 @100 / 0.9 | 0.011 @−10 °C | 0.763 |
| IMX571 | ASI2600MM, QHY268M | 6248×4176 | 3.76 | 16 | ~50 k | 2.8 / 1.5 @100 / 1.0 | 0.0022 @0 °C | 0.763 |
| IMX533 | ASI533MM | 3008×3008 | 3.76 | 14 | ~50 k | 3.8 / 1.5 @100 / 1.0 | ~0.002 @0 °C | 3.052 |
| IMX294 | ASI294MM | 4144×2822 | 4.63 | 14 | ~66 k | 7.0 / 1.3 @120 / 1.2 | 0.0022 @−20 °C | 4.028 |

QE, read-noise, dark, and full-well tables are per-sensor and worth exposing as editable data later.

---

## ASTAP catalog format (reader spec)

- **Files:** one per sky cell; extension = partition count — `.1476` (1476 cells ≈5°, current D-series) and `.290`
  (290 cells, older). Auto-detect which set is present under the configured path.
- **Cell selection `find_areas(ra,dec,fov)`** → the 1–4 cell files intersecting the FOV (Dec-ring + per-ring RA
  division from the reference reader).
- **Per file:** 110-byte text header with record size (5- or 6-byte layout) + Dec-zone info; records sorted
  bright→faint ⇒ stop reading at the limiting magnitude.
- **Decode:** RA = 24-bit unsigned × `2π/(256³−1)`; Dec = two's-complement × `(π/2)/(128·256²−1)` (high byte from the
  magnitude-header record); magnitude via periodic `FF FF FF` header records, `mag = (byte − 16)/10`; 6-byte records
  add color `(B−V)·50` (unused for mono v1, kept for future per-channel color).
- Port field-for-field from the MPL-2.0 reference reader `unit_star_database.pas` (han-k59/astap). License: reader
  MPL-2.0 (friendly to porting); DB data are Gaia-derived, freely downloadable, credit ESA/Gaia/DPAC.

---

## Architecture summary

New folder `Joko.NINA.Plugins.HocusFocus/CameraSimulator/` (+ `Catalog/`, `Rendering/`, `Sensors/`), interfaces in
`Interfaces/`. The device is modeled on NINA's `SimulatorCamera` (`BaseINPC, ICamera, ITelescopeConsumer`, DI'd with
`IProfileService, ITelescopeMediator, IExposureDataFactory, IImageDataFactory`), adding `IFocuserMediator`. It is
registered via `[Export(typeof(IEquipmentProvider))] IEquipmentProvider<ICamera>` — the plugin's first equipment
export. Options persist through `IPluginOptionsAccessor` (modeled on `AutoFocus/InspectorOptions.cs`), held as a
static singleton in `HocusFocusPlugin`, surfaced as a new tab in `Options.xaml` / `Resources/OptionsDataTemplates.xaml`.
`DownloadExposure` builds a row-major `ushort[width*height]` and returns
`exposureDataFactory.CreateImageArrayExposureData(...)` after `metaData.FromCamera(this)`.

The exposure pipeline: snapshot pointing + focuser at `StartExposure` → ASTAP query → TAN projection →
per-star local defocus (aberration surface) → build PSF kernel(s) per quantized defocus level → stamp `kernel·flux`
into a `float[]` accumulator → add sky + dark → Poisson/read noise → `ushort[]`. Exposures **fail with a descriptive
error** if the focuser or telescope is disconnected. See `plans/synthetic-camera-plan.md` for the component
breakdown, file layout, phased steps, and test matrix.
