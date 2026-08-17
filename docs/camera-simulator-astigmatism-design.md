# Camera Simulator — Astigmatic Tilt & Backfocus (Star Eccentricity) — Design

## Problem & goal

The synthetic camera renders a **rotationally symmetric** PSF at every field point.
`AberrationSurface` produces one scalar defocus Δ per point and `PsfKernelGenerator` renders an
annulus⊛Gaussian of that size — so injected tilt and backfocus change star *size* across the field but
never star *shape*. The rendering manual states the limitation outright: *"stars blur and donut
asymmetrically across the field, but no coma or astigmatism stretching is modeled."*

That leaves the simulator unable to reproduce the single most diagnostic symptom of a real
tilt/backfocus fault: **eccentric stars** — smeared radially in one corner, tangentially in the opposite,
round in a sweet spot displaced off-center. HocusFocus already *measures* that signal
(`StarDetection/PSFModel.cs` reports `Eccentricity = √(1 − b²/a²)` and `ThetaRadians`; `InspectorVM`
draws the eccentricity vector field) but nothing predicts it, so the measurement has never been
validated against known truth.

This design adds off-axis astigmatism to the injected aberration model, behind a toggle that defaults to
enabled, so that inject ⇄ recover closes on *shape* as well as on the best-focus surface.

