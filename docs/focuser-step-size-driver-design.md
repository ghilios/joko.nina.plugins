# Focuser step size from the driver, with the stored value as an override

**Status:** design, user-approved 2026-08-04. Ready to execute.

`IInspectorOptions.MicronsPerFocuserStep` is the µm of focuser travel per step — the scale factor that turns
every focuser-space measurement into microns. Today it has exactly one source: a number the user types.
Almost every focuser driver already reports it (ASCOM `Focuser.StepSize`, surfaced by NINA as
`FocuserInfo.StepSize`), and HocusFocus ignores it.

This design makes the driver the default source and demotes the stored value to an **override**, with a UI
flag when the two disagree.

## 1. Why this matters more than a convenience default

The setting is not cosmetic. It scales:

- the sensor-model fit's curvature effect and tilt effect, in microns;
- the per-screw axial corrections, and therefore the screw turns / stepper steps the guidance prints;
- `BackfocusMicronDelta` and the critical-focus-zone comparison;
- the camera simulator's rendered defocus.

Left unset it degrades gracefully — micron readouts go NaN and the affected rows drop out — so the current
failure mode is *missing* information, not wrong information. That is the property to preserve: nothing in
this design may turn "unset" into a silently wrong number.

The reason to reach for the driver at all is that the value is genuinely knowable. A user who never opens the
options page gets correct microns instead of no microns.

The reason to keep the override is that the driver is frequently wrong. ASCOM `StepSize` is optional; drivers
that do not implement it report `0` (handled — see §2), but a real minority report a *plausible* wrong number
— commonly `1`, meaning "one step per step" rather than one micron per step. That class of error is invisible
without a second opinion, which is what §4's flag exists to provide.

## 2. The resolver

Four layers, highest priority first:

| # | Source | Condition | Persisted |
|---|---|---|---|
| 1 | `InspectorVM.SensorModelFocuserSizeOverrideMicrons` | set (replaying a saved run at its captured step size) | no — exists today, unchanged |
| 2 | `IInspectorOptions.MicronsPerFocuserStep` | `> 0` | yes, per profile |
| 3 | `IInspectorOptions.DriverMicronsPerFocuserStep` | finite and `> 0` | **no** |
| 4 | unset (`-1`) | — | — |

Layer 3 is new. Its acceptance test is exactly "finite and `> 0`", which rejects the three ways a driver
declines to answer: `0`, a negative sentinel, and `NaN`. Note `> 0` rather than `!(<= 0)`: `NaN` fails both
comparisons, and only the positive form rejects it — the same trap already documented on
`CameraSimulatorOptions.EffectiveFocuserStepSizeMicrons`.

Three new members sit beside `MicronsPerFocuserStep` on `IInspectorOptions`:

- **`DriverMicronsPerFocuserStep`** — in-memory only, never written to the options accessor.
- **`EffectiveMicronsPerFocuserStep`** — the resolver over layers 2–4. Read-only. This is what every consumer
  that wants "the number to compute with" reads.
- **`HasFocuserStepSizeMismatch`** — §4.

### Why on `IInspectorOptions` and not a new service

A dedicated `IFocuserStepSizeProvider` is the tidier abstraction in isolation, but it would have to be
threaded through `InspectorOptions`, `CameraSimulatorOptions`, `SensorModel`, `TiltModel`,
`TiltAdapterWizardVM` and both TestApp runners to reach the places that already hold an `IInspectorOptions`.
The step size is *already* modelled as one shared variable living there — `CameraSimulatorOptions` takes an
`IInspectorOptions` for the sole purpose of reaching it — so the resolver belongs next to the value it
resolves.

The cost is that `IInspectorOptions` gains one member that is not an option. The doc comment carries that
contract explicitly, and a test asserts it never reaches the accessor.

### Stickiness (decided: sticky last-known)

`DriverMicronsPerFocuserStep` is **only ever written with a valid value**. A disconnect reports `StepSize = 0`,
which fails the acceptance test and is therefore not written, so the last-known value stands. This is the
whole mechanism — there is no separate "remember" step to get wrong.

The consequence is deliberate: disconnecting a focuser mid-session does not rescale an in-flight analysis, and
reconnecting the same focuser is a no-op. The accepted cost is that swapping to a focuser that reports nothing
leaves the previous focuser's value in place for the rest of the session.

