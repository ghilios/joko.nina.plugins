# Motorized Tilt Adapter Automation (ASG EAT) — Design

> Implementation plan: `plans/motorized-tilt-adapter-eat-plan.md`.

## Context

HocusFocus's Aberration Inspector already computes precise per-screw tilt + backfocus corrections
(signed stepper steps for the ASG EAT presets in `TiltAdapterDevicePreset.cs`), but the user must
apply them by hand in ASG's app. The ASG EAT is a 4-corner motorized tilt adapter controllable over
serial. This feature closes the loop: connect to the EAT from the Tilt Adapter Calibration pane,
compute a **minimal move sequence** from the fitted sensor model, get user approval, execute, and
offer to re-run the inspector to confirm. The calibration wizard also drives its own measurement
moves when connected (hands-off calibration). Design is extensible to other 4-corner coupled
devices and future 3-corner independent devices.

## Device facts (ASG EAT)

- Serial: **9600 baud, 8 data bits, 1 stop bit, no parity, XON/XOFF** flow control.
- Spec: `C:\Users\ghili\Downloads\ASG EAT Serial Commands.pdf` (commands only). **Response protocol
  unknown** — captured live in T15. Moves take **5–10 s**; every move/config persists to EEPROM.
  There is **no abort command** → cancellation is between-commands only.
- Commands (`<mnemonic>,<value>`): diagonal `tr/tl/br/bl,N` (corner +N, opposite −N, 2 motors);
  edge `tp/bt/lt/rt,N` (edge pair +N, opposite pair −N, 4 motors); backfocus `bf,N` (all 4 +N);
  `zr` (zero position counters, no motion), `cp` (query positions), `cA/cB/cC,x` (speed/max/accel,
  defaults 100/100/300), `or,1-4` (display-only graphic — cosmetic, NEVER rotation compensation),
  `ep`/`up` (EEPROM read/update). v1 deliberately does not expose `cA/cB/cC`, `or`, `ep`, `up`,
  **or `zr`** — zeroing positions is the user's job in the vendor app; the plugin never zeroes.
- Device corner labels: **TR=motor1, TL=motor2, BR=motor3, BL=motor4** (opposite pairs (1,4),(2,3)).
- Step size 1.8 µm/step, screw radius 55 / 62.75 mm (already in the two EAT presets,
  `TiltAdapterDevicePreset.cs:67-77`).

## User decisions already made (do not relitigate)

1. The wizard's calibration moves are automated too when the device is connected.
2. Approval dialog groups moves as **Tilt** and **Backfocus** with a checkbox per group.
3. Safety = soft position limits + configurable max-excursion and max-steps-per-command.
4. Manual connect button; device + COM port persisted per profile; **no auto-connect**.
5. **No zero-positions capability** in the plugin — the user zeroes in the vendor app.
6. **Per-screw current positions displayed while connected**, refreshed by polling `cp`.
7. After **30 minutes idle** while connected, a **modal dialog** asks whether to disconnect.
8. The wizard gets a **movement-amount-per-calibration-step setting**: screw adapters default to
   **1 rotation**, ASG EAT presets default to **150 steps**.
9. **One adjustment per measurement (hard invariant):** live automated adjustment never executes
   more than one approved plan before requiring user input again, and a **fresh sensor-model run
   is required to confirm the improvement** before any further automatic adjustment is possible.
   No auto-looping; the same measurement can never be applied twice.

## Core algorithm — minimal-move decomposition (4-corner coupled)

Work in **wizard screw indices** (consecutive around the image; opposite pairs (1,3),(2,4) —
`TiltCalibrationCalculator.ComputeScrewAngles`, TiltCalibrationCalculator.cs:209-230).
Per-screw signed step targets come from `TiltScrewGeometry.ScrewCorrectionMicrons` +
`SignedTotalAdjustment` semantics (`InspectorVM.FillNumericGuidance`, InspectorVM.cs:2006-2072):
`s_i = (tilt_i + σ·backfocus_i) / unitMicrons`, real-valued (σ = resolved
`ScrewInwardCurvatureSign`; **σ multiplies backfocus only** — tilt direction is already encoded in
the stored response-convention angles).

