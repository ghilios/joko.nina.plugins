# Tilt Adapter Wizard — screw-angle ↔ tilt-effect orientation

**Status:** Fixed (2026-07-22). The durable fix below is implemented — `RebuildDiagram` and the
info-panel readout both convert to physical via `PhysicalToStoredAngle`, and the diagram/readout now
refresh on an adapter-direction change. Covered by `TiltAdapterWizardVMTests`
(`RebuildDiagram_NegativeSign_PlacesPhysicalTopScrewAtCanvasTop` et al.). The camera-simulator virtual
tilt adapter (`SimulatedTiltAdapterVM`) shared the same latent bug — its `SimScrew{N}AngleDegrees` are
response-convention too — and was fixed the same way: the Screw 1 input, derived-angle readouts, diagram,
and row labels convert to physical (`ToPhysicalAngle`), while `SimScrew` stays response-convention so the
physics/coherence/copy paths are untouched. Covered by `SimulatedTiltAdapterVMTests`.
**Date:** 2026-07-04 (investigation); 2026-07-22 (fix)
**Trigger:** User reports, refined across two messages:
1. "The orientation in the tilt adapter calibration wizard is vertically flipped — 0° comes out of the bottom of the image instead of the top."
2. (with screenshot) "The user believes when they turn **Screw #2** it actually moves the **top** instead of the bottom."

NINA renders images with (0,0) at top-left, y-down. Angle convention: degrees CW from straight up (0°=top, 90°=right, 180°=bottom, 270°=left).

## TL;DR

Both reports are the **same root defect** seen under the two adapter-direction settings, on a rig that physically behaves as a **"CW moves adapter toward the objective" (−1)** rig:

- The single persisted field `TiltAdapterOptions.ScrewNAngleDegrees` is consumed **raw** as *both* the **physical screw position** (the wizard diagram) *and* the **response direction** (the inspector guidance). These differ by exactly **180° on −1 rigs**, 0° on the default +1 rig.
- **Screenshot config** (direction = "Toward the camera" = default +1, *assumed*, manual entry): the diagram is **correct** (screw 2 genuinely at 180°/bottom), but the guidance/tilt-effect is **180°-inverted** if the rig is really −1 → "turn screw 2, the top moves."
- **Set direction = "Toward the objective" (−1)** to fix the guidance → the diagram then draws screws 180° from their physical positions → the "0° comes out the bottom" symptom.
- You **cannot make both correct** with one raw-consumed field. That is the real defect.

There is **no rendering-math bug** and **no universal forward-physics sign bug**. The diagram trig `cy = 100 − 75·cosθ` is correct y-down (0° → top). The recent sign-convention commits (`93c52dd`, `81ff1dd`) did not flip the default.

## The two roles of `ScrewNAngleDegrees`

| Consumer | Code | Needs |
|---|---|---|
| Wizard diagram | `RebuildDiagram` plots raw `ScrewNAngleDegrees` via `cx=100+75·sinθ, cy=100−75·cosθ` (`TiltAdapterWizardVM.cs:1938-1949`); labeled "Rectangle = image as shown in NINA. 0° points up" / "top of image" (`DataTemplates.xaml:156,167`) | **physical** image position |
| Inspector guidance | `turns[i] = (2/n)(−A·sinθ + B·cosθ)`, arrow motion `= −σ·turns` (`InspectorVM.cs:1901-1923`); "the stored response-convention screw angle … takes NO sign factor" (`TiltScrewGeometry.cs:106-114`) | **response direction** (where a CW turn drives best-focus high) |

`PhysicalToStoredAngle(physical, sign)` (`TiltScrewGeometry.cs:176-180`) adds **180°** when `sign == −1` (`CurvatureSignWhenCwMovesAdapterTowardObjective`, L150), else 0°. Self-inverse. Manual entry applies it once at Apply (`TiltAdapterWizardVM.cs:841`); wizard-measured runs store `atan2(dA,−dB)` (the measured response) directly. So the stored field is **response-convention**, which equals the physical angle only on +1 rigs.

## Verified numeric traces (physical layout: screw 1 @60°, screw 2 @180°/bottom, screw 3 @300°)

