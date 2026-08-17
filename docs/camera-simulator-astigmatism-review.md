# Adversarial Review — Camera Simulator Astigmatism Model

Three independent reviews, run after the shipped model failed three times in a row on the bench:
an adversarial physics critique of the implementation, an empirical survey of what real imaging trains
actually do (web research), and a constructive redesign. They converged. This document records what they
found, what is established versus folklore, and the model that follows.

Subject of review: [`camera-simulator-astigmatism-design.md`](camera-simulator-astigmatism-design.md) and
`CameraSimulator/Rendering/AberrationSurface.cs` as of `ec2d761`. Reference material under review:
[`backfocus-eccentricity-modeling.md`](backfocus-eccentricity-modeling.md).

## Addendum — round three, and what the review got wrong

The review's central finding is upheld: a tilted **detector** cannot create astigmatism, and the
local-spacing model it rejected is the wrong functional form. What the review missed — and what the
model built from it therefore also missed — is that this makes tilt-induced eccentricity **decay** as the
tilt grows. With the split fixed at `a₂r'²` while tilt drives Δ without bound, the axis ratio
`|Δ−A|/|Δ+A| → 1`, so an extremely tilted corner renders round, and every field point can still be
brought to a *perfect point focus* by moving the focuser to it. A user reporting exactly that observation
is what reopened the question.

The gap is that "tilt" on a real rig is usually not the detector alone. A crooked camera in a square
adapter tilts only the sensor; a sagging focuser or a non-square thread tilts the **corrector** along with
the camera, and a tilted optical element does change the beam. Nodal aberration theory says it displaces
the astigmatic node off-axis, adding a term linear in field position and parallel to the tilt. The review
correctly identified nodal astigmatism as the mechanism for optical-element tilt but filed it as an
out-of-scope refinement rather than as the missing half of the answer.

The shipped model now carries both, and they are separable because one is even in field position and the
other odd — no amount of parameter fitting can substitute one for the other. Their signatures differ
visibly too: the residual mechanism elongates opposite edges *perpendicular* to each other and fades at
large tilt; the nodal mechanism elongates *every* edge radially and holds its axis ratio at
`(1+c_t)/(1−c_t)` at any tilt. Measured through the detector at 150 µm of tilt: e = 0.52 radial on both
edges for the nodal term, against 0.19/0.15 perpendicular for the residual term on the same frame.

## The three bench failures that triggered this

| # | symptom | cause found |
|---|---|---|
| 1 | ±2000 µm backfocus rendered identically | model linear in spacing error: both Δ and A flip, `\|Δ∓A\|` invariant |
| 2 | 5000 µm backfocus showed no elongation | kernel-cache budget silently disabled astigmatism (fixed) |
| 3 | zero backfocus + extreme tilt showed no elongation | **tilt→astigmatism coupling is physically fictitious** |

Failure 3 is the important one. It is not an arithmetic bug; it is the correct output of a model built on a
wrong mechanism.

## Finding 1 — sensor tilt cannot create astigmatism. The shipped mechanism is fiction.

The design spec's first key decision was that "a tilted sensor genuinely sits at the wrong spacing over most
of its area, so pure tilt produces eccentricity on its own", explicitly overriding the reference doc's
Tier-1 equations, which put tilt only in the common plane term. **That override was wrong and the reference
doc's math was right.**

The wavefront arriving at a field point is fixed by the telescope and corrector. The sensor is a passive
sampling plane: tilting it changes *where along each beam's caustic each patch samples*, and changes the
beam's aberration content by exactly nothing. Mis-spacing a corrector induces astigmatism because it forces
a compensating **refocus**, which changes the corrector's working conjugates; a tilted sensor at correct
mean spacing needs no such refocus, so no astigmatism is induced at any tilt.

