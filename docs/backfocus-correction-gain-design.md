# Backfocus Correction Gain: Why the Recommended Move Is ~7× Too Small

**Status:** design, not yet implemented. Companion plan: `plans/backfocus-correction-gain-plan.md`.

**Datasets:** `D:\TiltCalibrationDebug\WithExtraBaseline` (7-step calibration, schema 3) and
`D:\TiltCalibrationDebug\Minus_{100..500}` (five aberration-inspector runs at cumulative piston
offsets of −100 … −500 motor steps on all four screws), captured 2026-08-04 on a 4-screw ASG EAT
(spec 1.8 µm/step, radius 55 mm) with an IMX455-class sensor (9576×6388 @ 3.76 µm), focuser step
0.269 µm, f/6.3, 882 mm, corrector mounted on the drawtube.

---

## 1. The defect

`TiltScrewGeometry.ScrewCorrectionMicrons` computes the backfocus component as the fitted
paraboloid's curvature sag evaluated at the screw position:

```csharp
double backfocusCorrection = -(kx * cx * cx + ky * cy * cy);
```

`SignedTotalAdjustment` then divides by µm-per-step to get a screw move. The method's own doc
comment states the assumption plainly: *"Both are in axial best-focus microns (the same unit as
physical screw travel), so the total is simply their sum."*

That is an **implicit gain of 1.0** — move the plate by X microns, remove X microns of curvature.

For the **tilt** term the assumption is exactly right: displacing a screw by X tilts the plane by X
at that screw, by construction. The code applies the same reasoning to the **curvature** term, where
it does not hold. Translating a flat sensor along the optical axis cannot make it match a curved
focal surface; it only re-centres the defocus. What actually changes the field curvature is a
second-order effect, and it is much weaker than 1:1.

**Measured gain on this rig: 0.140.** The backfocus recommendation therefore understates the
required move by **7.1×**.

| | steps | plate travel |
|---|---|---|
| what the model recommends today | 137 | 0.25 mm |
| what the measurement says is required | 974 | 1.75 mm |

## 2. The mechanism

The chain matters, because it determines when the gain is non-zero at all.

1. The tilt adapter sits between the corrector and the sensor, so moving the plate by δ is a pure
   image-space displacement: defocus changes by δ, coefficient 1.
2. Restoring focus moves the focuser by δ/m², where m is the corrector's transverse magnification
   (the focuser moves corrector *and* camera together, so in the corrector's frame the object moves
   and the image follows with longitudinal magnification m²).
3. **Because the corrector rides the drawtube, that focuser move changes the corrector-to-focal-plane
   distance**, and therefore changes m: `dm = −(m/f)·Δ = −δ/(m·f)`.
4. A corrector only cancels the telescope's field curvature exactly at its design magnification. The
   change in m leaves residual curvature, which is what the sensor measures as a change in sag.

So the backfocus gain is a *composite* quantity: field-curvature sensitivity to corrector position,
mediated by the refocus that the plate move forces.

**Consequence worth stating explicitly:** on a rig where the corrector is threaded to the telescope
and only the camera moves, step 3 does not happen, m never changes, and γ ≈ 0 — the tilt adapter
could not correct backfocus at all, at any travel. The correction only works because the corrector
moves with the focuser.

## 3. Can γ be predicted from the reducer ratio?

**No — not from the ratio alone.** From §2, γ ∝ (1/f)·(dSag/dm). Predicting it requires:

- the corrector's **focal length** f (not the reducer ratio), and
- **dSag/dm**, how much residual field curvature a unit change in magnification produces — a property
  of the corrector's internal design (element powers and spacings), not of its net magnification.

Two correctors with the same 0.9× ratio can have quite different γ. Working the numbers backwards on
this rig: closing the measured 25.08 µm of sag at r = 14.43 mm needs Δm ≈ 0.0116 (a 1.2% change in
magnification), which implies dSag/dm ≈ 2172 µm at that radius. Nothing in "0.9×" predicts that.

**What *can* seed γ without measuring it:** manufacturers commonly publish a backfocus tolerance
("55 mm ± 0.5 mm"). That tolerance is γ in disguise — it is the spacing error at which residual
curvature becomes visually unacceptable. If a user knows their corrector's tolerance and the sag
threshold it implies, γ can be estimated to within a factor of ~2. That is far better than the
current 7× error, and it is the basis for the manual-override path in §5.

The measurement remains strictly better, and it is already free (§4).

## 4. The calibration run already measures γ

The **AllInward** step is a controlled piston: all four screws move by a known amount, and the
curvature effect is measured before and after. That is exactly the experiment needed.

