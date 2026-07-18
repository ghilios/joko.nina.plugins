# Motorized Tilt Adapter

For most tilt adapters, the correction loop ends with you and a hex key: the [Tilt & Aberration
Inspector](tilt-aberration-inspector.md) measures the tilt, and the guidance table says which screw to
turn by hand. A motorized adapter closes that loop in software. Select one of the **ASG Electronic
EAT** presets in the [Tilt Adapter Wizard](tilt-adapter-wizard.md), connect the adapter over its
serial port, and the wizard runs its calibration hands-off, sending the motor moves itself. Once a
connected calibration completes, the inspector gains an **Automatic Adjustment** button that turns a
measurement into motor commands, shows them to you for approval, and executes them. A built-in
**Simulator** port drives the [Camera Simulator's](camera-simulator.md) virtual adapter the same way,
so the entire workflow can be tried with no hardware attached.

## Motorized device presets

The **Device** list in the wizard includes two motorized presets, **ASG Electronic EAT - 90mm** and
**ASG Electronic EAT - ZWO 461**. Selecting either one makes a **Motorized Device Connection**
section appear below the preset. For the Manual entry and every screw preset the section stays
hidden, and the wizard behaves exactly as described on the [Tilt Adapter
Wizard](tilt-adapter-wizard.md) page.

The EAT is a 4-screw adapter whose motors move in coupled groups: a corner command drives that
corner's motor forward and the opposite corner's motor backward, and a backfocus command drives all
four together. The plugin plans corrections in those native moves, so a full tilt-plus-backfocus
correction takes at most three commands.

## Connecting

1. Plug the adapter in and pick its COM port under **Serial port** (the ⟳ button re-scans the list).
   The port is saved per profile.
2. Click **Connect**. Opening the port restarts the adapter's controller, so it takes a few seconds
   to boot before it responds; the status line then reads *Connected on COM7* (or whichever port you
   chose). There is no auto-connect: the plugin only opens the port when you click.
3. Click **Disconnect** when you are done. If the connection sits unused for 30 minutes, a dialog
   asks whether to disconnect it.

While connected, a **Motor positions (steps)** grid shows each corner's current position counter,
polled from the device. The corners are labeled with the device's own motor numbering (**TR ·
Motor 1**, **TL · Motor 2**, **BR · Motor 3**, **BL · Motor 4**) and with the wizard screw number
each corner maps to (screw 1 = TR, 2 = TL, 3 = BL, 4 = BR). The counters read "unknown" until the
first successful position query, and during a calibration run each one also shows Δ, its change
since the run started.

The counters are **absolute** and stored in the adapter's EEPROM: they survive power cycles and do
not reset between sessions, so they carry whatever position your earlier adjustments left them at.
The plugin never zeroes them; if you want the counters re-zeroed, do it in the vendor's app.

Automation keeps every motor inside a travel window of **0 to Max excursion**. Travel below 0 is
never sent, so the useful working room is whatever sits between your current counters and the cap.

## Hands-off calibration

With the device connected, the wizard performs its own calibration moves instead of prompting you to
turn screws, and the step instructions describe what the wizard is about to send rather than what
you must do.