It is cleared on **profile change** — a profile swap is the codebase's existing signal for "different rig", and
`InitializeOptions` already runs there.

## 3. Consumers

Switch to `EffectiveMicronsPerFocuserStep` (these want the number to compute with):

- `InspectorVM` — the sensor-model fit's `focuserSizeMicrons`, and `BackfocusMicronDelta`.
- `TiltModel.RebuildAdjustments` — the per-corner adjustment microns.
- `CameraSimulatorOptions.EffectiveFocuserStepSizeMicrons` — so the simulator renders at the same `k` the
  inspector recovers with. Two independent `k`s do not drift harmlessly; they break the inject⇄recover loop by
  exactly their ratio, silently. Its `2.0 µm` default moves to the end of the chain, after the driver.
- `TiltAdapterWizardVM` — the captured `TiltMeasurementContext.MicronsPerFocuserStep` and the drift comparison
  against it. A run captured under a driver-supplied step size must record what it actually used.
- Both TestApp runners — no focuser is connected there, so `Effective` reduces to the raw value and the change
  is a no-op that keeps the codebase on one accessor.

Stay **raw** (these *are* the override, not the resolved value):

- `InspectorOptions`' own load/store/reset.
- `CameraSimulatorOptions.FocuserStepSizeMicrons` get/set — the editable pass-through.
- `CameraSimulatorOptions`' legacy migration, which asks "is the *stored* value already calibrated?" and must
  not be answered by a driver.
- `TiltAdapterWizardVM.MicronsPerFocuserStepValue` — the wizard's editable box.
- The two-way XAML bindings.

## 4. The mismatch flag

`HasFocuserStepSizeMismatch` is true when **all** of:

- the override is set (`MicronsPerFocuserStep > 0`), and
- the driver value is valid (finite, `> 0`), and
- they differ by more than **1% relative** to the driver value.

The tolerance exists so that a user whose measured calibration lands at 1.02 against a driver-reported 1.00
does not carry a permanent badge, while the failure this check exists to catch — a driver reporting steps
rather than microns, or a 2× error — flags loudly. Relative rather than absolute, because step sizes across
real rigs span roughly 0.1–10 µm.

It is **advisory only**. It changes no value, blocks nothing, and the override continues to win. A user who
has deliberately measured their own step size and trusts it over the driver's claim is exactly right to, and
the flag must read as information rather than as an error.

Surfaced in both places the setting is editable — the Aberration Inspector dock's row and the plugin's Camera
Simulator options tab — as inline text naming both numbers.

## 5. Hint text

Both boxes are already `HintTextBox`es showing a greyed fallback when empty. Their hint changes from
`(disabled)` / the simulator's hardcoded default to the **effective** value, so an empty box shows what will
actually be used. This is what makes the driver fallback discoverable at all: without it, a user with a
driver-supplied step size sees an empty box and concludes the feature is off.

## 6. What this design does not do

- **No persistence of the driver value.** It is device state, re-read on every connect. Persisting it would
  create a second copy that goes stale against a swapped focuser — the exact failure the simulator's
  `FocuserStepSizeMicrons` was collapsed into `MicronsPerFocuserStep` to eliminate.
- **No auto-adoption into the override.** The driver value is never written into the persisted setting, so
  "clear the box" always means "go back to the driver" and is never a lossy operation.
- **No prompt or modal on mismatch.** §4 is a label.

## 7. Testing

- Resolver precedence across all four layers, including the per-run override still winning.
- Stickiness: a valid driver value survives a `StepSize = 0` disconnect; a profile change clears it.
- Rejection: `0`, negative, and `NaN` driver values are never adopted — with `NaN` called out, since it is the
  one that slips through a `<= 0` guard.
- The driver value never reaches the options accessor.
- Flag matrix: quiet at 1.00 vs 1.005; fires at 1.00 vs 2.00; never fires with no override; never fires with
  no valid driver value.
- `InspectorVM.UpdateDeviceInfo` writes a valid `FocuserInfo.StepSize` through and ignores an invalid one.
- The simulator's inject⇄recover loop still closes when `k` comes from the driver rather than the override.
