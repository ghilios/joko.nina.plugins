# Motorized Tilt Adapter Automation (ASG EAT) — Implementation Plan

## Context

HocusFocus's Aberration Inspector already computes precise per-screw tilt + backfocus corrections
(signed stepper steps for the ASG EAT presets in `TiltAdapterDevicePreset.cs`), but the user must
apply them by hand in ASG's app. The ASG EAT is a 4-corner motorized tilt adapter controllable over
serial. This feature closes the loop: connect to the EAT from the Tilt Adapter Calibration pane,
compute a **minimal move sequence** from the fitted sensor model, get user approval, execute, and
offer to re-run the inspector to confirm. The calibration wizard also drives its own measurement
moves when connected (hands-off calibration). Design is extensible to other 4-corner coupled
devices and future 3-corner independent devices.

**Execution notes for coordinating lower-powered models:**
- `/clear` before executing (project rule). Tasks are small, **test-first**, independently verifiable.
- Run `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` after every change
  (Windows dotnet.exe via WSL interop; timeout 600000). Never skip/ignore failing tests.
- Every new `.cs` file gets the MPL copyright header (copy from `TiltScrewGeometry.cs:1-13`).
- Parallelizable groups are marked; sequential spine is T1→T5→T6→T7→T11 and T2/T12/T13→T14.
- Work on branch `ghilios/motorized-tilt-adapter`; never push to `develop` directly.

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

## Implementation tasks

### T0 — Materialize spec + plan into the repo — DONE (planning session)
`docs/motorized-tilt-adapter-eat-design.md` and this file already exist (written at plan
approval, uncommitted). The executor's first commit on `ghilios/motorized-tilt-adapter`
should include both.

### T1 — Domain types (pure; no deps)
**Create** `TiltAdapterDevices/TiltAdapterMove.cs`, `TiltAdapterDevices/TiltAdapterMovePlan.cs`.
`enum TiltMoveAxis { DiagonalA, DiagonalB, EdgeVertical, EdgeHorizontal, Backfocus }`,
`enum TiltMoveGroup { Tilt, Backfocus }`, immutable `TiltAdapterMove { Axis, Steps (signed int),
Group, Description, double[] PerCornerSteps }` (wizard indices 1..4 at [0..3]),
`TiltAdapterMovePlan { Moves, ResidualMicronsPerCorner, TwistResidualSteps, EstimatedSeconds }`.
Mirror immutability of `TiltAdapterDevicePreset.cs:25-46`.
**Tests first** (`Tests/TiltAdapterDevices/TiltAdapterMoveTests.cs`): per-axis `PerCornerSteps`
match the generator table exactly.

### T2 — TiltMovePlanner (core math) [after T1; parallel with T3/T4/T8/T12]
**Create** `TiltAdapterDevices/TiltMovePlanner.cs` (pure static; `ITiltMovePlanner` interface for
the future 3-corner impl). `Plan(double[] sPerScrew, bool includeTilt, bool includeBackfocus,
double unitMicrons, double perMoveSeconds=10)` + `internal static Decompose(s) → (a,b,f,t)`.
Implements the command-space group split, minimal-move rules, independent rounding, cap-splitting
hook, residuals, twist. **Tests first** (`TiltMovePlannerTests.cs`): decomposition recovery
(pure-tilt antisymmetric → a,b only; uniform → f only), single-edge merge for `ra==±rb` (e.g.
a=4.6,b=5.4 → one `tp,5`), general → 2 diagonals, ≤3 moves total, sub-step targets → no moves,
off-center-curvature leakage lands in tilt group (regression for the command-space split),
residual math, twist reported-not-planned, group toggles, property-style random-input check
(applied == rounded projection).

