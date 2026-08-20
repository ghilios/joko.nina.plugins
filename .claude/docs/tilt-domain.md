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

The inspector's Tilt Adapter Guidance table uses two glyph vocabularies that answer different questions. The ⬆/⬇ arrows describe adapter-plate **motion** (⬆ = that corner moves toward the objective) — physics, identical on every rig *given the focuser-direction setting* (`IInspectorOptions.FocuserIncreasesTowardObjective`, the display-only `k`; translating a z-space quantity into "toward the objective" needs to know which way the focuser travels). The ⟳/⟲ glyphs (screws) and +/− step signs (steppers) on the numeric rows carry the rig-specific **rotation** that produces that motion, which depends on the adapter's direction setting (`ScrewInwardCurvatureSign`). Never present ⬆/⬇ as a rotation. The full contract and sign derivations are in `docs/tilt-guidance-motion-arrows-design.md`; the σ = m·k factorization, why the *measurement* needs no `k`, and the display-only contract are in `docs/focuser-direction-convention-design.md`.

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

## Reverting: putting the adapter back

Two mechanisms, in strict precedence, exposed as "Return to this run" (Aberration Inspector history) and the
"tilt got worse" banner's revert button. `AutoFocus/TiltRevertPlanFactory.cs` owns the choice.

1. **Absolute motor positions** recorded with an Inspector run (`TiltAdapterStateSnapshot`), driven to directly.
   Exact, and it consumes **no calibration at all** — not the screw angles, not `ScrewInwardCurvatureSign`, not the
   pitch. It is therefore deliberately **not** gated on `IsCalibrationDeviceLinked` / `CalibrationIsReliable`; it is
   raw hardware control toward a recorded counter. Keyed by device preset, because counters are per-device.
2. **Session journal** (the worsening banner only) — the inverse of each move just sent, in reverse order. Exact for
   exactly those moves, needs no model and no known positions, and unwinds any controller-prepended backfocus bias
   for free because the bias was part of the executed plan.
3. **Measurement differential** — `T(now) − T(run K)`, where `T` is `ComputePerScrewTargets`. Each run's targets are a
   *state* description ("signed units from here to flat"), so their difference is the motion between the two states.
   Only the two endpoint measurements matter: no assumption that intermediate guidance was applied (unknowable), no
   summing, no error accumulation. This is the only option for manual screws, and it **does** read the calibration,
   so it carries the same critical gate Automatic Adjustment does.

Backfocus stays in the differential: a curvature-only change decomposes to pure piston, with no spurious tilt and no
twist. What is weak about it is the source data — `Kx/Ky/X0/Y0` are the noisiest fitted parameters, and a spacer or
filter change between the runs is silently attributed to the adapter — so the target reports
`BackfocusTrustworthy = false` and the approval dialog is where that group can be dropped.

Two states the device genuinely reached differ by a rigid-plane delta, so a snapshot-vs-current **twist ≥ 1 step is
itself diagnostic**: position tracking drifted (lost steps, or a resync). It is surfaced, not silently projected away.

**Both the run history and the journal are SESSION-ONLY.** The history collections are in-memory and the journal is a
plain field, so after a NINA restart there is nothing to return to — even though the device itself still knows its
counters via `TiltDeviceShadowPositions`. If that is ever wanted, the right shape is a separately-named
"restore points" feature (a capped ring buffer of positions-only records beside the shadow positions), not persisted
measurement history: serializing fitted math DTOs in an options blob is a maintenance tax, and the differential path
needs a *current* model that is also gone after a restart.

## Image mirroring

Camera images may be mirrored horizontally and/or vertically depending on the optical train (e.g., a star diagonal introduces a mirror). **Do not assume that screws numbered clockwise around the physical adapter will appear clockwise around the sensor image.** The screw orientations must be determined from the actual image coordinates after accounting for any mirroring. Plans and features that involve tilt correction must track orientation in image-space, not physical-space.

## Wording: "inward/outward" is adapter-plate motion only

Screw and motor moves are worded CLOCKWISE / COUNTER-CLOCKWISE or as signed steps — never "inward/outward".
The wizard's all-motors step is titled "All Screws Clockwise" / "All Motors Positive Steps", never "All Screws
Inward": it applies `+N` to every motor and **measures** which way the plate travels (that is what sets
`ScrewInwardCurvatureSign`). `WizardStep.AllInward` keeps its historical enum name because it is persisted in
saved replay runs. `TiltAdapterWizardVMTests.NoUserFacingMoveWording_ClaimsInwardOrOutward` is the guard.

## Trusting a hand-entered calibration for automation