This is established, not inferred. Schechter & Levinson 2011 (PASP, *Generic Misalignment Aberration
Patterns in Wide-Field Telescopes*, [arXiv:1009.0708](https://arxiv.org/abs/1009.0708)) §6.3:

> A tilted detector produces a field pattern identical to misalignment curvature of field

— that pattern being pure defocus varying linearly across the field. No astigmatism term arises from
detector tilt at third order. Corroborated by practice: ASTAP's tilt metric is a pure star-*size* statistic
(best-vs-worst corner median HFD), and CCD Inspector fits a focal plane to FWHM. None of the mainstream tilt
tools use eccentricity as the primary tilt signal.

The one genuine shape effect of a tilted sampling plane — the oblique cut of a converging cone — is
O(tan²τ·tan²α). At the most extreme case in the failure table (1000 µm of tilt effect across a full-frame
half-diagonal at f/7) that is ~1.1 × 10⁻⁵. Zero.

The shipped model was also internally inconsistent on its own premise: if axial displacement of a sensor
patch drove astigmatism, then the focuser offset and the curvature sag would have to enter it too. They were
excluded only because including them would break the inject⇄recover proof. The correct resolution is the
other way — none of them belong, including the tilt term.

**So the pure-tilt render was physically correct for an ideally corrected train.** Real tilted rigs show
eccentric corners for a different reason, below.

## Finding 2 — the missing term: residual astigmatism at design spacing

Both the shipped model and the reference doc force the T–S split to zero at perfect spacing. No real optic
achieves that. Every shipped flattener leaves some split at the field edge, and uncorrected trains leave a
lot. That residual is what makes real tilted rigs show elongation, through this chain:

```
shipped:  tilt → local spacing error → astigmatism        (fiction)
correct:  tilt → local defocus Δ(x,y) → interacts with the field's pre-existing A(r) → eccentricity
```

The residual is invisible at best focus — the circle of least confusion is round and small — and is
*revealed* by defocus. Tilt is a defocus injector: it drags each corner to a different Δ, and the semi-axes
`|Δ−A|` and `|Δ+A|` separate. Far corner (Δ and A opposite signs) elongates radially, near corner
tangentially, with a high-eccentricity band where `|Δ| ≈ A` and a displaced round sweet spot. That is
exactly the reference doc's tilt phenomenology — which its prose describes and its own equations cannot
produce.

Independently corroborated by practitioners, e.g. Frank (MetaGuide author) on Cloudy Nights:

> sensor tilt would reveal itself as slightly bloated stars on one side of the image — and as you go through
> focus the bloat should shift to the other side. But the stars should look fairly round the whole time… if
> you see astigmatism or coma it means either the backspacing is wrong or the add-on element itself is
> slightly misaligned or tilted.

## Finding 3 — the spacing-direction flip is design-dependent, not a law

This is the finding that most changes the brief, because the feature was being built to reproduce it as a
universal behaviour.

**The folklore is consistent across vendors** — William Optics, OPT, Agena all publish the same chart: too
close → radial, too far → tangential arcs. **It could not be verified as a universal rule, and there is
direct counter-evidence:**

- **Roland Christen** (Astro-Physics founder, i.e. a flattener designer), in *Optimizing Your Field
  Flattener*: "In either case, just looking at an image with defocused stars in the corners one cannot
  determine whether the field is inward or outward curving (under or over corrected). Inspection programs
  like CCD inspector also cannot tell which way the field is curved."
- **A controlled bench test** (AstroBin, Oct 2024) with a 9 µm artificial star and 0.05 mm spacing steps on
  an 80PHQ + 0.76× reducer found the chart's sign **reversed**, and found the assignment differed between
  the tester's refractor/SCT and their Newtonian + coma corrector.
- **Multiple field reports of no reversal at all** — radial elongation both ways, never seeing the
  perpendicular case.
- But it **is** observed on some rigs by experienced imagers, so the mechanism is real, just conditional.

**Mechanism for the conditionality.** A flip requires the corner to cross from one side of the astigmatic
line-focus pair to the other. Whether that happens depends on the residual's structure. If the residual
keeps the Seidel ordering (tangential surface ~3× farther from Petzval than sagittal, both flipping
together) the same axis dominates on both sides — radial both ways. If the mis-spacing mainly flips the
defocus while the residual holds the T/S ordering, it flips. **So the outcome must be a parameter of the
simulated optic, not a law baked into the model.**

**How practitioners actually determine the direction** is a focus series comparing centre and corner best
focus — the Astro-Physics method, and precisely what HocusFocus's own Aberration Inspector mechanizes. A
simulator that made single-frame elongation direction a reliable spacing-sign cue would teach users
something false.

## Finding 4 — ρ is not a free parameter, and the shipped default was unphysical

Two textbook facts remove the knob:

1. **Petzval curvature is spacing-invariant** — the Petzval sum contains element powers and indices only,
   no separations.
2. **The Seidel 3:1 rule**: `z_T = z_P + 3s·r²`, `z_S = z_P + s·r²`.

So the mean surface sits at `z_P + 2s·r²` and the half-split is `s·r²` — the split is **exactly half** the
spacing-induced part of the mean curvature. ρ = 0.5 identically for any corrector's spacing response. The
shipped default of 0.7 (axis ratio 5.67) is outside the reachable range. The genuine per-optic freedom lives
in the *residual*, not the ratio.

## Finding 5 — calibration check, and why the bench tests were outside the model's range

`c_m0` encodes "1 mm of spacing error → 50 µm of corner curvature effect on full frame". The research
brackets the real figure at **20–70 µm per mm** for a typical 500–1000 mm f/5–f/7 refractor + flattener,
from vendor tolerances (±1–2 mm before visible deterioration; TS-Optics quotes 1–2 % of a ~110 mm working
distance for full frame) against a depth of focus of ±54 µm at f/7. So `c_m0` is well calibrated — the
middle of the bracket.

That calibration has a sharp consequence for how the feature was being tested. The **Backfocus Error** knob
is the corner *curvature effect* in µm, not the spacer error. So:

| Backfocus Error knob | implied spacer error | realistic? |
|---|---|---|
| 20–100 µm | 0.4–2 mm | yes — the whole usable range |
| 2000 µm | ~40 mm | no |
| 5000 µm | ~100 mm | no |
| 10000 µm | ~200 mm | no |

The bench tests were run 20–200× outside the physical range. Two things follow. First, the option's name and
units invite exactly that reading and should be fixed. Second — and this vindicates the observation rather
than dismissing it — **at errors that large, "radial both ways" is what the model should show, and what real
observers report.** The flip lives near design spacing; far outside it both signs converge to the Seidel 3:1
radial signature.

## The model that follows

Confined to `AberrationSurface`; the rasterizer and compositor are untouched.

```
mean surface (unchanged):   z(x,y) = Gx·x' + Gy·y' + K·r'² + Z0
half-split (new):           A(x,y) = a₂ · r'²
                            a₂     = (K − K_P)/2 + a_c/r_c²
```

- `K` — mean-curvature coefficient, still set by the Backfocus Error knob, still the inspector-identity
  anchor. **No tilt term in `A`** — `Gx, Gy` appear only in the mean surface, because that is the entire
  physical effect of sensor tilt.
- `a_c` — the corrector's **design-residual corner astigmatism** in µm, signed. The one new knob. Its sign
  is the corrector-design freedom that decides which spacing direction elongates radially; its magnitude
  sets the width of the flip window and the strength of the tilt signature. A defensible default is about a
  quarter of the depth of focus, ≈ λN²/2 ≈ 13 µm at f/7.
- `K_P` — Petzval share of the injected curvature (advanced, default 0). Petzval contributes to the mean
  surface but not to the split; setting it non-zero simulates an uncorrected, curvature-dominated rig.
- `ρ` and `Backfocus Spacing Error` are **deleted**. ρ is pinned to ½ by Seidel; the spacing error becomes a
  read-only derived readout (`e ≈ C / 50` mm).

Predicted for a 130 mm f/7 refractor, full frame, 3.76 µm pixels, `a_c` = +15 µm, seeing σ ≈ 1 px:

**Pure tilt, zero backfocus error** — the case that currently renders round:

| tilt effect | semi-axes | axis ratio | eccentricity | shipped model |
|---|---|---|---|---|
| 30 µm | 45 / 15 µm | 3.00 | **0.37** | 0.10 |
| 100 µm | 115 / 85 | 1.35 | **0.50** | 0.25 |
| 300 µm | 315 / 285 | 1.11 | 0.40 | — |
| 1000 µm | 1015 / 985 | 1.03 | 0.24 | — |

Radial far corner, tangential near corner, line stars where `|Δ| = a_c`. Note eccentricity **peaks at
moderate tilt and falls away** — extreme tilt makes large round donuts. Tilt moves the eccentric band across
the frame rather than intensifying the corners, so "crank the tilt until the corners go oval" is not a valid
acceptance test, for the simulator or for a real rig.

**Pure spacing error, after centre refocus:**

| e (mm) | corner effect C | semi-axes | orientation | ecc |
|---|---|---|---|---|
| +0.25 | +12.5 µm | 33.8 / 8.8 | radial | 0.30 |
| +1.00 | +50 µm | 90 / 10 | radial | 0.65 |
| −0.25 | −12.5 µm | 3.8 / 21.3 | **tangential** | 0.20 |
| −0.60 | −30 µm | 30 / 30 | **round but soft** (A = 0) | 0 |
| −1.00 | −50 µm | 60 / 40 | radial again | 0.37 |

Signed, asymmetric, analytic — no absolute value. The flip lives inside `|C| < 2·a_c`; beyond it both signs
converge to radial. This reproduces both the vendor chart *and* the field reports that contradict it,
because which one you observe depends on where you are relative to the window.

## What it still does not model

1. **Coma** — spacing error induces it too, and a Newtonian + coma corrector's residual is coma-*dominated*
   ("seagulls", one-sided, never tangential). Not expressible as any T/S defocus split. The renderer would
   need a union-of-displaced-circles mask and a fourth cache-key axis. Deferred: the consumer (HocusFocus's
   own detector and inspector) reduces every star to HFR plus centrally-symmetric eccentricity and
   orientation, so a comatic kernel would be scored as a slightly eccentric radial ellipse anyway.
2. **Tilted or decentred corrector elements** — a genuinely different fault from sensor tilt, producing
   field-constant coma plus field-linear (binodal) astigmatism. Much of what users call "tilt" is this.
   Approximable later by giving `A` and `θ` their own centre, displaced from the defocus axis.
3. **Vignetting / cat's-eye truncation** of defocused donuts — often the most visible real corner asymmetry,
   and frequently misattributed to tilt.
4. Higher-order (r'⁴) field terms; chromatic variation of the residual; diffraction structure.

## Sources

Astro-Physics, *Optimizing Your Field Flattener* (Christen) · Schechter & Levinson 2011, PASP,
[arXiv:1009.0708](https://arxiv.org/abs/1009.0708) · telescope-optics.net (field flattener, curvature of
field, astigmatism) · William Optics *Image Plane Calibration* · Agena Astro *Back Focus Primer* · AstroBin
forum, *Determining best backfocus distance through star testing* · Cloudy Nights threads 586504, 761570,
815063, 809050, 835186 · ASTAP documentation (Han Kleijn) · handprint.com astronomical optics ·
TS-Optics TSFLAT2 tolerance specification.