### T3 — EAT command formatting [after T1; parallel]
**Create** `TiltAdapterDevices/AsgEat/EatCommands.cs`. `enum EatSignEncoding { SignedArgument,
OppositeMnemonic }` + `const Default… = SignedArgument // LIVE-CAPTURE`. Axis→mnemonic:
DiagonalA→`tr`, DiagonalB→`tl`, EdgeVertical→`tp`/`bt`, EdgeHorizontal→`rt`/`lt`, Backfocus→`bf`.
Opposite pairs tr↔bl, tl↔br, tp↔bt, rt↔lt; **`bf` has no opposite mnemonic → negative bf MUST use
signed argument** (assert; this is the one blocking sign unknown). Formatter for `cp` (no `zr` — zeroing is deliberately unsupported).
**Tests first**: exact wire strings for the whole vocabulary under both encodings.

### T4 — Wizard-step ↔ device-move mapping [after T1; parallel]
**Create** `TiltAdapterDevices/AsgEat/EatWizardMapping.cs`.
`CornerLabelForWizardScrew(i)` → 1→TR, 2→TL, 3→BL, 4→BR. `MoveForStep(WizardStep, appliedSteps)`:
Screw1→DiagonalA(+N), Screw2→DiagonalB(+N), AllInward→Backfocus(+N), ReBaseline1→Backfocus(−N),
ReBaseline2→DiagonalA(−N), **Complete→DiagonalB(−N)** (the restore move), Baseline→null.
**Tests first**: each step's `PerCornerSteps` matches the manual instruction wording from
`StepInstructionsText` (TiltAdapterWizardVM.cs:639-691); inverses verified.

### T5 — ITiltMotionController + registry [after T1]
**Create** `Interfaces/ITiltMotionController.cs` (`Connected` INPC, `TiltDeviceCapabilities
{ Topology, SupportedAxes }`, `ConnectAsync/DisconnectAsync`, `ExecuteMoveAsync(move, progress, ct)`,
`QueryPositionsAsync` — **no zero-positions member**), `TiltAdapterDevices/TiltMotionControllerRegistry.cs`
keyed by `TiltAdapterDevicePreset.Name` (entries for both EAT presets; factory wired in T7).
**Tests first**: `IsMotorized` true for EAT presets / false otherwise; invariant guard
`AdjustmentType==StepperMotors ⇒ registry entry exists` over `TiltAdapterDevicePreset.All`.

### T6 — EAT transport + responses (THE SEAM FILES) [after T3, T5]
**Create** `AsgEat/EatSerialTransport.cs` (+ `IEatTransport`) and `AsgEat/EatResponses.cs`. ALL
response-format knowledge lives in these two files; every unknown marked `// LIVE-CAPTURE:`.
Transport: `SerialPortProvider.GetSerialPort(port, 9600, Parity.None, 8, StopBits.One,
Handshake.XOnXoff, …)`; own `SemaphoreSlim(1,1)`; `SendAsync(command, timeout, ct) →
EatRawExchange { Command, Lines, TimedOut }` — read lines until quiet-period or timeout; move
commands use ≥15 s timeout; **WriteTimeout > max move duration; write timeout = hard failure
(state-dirty)**; decide `DtrEnable` explicitly (CDC devices often need it; port-open may reset an
Arduino-class MCU and emit a boot banner — settle before first command); log every line
`Logger.Info("EAT TX: …" / "EAT RX: …")` (raw). Responses: `ParseMoveAck` (tolerant default:
success if !TimedOut), `ParseCpPositions` (TryParse-based; throws `InvalidDeviceResponseException`
with raw lines in the message). **Tests first** (NSubstitute `ISerialPort` fakes): port config
exact-args, line collection, timeout → TimedOut not throw, port-closed → throw, parser contracts.
Tests pin the seam CONTRACT, not the unknown wire format.

### T7 — EatTiltMotionController [after T4, T5, T6]
**Create** `AsgEat/EatTiltMotionController.cs`; wire registry factory. Ctor
`(IEatTransport, ITiltAdapterOptions)` + default ctor. Behavior:
- `ConnectAsync`: open → `cp` → cache per-motor positions; if unparseable (guaranteed pre-T15):
  positions unknown → excursion enforcement unavailable, surfaced as a UI warning flag.
