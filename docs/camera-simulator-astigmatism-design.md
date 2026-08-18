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

- **A tilted *detector* reveals astigmatism; a tilted *corrector* creates it.** These are different
  mechanisms with different signatures, and the model carries both. A detector is a passive sampling
  plane — it cannot change the beam in front of it, only where along that beam it takes its slice — so it
  contributes no split of its own and instead *exposes* the corrector's existing one by driving $\Delta$
  positive on one edge and negative on the other. A corrector tilted along with the camera, which is what
  a sagging focuser or a non-square thread actually produces, displaces the astigmatic node off-axis and
  adds a split proportional to the tilt.
- **The split has three terms and one knob apiece, none of them a fudge.** The corrector's *residual*
  astigmatism at design spacing $a_c$; the *induced* split from mis-spacing, which Seidel's 3:1 rule pins
  at exactly half the induced field curvature; and the *field-linear* term from a tilted corrector,
  scaled by how much of the tilt is optical rather than detector-only.
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

$$A(x,y) = a_2 \, r'^2 , \qquad a_2 = \frac{1}{r_c^2}\Big( \underbrace{\tfrac{1}{2} C}_{\text{induced by mis-spacing}} + \underbrace{a_c}_{\text{corrector residual}} + \underbrace{c_t \, T}_{\text{tilted corrector}} \Big) \qquad [\mu m^{-1}]$$

where $C$ is the backfocus error, $T$ the tilt amount, and $r_c^2 = \text{halfW}^2 + \text{halfH}^2$. All
three contributions are quoted at the **same place** — the sensor corner — and add **signed** into one
number, so the largest magnitude decides the sign and therefore whether the field elongates radially or
tangentially. A tilt term large enough and opposite in sign flips the whole field exactly as a bigger
spacer would.

and the two focal surfaces are

$$z_T = z_{\text{mean}} + A \qquad z_S = z_{\text{mean}} - A \qquad \tfrac{1}{2}(z_T + z_S) \equiv z_{\text{mean}}$$

where $a_c$ is the configured corner residual and $r_c^2 = \text{halfW}^2 + \text{halfH}^2$ is the
sensor's corner radius squared — the same radius `PredictedCurvatureEffectMicrons` reports $K$ at, so
both knobs are quoted at the same place on the sensor.

**Everything lives in the even part, and that is the whole trick.** $A$ is a single-signed paraboloid about
the optical axis, so it does *not* change sign across the field. $\Delta$ does, whenever tilt dominates.
That asymmetry is the entire source of the classic radial/tangential corner pair, and any term that made
$A$ vary in sign across the field would destroy it — see
[Why the tilt term is even](#why-the-tilt-term-is-even-and-not-a-field-gradient).

**Sensor tilt contributes nothing here, deliberately.** Sensor tilt is a rigid-body motion of the detector; it
changes which plane of the converging beam is sampled, not the beam's aberration content. Changing a
wavefront's reference sphere changes its defocus coefficient and nothing else — astigmatism is invariant
under it. Schechter & Levinson (2011, PASP; arXiv:1009.0708) §6.3 state the same result from the
third-order side: *"a tilted detector produces a field pattern identical to misalignment curvature of
field"* — pure defocus, no astigmatism.

### The tilt term: a tilted corrector, not a tilted sensor

The first two terms alone say something false about a badly tilted rig. $a_2$ is then fixed while tilt
drives $\Delta$ without bound, so the axis ratio

$$\frac{\lvert \Delta - A \rvert}{\lvert \Delta + A \rvert} \longrightarrow 1$$

and past a few hundred µm of tilt the corners render **round** again. Measured at the corner of a QHY600
at f/7 with the shipped 15 µm residual: 1.35 at 100 µm of tilt, 1.03 at 1000 µm, 1.00 at 10 mm. Worse,
every field point can still be brought to a *perfect point focus* by moving the focuser to it — a tilted
rig would just be one that needs a different focus per corner. Neither is what real rigs do.

The reason is that real tilt is rarely the detector alone. A crooked camera inside a square adapter tilts
only the sensor; a sagging focuser or a non-square thread tilts the **corrector** along with the camera.
Nodal aberration theory (Thompson 2005) says a tilted or decentred element displaces the astigmatic node
off the optical axis: the astigmatic field becomes $a_2 \lvert \vec r\,' - \vec s \rvert^2$ instead of
$a_2 r'^2$, with $\lvert \vec s \rvert$ proportional to the perturbation. On a *visibly* tilted rig that
displacement is large compared with the sensor, and in that limit

$$a_2 \lvert \vec r\,' - \vec s \rvert^2 \;\approx\; a_2 s^2 \left(1 - \frac{2\,\vec r\,' \cdot \hat s}{s}\right)$$

