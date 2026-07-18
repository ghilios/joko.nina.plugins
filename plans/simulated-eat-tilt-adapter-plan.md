# Simulated ASG EAT Tilt Adapter — Connect & Calibrate Without Hardware — Plan

> Per project convention (CLAUDE.md), before execution this plan should be saved as
> `plans/simulated-eat-tilt-adapter-plan.md`. Design rationale (convergence proof, seam choice) can be
> distilled into `docs/simulated-eat-tilt-adapter-design.md` if a separate spec is wanted.

## Context

The motorized tilt-adapter (ASG EAT) automation (PR #140, branch `ghilios/motorized-tilt-adapter`, tasks
T0–T14 done) closes the loop: connect to the EAT over serial, run a hands-off calibration, compute a minimal
move plan from the fitted sensor model, and apply it — but the whole feature is currently **blocked on T15
live-hardware serial capture** and cannot be exercised end-to-end without the physical device.

Separately, the plugin already ships a **camera simulator** (`HocusFocusSimulatorCamera`) and a **virtual tilt
adapter** (`CameraSimulator/TiltAdapter/SimulatedTiltAdapter`) whose whole purpose is to close the calibration
loop in software: inject a tilt → the inspector says how far to turn each screw → apply it → the injected tilt
converges to flat. Today that virtual adapter is driven only by manual button clicks on a dockable panel.

**This change makes the virtual tilt adapter connectable as if it were a real EAT over serial**, so a user can
run the *entire* automated calibration + inspector "Automatic Adjustment" loop against the camera simulator with
**no hardware connected**. Concretely: a `"Simulator"` entry appears in the Tilt Adapter Wizard's COM-port
dropdown; selecting it and connecting routes the (unchanged) EAT motion controller onto a simulated transport
that drives the virtual adapter. On connect, if the simulator's tilt config doesn't match the selected EAT
preset, the user is asked whether to auto-fix it; if they decline, connect aborts.

This also delivers a durable pre-hardware **testbed** for the T15 automation logic (planner, limits, shadow
tracking, one-adjustment-per-measurement gate) — all of which the transport-level seam exercises unchanged.

## Approach (validated by two independent design reviews)

Insert the simulator at the **transport seam** (`IEatTransport`), keeping `EatTiltMotionController`,
`EatCommands`, `EatResponses`, `TiltMovePlanner`, `EatWizardMapping`, and the wizard/inspector automation
**completely unchanged**. When the wizard connects with the `"Simulator"` port sentinel, the connection service
builds `new EatTiltMotionController(new SimulatedEatTransport(actuator), options)` instead of the serial-backed
controller. The simulated transport answers `cp` with per-motor counters and turns each move command into a
change in the camera simulator's injected aberration, so the next simulated exposure reflects it.

### Why the closed loop converges (the correctness core)

The wizard's **device-linked calibration measures** the sim's response: `RunCalibrationMath` overwrites the
stored screw angles (`ComputeScrewAngles`) and — on the 6-step run — the curvature sign (`ComputeCurvatureSign`)
with measured values, while the **hardware fields stay locked to the preset** (`ApplyDevice` sets
`ScrewCount/AdjustmentType/StepperStepSizeMicrons/ScrewRadiusMillimeters`). Guidance then consumes preset-locked
hardware + measured angles/sign. Because `SimulatedTiltAdapter.ApplyMoves` is the exact inverse of
`TiltScrewGeometry.TiltCorrectionMicrons` under one shared plane model, the loop is the identity **iff the four
hardware fields match a priori** — angles and sign self-correct via calibration. Load-bearing details:

- **Radius must match** (it is not merely conservative): the backfocus target scales as R² while the sim's
  backfocus response scales as 1/R²; a step-size adjustment cannot absorb both the R¹ tilt term and the R²
  backfocus term. So a radius mismatch diverges backfocus.
- **The 6-step curvature run must actually execute** for the sign to self-correct; otherwise backfocus can
  diverge. The connect flow already defaults `MeasureCurvatureDuringCalibration = true` on connect
  (`TiltAdapterWizardVM.cs:1302-1315`) — keep it.
