# Modeling Backfocus- and Tilt-Induced Star Eccentricity

## Why the elongation direction flips

The culprit is **off-axis astigmatism** combined with which side of the astigmatic focal surfaces the sensor lands on.

For an off-axis star, rays in the *tangential* (meridional) plane — the plane containing the optical axis and the star — focus at a different distance than rays in the *sagittal* plane perpendicular to it. So instead of one focal point there are two focal surfaces, T and S, both curved and both functions of field radius. At the T surface the light forms a short line oriented *azimuthally* (perpendicular to the radius); at the S surface it forms a line oriented *radially* (pointing at the field center / out toward the corners); midway between them lies the circle of least confusion.

A flattener/reducer is designed so that at exactly the design backfocus, the T and S surfaces collapse onto each other *and* onto a flat plane at the sensor. The correction is spacing-dependent: the residual astigmatism and field curvature pass through zero at the design distance and change **sign** on either side of it. That sign flip is the whole story:

- Spacing error one way → at the field edges the sensor sits closer to (or beyond) the S surface, so the sagittal blur is small and the tangential blur is large → stars smear **radially**, appearing to point at the corners/center.
- Spacing error the other way → sensor sits on the T side → radial blur small, azimuthal blur large → stars smear **tangentially**, like short arcs concentric around the field center.

In pure wavefront terms: an astigmatic beam elongates along one axis inside its waist and along the perpendicular axis outside it. Changing backfocus moves the sensor through that waist, so the ellipse rotates 90°. Which physical direction (too much vs. too little spacing) maps to radial vs. tangential depends on the corrector design, but it's consistent for a given optic — which is why the elongation direction is diagnostic. The effect grows roughly as field radius², which is why center stars stay round and corners suffer worst.

**Tilt** is the asymmetric cousin: instead of a rotationally symmetric spacing error, one side of the sensor is too close and the opposite side too far. The result is radial elongation in one corner, tangential in the opposite corner, and the "sweet spot" of round stars displaced off-center — the center of symmetry of the whole pattern shifts.

## Modeling it in a star-field simulator

Two tiers, depending on required fidelity.

### Tier 1: Geometric anisotropic blur

Give each star a blur ellipse whose principal axes are locked to the radial/tangential directions at its field position, with the two axis lengths driven by two separate defocus distances.

**1. Define the focal surfaces relative to the sensor plane.** For a star at field position (x, y), radius r, with backfocus error Δb and sensor tilt (t_x, t_y):

```
z_T(x, y) = z0 + t_x·x + t_y·y + (c_m + c_a)·Δb·r²
z_S(x, y) = z0 + t_x·x + t_y·y + (c_m − c_a)·Δb·r²
```

where:

- `z0` — global defocus (focuser position; presumably already modeled)
- `c_m` — residual mean field curvature per mm of spacing error
- `c_a` — residual astigmatism per mm of spacing error

Both correction terms are proportional to Δb to first order and vanish at design spacing. The tilt terms make each defocus distance a plane across the field.

**2. Convert each defocus to a blur size** using the geometric relation blur diameter ≈ defocus / f-number (N):

```
b_radial     = |z_T(x, y)| / N
b_tangential = |z_S(x, y)| / N
```

Note the crossing: the *tangential-ray* defocus z_T controls the *radial* extent of the blur, and vice versa — that is the mechanism behind the 90° flip, and it falls out of the equations for free. When z_T and z_S have the same sign but different magnitudes the result is an ellipse; when the sensor sits between the surfaces (z_T and z_S with opposite signs), the smaller one passes through zero and the orientation flips.

**3. Build the kernel.** Rotate the ellipse to the star's position angle θ = atan2(y, x) so the axes are radial/tangential, then convolve with the seeing/optics PSF:

```
σ_rad² = σ_psf² + (k·b_radial)²
σ_tan² = σ_psf² + (k·b_tangential)²

Covariance = R(θ) · diag(σ_rad², σ_tan²) · R(θ)ᵀ
```

with k ≈ 0.3–0.5 to map a top-hat disk diameter onto a Gaussian sigma. Render the elliptical Gaussian (or elliptically-scaled Moffat), scale to the star's flux, and add the noise model as usual. If the defocus renderer draws obstruction donuts rather than Gaussians, the equivalent move is to scale the donut anisotropically: stretch it by |z_T|/N along the radial axis and |z_S|/N along the tangential axis.

This yields exactly the observed behavior — eccentricity growing quadratically toward the corners, orientation radial or tangential depending on the sign of Δb, and for tilt, a pattern whose round-star centroid wanders off-center with orientation flipping across the field. It is also directly invertible: fit measured HFR/eccentricity maps back to (Δb, t_x, t_y, z0), which is precisely what an aberration inspector wants to validate against.

### Tier 2: Pupil-plane / Fourier optics

For diffraction realism, build a field-dependent pupil function and FFT it:

```
W(ρ, φ; x, y) = Z4(x, y)·(2ρ² − 1) + A(x, y)·ρ²·cos 2(φ − θ)

Z4 = z0 + t_x·x + t_y·y + c_m·Δb·r²      (defocus coefficient)
A  = c_a·Δb·r²                            (astigmatism magnitude, oriented along θ)

PSF = | FFT( P(ρ) · exp(i·2π·W/λ) ) |²
```

with P including the central obstruction and spider. The radial/tangential flip emerges naturally from the interaction of the defocus and astigmatism terms, and near-focus structure (astigmatic cross patterns, donut asymmetry) comes out correct. It costs roughly 10–100× the compute per star, so a common trick is to precompute PSFs on a coarse field grid and interpolate.

### Tilt rigor

Tier 1's plane term captures the dominant tilt effect. For full rigor, nodal aberration theory adds a *field-linear* astigmatism term for tilt/decenter (the astigmatism null moves off-axis and can split into two nodes — binodal astigmatism), modeled by replacing `c_a·Δb·r²` with a vector expression `|c_a·Δb·H² + a_tilt·H|` in the field vector H. For realistic sensor tilt magnitudes, the plane-defocus term plus quadratic astigmatism captures nearly all of the visual behavior.

### Empirical calibration

The constants c_m and c_a can be pinned down for a real corrector empirically: take frames at a few known backfocus offsets (spacer swaps), measure eccentricity and orientation vs. field radius, and fit. That grounds the simulator's sign convention to the actual optic rather than guessing which direction is radial.
