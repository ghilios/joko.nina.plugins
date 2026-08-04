# ASG EAT Serial Protocol — Live Hardware Capture (T15)

**Status:** Captured from real hardware. Resolves the `// LIVE-CAPTURE:` unknowns in
`EatSerialTransport.cs` and `EatResponses.cs`.

**Capture context**
- **Date:** 2026-07-17
- **Device:** ASG Electronic EAT, enumerated by Windows as **"Arduino Uno (COM7)"** (ATmega328P + ATmega16U2 USB-serial bridge).
- **Firmware version:** **7.1.0** (device reports `FW:7.1.0` at the end of its boot banner).
- **Method:** direct `System.IO.Ports.SerialPort` session (PowerShell) using our production serial parameters, but logging **raw bytes** (hex + ASCII) instead of `ReadLine()`, so exact framing, banners, and chunk boundaries are visible. Commands were sent one at a time; each response captured in full. No move exceeded 5 steps; every move was reversed, leaving all four motors back at their starting value of **600** with EEPROM re-saved.

> This device is fundamentally a **companion-GUI driver**: alongside its machine-readable answers it emits a stream of `{UI|SET|<widget>.<property>=<value>}` lines meant to drive a desktop app. Our parser must **ignore** those and key off the device's delimited data blocks and sentinels.

---

## 1. Serial parameters (confirmed)

| Parameter | Value | vs. our code |
|---|---|---|
| Baud | **9600** | ✅ `EatSerialTransport.BaudRate` correct |
| Data bits | **8** | ✅ correct |
| Parity | **None** | ✅ correct |
| Stop bits | **1** | ✅ correct |
| Handshake | **None** | ⚠️ **code uses `Handshake.XOnXOff` — change to `None`** (see §7.2) |
| DTR | **asserted** (required; also triggers reset-on-open) | ✅ `DefaultDtrEnable = true` correct |
| RTS | not required (left deasserted) | — |
| Line terminator (TX & RX) | **`\r\n` (CRLF, `0D 0A`)** | ✅ `LineTerminator = "\r\n"` correct |
| Command echo | **none** (device does not echo the raw command line) | ✅ no echo-skipping needed |

---

## 2. Connection sequence & boot banner

Opening the port **resets the MCU** (classic Arduino DTR auto-reset). Observed timeline:

```
T+0.00 s   port opened (DtrEnable=true)
T+~1.5 s   first bytes arrive  ← bootloader delay; nothing before this
T+~1.5–2.5 s   boot banner streams (see below)
T+~2.5 s   banner ends with the FW version line
```

**⚠️ Our `PostOpenSettleDuration = 500 ms` is too short** — the device doesn't even *start* emitting until ~1.5 s after open, and the banner runs to ~2.5 s. Sending `cp` at 500 ms would hit the bootloader. See §7.1.

**Boot banner (decoded, `\r\n` shown as line breaks):**

```
<LF>Initialize Setup
{UI|SET|ready_light.On=True}
{UI|SET|ready_light.IndicatorColor=green}
{UI|SET|TR_input.Text=0}      {UI|SET|TL_input.Text=0}
{UI|SET|BR_input.Text=0}      {UI|SET|BL_input.Text=0}
{UI|SET|TP_input.Text=0}      {UI|SET|RT_input.Text=0}
{UI|SET|LT_input.Text=0}      {UI|SET|BF_input.Text=0}
{UI|SET|TR_curr_position.Text=600}   {UI|SET|TL_curr_position.Text=600}
{UI|SET|BR_curr_position.Text=600}   {UI|SET|BL_curr_position.Text=600}
{UI|SET|TL_input_setting.Text=600}   {UI|SET|TR_input_setting.Text=600}
{UI|SET|BL_input_setting.Text=600}   {UI|SET|BR_input_setting.Text=600}
{UI|SET|set_speed_input.Text=50}
{UI|SET|set_max_speed_input.Text=50}
{UI|SET|set_accel_input.Text=300}
{UI|SET|orientationSelection.Value=1}
{UI|SET|orientation_img_1.Visible=True}
{UI|SET|orientation_img_2.Visible=False}
{UI|SET|orientation_img_3.Visible=False}
{UI|SET|orientation_img_4.Visible=False}
FW:7.1.0
```

