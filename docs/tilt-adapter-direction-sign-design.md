# Adapter direction (`ScrewInwardCurvatureSign`): the measured sign came out inverted

**Status: RESOLVED — superseded by [`docs/focuser-direction-convention-design.md`](focuser-direction-convention-design.md), implemented 2026-08-04.**

**Hypothesis 1 was confirmed, in a strengthened form.** `ComputeCurvatureSign` was not merely inverted on
*this* rig — it was inverted on **every** rig. Writing the stored sign as `σ = sign(m)·sign(k)` (adapter
mechanics × focuser convention) shows that `m` and `k` enter both σ and the all-screws probe only through
their product, so the focuser convention **cancels out of the measurement**. That kills hypothesis 2 ("this
rig's focuser is inverted; the code is right in general"): there is no focuser convention under which the old
formula measured the sign the consumers need. The fix is therefore unconditional and needs no user input.

The same factorization explains why the existing direction setting felt opaque: it asks the user a question
about `m` but stores σ, with the `m ↔ σ` conversion silently pinned at `k = +1`. The resolution keeps σ
measured and authoritative for all motion, and adds `k` as a separate, **display-only** setting so the
mechanical wording is true rather than assumed.

The investigation also turned up a **second** inversion hiding behind the first: `PhysicalToStoredAngle`
assigned its 180° offset to the wrong half of the rigs, and the two had been cancelling in the screw diagram.
They were corrected together.

Retained below for the log evidence and the original reasoning.

---

**Original status:** analysis + options. **Needs a decision before any code change** — this sign decides which way every
automated correction turns, and getting it wrong drives tilt in the wrong direction unattended.

## What happened

Session `20260803-200647` (NINA log). The first hands-off calibration run measured the adapter direction as
**"+ steps move the adapter toward the objective"**. Automatic Adjustment then made aberration *worse*.
Manually flipping the setting to **"toward the camera"** made subsequent adjustments *improve* the result.

## What the log shows

The run's six steps, with the fitted sensor model's `Z0` (the best-focus focuser position at the sensor centre)
and the curvature coefficient `Kx = Ky`:

| Time | Step | Device move | `Z0` (focuser steps) | `Kx` |
|---|---|---|---|---|
| 21:27:50 | Baseline (a) | — | 5498.4 | −1.3271e−07 |
| 21:32:12 | AllInward (b) | `bf,+150` | **5136.6** | −1.4604e−07 |
| 21:35:57 | ReBaseline1 | `bf,−150` | 5445.5 | −1.4176e−07 |
| 21:39:30 | Screw1 | `tr,+150` | 5383.8 | −1.5247e−07 |
| 21:42:53 | ReBaseline2 | `tr,−150` | 5373.8 | −1.7403e−07 |
| 21:46:21 | Screw2 | `tl,+150` | 5359.8 | −2.0968e−07 |

`RunCalibrationMath` computes the direction as
`ComputeCurvatureSign(b.Mean, a.Mean) = sign(b.Mean − a.Mean)`, where `Mean` is `MeanFocuserPosition`:

    sign(5136.6 − 5498.4) = sign(−361.8) = −1

`CurvatureSignWhenCwMovesAdapterTowardObjective = −1`, so −1 reads as **"CW moves the adapter toward the
objective"** — exactly what the user saw, and the opposite of what their rig actually does. The measurement was
not corrupted by the serial desync fixed alongside this: the physical moves did execute, and the AF/sensor-model
readings are independent of the serial link.

## Why it can come out inverted

The convention is stated in `docs/tilt-guidance-motion-arrows-design.md` §"Two vocabularies":

> Because "adapter moves toward the objective" ⇔ "the local best-focus position decreases" …

(That quoted equivalence is itself backwards, and it has since been corrected in place — see
`docs/focuser-direction-convention-design.md` §1(a). It is quoted here as it stood, because it is what
`ComputeCurvatureSign` encoded.)

Working it through for a standard focuser (increasing
position = drawtube extends = camera moves **away** from the objective):

- Let `S` be the objective→sensor distance. `S = k·P + B + const`, with `P` the focuser position and `B` the
  adapter's spacing contribution.
- The objective's focal plane `F` is fixed. In focus, `S = F`.
- If the adapter moves the sensor **toward the objective** (`B` decreases by Δ), restoring `S = F` requires
  `k·P` to *increase* by Δ → **the best-focus focuser position increases.**

So on a standard focuser the equivalence runs the other way: *toward the objective ⇔ best-focus position
**increases***. The log agrees with the standard reading and disagrees with the code's: `bf,+150` — which the
vendor's own mnemonic calls *increasing backfocus*, i.e. the camera moving away from the objective — lowered the
best-focus position by 362 steps, and the user's rig confirms "+ = toward the camera".

Note the two rules in the codebase are not independent: the curvature-effect anchor
(`TiltScrewGeometry`: "moving the adapter toward the objective DECREASES the curvature effect", user measurement
2026-07-02) and the focuser-position rule are two faces of the same stored sign, so the log's curvature change
(effect at the screw radius went −401 µm → −442 µm, i.e. decreased) agrees with the code's conclusion by
construction. It is not independent confirmation.

**Therefore one of these is true, and the log alone cannot separate them:**

1. **The convention is inverted.** The equivalence above (and hence the 2026-07-02 anchor derived with it) has
   the wrong sense, and every rig has been getting the direction backwards whenever it was measured rather than
   set by hand. The physics argument above says this is the most likely one.
2. **This rig's focuser is inverted.** Its driver reports increasing position as moving *toward* the objective.
   Then the code is right in general and needs to learn the focuser's convention rather than assume it.
3. **The inversion is downstream.** The sign is measured correctly but consumed with the wrong sense in
   `TiltScrewTargets.ComputePerScrewTargets` (or the guidance arrows), and flipping the *setting* merely
   cancels that bug — in which case the displayed mechanical direction is now wrong even though corrections
   improved.

## The decisive experiment (≈10 minutes, no code)

On the affected rig, with the device connected:

1. Note the current best-focus focuser position.
2. Send `bf,+150` (or use the wizard's all-screws step) and re-run autofocus. Note the new position.
3. Independently establish which way the plate physically moved — caliper across the adapter, or the vendor
   app's own backfocus labelling.
4. Also note whether the focuser's position increases as the drawtube extends.

That fixes the mapping for this rig and, with (4), separates option 1 from option 2. Repeating on a second rig
with a known-standard focuser settles it outright.

## Options

| # | Change | Pros | Cons |
|---|---|---|---|
| A | **Flip the equivalence** in `ComputeCurvatureSign` (and correct the anchor + motion-arrows doc) | One-line fix at the true root if option 1 holds; every future calibration is right | Wrong if the real cause is 2 or 3; silently inverts existing stored calibrations, so it needs a migration/re-measure prompt |
| B | **Derive the sign from the focuser's own convention** rather than assuming it | Rig-independent and correct by construction | NINA does not expose "which way is out"; would need a user-answered question or a probe move |
| C | **Confirm with the user during calibration** — show the measured direction and the evidence, ask them to confirm before it is stored | Cheap, safe, and puts a human on the one decision that silently inverts automation | Breaks the hands-off promise of the 6-step run (one prompt at the end) |
| D | **Cross-check the two signals and warn on disagreement** — measure via focuser mean *and* via the curvature effect's magnitude, and refuse to mark the sign "measured" when they disagree | Turns a silent wrong answer into a visible one | They are not independent under the current anchor, so this only helps once the anchor is settled |

**Recommendation:** run the experiment, then take **A** if it confirms option 1 (with a one-time "re-measure
your adapter direction" prompt for existing profiles), plus **C** as a permanent backstop — the cost of a single
confirmation prompt is trivial against an unattended correction that drives tilt the wrong way.

## Already done (no behaviour change)

`RunCalibrationMath` now logs the decision and its inputs — the mean-focus positions on both sides, the
curvature effects, the resulting sign, and the mechanical direction in words. Reaching this analysis previously
required reconstructing `Z0` values from raw model solves; the next run answers it from one line.