- **Device-linked gate ordering:** Automatic Adjustment is blocked until a *connected* calibration completes
  (`DeviceLinkedCalibrationDeviceName`), which is exactly what forces the angle/sign measurement to happen first.
  Since the simulated controller is a real `EatTiltMotionController` under the EAT preset, a completed simulated
  hands-off calibration sets this marker and unlocks Automatic Adjustment — no special-casing needed.
- **Index mapping:** feed each move's **wizard-order** `PerCornerSteps` (× step size) directly to
  `ApplyMoves` (sim screw `i` ≡ wizard screw `i+1`). Do **not** apply `PermuteWizardToDeviceMotorOrder` on the
  optics path — that would turn a pure tilt into a twist and break recovery. Track the `cp`/shadow counters
  **separately in device order** (TR,TL,BR,BL) via `PermuteWizardToDeviceMotorOrder`, matching real hardware.
- **Do NOT sync angles/sign** in the config-match fix — only the four hardware fields. Syncing angles/sign would
  be immediately discarded by calibration and would remove the exact coverage the simulator exists to provide.

### Operational preconditions (per user decisions)

- **Block connect until the HocusFocus camera simulator is the active camera.** The calibration exposures only
  respond to the virtual adapter when `HocusFocusSimulatorCamera` is the connected camera. Gate on
  `cameraMediator.GetInfo().Connected && DeviceId == "HocusFocus_SimulatorCamera"`.
- **Auto-enable aberration rendering + notify.** `ICameraSimulatorOptions.EnableAberrations` defaults **off** and
  gates rendering (`HocusFocusSimulatorCamera` renders a flat field when off), making the loop inert. On connect
  to the simulator, if it's off, enable it and show a status notification.

## Components

### New files

| File | Responsibility |
|---|---|
| `TiltAdapterDevices/SimulatedTiltPort.cs` | Sentinel constant: `const string PortName = "Simulator"` + `IsSimulator(string)`. Shared by the connection service (routing), wizard VM (dropdown + gate), and plugin (factory). |
| `CameraSimulator/TiltAdapter/ISimulatedTiltActuator.cs` | Seam the transport drives: `void ApplyWizardScrewSteps(double[] wizardOrderSteps)` (optics) and `int[] GetPerMotorPositions()` (device-order counters for `cp`). Unit-testable. |
| `CameraSimulator/TiltAdapter/SimulatedTiltActuator.cs` | Impl over `ICameraSimulatorOptions` + `IApplicationDispatcher`. Builds a `SimulatedTiltAdapter` from `Sim*` options (mirror `RebuildAdapter`), feeds **wizard-order** steps to `ApplyMoves`, folds the result via `SimulatedTiltInjection.Fold` (marshalled onto the dispatcher so the shared `CameraSimulatorOptions` writes land on the UI thread and the dockable updates live), and accumulates **device-order** `cp` counters via `PermuteWizardToDeviceMotorOrder`. Catches the collinear-geometry guard like the VM does. |
| `CameraSimulator/TiltAdapter/SimulatedTiltInjection.cs` | Behavior-preserving extraction of the sign-critical fold from `SimulatedTiltAdapterVM.ApplyDelta` (`:705-746`): `static bool Fold(ICameraSimulatorOptions, AberrationDelta, int pistonDirectionSign)`. Shared by the VM (delegates to it) and the actuator so the two halves of the loop cannot drift. |
| `CameraSimulator/TiltAdapter/SimulatedEatTransport.cs` | `IEatTransport` impl. `SendAsync("cp")` → canonical `"p0,p1,p2,p3"` device-order line (parses via `EatResponses.ParseCpPositions`). `SendAsync(moveWire)` → `EatCommands.TryParse` → `TiltAdapterMove.PerCornerSteps` (wizard order) → `actuator.ApplyWizardScrewSteps(steps × unit)` → ack (`TimedOut=false` → `ParseMoveAck` true). Ctor takes `ISimulatedTiltActuator` (test seam). Unparseable command → throw (only `cp`/`Format` output is ever sent). |