**Interpretable connection signals:**
- **`FW:7.1.0`** — firmware version; the last banner line. A good "boot complete / device is an EAT" fingerprint and a robust end-of-banner marker to wait for instead of a fixed drain time.
- **`Initialize Setup`** — first banner line (start-of-boot marker).
- **`ready_light.IndicatorColor=green`** + **`ready_light.On=True`** — device-ready indicator.
- The banner also reveals the full command vocabulary (`*_input` widgets: `TR TL BR BL TP RT LT BF`), current motor positions, and motion parameters (`speed=50`, `max_speed=50`, `accel=300`, `orientation=1`).

Recommended connect handshake: open → **read/discard until a line matching `^FW:` is seen (or ~3 s timeout)** → then send `cp`.

---

## 3. Response framing & the `{UI|SET|...}` noise

- Every line is terminated with **CRLF**.
- Interleaved throughout every response are `{UI|SET|<widget>.<property>=<value>}` lines that drive the vendor GUI. **These are noise for us and must be ignored.**
- The device's actual machine-readable output is delimited by `***...***` marker lines and (for the position list) bare integer lines.
- **A single logical token can be split across serial reads.** Parse on **reassembled complete lines** (which `ReadLine()` gives us), never on raw read-chunk boundaries. (During capture, `***finished movement***` arrived split across two reads; our production `ReadLine()` approach is immune, but this confirms the requirement.)

---

## 4. Command reference

### 4.1 `cp` — query current positions (read-only)

**Request:** `cp\r\n`

**Response (decoded; `{UI|SET|...}` lines omitted):**
```
{UI|SET|ready_light.IndicatorColor=Red}      ← busy
… full {UI|SET|...} state dump …
***Get Current Positions***
600
600
600
600
***End Current Positions***
{UI|SET|ready_light.IndicatorColor=green}    ← ready
***Action Processed***                        ← QUERY completion sentinel
```

- The four positions are **bare integer lines between `***Get Current Positions***` and `***End Current Positions***`**.
- **Completion sentinel for a query is `***Action Processed***`** (distinct from a move — see §5).
- Positions are **absolute counters** (persisted in EEPROM), not deltas.
- Duration ≈ 0.9 s (dominated by transmitting the ~1 KB `{UI|SET|...}` dump at 9600 baud).

### 4.2 Move commands — `<mnemonic>,<value>`

**Request:** e.g. `tr,5\r\n`, `tr,-5\r\n`, `bf,-5\r\n`

**Response structure (decoded; `{UI|SET|...}` omitted):**
```
{UI|SET|ready_light.IndicatorColor=Red}      ← busy
start_cmd: tr                                 ← echoes the mnemonic
tilt_value: 5.00                              ← echoes the argument (as a float)
moving TR + 5.00                              ← per-motor motion report (one line per moved motor)
moving BL - -5.00
***moving***                                  ← motion heartbeat (repeats during motion)
***moving***  …
{UI|SET|*_input.Text=0}                        ← UI resets input fields
***Get Current Positions***
600
605
595
600
***End Current Positions***
***Save EEPROM***                             ← ⚠ positions persisted to EEPROM (no undo)
… {UI|SET|...curr_position...} dump …
{UI|SET|ready_light.IndicatorColor=green}    ← ready
***finished movement***                       ← MOVE completion sentinel
```

- **Argument is parsed as a float** and echoed as `tilt_value: N.NN`.
- **`start_cmd:` / `tilt_value:`** provide a free confirmation that the device parsed our command as intended (useful as a validation hook).
- **`moving <MOTOR> <sign> <value>`** lines are the authoritative per-motor report of what physically moved. Backfocus reports as a single `moving Backfocus  + <value>` line (all four motors, one line).
- Every move ends with **`***Save EEPROM***`** then **`***finished movement***`**. The EEPROM save confirms our whole no-undo safety model.
- A response also embeds a fresh `***Get Current Positions***` block, so a move doubles as a position read (no separate `cp` needed after a move). **Implemented:** `EatResponses.TryParseMovePositions` + `EatTiltMotionController.ExecuteMoveAsync` reconcile the shadow from this block (falling back to advancing by the commanded delta only when it is absent, e.g. the simulator), and expose it as `LastKnownPositions` — which is what drives the live per-motor display between the moves of a run, with no follow-up `cp`.