`ApplyManualCalibration` clears `DeviceLinkedCalibrationDeviceName` and `CalibrationIsReliable`, which blocks
Automatic Adjustment — a hand-entered screw numbering is not guaranteed to match the device's motor wiring.
The wizard now warns when that revokes something, and the saved-calibration pane offers **Trust This
Calibration for Automation** (a two-step in-pane confirmation, `TrustCalibrationCommand` →
`ConfirmTrustCalibrationCommand`) which re-arms both markers deliberately. There is no new persisted option:
`CalibrationIsManual == true` together with a device link already means "manually trusted". The trust is
revoked by a fresh manual entry, by `ClearCalibration`, and for free by a device-preset change.

## Auto Focus Binning: pair a binned frame with a binned pixel pitch

The wizard's readings are `TiltPlaneModel.A`/`B` — focuser steps per **normalized** image coordinate. Turning
those into a physical gradient (µm of focuser travel per µm of sensor displacement) needs the sensor's physical
extent, `ImageSize.Width × pixelSizeMicrons`, and that product is only right when both factors describe the
**same frame**.

Under NINA's Auto Focus Binning of N (`FocuserSettings.AutoFocusBinning`, or a filter's `AutoFocusBinning`) the
captured frame is N× smaller in each axis and each of its pixels is N× coarser. Star detection already accounts
for this — `HocusFocusStarDetection.BuildResultHeader` sets `PixelSize = metadataPixelSize × BinX`, and the
sensor model converts star positions with it — so the paraboloid's `Gx/Gy/Kx/Ky` and everything the Aberration
Inspector computes from them (including the per-screw correction targets) are physically correct at any binning.

The trap is the round trip back out of `(A, B)`. The wizard used to pair the model's **binned** `ImageSize` with
the profile's **native** `CameraSettings.PixelSize`, understating the sensor by N. Consequences, measured by
`TiltAdapterWizardVMTests.RunCalibrationForTest_AutoFocusBinning_*`:

- **Recovered thread pitch / stepper step size inflated by exactly N** (2× at 2×2, 4× at 4×4). This is the
  number `LastMeasuredThreadPitchMicrons` stores and every automated correction later divides a required µm
  move by, so adopting it made every correction N× too small.
- **The tilt-vs-piston agreement check fires on a perfect calibration.** `PistonImpliedMicronsPerStep` uses no
  sensor geometry at all (mean focuser positions only), so it stayed honest while the tilt-derived pitch
  doubled — a 50% disagreement at 2×2, well past the 20% warning threshold.
- **Screw position angles were unaffected** on symmetric binning: `gx` and `gy` inflate by the same factor and
  `atan2` is scale-invariant. That is why the diagram looked right while the pitch was silently wrong. It would
  *not* survive asymmetric binning (`BinX ≠ BinY`) — but neither would the sensor model itself, which reads
  `BinX` alone.

The fix is structural: `TiltPlaneModel` carries the `PixelSizeMicrons` it was built from, and the wizard's
`EffectivePixelSizeMicrons` prefers it over the profile value, so the pairing cannot be got wrong. Replays are
covered for old and new saved runs alike, because the pitch is re-derived from the frames rather than read from
`metadata.PixelSizeMicrons`.

**Detection binning is a different setting and was never affected.** `StarDetectorParams.DetectionBinning` is
software binning applied inside the detector; it scales every pixel-space output back to source pixels before
returning (`stars.Select(s => s.ScaleToSourcePixels(binning))`, `metrics.ScaleBounds`), and the result header's
`PixelSize`/`ImageSize` are built from the camera metadata and the source frame, never from it. `DetectionBinning`
touches only `PixelScale`, which the tilt path does not read. `DetectionBinningTests` pins the scale-back.

`TiltCalibrationMetadata.PixelSizeMicrons` is the **effective** (binning-scaled) pitch from schema 4 onward;
files written by schema ≤ 3 at a binning above 1×1 hold the native pitch. The headless `TestApp tilt` validator
therefore does **not** trust that field: it reads the pitch off a saved frame's own header
(`EffectivePixelSize.FromFrameHeader`, `Camera.PixelSize × max(BinX, 1)` — the same derivation
`HocusFocusStarDetection.BuildResultHeader` uses live), which is unambiguous at every schema because NINA's FITS
and XISF readers both divide the stored binned `XPIXSZ` back out by `XBINNING`. It falls back to the metadata
value only when no frame carries a pixel size, and prints the provenance either way — plus an explicit note when
the header and the metadata disagree, which is exactly the pre-schema-4 binned case. That also fixes the
detection `PixelScale` the harness tunes with, which had the same native-pitch assumption.