— an approximately **uniform raised level** with a relative variation of only $2 r_c / s$ across the field.
That raised level is what the $c_t T$ term models.

Because it scales with the tilt that also drives $\Delta$, it does not wash out. At the corner, where both
the split and the tilt reach their quoted values, $\lvert \Delta \rvert = T$ and $A = c_t T$ (taking
$C = a_c = 0$), so

$$\frac{\lvert \Delta - A \rvert}{\lvert \Delta + A \rvert} = \frac{1 + c_t}{1 - c_t}$$

**independently of tilt magnitude** — 1.67 at $c_t = 0.25$, from 100 µm of tilt to 10 mm. Its other
observable is the one the user reported: at a field point brought exactly to its own focus, $\Delta = 0$
and both semi-axes equal $\lvert A \rvert / (2Np)$ — a round *disc*, the circle of least confusion, whose
radius **grows with the tilt**. A tilted corner can no longer be focused sharp, which is the honest
reading of "the tilt puts that part of the sensor at a spacing the corrector was not designed for."

$\lvert c_t \rvert$ is required to be $< 1$: at 1 one semi-axis collapses to zero across the whole field
at once, and beyond it the two foci swap sides — a differently-signed corrector, not a mis-set one.

### Why the tilt term is even, and not a field gradient

Two pieces of the exact nodal form are deliberately dropped, and the first one matters enormously.