### 4.3 Mnemonic → motor mapping

Wizard/optics screw labels: **TR, TL, BL, BR**. Motor moves observed:

| Mnemonic | Confirmed? | Motor effect (for `+N`) | Our model | Match |
|---|---|---|---|---|
| `tr` | ✅ **confirmed** | TR **+N**, BL **−N** | DiagonalA | ✅ |
| `tl` | ✅ **confirmed** | TL **+N**, BR **−N** | DiagonalB | ✅ |
| `bf` | ✅ **confirmed** | all four **+N** | Backfocus | ✅ |
| `bl` | ⛔ not exercised | expected TR −N, BL +N (opposite of `tr`) | DiagonalA (opp.) | inferred |
| `br` | ⛔ not exercised | expected TL −N, BR +N (opposite of `tl`) | DiagonalB (opp.) | inferred |
| `tp` | ⛔ not exercised | expected TR+, TL+, BL−, BR− (top edge) | EdgeVertical | inferred |
| `bt` | ⛔ not exercised | expected opposite of `tp` | EdgeVertical (opp.) | inferred |
| `rt` | ⛔ not exercised | expected TR+, BR+, TL−, BL− (right edge) | EdgeHorizontal | inferred |
| `lt` | ⛔ not exercised | expected opposite of `rt` | EdgeHorizontal (opp.) | inferred |

The three exercised commands (`tr`, `tl`, `bf`) all matched our per-corner model exactly. The remaining mnemonics follow the identical `<mnemonic>,<value>` grammar; their exact per-motor directions were **not** exercised on hardware and should be confirmed opportunistically.

### 4.4 Sign encoding — **`SignedArgument` confirmed**

- `tr,-5` → `tilt_value: -5.00`, `moving TR + -5.00` / `moving BL - 5.00`. The device parses a **negative signed argument** directly.
- `bf,-5` → `tilt_value: -5.00`, all four motors −5. **Negative backfocus works via a signed argument** on `bf` (no opposite mnemonic).
- ✅ `EatCommands.DefaultSignEncoding = SignedArgument` is correct; the `Backfocus`-always-signed special case is correct. The `OppositeMnemonic` path is **not** needed for this firmware.

---

## 5. Completion sentinels (critical for the read loop)

| Command type | Terminal sentinel |
|---|---|
| Query (`cp`) | `***Action Processed***` |
| Move (`tr`, `tl`, `bf`, …) | `***finished movement***` |

The read loop should **terminate an exchange when it sees the matching sentinel** (accept either, to be robust), instead of relying on the 200 ms quiet-window heuristic. `ready_light.IndicatorColor` transitions **Red → green** also bracket the busy period and can drive a live busy indicator.

---

## 6. Position block ordering — **⚠️ differs from our code**

The bare-integer block in `***Get Current Positions***` is emitted in **raster order `[TL, TR, BL, BR]`**:

| Block index | 0 | 1 | 2 | 3 |
|---|---|---|---|---|
| **Actual device** | **TL** | **TR** | **BL** | **BR** |
| Our `TiltDevicePositions` assumption | TR | TL | BR | BL |

Empirical proof:
- `tr,+5` (TR→605, BL→595): block = `[600, 605, 595, 600]` ⇒ index 1 = TR, index 2 = BL.
- `tl,+5` (TL→605, BR→595): block = `[605, 600, 600, 595]` ⇒ index 0 = TL, index 3 = BR.

This is a **pairwise swap** (0↔1 and 2↔3) versus the assumed `[TR, TL, BR, BL]`. Shipping as-is would map every motor's counter to the wrong corner and corrupt shadow tracking / excursion enforcement.

> **Quirk:** the *labeled* `{UI|SET|<corner>_curr_position.Text=N}` fields are emitted in a **different** order (`TR, TL, BR, BL`) than the bare block (`TL, TR, BL, BR`). The most robust parser reads the **labeled** `*_curr_position` fields **by corner name** (order-independent); the bare block is the fallback, parsed with the confirmed order `[TL, TR, BL, BR]`.

---

## 7. Required changes to our assumptions (T15 resolution)

### 7.1 `EatSerialTransport` — settle / boot handling
- **`PostOpenSettleDuration`**: 500 ms → **wait for the boot banner to finish**, i.e. read/discard until a line matching `^FW:` (with a ~3 s cap), because the MCU resets on open and is silent for ~1.5 s. A blind 500 ms drain sends `cp` into the bootloader.