- **Steps applied per screw** defaults to 150 steps for the EAT presets (270 µm at 1.8 µm per step),
  large enough that each calibration move stands well above measurement noise. The value must fit
  within the [safety limits](#safety-limits) before a run will start.
- The first time you connect in a session, the wizard turns on **Measure direction** (the six-step
  calibration) and notifies you. Leaving it on is recommended: the adapter's direction is then
  measured with the same backfocus command that Automatic Adjustment later sends. If you turn it
  off, backfocus moves keep an "(assumed direction)" warning.
- **Auto Run All** drives every remaining step without further clicks: the wizard sends the step's
  move, runs the measurement, advances, and finishes with a restore move that returns every motor to
  its starting position. To step through manually instead, use **Run This Step** (the **Run
  Measurement** button is relabeled while a motorized device is connected), which sends one step's
  move and measurement at a time.
- Cancelling an automated run (**Cancel Run**) is safe but not instant: the device has no abort
  command, so the in-flight move and measurement finish first, and the wizard then sends the inverse
  of every move applied so far, returning the adapter to its starting position.

Completing a calibration with the device connected also records that the calibration is **linked to
that device**: because the wizard sent every move itself, the correspondence between wizard screws
and physical corners is established by measurement rather than assumption. [Automatic
Adjustment](#automatic-adjustment) requires this link. It is cleared by any calibration the wizard
did not drive itself: a
[Manual Calibration Entry](tilt-adapter-wizard.md#manual-calibration-entry), a
[replayed](tilt-adapter-wizard.md#saving-and-replaying-a-calibration-run) calibration, a run
performed without the device connected, or substituting **Use Saved AF** for a live measurement
partway through a connected run.

## Automatic Adjustment

While the device is connected, the inspector's **Tilt Adapter Guidance** section shows the live
motor positions and an **Automatic Adjustment** button beneath the numeric guidance table. Clicking
it computes a move plan from the current measurement's fitted sensor model (the same numbers the
guidance table displays) and opens a **Review motor commands** dialog. Nothing moves until you
approve the plan there.

The dialog shows:

- **Apply** checkboxes for **Tilt correction** and **Backfocus correction**. Toggling either one
  recomputes the plan, so the listed moves are always exactly what will be sent.
- The moves to send, each described by which screws travel and by how many signed steps, with the
  raw serial command alongside for auditing.
- The residual left at each screw after the moves: plans are rounded to whole steps, which leaves at
  most about one step (1.8 µm) of error per corner.
- Warnings when something needs attention: the adapter direction is still assumed rather than
  measured, the saved step size disagrees with the wizard's last measured value, the measured tilt
  contains a twist component that no rigid adapter can remove, a [backfocus bias](#why-corrections-near-zero-add-a-backfocus-move)
  had to be added to keep the motors at or above 0, or a move would violate a travel limit (which
  disables sending).
- The move count and an estimated duration. Each move takes roughly five to ten seconds.

**Cancel** sends nothing. The confirm button, labeled **Send 2 moves** (or however many are
planned), executes the moves in order and then offers to re-run the Aberration Inspector to confirm
the improvement. Every move is physical and is stored in the adapter's EEPROM, so review the list
before sending; there is no automatic undo.

!!! note "One adjustment per measurement"
    After a plan executes, **Automatic Adjustment** stays disabled until a new Detailed Analysis
    completes, so the same measurement can never be applied twice and every adjustment is confirmed
    by a fresh measurement before the next one. If the confirming run shows the tilt got *worse*
    (a sign of a stale calibration or a camera rotated since calibration), the plugin says so and
    offers to revert, sending the inverse of each move in reverse order. A failure partway through
    a plan brings the same revert offer.

## Safety limits

Three persisted settings in the **Motorized Device Connection** section, under **Safety Limits**,
bound what automation may send:

| Setting | Default | What it does |
|---|---|---|
| **Max steps per command** | 200 | Cap on the magnitude of a single commanded move. Larger planned moves are split or refused. |
| **Max excursion (steps)** | 2000 | Upper bound of the travel window. Every motor is kept between 0 and this value; a move that would carry one outside it is refused before anything is sent. |
| **Settle time (s)** | 3 | Seconds to wait after each commanded move before polling positions or sending the next command. |

The excursion limit is compared against the adapter's absolute, EEPROM-persisted counters, not
against how far the current session has moved. It is the **upper** bound of a travel window whose
lower bound is fixed at 0: with the default of 2000, every motor is kept between 0 and 2000.
Because the counters carry over between sessions, the cap has to exceed wherever your motors
currently sit plus the travel you intend to use — a limit set tighter than a motor's current
position refuses the very first move, however small that move is. A refused move names the motor and
the position it would have reached, and nothing is sent.

### Why corrections near zero add a backfocus move

A tilt correction is **differential**: it drives one corner up and the opposite corner down by the
same amount. With the motors at or near 0 there is nothing below to give, so the correction cannot
be applied as-is.

Rather than refuse it, the plugin lifts the whole adapter first: it prepends a **backfocus bias**, a
move that raises all four motors by the smallest amount that keeps the correction inside the window.
The approval dialog shows the bias as its own move and warns that it is present.

The bias is not free. Moving all four screws together *is* a backfocus change, so it shifts your
backfocus by the bias amount, and that shift is included in the residuals the dialog reports. To
avoid it, give the motors room to work before adjusting — raise them away from zero in the vendor's
app, or apply a positive backfocus move of your own — so corrections have travel underneath them.

## The Simulator port

When a motorized preset is selected, the **Serial port** list always offers **Simulator** as its
first entry. Connecting to it drives the [Camera
Simulator's](camera-simulator.md#rehearse-a-tilt-calibration-in-the-daytime) virtual tilt adapter
instead of real hardware. Everything behaves as it does with the real device, and the injected tilt
converges toward flat as corrections are applied, so you can walk through the whole motorized
workflow at your desk before connecting a real adapter.

Connecting to the Simulator port checks three preconditions:

- The **Hocus Focus Simulator** must be the connected camera. Otherwise the connect is refused with
  an error, since the simulated adapter only changes the simulator's images.
- The simulator's tilt-adapter geometry (screw count, adjustment type, step size, screw radius) must
  match the selected EAT preset, or the calibration loop cannot converge. If they differ, a dialog
  offers to change the simulator's configuration to match; declining aborts the connect.
- **Enable Aberrations** on the **Camera Sim** options tab is switched on automatically (with a
  notification) if it was off, because the loop measures nothing on aberration-free frames.

## Troubleshooting

**Connect fails or the port is missing.** Click ⟳ to re-scan after plugging the device in, and make
sure no other program (the vendor's app, a serial monitor) is holding the port. The adapter takes a
few seconds to boot after the port opens, so a slow first response is normal.

**Automatic Adjustment is not visible.** The button only appears while a device is connected.
Connect it from the wizard's **Motorized Device Connection** section; the inspector uses the same
connection.

**Automatic Adjustment is disabled with "This calibration is not linked to the connected
device."** Run a calibration with the device connected (Auto Run All is the easiest path). A
calibration entered by hand or replayed from disk cannot prove which physical corner its screw 1
refers to, and automation applying a rotated correction would make tilt worse
unattended. The companion message, "This calibration is low-confidence", means the connected
calibration ran but did not pass its own quality validation; re-run it under better conditions.

**Automatic Adjustment is disabled right after an adjustment.** That is the
one-adjustment-per-measurement rule. Run a new Detailed Analysis; the button re-enables when it
completes.

**A move was refused by a limit.** The error says which limit. For **Max steps per command**, either
reduce the amount being sent (for calibration, **Steps applied per screw**) or raise the limit. For
**Max excursion (steps)**, remember the check is against the absolute counters: a motor already
sitting near the cap has no headroom left, so either raise the cap or re-zero the counters in the
vendor's app.

**A move was refused for going below 0.** Travel below zero is never sent. During a calibration run
(which sends its moves directly rather than through the planner) this means the motors are sitting
too close to zero for a differential move of that size — raise them first, in the vendor's app or
with a positive backfocus move, then re-run. If a motor is *already* at a negative counter from
earlier work outside the plugin, no move will be accepted until you re-zero it in the vendor's app.

**The Simulator port refuses to connect.** Connect the **Hocus Focus Simulator** camera first, and
answer Yes when asked to change the simulator's tilt configuration to match the preset (or align the
two yourself on the **Camera Sim** tab).