Physics background: [`backfocus-eccentricity-modeling.md`](backfocus-eccentricity-modeling.md). This
design implements its **Tier 1** (geometric anisotropic blur). Tier 2 (pupil-plane Fourier optics) is an
explicit non-goal — see [Non-goals](#non-goals).

## Key decisions

- **Astigmatism is driven by the *local* axial spacing error**, not by the backfocus knob alone. A tilted
  sensor genuinely sits at the wrong spacing over most of its area, so pure tilt produces eccentricity on
  its own. The reference doc's Tier 1 puts tilt only in the common plane term, which makes pure tilt
  perfectly round; that is a simplification we deliberately do not adopt.
- **The two astigmatic surfaces straddle today's surface.** Their mean is exactly the existing
  tilted-paraboloid best-focus surface, which is what preserves the inspector's inject ⇄ recover identity
  (proved below, not merely asserted).
- **The rendered kernel is an elliptical annulus ⊛ isotropic Gaussian** — the exact Tier-1 kernel, not the
  reference doc's Gaussian-covariance approximation, and it keeps the central-obstruction donut hole that
  the current renderer models exactly.
- **Blank-means-infer for the spacing error.** The existing `BackfocusErrorMicrons` knob is the *corner
  curvature effect*, not the spacer error; rather than redefine a persisted option, a new spacing field
  defaults to unset and is inferred from a documented nominal constant.
- **`BackfocusErrorMicrons` ships nonzero (50 µm)** so the effect is visible without hunting for knobs.

## The model

### Two surfaces whose mean is today's surface

In centered sensor microns (`x' = x − X0`, `y' = y − Y0`, `r'² = x'² + y'²`), valued in µm of focuser
travel:

$$z_{\text{mean}}(x,y) = G_x x' + G_y y' + K r'^2 + Z_0$$

unchanged — this is `AberrationSurface.ZBestFocusMicrons`, the algebraic inverse of
`Inspection.SensorParaboloidModel`. The astigmatic half-split is

$$e(x,y) = e_c + G_x x' + G_y y' \qquad [\mu m]$$
$$c_m = \begin{cases} K / e_c & e_c > 0 \\ c_{m0} & \text{otherwise}\end{cases} \qquad [\mu m^{-2}]$$
$$A(x,y) = \rho \, c_m \, e(x,y) \, r'^2 \qquad [\mu m]$$

and the two focal surfaces are

$$z_T = z_{\text{mean}} + A \qquad z_S = z_{\text{mean}} - A \qquad \tfrac{1}{2}(z_T + z_S) \equiv z_{\text{mean}}$$

- $e(x,y)$ is the **local** axial spacing error: the configured center spacing error $e_c$ plus the tilt
  plane, which is literally how far that patch of sensor has moved along the optical axis.
- $c_m$ is the corrector's residual field curvature per µm of spacing error, and $\rho = c_a/c_m$ is the
  reference doc's dimensionless astigmatism-to-curvature ratio — the constant it says to calibrate
  empirically.

**There is no singularity.** Leaving the spacing field blank infers $e_c = K / c_{m0}$, which makes
$c_m \equiv c_{m0}$ identically. And when `BackfocusErrorMicrons = 0` — so $K = 0$ and the inferred
$e_c = 0$ — the fallback keeps $c_m = c_{m0}$ and

$$A(x,y) = \rho \, c_{m0} \, (G_x x' + G_y y') \, r'^2 \neq 0$$

i.e. **pure tilt still produces eccentricity**, which is the whole reason this model was chosen over the
simpler backfocus-only form.

### The inference constant $c_{m0}$

Pinned to a nominal flattener: 1 mm of spacing error produces 50 µm of corner curvature effect on a
full-frame corner ($R_c = 21.63$ mm for a 36 × 24 mm sensor):

$$c_{m0} = \frac{50}{1000 \times 21633^2} = 1.0684 \times 10^{-10} \ \mu m^{-2}$$

It multiplies $r'^2$, so it scales correctly to smaller sensors — the same spacing error produces less
corner curvature on a smaller chip, as it should. Exposed as
`AberrationSurface.NominalCurvaturePerSpacingPerAreaMicrons`.

### Per-star blur geometry

$$\Delta = z_{\text{focuser}} - z_{\text{mean}}(x,y) \qquad \Delta_T = \Delta - A \qquad \Delta_S = \Delta + A$$
$$\theta = \operatorname{atan2}(y', x')$$
$$a_{\text{rad}} = \frac{\lvert \Delta_T \rvert}{2N p} \qquad a_{\text{tan}} = \frac{\lvert \Delta_S \rvert}{2N p} \qquad [\text{px}]$$

with inner radii $\varepsilon a_{\text{rad}}$, $\varepsilon a_{\text{tan}}$. $\Delta$ is today's
`LocalDefocusMicrons`, unchanged. $\theta$ is measured about the **optical axis** $(X_0, Y_0)$, not the
sensor centre.

Note the crossing: the *tangential-ray* defocus $\Delta_T$ drives the **radial** extent of the blur, and
vice versa. That is the reference doc's mechanism for the 90° flip and it falls out for free. The doc's
$b_{\text{radial}} = \lvert z_T \rvert / N$ is a blur *diameter* while the codebase's
`OuterAnnulusRadiusPixels` is a *radius*, so the factor of 2 is already accounted for.

### Why the kernel is exactly an elliptical annulus

For a wavefront $W = c_\xi \xi^2 + c_\eta \eta^2$ over the pupil, the transverse ray intercept is
$\propto (2c_\xi \xi, \, 2c_\eta \eta)$ — a **linear** map $M = R(\theta) \operatorname{diag}(a_{\text{rad}}, a_{\text{tan}})$
of pupil coordinates. The annular pupil therefore maps to an elliptical annulus, and because the Jacobian
is constant the intensity inside it is **uniform**. So the exact Tier-1 kernel is

$$\text{PSF} = \Big(\text{uniform elliptical annulus}\big[\varepsilon(a_{\text{rad}}, a_{\text{tan}}) \to (a_{\text{rad}}, a_{\text{tan}})\big] \text{ rotated by } \theta\Big) \circledast \mathcal{G}(\sigma_{\min})$$

This is strictly better than the reference doc's Gaussian-covariance form and needs none of its
$k \approx 0.3\text{–}0.5$ fudge factor: we convolve the true annulus with the true seeing ⊕ diffraction
Gaussian instead of adding variances in quadrature. The doc's form is recovered exactly as the
second-moment limit — see [Predicted eccentricity](#predicted-eccentricity).

## Three properties that make this safe

### 1. The orientation rule

$$a_{\text{rad}} > a_{\text{tan}} \iff \lvert \Delta - A \rvert > \lvert \Delta + A \rvert \iff \Delta \cdot A < 0$$

> **Stars elongate radially where Δ and A have opposite signs, tangentially where they share a sign, and
> are round wherever Δ = 0 or A = 0.**

Because Δ changes sign across a tilted field, tilt gives tangential elongation on one side and radial on
the other, with the round-star locus displaced off-centre — the classic tilt signature. And because
$e(x,y)$ is signed under this model, $A$ gains a **second** flip locus where the local spacing error
crosses zero, reachable when the tilt plane exceeds $e_c$ (i.e. backfocus nearly right, tilt bad).

### 2. Round at Δ = 0, but not a point

At $\Delta = 0$, $a_{\text{rad}} = a_{\text{tan}} = \lvert A \rvert / (2Np) \neq 0$. The sweet spot has
round stars with a **finite HFR floor** — the circle of least confusion. Physically correct: at large
field radius no focuser position yields a sharp star.

### 3. Inject ⇄ recover survives, provably

At a fixed field point $A$ is independent of the focuser position, so $\Delta \to -\Delta$ maps the
semi-axis pair $(\lvert \Delta - A \rvert, \lvert \Delta + A \rvert)$ to
$(\lvert \Delta + A \rvert, \lvert \Delta - A \rvert)$ — **the PSF at $-\Delta$ is the PSF at $+\Delta$
rotated by exactly 90°**.

Every rotation-invariant statistic is therefore identical: HFR, flux-weighted moments, and the detector's
square bounding-box HFR (a square is 90°-symmetric). So $\mathrm{HFR}(\Delta)$ remains *exactly* an even
function of Δ about the same per-star best focus, the per-star $\mathrm{HFR}^2$ parabola vertex does not
move, and the surface fit recovers the same $(G_x, G_y, K, Z_0)$. Astigmatism only lifts the curve's
floor by a constant $\propto A^2$, which the fit absorbs into its constant term.

This is a much stronger guarantee than "the mean surface is unchanged", and it is why
`AberrationSurfaceRecoveredThroughFullPipeline_TiltAndBackfocus` must keep passing at its **existing**
tolerances with astigmatism enabled. If it does not, the model or the rasterizer is wrong — the fix is
never to widen the tolerance.

## Magnitude, stated honestly

With the shipped defaults (`BackfocusError = 50 µm` ⇒ inferred $e_c = 1$ mm) a 20 µm tilt modulates $A$
by only about ±2 %. For typical rigs the local-spacing term is a small correction, and the visible tilt
signature comes from the sign of Δ either way. It dominates only in the small-spacing / large-tilt
regime, where it produces the second flip locus described above. This is recorded here so the term is not
later "fixed" for being small — it is small by construction for a well-spaced rig, and that is correct.

## Options

| Option | Type | Default | Meaning |
|---|---|---|---|
| `EnableFieldAstigmatism` | bool | `true` | Master toggle for the model. |
| `BackfocusSpacingErrorMicrons` | double | unset (`-1`) | $e_c$. Blank ⇒ inferred as $K / c_{m0}$. |
| `AstigmatismRatio` | double | `0.7` | $\rho = c_a/c_m$, range [0, 3]. |

Plus one changed default: **`BackfocusErrorMicrons` 0 → 50 µm**. On a full frame that is ≈ 0.75× the
critical focus zone at f/7 and corresponds to a 1 mm spacer error under $c_{m0}$.

All three live inside the Field Aberrations group, which is already gated on `EnableAberrations` — with
aberrations off the surface is flat and astigmatism is meaningless.

**Sign convention.** $\rho$ is non-negative; the radial ⇄ tangential flip is carried by the *sign* of the
spacing / backfocus error, which is already a signed option. The reference doc notes that which physical
spacing direction maps to radial depends on the corrector design, so users flip the pattern the same way
they flip a spacer.

**Virtual tilt adapter.** `SimulatedTiltInjection.Fold` folds screw piston into `BackfocusErrorMicrons`.
A piston is a literal axial displacement, so it changes the spacing error by exactly `pistonMicrons`;
`Fold` therefore also adds it to `BackfocusSpacingErrorMicrons` **when that option is explicitly set**.
When blank, the inference already tracks `BackfocusErrorMicrons` and nothing is needed — blank is never
silently converted to explicit.

## Rasterization

The existing exact 1-D radial LUT does not generalize: under an elliptical annulus the angular integral
loses its Bessel closed form. Three approaches were evaluated.

| approach | verdict |
|---|---|
| **Antialiased elliptical-annulus mask on the S×-oversampled grid ⊛ separable Gaussian** | **chosen.** Error is $O(h^2)$ with $h = 1/S = 0.25$ px; exact in the limit. |
| Anisotropic coordinate warp of the isotropic LUT | **rejected.** It stretches σ along with the geometry, so at the astigmatic line focus ($a_{\text{rad}} \to 0$) it predicts a zero-width line and eccentricity → 1, versus the truth of a σ-wide line. The error is $O(1)$ exactly in the near-focus corner regime this feature exists to model. |
| Elliptical Gaussian only | **rejected as a render path.** It discards the donut hole the current renderer models exactly at all defocus, making it a fidelity *regression* rather than an approximation. Its second-moment formula survives only as a reported prediction. |

Numerical validation of the chosen path, against the repo's own exact radial integral at
σ = 1.442 px, ε = 0.3:

| $r_{\text{out}}$ (px) | HFR exact | HFR mask+blur | ΔHFR | max profile deviation |
|---|---|---|---|---|
| 2 | 2.2469 | 2.2486 | 0.08 % | 0.13 % of peak |
| 5 | 3.9081 | 3.9085 | 0.01 % | 0.14 % |
| 10 | 7.2916 | 7.2927 | 0.01 % | 0.08 % |
| 20 | 14.3367 | 14.3373 | 0.00 % | 0.09 % |
| 40 | 28.5529 | 28.5532 | 0.00 % | 0.11 % |

and for elliptical kernels, measured second-moment eccentricity against the closed form below:

| $a_{\text{rad}}$ | $a_{\text{tan}}$ | predicted $e$ | measured $e$ |
|---|---|---|---|
| 10.00 | 10.00 | 0.000 | 0.000 |
| 8.00 | 12.00 | 0.725 | 0.726 |
| 5.00 | 15.00 | 0.926 | 0.927 |
| 2.00 | 18.00 | 0.981 | 0.982 |
| 0.35 | 20.00 | 0.990 | 0.990 |

The last row is the near-line-focus case: eccentricity stays **below** 1 because the minor axis retains
its σ-wide seeing floor. That is precisely the behaviour the rejected warp approach gets wrong, and it is
pinned by `Astigmatic_LineFocus_HasFiniteMinorWidth`.

### Implementation notes

- **Antialiased mask, not a hard 0/1 mask.** A binary mask sampled at $h$ folds rim content down to low
  frequency where the Gaussian cannot suppress it (~0.5 % ripple on a 5 px annulus). Coverage comes from
  the signed distance to each rim: for $q = \sqrt{(u/a_{\text{rad}})^2 + (v/a_{\text{tan}})^2}$, the
  first-order rim distance is $d \approx (q-1)q / \lVert \nabla q \rVert$ and the cell coverage is
  $\operatorname{clamp}(0.5 - d/h,\, 0,\, 1)$, taken as a product of the outer and inner ramps — the same
  product-of-ramps form `StarStamper.AnnulusIntensity` already uses.
- **Double-box correction.** The soft mask is already $\text{mask} \circledast \text{box}_h$, and the
  final box-bin applies another. Build the Gaussian taps with
  $\sigma_{\text{taps}}^2 = \max(\sigma^2 - h^2/12,\ (\sigma/2)^2)$ so the result lands on point samples
  of $\text{mask} \circledast \mathcal{G}$, which is what the isotropic path produces and what the
  box-bin expects. Without it the elliptical kernel is systematically ~0.2 % softer than the isotropic
  one.
- **Support radius** $R = \lceil \max(h_x, h_y) \rceil$ with
  $h_x = \sqrt{(a_{\text{rad}}\cos\theta)^2 + (a_{\text{tan}}\sin\theta)^2} + 5\sigma$ and $h_y$ the
  complement. Tighter than $\max(a,b) + 5\sigma$, reduces to the isotropic expression at $a = b$, and
  stays square so the stamper and golden box math are untouched.
- **Managed convolution, not OpenCV.** `Cv2.SepFilter2D` dispatches SIMD paths by runtime CPU feature,
  and `Render` is contractually a pure function with byte-for-byte determinism asserted. Managed float
  summation in a fixed order is portable-deterministic.
- **Hard requirement: equal semi-axes take the existing exact radial path, untouched**, so nothing
  regresses when the feature is off or for on-axis stars.

### Analytic HFR stays exact

The Rice closed form generalizes with no approximation — averaging the Rice mean radius over the uniform
*pupil* annulus mapped through $M$:

$$\mathrm{HFR}_{\text{ell}} = \frac{1}{\pi(1-\varepsilon^2)}\int_0^{2\pi}\!\!\int_\varepsilon^1 \mathrm{RiceMean}\Big(\rho\sqrt{(a_{\text{rad}}\cos\varphi)^2 + (a_{\text{tan}}\sin\varphi)^2},\ \sigma\Big)\,\rho \, d\rho \, d\varphi$$

evaluated by 2-D Simpson reusing the existing `RiceMean`, and reducing algebraically to the existing
`RiceHfr` at $a_{\text{rad}} = a_{\text{tan}}$. Keeping it preserves measured-vs-analytic HFR as a
genuine *independent* check on the new rasterizer, which is otherwise the least-validated new code.

### Predicted eccentricity

Second moments of an elliptical annulus ⊛ Gaussian are exact and closed-form. For a uniform annulus of
radii $\varepsilon a \ldots a$ the per-axis variance is $a^2(1+\varepsilon^2)/4$, so

$$\mathrm{Var}_{\text{rad}} = \sigma^2 + \frac{a_{\text{rad}}^2(1+\varepsilon^2)}{4} \qquad \mathrm{Var}_{\text{tan}} = \sigma^2 + \frac{a_{\text{tan}}^2(1+\varepsilon^2)}{4}$$
$$e_{\text{pred}} = \sqrt{1 - \mathrm{Var}_{\min}/\mathrm{Var}_{\max}}$$

This is the reference doc's $\sigma_{\text{rad}}^2 = \sigma_{\text{psf}}^2 + (k b_{\text{radial}})^2$
with $k = \sqrt{1+\varepsilon^2}/4 \approx 0.25\text{–}0.27$ against a blur *diameter* — **derived rather
than fudged**, and comfortably inside the doc's quoted 0.3–0.5 band.

Note that this is a *second-moment* eccentricity while `PSFModel.Eccentricity` is an *FWHM-ratio*
eccentricity from a Moffat fit. They agree in direction and ordering but not in magnitude, especially for
a donut with a hole — so tests assert orientation and ordering tightly, magnitude loosely.

## Rendering cost

The PSF kernel cache is keyed today by a single quantized defocus level and holds ~1 kernel with
aberrations off, ~15 with them on. The astigmatic key is a triple — tangential level, sagittal level,
orientation bin — and orientation is a continuous function of field azimuth, so cardinality is the
dominant cost risk.

**Adaptive orientation binning.** Rotating an ellipse by δ displaces its rim by at most
$\sqrt{2}\,\delta\,(a - b)$. Generating each kernel at its **bin centre** bounds $\delta \le \pi/(2n_\theta)$
(orientation is mod π for an ellipse, which halves the bin count for free). Requiring rim error ≤ 0.25 px
— deliberately the same number as `DonutRadiusQuantumPixels`, so angular and radial error are budgeted
alike:

$$n_\theta = \operatorname{clamp}\left(\left\lceil \frac{\pi \, s_{\max}}{\sqrt{2} \times 0.25} \right\rceil,\ 1,\ 64\right), \qquad s_{\max} = \max_{\text{field}} \lvert a_{\text{rad}} - a_{\text{tan}} \rvert = \max_{\text{field}} \frac{\min(\lvert \Delta \rvert, \lvert A \rvert)}{N p}$$

computed once per render. As $s_{\max} \to 0$, $n_\theta \to 1$ and every star also collapses to a single
level, so the frame degenerates to today's kernel set exactly.

**Isotropy is decided from the quantized levels, never from the raw Δ.** Branching on
$\lvert a_{\text{rad}} - a_{\text{tan}} \rvert < \epsilon$ while keying on levels would let two stars
share a key and want different kernels — the cache would stop being a function. With the level rule,
`levelT == levelS` dispatches to the existing isotropic call. The supporting lemma: if
$\operatorname{round}((\Delta-A)/q) = \operatorname{round}((\Delta+A)/q) = L$ then
$\lvert \Delta - Lq \rvert \le q/2$, so $L = \operatorname{round}(\Delta/q)$ — the collapsed level is
*exactly* the level today's code would have chosen. Combined with $A \equiv 0.0$ when the feature is off,
"astigmatism disabled ⇒ byte-identical frame" is a proof, not a hope.

**Memory budget.** One kernel is $S^2(2R+1)^2 \times 4$ bytes — 223 KB at $R = 29$, 817 KB at $R = 56$ —
on top of a 244 MB accumulator and a 122 MB output for a 61 MP frame. The cache estimate is computed
up-front and, if it exceeds the budget, the defocus quantum and orientation bins are coarsened uniformly
across the frame (count shrinks roughly as $g^3$); if still over, astigmatism is disabled for that render
with a warning. Never mid-loop, never non-uniform quality within one frame, never an OOM.

**Rejected alternative**, recorded so it is not re-proposed: caching per (levelT, levelS) in a canonical
orientation and rotating at stamp time. Rotation and sub-pixel phase selection do not commute — the phase
bank is box-binned from an axis-aligned fine grid — so it would need a per-star resample, which is worse
than the extra kernels.

Measured results, methodology, and the pass/fail gates live in
[`camera-simulator-astigmatism-results.md`](camera-simulator-astigmatism-results.md).

## Non-goals

- **Tier 2 pupil-plane Fourier optics.** `PsfKernelMethod.Fft` remains the reserved, unimplemented seam
  and must keep throwing on the astigmatic path too. It would add diffraction rings and astigmatic cross
  structure at roughly 10–100× the compute per star.
- **Nodal / binodal astigmatism.** Nodal aberration theory adds a field-*linear* astigmatism term for
  tilt and decenter, replacing the quadratic magnitude with a vector expression in the field vector.
  For realistic sensor tilts the plane-defocus term plus quadratic astigmatism captures nearly all of the
  visual behaviour; the seam is `AberrationSurface.AstigmatismCoefficient`.
- **Coma.** Still not modeled. The surface remains a pair of *defocus* surfaces; coma is a third-order
  term with a different field dependence and an asymmetric (not elliptical) PSF.
- **Fitting astigmatism on the recovery side.** The inspector's `SensorParaboloidModel` stays
  scalar-valued. This change is inject-only; recovering $\rho$ from measured eccentricity maps is
  possible in principle (the reference doc notes the model is directly invertible) but is separate work.