### 7.2 `EatSerialTransport` — handshake
- **`Handshake.XOnXOff` → `Handshake.None`.** The Uno sketch has no software flow control; with `XOnXOff` the PC driver can inject `0x11`/`0x13` bytes into the sketch's input. `None` captured a ~1 KB burst cleanly with no loss at 9600 baud. Keep the write timeout generous regardless.

### 7.3 `EatResponses.ParseCpPositions` — parsing
- **Replace the "first four integers found" heuristic.** With the `{UI|SET|...}` dump present, the first integers encountered are GUI-widget values, not positions. Parse **either**:
  1. the four bare integers **between `***Get Current Positions***` and `***End Current Positions***`**, mapped as `[TL, TR, BL, BR]`, **or** (preferred, order-independent)
  2. the `{UI|SET|<corner>_curr_position.Text=N}` lines **by corner label**.
- **Fix the block/motor order** to `[TL, TR, BL, BR]` (see §6) wherever `TiltDevicePositions` order is assumed.

### 7.4 `EatResponses.ParseMoveAck` — ack
- **Recognize `***finished movement***`** as the move-success sentinel (and `***Action Processed***` for queries), instead of "did not time out." Optionally validate `start_cmd:` / `tilt_value:` echo against what we sent.
- The read loop can end each exchange **on the sentinel line**, which is faster and more reliable than the quiet-window race.

### 7.5 Confirmed-correct (no change)
- Serial params 9600/N/8/1, `DtrEnable=true`, CRLF framing.
- `SignedArgument` sign encoding, including negative `bf` via signed argument.
- Absolute EEPROM-persisted position counters; `***Save EEPROM***` after every move (validates the no-undo safety model).
- `MoveTimeout = 20 s` is very safe: a 5-step move completed in **~1.4 s**; the "5–10 s" figure applies only to large moves.

---

## 8. Coverage / open items

Exercised on hardware: connect+banner, `cp` (×2), `tr,5`, `tr,-5`, `tl,5`, `tl,-5`, `bf,5`, `bf,-5`. Not yet exercised (follow up opportunistically, ≤10-step reversible moves):
- `tp`, `rt` and the opposite mnemonics `bl`, `br`, `bt`, `lt` — grammar is identical; only the exact per-motor directions are unconfirmed.
- Device behavior on an **unrecognized/malformed** command (does it emit an error line or `***Action Processed***` with no effect?).
- Behavior at/near travel limits (our soft limits should prevent reaching them; device-side clamping unknown).
- Large-move timing (to sanity-check the 5–10 s figure and the 20 s timeout margin).

---

## Appendix A — Raw evidence excerpts

`tr,5` (raw ASCII, `<CR><LF>` = CRLF):
```
start_cmd: tr<CR><LF>tilt_value: 5.00<CR><LF>moving TR + 5.00<CR><LF>moving BL - -5.00<CR><LF>
***Get Current Positions***<CR><LF>600<CR><LF>605<CR><LF>595<CR><LF>600<CR><LF>***End Current Positions***<CR><LF>
***Save EEPROM***<CR><LF> … ***finished movement***<CR><LF>
```

`tl,5`:
```
start_cmd: tl<CR><LF>tilt_value: 5.00<CR><LF>moving TL + 5.00<CR><LF>moving BR - 5.00<CR><LF>
***Get Current Positions***<CR><LF>605<CR><LF>600<CR><LF>600<CR><LF>595<CR><LF>***End Current Positions***<CR><LF>
```

`bf,5`:
```
start_cmd: bf<CR><LF>tilt_value: 5.00<CR><LF>moving Backfocus  + 5.00<CR><LF>
***Get Current Positions***<CR><LF>605<CR><LF>605<CR><LF>605<CR><LF>605<CR><LF>***End Current Positions***<CR><LF>
```

`cp` (query terminator differs):
```
***Get Current Positions***<CR><LF>600<CR><LF>600<CR><LF>600<CR><LF>600<CR><LF>***End Current Positions***<CR><LF>
{UI|SET|ready_light.IndicatorColor=green}<CR><LF>***Action Processed***<CR><LF>
```