- **Shadow position tracking**: persisted per-profile per-motor cumulative counters (options, T8),
  seeded from the first successful `cp` parse after connect; updated on every successful command;
  reconciled against each `cp` poll once the format is known. (No plugin-side zeroing — if the user
  zeroes in the vendor app, the next `cp` reconciliation picks it up.)
- `ExecuteMoveAsync`: validate |steps| ≤ MaxStepsPerCommand and predicted per-motor positions
  (wizard→device order via `EatWizardMapping`) ≤ MaxExcursion **including intermediate states**
  (order a multi-move plan to minimize peak excursion — with ≤3 orthogonal moves just evaluate all
  orderings); throw `TiltDeviceLimitException` BEFORE sending; format via `EatCommands`; send;
  update cached+shadow positions; settle `TiltDeviceSettleSeconds`; progress "Move 1 of 3: …".
- Policy when absolute position unknown: per-command cap always enforced; session-cumulative
  excursion from connect enforced as fallback; warning shown in the approval dialog.
- Capabilities: FourCornerCoupled; declares NO per-screw independent axis.
**Tests first** (NSubstitute `IEatTransport`): cp on connect, formatted sends, cap violation →
no send, excursion violation (incl. intermediate) → no send, position updates on success only,
cancellation semantics (between-commands; current move completes).