Device generators in `(s1,s2,s3,s4)` space (with the T4 mapping wizard1=TR, wizard2=TL,
wizard3=BL, wizard4=BR):

| Generator | Per-corner effect | EAT command |
|---|---|---|
| D1·x | (+x, 0, −x, 0) | `tr,x` |
| D2·y | (0, +y, 0, −y) | `tl,y` |
| E⁺·m = (D1+D2)·m | (+m, +m, −m, −m) | `tp,m` |
| E⁻·m = (D1−D2)·m | (+m, −m, −m, +m) | `rt,m` |
| BF·k | (+k, +k, +k, +k) | `bf,k` |

D1, D2, BF are mutually orthogonal spanning a 3-D subspace of R⁴. Exact least-squares projection:
`a = (s1−s3)/2` (D1), `b = (s2−s4)/2` (D2), `f = (s1+s2+s3+s4)/4` (BF), twist residual
`t = (s1−s2+s3−s4)/4` — unreachable by ANY rigid-plane device. Report `t`, never plan it. (Note:
`t` is exactly 0 for a square-clocked EAT with the axis-aligned paraboloid model; it becomes
nonzero only when the adapter is clocked off 45° — don't treat the reporting path as dead code.)

**Group split is defined in COMMAND space, not by physical origin** (critical: off-center curvature
X0,Y0≠0 leaks a *linear* term into the D1/D2 components — a split by physical origin would drop it):
- **Backfocus group** = BF projection `f = mean(s)` → one `bf` move.
- **Tilt group** = D1/D2 projections of `(s − f)` → diagonal/edge moves.
Groups stay orthogonal so the dialog checkboxes are independent and nothing is dropped.

**Minimal command count (≤ 3):** round `a`, `b`, `f` independently to integers (bounds per-corner
residual at 1.0 step = 1.8 µm — do not add a fancier optimizer).
- Tilt: 0 moves if `ra==rb==0`; **1 edge move** iff `ra == ±rb ≠ 0` (all four sign combos map to
  tp/bt/rt/lt with positive N); **1 diagonal** if exactly one nonzero; else **2 diagonals**.
  Never force a common magnitude to save a move (a=3.4, b=2.4 via `tp,3` is worse than 2 diagonals).
- Backfocus: +1 move if `rf≠0`.
- Per-move cap splitting (if |magnitude| > cap) happens AFTER minimal decomposition; the axes are
  orthogonal so Σ ceil(|mᵢ|/cap) is provably minimal under the cap.
- Report per-corner residual µm = (applied − target)·1.8 in the approval dialog.

**3-corner extensibility:** planner per topology behind one interface — `FourCornerCoupledPlanner`
(above) vs future `ThreeCornerIndependentPlanner` (one move per screw; requires per-screw
independent capability which the EAT driver declares it lacks).

## Mapping contract + device-linked calibration gate

The wizard requires opposite pairs at indices (1,3)/(2,4); EAT labels have opposite pairs
(1,4)/(2,3). **Resolution:** when calibrating with the device connected, the wizard *defines*
wizard-screw1 ≡ TR (`tr`), wizard-screw2 ≡ TL (`tl`) ⇒ wizard-screw3 ≡ BL, wizard-screw4 ≡ BR.
Every wizard step then maps 1:1 to a command (Screw1 step "+N motor1/−N motor3" = `tr,N`;
Screw2 = `tl,N`; AllInward = `bf,N`; ReBaseline1 = `bf,−N`; ReBaseline2 = `tr,−N`;
**Complete's "return to original position" = `tl,−N`**). `ComputeScrewAngles` supports both
windings (clockwise = rawDiff<180, TiltCalibrationCalculator.cs:215), so TR→TL adjacency fits
regardless of image mirroring. Sign convention becomes self-consistent: calibration measures the
response of the device's actual `+` direction because the wizard itself sent the commands.

**[CRITICAL GATE]** Automation must be blocked unless the stored calibration is device-linked.
`IsCalibrated` is set by ANY completed run (manual, manual-entry `ApplyManualCalibration`, replay)
— a pre-existing manual EAT calibration where "screw 1" was, say, BR would make automation apply
corrections rotated 90°/180°, worsening tilt unattended. Persist a **`DeviceLinkedCalibration`**
marker (device preset name, set ONLY when a connected hands-off calibration completes; cleared by
manual entry, replay-driven `RunCalibrationMath`, and disconnected runs). Gate the inspector's
"Automatic Adjustment" button AND wizard auto-apply on it; remediation text: "Re-run calibration
with the device connected." Also gate on calibration quality: `RunCalibrationMath` sets
`IsCalibrated=true` even when quality validation fails — persist/reuse the confidence result and
hard-block automation on unreliable calibrations.

## Architecture (component map)

```
Interfaces/
  ITiltMotionController.cs        device abstraction (topology, capabilities, moves, positions)
TiltAdapterDevices/
  TiltAdapterMove.cs              move axes/groups + per-corner effect vectors
  TiltAdapterMovePlan.cs          moves + residuals + twist + time estimate
  TiltMovePlanner.cs              pure static decomposition (core math)
  TiltMotionControllerRegistry.cs preset-name → driver factory ("append one entry" pattern)
  TiltDeviceConnectionService.cs  shared connection singleton (HocusFocusPlugin static):
                                  cp position polling + 30-min idle prompt + operation token
  Prompt/                         approval dialog (4-file ReplaySettingsPrompt pattern)
  AsgEat/
    EatCommands.cs                wire formatting + EatSignEncoding strategy   [sign seam]
    EatSerialTransport.cs         custom read loop, transcript logging        [SEAM FILE]
    EatResponses.cs               tolerant parsers (ack, cp)                  [SEAM FILE]
    EatWizardMapping.cs           wizard-index/step ↔ device move
    EatTiltMotionController.cs    limits, shadow positions, execution
TiltAdapterWizard/
  TiltScrewTargets.cs (new)       shared pure per-screw target computation
  TiltAdapterOptions.cs (+ Interfaces/ITiltAdapterOptions.cs)  new persisted options
  TiltAdapterWizardVM.cs          connection UI + hands-off calibration
  DataTemplates.xaml              connection section + option controls
AutoFocus/
  InspectorVM.cs                  Automatic Adjustment command; guidance uses shared helper
  DataTemplates.xaml              button in Tilt Adapter Guidance expander (:2809+)
```

Key reuse: NINA.Core `ISerialPort`/`SerialPortProvider` (WMI port enumeration,
`Handshake.XOnXoff` supported), `SerialPortClosedException`, `InvalidDeviceResponseException` —
already resolved transitively (System.IO.Ports 8.0.0 + System.Management 8.0.0 via
Microsoft.Windows.Compatibility; **no new package refs**). Do **NOT** subclass `SerialSdk`: it does
one `ReadLine` (500 ms default, 2 retries) per command and swallows write timeouts — unusable for
5–10 s moves under XON/XOFF (verified in NINA source). Custom transport is a decision, not an option.
Modal dialog pattern: `AutoFocus/Replay/ReplaySettingsPrompt.ShowAsync` (IWindowServiceFactory +
TaskCompletionSource). Re-run inspector: `InspectorVM.AnalyzeAutoFocus(token, captureCameraBlock,
saveOverride)` (InspectorVM.cs:251; the wizard already calls it at TiltAdapterWizardVM.cs:1266).

## Risks / future work

- **Response protocol unknown** — contained to 2 seam files + 2 sign constants; everything else
  is contract-tested and must not change in T15.
- **σ / direction errors** are the highest-consequence failure (EEPROM-persisted wrong-way moves);
  mitigations: 6-step default when connected, device-linked gate, per-mnemonic live cross-check,
  approval dialog with signed moves, post-adjustment worsening check + revert journal.
- Camera/adapter rotation after calibration invalidates angles (no cheap detection) — documented;
  worsening check is the backstop. `or` is cosmetic only.
- Future: `ThreeCornerIndependentPlanner` (interface in place), other 4-corner devices (registry
  "append one entry" + driver), sequence-item automation, manual-manual EAT users declaring a
  corner mapping without recalibrating (deliberately out of v1 — recalibration is the safe path).
- v1 leaves `cA/cB/cC`, `or`, `ep`, `up` unexposed.
- Manual (documentation/docs) updates for the new UI belong in a follow-up PR per documentation
  conventions (`.claude/docs/documentation-style.md`).