**The field gradient $-2 a_2 (\vec r\,' \cdot \vec s\,)$ is dropped because it destroys the observed
pattern.** It is *odd* in field position and parallel to the tilt, so it flips sign in exactly the places
$\Delta$ does. That leaves $\Delta \cdot A < 0$ everywhere and elongates **every** corner radially — no
perpendicular pair at any setting. A revision that kept it shipped and was rejected on sight against a
real 3×3 corner panel, which is the strongest possible evidence: whatever the term's status in theory, the
thing it predicts is not what a tilted rig looks like. The classic radial/tangential pair requires $A$ to
hold one sign while $\Delta$ changes sign, so the tilt has to enter through the even part.

**The quadratic growth in $\lvert \vec s \rvert$ is dropped because it overshoots.** Strict NAT gives
$A \propto s^2 \propto T^2$ against $\Delta \propto T$, so $\lvert A \rvert$ eventually exceeds
$\lvert \Delta \rvert$ everywhere and the corners return to round — just as large blobs this time. Keeping
the term linear in $T$ holds the axis ratio at a finite constant, which is the behaviour a badly tilted rig
actually shows.

What is kept is the part that carries the observable: a split that scales with the tilt and holds one sign
across the field.

### Where the signed backfocus-versus-tilt competition actually lives

It is worth being explicit, because the intuition "backfocus one way, tilt the other, larger magnitude
wins" is right and is already implemented — in **two** independent places.

**In the split.** $\tfrac{1}{2}C$, $a_c$ and $c_t T$ are all quoted at the corner and added signed, so a
tilt term of opposite sign and larger magnitude flips $a_2$ and with it the whole field's orientation, the
same way a bigger spacer would. Worked: $C = 100$ (contributing $+50$), $a_c = 15$, $c_t = -0.25$; at
$T = 100$ the tilt contributes $-25$ and the corner split is $+40$, while at $T = 400$ it contributes
$-100$ and the corner split is $-35$. The sign, and therefore radial-versus-tangential, has flipped.

**In the local defocus.** $\Delta = \Delta_0 - (\vec G \cdot \vec r\,') - K r'^2$ adds the uniform
curvature and the tilt plane signed at every field point. When the curvature wins, every corner has the
same sign of $\Delta$ and therefore the same orientation — the *backfocus* signature. When the tilt wins,
opposite corners straddle focus, their $\Delta$ have opposite signs, and their orientations are
perpendicular — the *tilt* signature. This competition needs no astigmatism term at all: it is what
decides which of the two classic patterns a frame shows, and it is why "mostly backfocus with a little
tilt" and "mostly tilt with a little backfocus" look nothing alike.

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
| `TiltAstigmatismFraction` | double | `0.25` | $c_t$, **signed**, $\lvert c_t \rvert < 1$. How much of the tilt carries the corrector with it, as µm of corner split per µm of tilt effect. |

Plus one changed default: **`BackfocusErrorMicrons` 0 → 50 µm**, so a user who enables aberrations sees
the effect without hunting for a second knob. On a full frame 50 µm is ≈ 0.75× the critical focus zone at
f/7.

Both live inside the Field Aberrations group, which is already gated on `EnableAberrations` — with
aberrations off the surface is flat and astigmatism is meaningless.

**Sign convention.** $a_c$ and $c_t$ are both signed, and they add signed into the same corner split
alongside $\tfrac{1}{2}C$. There is no separate direction knob: the orientation at a field point is fully
determined by $\operatorname{sign}(\Delta \cdot A)$, and every factor is already signed.

**Why $c_t$ is a knob and not a derived constant.** The coupling between a corrector's tilt and its nodal
shift depends on the design's $W_{222}$ and on its tilt sensitivity, neither of which the simulator knows;
and how much of a given rig's measured tilt is optical rather than detector-only depends on the mount, not
on the optics. An attempt to derive it from $a_c$, focal length and sensor size — via
$\sigma/H_c \approx \beta/\theta_{\max}$ — lands anywhere from 0.06 to 1.1 across ordinary rigs, i.e. from
invisible to a line focus, which is a good sign that the order-unity factor in it is doing all the work.
So it is exposed and its default is chosen against a stated target instead: **0.25 gives a 1.67:1 corner
at any tilt** — clearly eccentric, not a line. Setting 0 recovers the detector-only model exactly.

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

**Round three.** The fix for round two was a *field-linear* term, $c_t(\vec G \cdot \vec r\,')$ — the
leading nodal perturbation, kept verbatim. It holds its axis ratio at any tilt, which was the requirement,
but it is **odd** in field position, so it flips sign wherever $\Delta$ does and leaves every corner
elongated radially. A 3×3 corner panel from a real render showed both far corners tilted the same way
instead of perpendicular, which is exactly what an odd term predicts and exactly what a tilted rig does
not do. The same coupling now enters the **even** part instead, as a raised corner level $c_t T$; see
[Why the tilt term is even](#why-the-tilt-term-is-even-and-not-a-field-gradient).

**Round two.** The residual-plus-Seidel model is correct as far as it goes, and it produces the classic
perpendicular-edge signature — but it makes tilt-induced eccentricity *decay* as tilt grows, so an
extremely tilted corner rendered round and could still be focused to a point. That is right for a tilted
detector and wrong for a tilted train. Note what did **not** change through rounds two and three: sensor
tilt contributes nothing to the split, and the claim it encodes — a detector cannot alter the beam — is
still why the tilt coupling is named for the *corrector*.

**Round one.** The first version of this design made the split proportional to the **local** axial spacing error,
$A = \rho\, c_m\, e(x,y)\, r'^2$ with $e$ carrying the tilt plane, on the reasoning that a tilted sensor
is genuinely mis-spaced across most of its area. It does not work, for a reason that is easy to state
once seen: under that form $A$ flips sign in lockstep with Δ, so $\Delta \cdot A < 0$ *everywhere* and
every edge elongates radially by the same modest amount — measured axis ratio 1.07 at every tilt
magnitude, no perpendicular pair anywhere. Rendered frames confirmed it: pure tilt produced defocus with
no visible elongation, and ±2000 µm of backfocus error produced near-identical frames.

Note that it is the wrong *form*, not merely the wrong size — no coefficient rescues it, because the
perpendicular pair never appears at any setting. The physical error underneath was treating the sensor's
position as an input to the beam's aberrations.
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
- **The full binodal structure.** The model keeps only the large-displacement *magnitude* of nodal
  aberration theory's perturbation — a raised, field-uniform astigmatism level scaling with the tilt — and
  drops the field gradient, the quadratic growth, the second node, and the rotation of the astigmatic
  *axis* about the displaced node. The gradient and the quadratic growth are dropped on evidence, above.
  The axis rotation is the most visible remaining omission: orientation here stays radial/tangential about
  the optical axis, whereas NAT ties it to $\vec H - \vec\sigma$, so a real displaced-node eccentricity map
  swirls around the node rather than around the sensor centre — which would put the round sweet spot
  off-centre in *orientation* as well as in size. The seams are
  `AberrationSurface.PredictedTiltAstigmatismEffectMicrons` and `FieldAngleRadians`.
- **Coma.** Still not modeled. The surface remains a pair of *defocus* surfaces; coma is a third-order
  term with a different field dependence and an asymmetric (not elliptical) PSF.
- **Fitting astigmatism on the recovery side.** The inspector's `SensorParaboloidModel` stays
  scalar-valued. This change is inject-only; recovering $a_c$ from measured eccentricity maps is
  possible in principle (the reference doc notes the model is directly invertible) but is separate work.