It must be **drift-corrected**, using the same `mid(Baseline, ReBaseline1)` reference the piston-implied
pitch already uses — the curvature itself drifts substantially over a run (−268.6 → −194.1 µm across
this run's four baseline-equivalent states, ≈ +3.1 µm/min):

| | value |
|---|---|
| Baseline curvature effect at r = 55 mm | −268.6 µm |
| ReBaseline1 | −223.7 µm |
| drift-corrected reference, mid(Baseline, ReBaseline1) | **−246.11 µm** |
| AllInward | −208.2 µm |
| Δ over 150 steps | **+37.88 µm → 0.2526 µm/step** |

**Validation against an independent experiment.** The five-run piston sweep (−100 … −500, a 3.3×
larger excursion in the opposite direction) gives **0.258 µm/step**. The two agree to **2.1%**.

Drift correction is not optional: uncorrected, the AllInward delta reads 0.402 µm/step, **59% too
high**. The same sweep, uncorrected, reads 1.958 µm/step for the piston pitch instead of 2.32 —
thermal drift of −26.2 focuser steps/min ate 18% of the signal.

### Supporting results from the same datasets

- **Piston pitch cross-validated.** AllInward (single +150 push) gives 2.3058 µm/step; the
  drift-corrected 5-run sweep (−500 pull) gives 2.3226–2.3404. Agreement ~1% across opposite
  directions and a 3× range difference.
- **Frame factor F = 1.29–1.30 → m ≈ 0.88**, consistent with a ~0.9× corrector.
- **The piston response is linear to ~1.3%** over 930 µm of focuser travel. The residual quadratic
  has the sign and magnitude predicted by F = 1/m² with the corrector moving. Non-linearity is real
  but is not a significant error source at these excursions.
- **Field dependence of F is 0.76%** out to r = 14.43 mm, measured drift-immune as the difference
  between the centre and corner-mean piston slopes (8.700 vs 8.634 focuser steps per motor step).
  This is tighter than, and supersedes, the 3.5% ± 1.3% figure derived earlier from separately
  drift-corrected centre and corner pistons.

## 5. Design

**γ is measured, persisted, and shown — and the corrected number is presented alongside the current
one rather than replacing it.** The 7× change is too large to apply silently on the strength of one
measurement per run.

1. **Compute γ** in `TiltCalibrationCalculator`, from the drift-corrected AllInward curvature delta,
   as a dimensionless gain (µm of curvature effect per µm of plate travel). NaN when no piston ran.
2. **Sanity-band it.** Accept only γ ∈ [0.05, 0.5]. Outside that, treat it as unmeasured — a
   mis-measured γ is the principal risk of this change, and one noisy run must not produce a
   multi-millimetre recommendation.
3. **Persist** the last accepted γ alongside the measured pitch, so runs that skip the piston step
   still benefit. Record its provenance (measured this run / persisted from an earlier run /
   manual override / unavailable).
4. **Manual override.** Let the user enter γ directly, for rigs where the corrector's published
   backfocus tolerance is the best available information (§3), or where they have measured it
   themselves.
5. **Show both values.** The guidance displays the current (gain = 1) backfocus figure and the
   gain-corrected figure side by side, labelled with γ and its provenance. The user chooses. This is
   deliberately not an auto-apply: see §7.
6. **Default `MeasureCurvatureDuringCalibration` to ON.** It is now the source of three distinct
   quantities — adapter direction, piston-implied pitch, and γ — and without it most users never get
   an accurate backfocus number. Both optional measurement steps gain explanatory copy stating what
   the extra autofocus runs buy.

## 6. What this does not fix

γ corrects the **conversion**. It does not correct the **curvature magnitude** being converted, and
the two errors compound.

The magnitude comes from the fitted paraboloid, which §4 of
`tilt-calibration-pitch-nonlinearity-design.md` shows under-reads curvature. On this run the
paraboloid puts the baseline curvature effect at −246 µm while the corner-region AF puts it at
−364 µm — a factor of 1.48, which is the difference between a 1.75 mm and a 2.54 mm recommendation.

So after this change the recommendation should still be expected to land low, and should be treated
as a floor rather than a target until the vertex-estimator work in §8 of that document is done.
Iterating (apply, re-measure, refit) converges regardless, because the *local* gain is well
determined even when the extrapolation to zero sag is not.

## 7. Deliberate decisions

- **Show both values rather than auto-applying.** A 7× change in a recommendation that drives
  physical hardware adjustment should not switch over silently on one run's measurement.
- **Sanity band before persistence**, so a bad run cannot poison later runs that rely on the
  persisted value.
- **γ is reported as a gain, not folded into the pitch.** They are independent quantities measured by
  different parts of the same step, and conflating them would make both harder to debug.
- **No change to the tilt term.** Gain 1.0 is correct there and is validated by the synthetic
  round-trip test added in PR #178.

## 8. Open questions

- Whether γ is stable across focuser position. It is measured near the calibration focus; the
  required correction may be millimetres away, where m — and therefore γ — differs. The measured 1.3%
  non-linearity in the piston response suggests the drift is small over ~1 mm, but this is untested.
- Whether the sag target is exactly zero. The design assumes the corrector fully flattens at correct
  spacing; a corrector that leaves residual curvature by design has a non-zero target.
- Whether γ should eventually be measured at two piston offsets within one calibration, which would
  give the local slope without relying on the drift correction being right.