### T8 — New persisted options [independent; parallel from start]
**Modify** `TiltAdapterOptions.cs` + `Interfaces/ITiltAdapterOptions.cs` (mirror the
`ScrewInwardCurvatureSign` pattern: field + `InitializeOptions` load + persisting setter + INPC):
`TiltDeviceSerialPortName` (""), `TiltDeviceMaxStepsPerCommand` (200 — tune in T15),
`TiltDeviceMaxExcursionSteps` (500 — tune in T15), `TiltDeviceSettleSeconds` (3),
`DeviceLinkedCalibrationDeviceName` ("" = not linked; the CRITICAL gate),
`TiltDeviceShadowPositions` (string, serialized per-motor counters + validity flag), and
**`CalibrationAppliedAmount` (double, persisted; −1 = unset → resolve to the preset default)** —
this replaces the current session-only VM field (TiltAdapterWizardVM.cs:752-764). Also add a
**`DefaultCalibrationAmount` field to `TiltAdapterDevicePreset`**: 1.0 (turns) for Manual and all
screw presets, **150 (steps) for both EAT presets**; `ApplyDevice` (VM:1462) writes the preset
default into the option on preset change (still user-editable afterward via the existing
applied-amount input, which re-binds to the persisted option — the option's required UI control).
**Tests first**: round-trips, defaults, INPC — mirror existing `TiltAdapterOptionsTests`; preset
defaults (`EatPresets_DefaultCalibrationAmount_Is150`, screw presets 1.0); `ApplyDevice` writes
the default. UI controls land in T10 (mandatory, project invariant).

### T9 — Shared connection service singleton [after T5, T8]
**Create** `TiltAdapterDevices/TiltDeviceConnectionService.cs` (`BaseINPC`): `Controller`,
`Connected`, `ConnectAsync(presetName, port, ct)` via registry, `DisconnectAsync`,
**exclusive-operation token** `TryBeginOperation(name) → IDisposable` + `IsOperationActive`
(wizard calibration and inspector plan execution are mutually exclusive; a per-command semaphore
is not enough), **force-disconnect on `IProfileService.ProfileChanged`** (waits for the in-flight
command). Also owns:
- **Position polling**: while connected, poll `cp` on a timer (constant, ~5 s) and expose
  `int[] CurrentPositions` (+ `PositionsKnown`) via INPC for the UI; **pause polling while an
  operation token is held** (moves/calibration) and resume after; pre-T15 (cp unparseable) surface
  `PositionsKnown=false` so the UI shows "unknown". Polling does NOT count as activity.
- **Idle tracking**: record last *user-initiated* activity (connect, any executed move/plan/
  calibration step — polling excluded). After **30 min idle**, raise an `IdlePromptRequested`
  event; the wizard VM (T10) shows a modal "Device idle for 30 minutes — disconnect?" via its
  `windowServiceFactory` (marshaled through `applicationDispatcher`). Yes → `DisconnectAsync`;
  No → reset the idle timer. Suppress re-prompting while a prompt is open.
Wire as `HocusFocusPlugin.TiltDeviceConnectionService` static (mirror HocusFocusPlugin.cs:109-110,
:252). **Tests first**: connect/disconnect state + INPC, connect-while-connected, operation-token
exclusivity, profile-change disconnect, polling pauses during operations and resumes, poll updates
`CurrentPositions`, idle timer fires `IdlePromptRequested` after 30 min without user activity (use
an injectable clock/timer for tests), polling does not reset the idle timer, "No" resets it.

### T10 — Wizard pane connection UI + option controls [after T8, T9]
**Modify** `TiltAdapterWizardVM.cs`: `IsMotorizedDevice` (registry lookup on DeviceName; raise
from `ApplyDevice` like `IsManualDevice` at :984), `AvailablePortNames`
(`SerialPortProvider.GetPortNames()`), `RefreshPortsCommand`, `ConnectDeviceCommand` /
`DisconnectDeviceCommand` (AsyncRelayCommand; canExecute motorized + port selected), status text,
service injected via dual-ctor. Subscribe to the service's `IdlePromptRequested` and show the
modal disconnect prompt (windowServiceFactory, ReplaySettingsPrompt pattern; result routed back
to the service). **No zero-positions command anywhere** (vendor app's job).
**Modify** `TiltAdapterWizard/DataTemplates.xaml`: "Motorized Device Connection" GroupBox in Panel
A after the preset ComboBox (:288-315), Visibility on `IsMotorizedDevice`: COM-port ComboBox +
refresh + Connect/Disconnect + status + a **live per-screw positions display** (4 corners labeled
TR/TL/BR/BL with wizard screw numbers, bound to the service's polled `CurrentPositions`; shows
"unknown" when `PositionsKnown=false`); numeric controls for the T8 limit/settle options (reuse
existing `ValidationRules/`). **Tests first**: VM-level gating, command canExecute, connect
persists port, idle prompt Yes → service disconnect / No → timer reset. Verify: suite + full
solution build.

### T11 — Hands-off wizard calibration [after T4, T7, T9, T10]
**Modify** `TiltAdapterWizardVM.cs` (+ Panel B XAML :768+):
- `internal async Task<bool> ExecuteDeviceMoveForCurrentStepAsync(ct)`: no-op for
  Baseline; otherwise `EatWizardMapping.MoveForStep` → `Controller.ExecuteMoveAsync`; on
  failure/cancel execute the inverse move (recovery table = T4 inverses; wire into the existing
  failure-retry path at :1185-1187) and surface the error.
- In the measurement flow (`RunMeasurementAsync` :1136 / `MeasureStep` :1173): when connected +
  motorized, execute the device move BEFORE the inspector measurement; hold the T9 operation token
  for the whole run.
- `AutoRunAllCommand`: loop move→measure→`NextStep` to Complete (including Complete's restore
  move `tl,−N`), honoring progress + cancellation (cancel finishes the current move, then recovers
  toward baseline).
- **When connected: default `MeasureCurvatureDuringCalibration` ON** (6-step flow measures σ via
  the real `bf` command — the exact command automation later replays). If the user disables it,
  backfocus moves later carry an "(assumed direction)" warning.
- **Applied amount**: use the persisted `CalibrationAppliedAmount` setting (T8; EAT preset default
  150 steps = 270 µm — the old 1.0 default would be 1.8 µm, noise-dominated against
  `MinReliableSignalToNoise=2.0`); validate against `TiltDeviceMaxStepsPerCommand` and soft limits
  before `StartAsync` proceeds.
- On successful completion of a connected run: write `DeviceLinkedCalibrationDeviceName`; clear it
  in `ApplyManualCalibration`, replay-driven `RunCalibrationMath`, and disconnected runs.
- Instruction text: automated status ("Applying tr,+50 …") from `Move.Description` when connected;
  manual text otherwise.
**Tests first** (NSubstitute controller via service test ctor): per-step commands, baseline no-op,
failure → inverse recovery, AutoRunAll walks 4-step and 6-step sequences incl. Complete restore,
cancel → recovery, disconnected regression (behaves exactly as before), device-linked marker
set/cleared correctly, applied-amount defaulting.

### T12 — Shared per-screw target computation [independent; parallel from start]
**Create** `TiltAdapterWizard/TiltScrewTargets.cs` (pure static, beside `TiltScrewGeometry`):
`ComputePerScrewTargets(gx, gy, kx, ky, x0, y0, angles[], radiusMicrons, unitMicrons, σ) →
double[] sPerScrew` (and tilt/backfocus micron components for display). **Modify**
`InspectorVM.FillNumericGuidance` (:2006) to format its display strings FROM this helper
(behavior unchanged); the planner (T14) consumes the same helper directly from
`SensorModel.DisplayedSensorModel` — single source of truth, no widening of the display VM.
**Tests first**: helper outputs formatted with `TiltAdapterGuidanceVM.FormatAmount` equal the
previously displayed strings (regression lock); σ applies to backfocus only; positive = wizard "+".

### T13 — Approval dialog [after T1; parallel with T6–T12]
**Create** `TiltAdapterDevices/Prompt/` — 4-file `ReplaySettingsPrompt` pattern
(`ReplaySettingsPrompt.cs:29-57`): `TiltDeviceAdjustmentPromptVM` (+Control.xaml, +static
`ShowAsync`, +`TiltDeviceAdjustmentChoice { Proceed, ApplyTilt, ApplyBackfocus, FinalPlan }`).
Contents: move list (wire command + human description + group), **Tilt / Backfocus checkboxes**
(re-plan on toggle via injected replanner `Func<bool,bool,TiltAdapterMovePlan>`), per-corner
residual µm, twist warning when |t| ≥ 1 step, "(assumed direction)" warning on backfocus when
`!ScrewInwardCurvatureSignIsMeasured`, pitch-mismatch warning (reuse the
`PitchMismatchExceeds` text from InspectorVM.cs:2065-2071), limit warnings / unknown-position
warning, estimated duration; Proceed disabled when both groups off or a hard limit is violated.
**Tests first**: toggle → replan, both-off → disabled, limit warning → disabled, choice carries plan.

### T14 — Inspector "Automatic Adjustment" end-to-end [after T2, T9, T12, T13]
**Modify** `AutoFocus/InspectorVM.cs`: `AutomaticAdjustmentCommand` (AsyncRelayCommand; canExecute:
service `Connected` **&& `DeviceLinkedCalibrationDeviceName` matches the connected preset** &&
numeric guidance available && `!IsWizardRunning` via operation token && calibration quality OK
**&& the current sensor model is newer than the last executed adjustment** — a
measurement-generation counter incremented on each completed inspector analysis and stamped onto
any executed plan enforces the one-adjustment-per-measurement invariant: after a plan executes,
the command stays disabled ("Run the Aberration Inspector to confirm before adjusting again")
until a fresh sensor-model run completes; the same measurement can never drive two plans;
re-raise on service INPC). Flow: `TiltScrewTargets` from `DisplayedSensorModel` →
`TiltMovePlanner.Plan` → `TiltDeviceAdjustmentPrompt.ShowAsync` → hold operation token → execute
`FinalPlan.Moves` sequentially with progress (`applicationStatusMediator`) and an **applied-move
journal** → on failure/cancel: stop, offer one-click revert (inverse mnemonics, reverse order; if
revert fails → mark positions dirty, force resync on next connect) → completion prompt "Re-run
Aberration Inspector to confirm?" → yes → `await AnalyzeAutoFocus(token, true)`. After the re-run,
compare before/after tilt magnitude and **warn loudly + offer revert if tilt worsened** (catches
stale calibration / rotated camera). Extract `internal static` helpers for plan building.
**Modify** `AutoFocus/DataTemplates.xaml`: button in the "Tilt Adapter Guidance" Expander (:2809+)
near the numeric grid (:2967+); Visibility on service Connected, IsEnabled via canExecute; when
connected but not device-linked, show the remediation hint instead.
**Tests first**: targets-from-model (not strings), canExecute gates (disconnected / not linked /
wizard running), dialog cancel → no moves, approved moves in order, mid-plan failure → journal +
revert offer, re-run invoked on accept, **stale-measurement lockout** (after execution canExecute
is false until a new analysis completes; a second execution off the same measurement is
impossible; dialog-cancel does NOT consume the measurement).

### T15 — Live protocol capture session (hardware; LAST; touches ONLY seam files)
Resolve every `// LIVE-CAPTURE:` marker against the real EAT (transcript logging from T6 gives the
raw data). **Files:** `EatResponses.cs`, `EatSerialTransport.cs` (+ possibly flip
`EatCommands.DefaultSignEncoding`, + sign constants in `EatWizardMapping` if the device `+` is
inverted — convention constants, not protocol code). Checklist:
1. Framing: line terminator (\r vs \r\n vs \n), lines per command, boot banner on port open,
   DTR requirement, reset-on-open behavior.
2. Ack: format, error strings, and WHEN it arrives (command receipt vs move completion) → pick
   completion strategy: ack-is-completion / poll `cp` (does the device answer `cp` mid-move or
   does XOFF block it?) / fixed settle fallback.
3. Negative values: is `tr,-10` accepted? (Only truly blocking for `bf` — no opposite mnemonic.)
4. `cp` format: motor order (TR/TL/BR/BL?), absolute vs relative, units; reconcile shadow
   counters; confirm the ~5 s polling cadence is safe (no firmware confusion, no XOFF stalls) and
   that positions reflect vendor-app zeroing done outside the plugin.
5. Semantics: `tr,10` relative move vs absolute target; EEPROM position after power-loss mid-move
   (kill power mid-move; does cp reflect pre- or post-move?).
6. Directions: per-mnemonic cross-check with `cp` before/after `tr/tl/tp/rt/bf` — verify `bf,+N`
   moves TR the same physical direction as `tr,+N`; verify device `+` matches the wizard "+"
   convention (one manual measurement); store per-mnemonic sign factors in the driver if needed.
7. Travel limits → tune `MaxStepsPerCommand` / `MaxExcursionSteps` / `SettleSeconds` defaults (T8).
8. Update `EatResponsesTests` fixtures with REAL captured lines; suite green.
Then: full hands-off wizard calibration + Automatic Adjustment + re-run confirm on hardware.

## Verification

1. After every task: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` green.
2. Full build (`dotnet build … -c Debug`) — the csproj PostBuild deploys the plugin into NINA's
   plugin folder automatically (including on test runs).
3. Pre-hardware smoke (NINA, no device): EAT preset selected → connection section appears; Manual
   preset → hidden; disconnected wizard + inspector behave exactly as before (regression).
4. Hardware (T15): capture transcript → parser fixtures updated → hands-off 6-step calibration →
   inspector run → Automatic Adjustment (verify move list matches guidance table signs, residuals
   sane) → approve → re-run → tilt/backfocus reduced; confirm the button stays disabled after
   execution until the confirming re-run completes (one adjustment per measurement); test
   cancel-mid-plan revert; test soft-limit refusal; test port-unplug mid-session
   (SerialPortClosedException → clean disconnect + notify).

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