### Edits to existing files

- **`TiltAdapterDevices/AsgEat/EatCommands.cs`** — add `public static bool TryParse(string wire, out TiltMoveAxis axis, out int steps, EatSignEncoding encoding = DefaultSignEncoding)`, the exact inverse of `Format`, using the same mnemonic tables. `bf` and `SignedArgument` decode the signed value; `OppositeMnemonic` decodes negatives from the opposite mnemonic. Round-trip-tested against `Format`.
- **`TiltAdapterDevices/TiltDeviceConnectionService.cs`** — additive nullable `Func<ITiltMotionController> simulatedControllerFactory` on the public ctor (`:142`, only the plugin calls it) and the internal ctor (`:147`, all four test sites pass 4 positional args → unaffected). In `ConnectAsync` (`:215`), route: `SimulatedTiltPort.IsSimulator(portName) && simulatedControllerFactory != null ? simulatedControllerFactory() : controllerFactory(presetName, options)`. `controller.ConnectAsync(portName)` is unchanged (the sim transport ignores the port string). **Registry stays pure.**
- **`HocusFocusPlugin.cs`** (`:125-127`) — pass the sim factory when constructing `TiltDeviceConnectionService`. A lazily-created shared `SimulatedTiltActuator(CameraSimulatorOptions, ApplicationDispatcher)` (captured in the closure) keeps counters + injected aberration coherent across reconnects; the closure runs only at Connect time, by when both singletons exist.
- **`TiltAdapterWizard/TiltAdapterWizardVM.cs`**
  - Inject `ICameraSimulatorOptions` (via `HocusFocusPlugin.CameraSimulatorOptions` in the `[ImportingConstructor]`; optional test-seam param in the full ctor, mirroring `confirmIdleDisconnectAsync`). Add an injectable `Func<string,string,Task<bool>> confirmSimConfigChangeAsync` seam defaulting to a `MyMessageBox.Show(..., YesNo, No)` wrapper.
  - `EnumeratePortNames()` (`:1271`) — prepend `SimulatedTiltPort.PortName`. The sentinel is non-empty (survives the `SelectedPortName` null/empty guard) and enables `ConnectDeviceCommand`. The whole GroupBox is gated on `IsMotorizedDevice`, so it only appears under an EAT preset — correct scoping for free.
  - `ConnectTiltDeviceAsync()` (`:1292`) — before `svc.ConnectAsync`, when `SimulatedTiltPort.IsSimulator(port)`:
    1. **Block-until-active gate:** if `cameraMediator.GetInfo()` is not Connected or `DeviceId != "HocusFocus_SimulatorCamera"`, `Notification.ShowError(...)` and return (do not connect).
    2. **Config-match:** compare `Sim{ScrewCount,AdjustmentType,StepperStepSizeMicrons,ScrewRadiusMillimeters}` vs `TiltAdapterDevicePreset.ByName(DeviceName)`. If differ → `confirmSimConfigChangeAsync(...)`; **No → return** (don't connect); **Yes →** write the four preset fields into the `Sim*` setters (mirror `ApplyDevice` `:2231-2235`).
    3. **Auto-enable aberrations:** if `!cameraSimulatorOptions.EnableAberrations`, set it true and `Notification.ShowInformation("Enabled simulator aberrations for calibration.")`.
- **`CameraSimulator/TiltAdapter/SimulatedTiltAdapterVM.cs`** — `ApplyDelta` (`:705`) becomes a one-line delegate to `SimulatedTiltInjection.Fold`. Behavior-preserving; the pinned sign tests keep passing.
- **`CameraSimulator/HocusFocusSimulatorCamera.cs`** — add `public const string DeviceId = "HocusFocus_SimulatorCamera";` and make `Id => DeviceId`, so the wizard gate references the constant, not a magic string.

## Task sequence (test-first; run the suite after each)

1. **T1 — `EatCommands.TryParse`** + `EatCommandsTests`: round-trips `Format` for the whole vocabulary under both sign encodings; `"cp"` and malformed strings return false; negative `bf` under both encodings.
2. **T2 — Extract `SimulatedTiltInjection.Fold`** from `ApplyDelta`; VM delegates. Existing `SimulatedTiltAdapterVMTests` sign/clamp tests are the regression lock (must stay green unchanged).
3. **T3 — `ISimulatedTiltActuator` + `SimulatedTiltActuator`** + tests: counters start at 0 and advance in device order; wizard-order feed to `ApplyMoves`; diagonal pair → tilt-only, backfocus (all four) → backfocus-only; apply-then-inverse returns to baseline; fold marshalled through a recording dispatcher; collinear geometry still tracks counters and acks.
4. **T4 — `SimulatedEatTransport`** + tests (substitute `ISimulatedTiltActuator`): `cp` line parses via `EatResponses.ParseCpPositions`; `"tr,5"`/`"tl,5"`/`"bf,150"` reach the actuator with correct wizard-order effects; move acks; unparseable throws. Plus an **integration test** through the real unchanged `EatTiltMotionController` (connect seeds shadow from `cp`; a move advances counters + folds aberration; `QueryPositionsAsync` returns them).
5. **T5 — `SimulatedTiltPort` + connection-service routing** + tests: sentinel port uses the sim factory (registry not invoked); non-sentinel uses the registry; null sim factory is inert (existing tests unaffected).
6. **T6 — Plugin wiring** (`HocusFocusPlugin`): construct the service with the sim factory; add the camera `DeviceId` const.
7. **T7 — Wizard VM** + tests: dropdown contains the sentinel first; sentinel enables Connect; block-until-active refuses when the HF sim camera isn't active; config-match Yes writes the four `Sim*` fields then connects; No does not connect and writes nothing; matching config connects without prompt; aberrations auto-enabled on connect; a real COM port never prompts/gates. Update the one existing test that asserts exact `AvailablePortNames` contents (`AvailablePortNames_EnumeratesLazily...`) to include the sentinel.
8. **T8 — End-to-end verification** (below).

## Verification

1. **Unit suite green after every task:** `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` (Windows `dotnet.exe` via WSL interop; timeout 600000). Never skip/ignore failing tests.
2. **Full build** deploys the plugin into NINA's plugin folder (PostBuild xcopy).
3. **Manual end-to-end in NINA, no hardware:**
   - Connect the **HocusFocus camera simulator** + a simulator focuser. Inject a tilt via the Simulator Tilt Adapter panel.
   - In the Tilt Adapter Wizard, select an **EAT preset** (e.g. "ASG Electronic EAT - 90mm") → the connection section appears with **"Simulator"** in the port dropdown.
   - Connect with a real COM port first → **no** config prompt/gate (regression). Switch to "Simulator": if the HF sim camera isn't active, connect is **refused**; with it active and a mismatched sim config, the **Yes/No popup** appears; **No** → not connected; **Yes** → the four sim fields update and it connects; aberrations auto-enable with a notification.
   - Run the **hands-off 6-step calibration** through the simulated EAT → completes and sets the device-linked marker; the live per-corner positions update from `cp`.
   - Run the **Aberration Inspector**, then **Automatic Adjustment** → approve the plan → moves apply to the sim → re-run confirms the injected tilt/backfocus **reduced**. Confirms the full loop with no hardware.
   - Regression: Manual/screw presets show no "Simulator" entry and behave exactly as before.

## Risks

- **Sign/convergence** is the highest-consequence area; mitigated by extracting (not duplicating) the fold, the wizard-order-feed invariant, the radius-must-match gate, and defaulting the 6-step curvature run on connect. The integration test through the real controller is the backstop.
- **Sim config drift after the config-fix** (user edits `Sim*` between connect and calibrate) — out of scope; the coherence badge already flags mismatches, and a fresh connect re-checks.
- **`AvailablePortNames` test** is the only existing test that must change; all ctor changes are additive/optional so other test sites stay green.
- This feature does not touch the T15 seam files and does not depend on the real wire format (the sim defines a clean, self-consistent `cp`/ack format the tolerant parsers already accept).
