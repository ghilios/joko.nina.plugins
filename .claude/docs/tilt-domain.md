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

## Image mirroring

Camera images may be mirrored horizontally and/or vertically depending on the optical train (e.g., a star diagonal introduces a mirror). **Do not assume that screws numbered clockwise around the physical adapter will appear clockwise around the sensor image.** The screw orientations must be determined from the actual image coordinates after accounting for any mirroring. Plans and features that involve tilt correction must track orientation in image-space, not physical-space.
