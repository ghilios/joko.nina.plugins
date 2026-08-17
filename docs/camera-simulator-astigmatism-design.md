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

- **Tilt does not create astigmatism; it reveals it.** A sensor is a passive sampling plane — it cannot
  change what the beam in front of it is doing, only where along that beam it takes its slice. So the
  split $A$ carries no tilt term at all. What makes a *tilted* rig show eccentric stars is that tilt
  drives the local defocus $\Delta$ positive on one edge and negative on the other against a split that
  is the same on both, so the two edges land on opposite sides of the astigmatic pair. This corrects an
  earlier version of this design (see [What changed, and why](#what-changed-and-why)).
- **The split has exactly two terms, and neither is a free knob.** The corrector's *residual* astigmatism
  at design spacing, $a_c$ — a real, measurable property of every corrector — plus the *induced* split
  from mis-spacing, which Seidel's 3:1 rule pins at exactly half the induced field curvature.
- **The two astigmatic surfaces straddle today's surface.** Their mean is exactly the existing
  tilted-paraboloid best-focus surface, which is what preserves the inspector's inject ⇄ recover identity
  (proved below, not merely asserted).
- **The rendered kernel is an elliptical annulus ⊛ isotropic Gaussian** — the exact Tier-1 kernel, not the
  reference doc's Gaussian-covariance approximation, and it keeps the central-obstruction donut hole that
  the current renderer models exactly.
- **`BackfocusErrorMicrons` ships nonzero (50 µm)** so the effect is visible without hunting for knobs.

## The model

### Two surfaces whose mean is today's surface

In centered sensor microns (`x' = x − X0`, `y' = y − Y0`, `r'² = x'² + y'²`), valued in µm of focuser
travel:

$$z_{\text{mean}}(x,y) = G_x x' + G_y y' + K r'^2 + Z_0$$

unchanged — this is `AberrationSurface.ZBestFocusMicrons`, the algebraic inverse of
`Inspection.SensorParaboloidModel`. The astigmatic half-split is

$$A(x,y) = a_2 \, r'^2 , \qquad a_2 = \underbrace{\tfrac{1}{2} K}_{\text{induced by mis-spacing}} + \underbrace{\frac{a_c}{r_c^2}}_{\text{corrector residual}} \qquad [\mu m]$$

and the two focal surfaces are

$$z_T = z_{\text{mean}} + A \qquad z_S = z_{\text{mean}} - A \qquad \tfrac{1}{2}(z_T + z_S) \equiv z_{\text{mean}}$$

where $a_c$ is the configured corner residual and $r_c^2 = \text{halfW}^2 + \text{halfH}^2$ is the
sensor's corner radius squared — the same radius `PredictedCurvatureEffectMicrons` reports $K$ at, so
both knobs are quoted at the same place on the sensor.

**No tilt term, deliberately.** $G_x, G_y$ appear in $z_{\text{mean}}$ and nowhere else. Sensor tilt is a
rigid-body motion of the detector; it changes which plane of the converging beam is sampled, not the
beam's aberration content. Schechter & Levinson (2011, PASP; arXiv:1009.0708) §6.3 state the same result
from the third-order side: *"a tilted detector produces a field pattern identical to misalignment
curvature of field"* — pure defocus, no astigmatism.

### The induced term: why exactly $K/2$

For a Seidel system the tangential and sagittal focal surfaces sit on either side of the Petzval surface
in the fixed ratio 3:1,

$$z_T = z_P + 3 s r^2, \qquad z_S = z_P + s r^2 ,$$

so the *medial* surface — the one a focus run finds — is at $z_P + 2 s r^2$ while the half-split is
$s r^2$. Petzval curvature depends only on the elements' powers and indices, not on their separations,
so it does **not** move when a spacer changes; all of the spacing-induced change lands in the
astigmatism term $s$. Therefore a mis-spacing that shifts the medial surface by $K r'^2$ necessarily
splits the pair by exactly $\tfrac{1}{2} K r'^2$. The ratio is fixed by the optics, which is why the
free "astigmatism ratio" knob of the earlier design was removed rather than re-tuned.

### The residual term $a_c$

Every real corrector leaves *some* astigmatism at the corner even at its design spacing. That residual
is what a tilted-but-well-spaced rig reveals, and it is the only reason such a rig shows eccentric stars
at all. An order-of-magnitude anchor: a diffraction-limited-at-centre design at f/7 that just reaches
$\lambda/4$ of astigmatism at the corner corresponds to a longitudinal split of order
$\lambda N^2 / 2 \approx 13\ \mu m$, which is where the 15 µm default comes from. Field flatteners in the
f/5–f/7 range typically sit in the 10–20 µm band; a poor one is several times that.

Because $a_c$ is quoted *at the corner* and divided by $r_c^2$, it scales as $r'^2$ across the field and
transfers correctly between sensor sizes.

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

Because Δ changes sign across a tilted field while $A$ does not, tilt gives tangential elongation on one
side and radial on the other, with the round-star locus displaced off-centre — the classic tilt
signature, and the thing this model exists to produce. $A$ itself has one sign everywhere on the sensor
(it is $a_2 r'^2$ with $a_2$ a constant), so every orientation flip in a frame comes from Δ crossing zero.
That is a falsifiable structural claim, and it is what
`PureTilt_WithPerfectSpacing_StillFlipsRadialToTangentialAcrossTheField` pins.

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

Two numbers set the scale, and they compete:

- The **induced** half-split at the corner is $C/2$ for a backfocus error $C$. At the shipped 50 µm
  default that is 25 µm.
- The **residual** is $a_c$, 15 µm by default.

So on a shipped-default rig the two are comparable, which is the interesting regime: reversing the
spacer flips the orientation, because $A_{\text{corner}} = C/2 + a_c$ changes sign between
$C = +50$ (giving $+40$) and $C = -50$ (giving $-10$) while Δ also flips. Push the spacing error past
$\lvert C \rvert = 2 a_c$ and the induced term wins outright: $A$ then tracks $C$'s sign, both flip
together, and **both spacing directions render radially**. The axis ratio in that limit tends to
$\lvert \Delta - A \rvert / \lvert \Delta + A \rvert \to 3$ — Seidel's 3:1 showing through directly, and
a hard ceiling on how eccentric a purely mis-spaced rig can look.

For a *tilted* rig the numbers run the other way. $\Delta$ from tilt can be hundreds of µm while $A$
stays at $a_2 r'^2$, so the axis ratio $\lvert \Delta - A\rvert / \lvert \Delta + A \rvert \to 1$ as tilt
grows: **more tilt means larger stars and less relative eccentricity**, with the peak eccentricity where
$\lvert \Delta \rvert \approx \lvert A \rvert$. Worked for the test scene (IMX533, $a_c$ = 15 µm, no
backfocus error), at the sensor edge: 30 µm tilt → axis ratio 3.0; 100 µm → 1.35; 1000 µm → 1.03. This is
recorded because it looks like a bug from the outside — cranking tilt to an extreme makes stars *rounder*
— and it is correct.

## Options

| Option | Type | Default | Meaning |
|---|---|---|---|
| `EnableFieldAstigmatism` | bool | `true` | Master toggle for the model. |
| `CornerAstigmatismMicrons` | double | `15.0` | $a_c$, **signed**, range [−1000, 1000] µm. The corrector's residual T–S half-split at the sensor corner. |

Plus one changed default: **`BackfocusErrorMicrons` 0 → 50 µm**, so a user who enables aberrations sees
the effect without hunting for a second knob. On a full frame 50 µm is ≈ 0.75× the critical focus zone at
f/7.

Both live inside the Field Aberrations group, which is already gated on `EnableAberrations` — with
aberrations off the surface is flat and astigmatism is meaningless.

**Sign convention.** $a_c$ is signed, and its sign is what picks which side of design spacing gives
radial elongation. There is no separate direction knob: the orientation at a field point is fully
determined by $\operatorname{sign}(\Delta \cdot A)$, and both factors are already signed.

**The spacing flip, honestly.** Swapping a spacer flips Δ and flips the induced half of $A$, but cannot
touch $a_c$. So the familiar "reverse the spacer and the corners rotate 90°" holds only while
$\lvert C \rvert < 2 a_c$; past that both directions read radial. This is the honest form of a widely
repeated piece of field lore. Published reports of the flip and flat denials of it — Roland Christen's
among the latter — are both consistent with this window; which one an observer sees depends on how their
spacing error compares with their corrector's residual astigmatism. The window is pinned end-to-end by
`ReversingTheBackfocusError_FlipsRadialToTangential_OnlyInsideTheResidualWindow`, which asserts *both*
halves: the flip inside it and the absence of a flip outside it.

**Virtual tilt adapter.** `SimulatedTiltInjection.Fold` folds screw piston into `BackfocusErrorMicrons`
and nothing else. A piston changes the sensor's spacing, which is exactly what `BackfocusErrorMicrons`
already encodes as its corner effect; the induced split follows from $K$ automatically, and the
corrector's residual $a_c$ is a property of the glass that no screw move can alter.

### What changed, and why

The first version of this design made the split proportional to the **local** axial spacing error,
$A = \rho\, c_m\, e(x,y)\, r'^2$ with $e$ carrying the tilt plane, on the reasoning that a tilted sensor
is genuinely mis-spaced across most of its area. It does not work, for a reason that is easy to state
once seen: under that form $A$ flips sign in lockstep with Δ, so $\Delta \cdot A < 0$ *everywhere* and
every edge elongates radially by the same modest amount — measured axis ratio 1.07 at every tilt
magnitude, no perpendicular pair anywhere. Rendered frames confirmed it: pure tilt produced defocus with
no visible elongation, and ±2000 µm of backfocus error produced near-identical frames.

The physical error underneath was treating the sensor's position as an input to the beam's aberrations.
It is not. Mis-spacing induces astigmatism because it changes the *corrector's* conjugates, and the
sensor plane's own tilt has no such effect — it only chooses the sampling plane. Three independent
reviews converged on this; the details, including the Seidel derivation of the $K/2$ factor and the
literature on the spacing-flip folklore, are in
[`camera-simulator-astigmatism-review.md`](camera-simulator-astigmatism-review.md).

Removed with it: `BackfocusSpacingErrorMicrons` (and its blank-means-infer plumbing), `AstigmatismRatio`,
and the nominal inference constant $c_{m0}$. The reference doc
[`backfocus-eccentricity-modeling.md`](backfocus-eccentricity-modeling.md) is retained for its Tier-1
geometry, which is unchanged and correct; note that its prose implies a tilt-driven split its own
equations do not contain, and that this design follows its equations rather than its prose.

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
- **Nodal / binodal astigmatism.** Nodal aberration theory adds a field-*linear* astigmatism term when an
  *optical element* is tilted or decentred, splitting the astigmatic node into two and breaking the
  rotational symmetry of $A$. That is a real effect and it is what a misaligned corrector (as opposed to a
  misplaced sensor) produces; modelling it means replacing the scalar $a_2 r'^2$ with a vector expression
  in the field vector. It is out of scope here — the option surface describes a *sensor* fault, and a
  rotationally symmetric $A$ is the right model for that. The seam is
  `AberrationSurface.AstigmatismCoefficient`.
- **Coma.** Still not modeled. The surface remains a pair of *defocus* surfaces; coma is a third-order
  term with a different field dependence and an asymmetric (not elliptical) PSF.
- **Fitting astigmatism on the recovery side.** The inspector's `SensorParaboloidModel` stays
  scalar-valued. This change is inject-only; recovering $a_c$ from measured eccentricity maps is
  possible in principle (the reference doc notes the model is directly invertible) but is separate work.
