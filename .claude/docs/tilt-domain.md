# Domain Concepts: Tilt and Tilt Adapters

Read this when working on sensor tilt, the aberration inspector, tilt adapters, or any feature that reasons about screw orientation or tilt correction.

**Sensor tilt** occurs when a camera sensor is not perfectly orthogonal to the optical axis of the attached telescope. This causes one side of the image to be in better focus than another — stars in one region are sharp while stars in the opposite region are bloated/defocused. The aberration inspector detects and quantifies this tilt.

**Tilt adapters** sit between the telescope and camera and allow the sensor plane to be physically adjusted. Turning a screw inward pushes that corner of the sensor away from the telescope (increases the distance on that side), tilting the sensor plane toward the opposite side.

## Adapter configurations

- **3-screw adapter**: Screws are arranged in a triangle. Each screw can be turned fully independently.
- **4-screw adapter**: Screws are arranged in a square. Opposite screws are mechanically coupled, so adjustments must be made in pairs (or all four at once).

## Screw orientation and coordinate system

Each screw has a physical orientation relative to the sensor that determines how turning it affects the tilt vector:

- **Origin**: center of the sensor image.
- **Angle convention**: clockwise from straight up (12 o'clock) = 0°. So 0° is straight up, 90° is to the right, 180° is straight down, 270° is to the left.
- **Effect**: a screw at angle θ pushes the sensor away from the telescope in the direction opposite to θ — i.e., turning the screw inward tilts the sensor plane such that the side at angle θ moves away, which brings the opposite side (θ + 180°) closer.

Example: a screw at 0° (straight up from center) — turning it inward tilts the sensor plane along the vertical axis, pushing the top of the sensor away from the telescope.

## Guidance arrows vs rotation glyphs

The inspector's Tilt Adapter Guidance table uses two glyph vocabularies that answer different questions. The ⬆/⬇ arrows describe adapter-plate **motion** (⬆ = that corner moves toward the objective) — pure physics, identical on every rig. The ⟳/⟲ glyphs (screws) and +/− step signs (steppers) on the numeric rows carry the rig-specific **rotation** that produces that motion, which depends on the adapter's direction setting (`ScrewInwardCurvatureSign`). Never present ⬆/⬇ as a rotation. The full contract and sign derivations are in `docs/tilt-guidance-motion-arrows-design.md`.

## Live motor positions while a motorized device is being driven

`TiltDeviceConnectionService` polls the device's per-motor counters (`cp`) every 5 s, but that poll is
**suspended for the entire lifetime of a `TryBeginOperation` lease** — a wizard calibration run, or an
Automatic Adjustment plan including its confirming inspector run and any revert. Anything that displays
`CurrentPositions` therefore freezes for the whole operation unless the operation itself republishes.

- **The lease holder must call `TiltDeviceConnectionService.PublishControllerPositions()` after every move it
  sends** — forward moves, revert moves, and recovery sweeps alike. It publishes to `CurrentPositions` /
  `PositionsKnown`, so *every* panel bound to them (inspector and wizard) updates from one call.
- **Never issue a follow-up `cp` per move to get those numbers.** Every EAT move response already embeds a
  fresh `***Get Current Positions***` block (`docs/asg-eat-serial-protocol-design.md` §4.2), which
  `EatTiltMotionController.ExecuteMoveAsync` reconciles into the shadow and exposes as
  `ITiltMotionController.LastKnownPositions`. `PublishControllerPositions` reads that — no I/O, no extra ~1 s
  round trip, and no second chance for a read to come back unparseable and silently freeze the display.
- Positions that were never confirmed read **"unknown"**; a publish that finds none deliberately leaves the
  last published values alone (a mid-plan gap must not blank a display that was right a moment ago) and logs
  a warning, so a stale panel is diagnosable rather than silent.

## Image mirroring

Camera images may be mirrored horizontally and/or vertically depending on the optical train (e.g., a star diagonal introduces a mirror). **Do not assume that screws numbered clockwise around the physical adapter will appear clockwise around the sensor image.** The screw orientations must be determined from the actual image coordinates after accounting for any mirroring. Plans and features that involve tilt correction must track orientation in image-space, not physical-space.