**Case B — screenshot (direction = +1 "toward the camera", assumed):**
- `PhysicalToStoredAngle(60,+1)` → offset 0 → stored 60; `ComputeManualScrewAngles(60,CW,3)` → **60 / 180 / 300** (matches the info panel).
- Diagram: screw 2 @180° → `cy = 100 − 75·cos180° = 175` → **bottom** = correct physical position.
- Guidance reads 60/180/300 as *response* angles. On a true −1 rig, screw 2's real response is 180+180 = **0° (top)**, so the guidance/effect for the bottom screw is 180° inverted → **"turn screw 2, the top moves."**

**Case A — direction correctly set to −1 ("toward the objective"):**
- `PhysicalToStoredAngle(60,−1)` → offset 180 → stored 240; `ComputeManualScrewAngles(240,CW,3)` → **240 / 0 / 120**.
- Guidance reads 240/0/120 = physical+180 = the correct response angles → **guidance correct**.
- Diagram: screw 2 stored 0° → `cy = 25` → **top**, but screw 2 is physically at the bottom → **diagram 180°-flipped** ("0° comes out the bottom").

One field, two masters: fixing one flips the other by 180° on −1 rigs.

## Is "screw 2 moves the top" proof of a −1 rig? (Important nuance)

**Not by itself.** Turning one screw of a 3-screw adapter pivots the plane about the line of the other two (lever arm 1.5·R, `TiltScrewGeometry.cs:128-131`), and the **fitted tilt plane the inspector displays is symmetric top↔bottom about field center** — so the opposite side visibly changing is **normal see-saw physics**, independent of the curvature sign. "The top moved" alone does not diagnose −1.

The genuine −1 signature is **directional**: following the guidance moves tilt the **wrong way** / the side that goes high is opposite what the wizard predicts. That comes from the 180° response offset, not a "which side moved more" argument.

**Definitive test:** run a **6-step "Measure direction" calibration** — it measures the true response of each screw and removes the assumption. Equivalently, check whether following the guidance reduces or worsens the measured tilt.

## Config vs. bug

- The **guidance inversion** (report 2) is a **configuration/assumption mismatch**, not a code bug: direction is `ScrewInwardCurvatureSignIsMeasured == false` here (`TiltAdapterWizardVM.cs:849`; screenshot "Direction is assumed / Curvature ↑ assumed"). The math is correct for whatever σ it's told. **User remedy:** measure the direction (6-step) or set "Toward the objective" and re-Apply (conversion is not retroactive, `TiltAdapterWizardVM.cs:839-841`).
- The **diagram vs. guidance duality** is a **genuine design defect**: even after correctly setting the direction, the diagram will misplace screws by 180° on −1 rigs (Case A). The wizard diagram is untested (`RebuildDiagram` / `ScrewDiagramItems` have zero test references), which is why it went unnoticed.

## Durable fix (decouple the two angles)

Keep the guidance consuming the response-convention angle (do not change), and make every **image-space display** convert to physical first (self-inverse `PhysicalToStoredAngle`):

- **Diagram** — `RebuildDiagram` (`TiltAdapterWizardVM.cs:1938-1943`): convert each `ScrewNAngleDegrees` via `PhysicalToStoredAngle(angle, ScrewInwardCurvatureSign)` before plotting.
- **Info panel** — `DataTemplates.xaml:181/185/189/204` binds raw stored angles (prints e.g. "210.0°"); needs a converter or a VM-exposed `PhysicalScrewNAngleDegrees` so it prints the physical angle.
- **Regression test:** assert that a −1-rig calibration with a physically-top screw renders its `TiltScrewDiagramItem` at canvas-top (`Y ≈ 25 − 12`), not bottom.

This makes the diagram/info panel always show physical image positions (matching their labels) while the guidance stays correct — resolving both reports. It does **not** change any tilt/backfocus math.

Caveat: this fixes the *display*. The underlying user problem in the screenshot is still that the **direction is assumed wrong**; the guidance is only correct once the direction is measured/set. The display fix and the "measure your direction" guidance are complementary.

## Evidence index

`TiltScrewGeometry.cs:150,153-167,176-180` (sign anchor, default=+1, PhysicalToStoredAngle) · `TiltAdapterWizardVM.cs:841-849,1938-1949` (manual store, diagram) · `InspectorVM.cs:1901-1923` (guidance turns/arrows) · `TiltCalibrationCalculator.cs:231-238` (ComputeManualScrewAngles) · `TiltModel.cs:91-111` (B<0 for top-high; y-down) · `DataTemplates.xaml:156,167,181-204` (diagram labels, info panel) · commits `93c52dd` (additive anchor), `81ff1dd` (sign restricted to backfocus; manual-entry convention).
