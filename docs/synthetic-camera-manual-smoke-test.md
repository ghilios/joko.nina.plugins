# Synthetic Star-Field Camera — Manual Smoke Test (Windows / NINA)

Executes step 23 of `plans/synthetic-camera-plan.md` and Task 10 of
`plans/camera-simulator-interactive-tilt-plan.md`. Everything else in both plans is covered by the automated
suite (2105 tests, incl. capstones that render frames and recover star positions/HFR and the injected aberration
surface through HocusFocus's own detector, and one that drives the *real* screw-guidance math through the
simulated tilt adapter). This checklist covers what only a human at a running NINA can confirm: that the device
shows up, connects, renders, and that the UI binds and behaves in the real app.

Checks (a)–(f) cover the camera itself. Checks (g)–(q) and the headline loop cover the rig setup dialog and the
virtual tilt adapter. Check (r) covers a full autofocus run — the loop everything else is in service of.

## Prerequisites

1. **Build + install the plugin.** `dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug` — the
   project's PostBuild step copies the assembly + dependencies to `%localappdata%\NINA\Plugins\3.0.0\Hocus Focus`.
   Restart NINA.
2. **ASTAP star database** (optional but needed for stars). Install an ASTAP DB (`.1476` or `.290` cell files,
   e.g. the `g17`/`h17`/`h18` Gaia sets) — by default under `C:\Program Files\astap`. Without it the camera
   still works: it renders a **starless** frame (sky+dark+noise) and logs a warning naming the missing path.
3. **Simulator devices**: NINA's simulator **focuser** and simulator **telescope/mount**.

## Setup

- **Options → Hocus Focus → Camera Sim** tab. Defaults: IMX455, filter L, aperture and focal length **blank**
  (⇒ inferred from the profile — see (p)), obstruction on @ 0.3, throughput 0.85, gain 100,
  pedestal 500 ADU, −10 °C, sky 20.5 mag/arcsec², seeing 2.5″, ASTAP path, limiting mag 16, rotation 0,
  noise seed 42, aberrations off.
- **Type Aperture = 100 and Focal Length = 800** into the setup dialog (⇒ f/8) so the numbers in checks (a)–(c)
  apply. Left blank they infer from the profile instead and you will not get f/8 — see (p).
- **Equipment → Camera** → choose **"Hocus Focus Simulator"** → Connect.
- Connect the simulator **Focuser** and **Mount**. Slew the mount to a star-rich field (e.g. near the galactic
  plane) so the catalog returns stars.

> **Where the rig lives:** sensor, aperture, focal length, obstruction and throughput are **edited in the
> camera's setup dialog** — the **gear** button beside the camera in **Equipment → Camera** — and render
> read-only on the Camera Sim options tab. Everything you change per session stays on the options tab. Checks
> (g)–(i) cover this.

> **Configure-before-connect:** reported geometry (resolution / pixel size / bit depth) is latched at connect.
> After changing **Sensor Model**, reconnect the camera.

---

## Checks

### (a) HFR V-curve across focuser positions
Step the focuser around the configured **Optimal Focuser Position** (default 5000) and take a short exposure at
each; watch HFR (HocusFocus star detection / the AF graph).

- **Expect:** a clean V/hyperbola with the minimum at 5000, following `HFR(x) = √(HFR_min² + (κ·(x−5000))²)`.
- With the defaults above (D=100, f=800 ⇒ N=8, ε=0.3, p=3.76 µm, seeing 2.5″, L filter, step size 2.0 µm/step):
  **HFR_min ≈ 1.5 px**, **κ ≈ 0.024 px/step**, so HFR reaches **≈ 4× HFR_min (~6 px) at about ±245 steps**.
  Sweep ~4700 → 5300 to see the full curve. (Smaller `Focuser Step Size` ⇒ proportionally wider curve:
  range ≈ √15·HFR_min/κ.)
- **Also:** run a real **Auto Focus** run — it should converge on ~5000.

### (b) Donuts with the correct hole far from focus
Move well off focus (e.g. 5000 ± 500 steps) and inspect a bright star.

- **Expect:** an annular **donut** with a distinct central hole; hole/outer radius ratio ≈ the **central
  obstruction fraction (0.3)**.
- Set **Central Obstruction Enabled = off** → the donut becomes a **filled disk** (no hole).
- Increasing the obstruction fraction (e.g. 0.5) visibly widens the hole.

### (c) Narrowband needs longer exposures
Take a fixed-length exposure with filter **L**, then switch to **Hα 5 nm** at the same exposure.

- **Expect:** dramatically fainter stars — the Hα-5 frame collects **≈ 93× fewer electrons** than L
  (bandwidth 320→5 nm × QE 0.73→0.50). Restoring similar signal needs ~93× the exposure.
- Sky background drops correspondingly (narrowband sky ∝ Δλ) — which is why NB tolerates long subs.

### (d) Aberration inspector recovers the injected tilt/backfocus
In **Camera Sim** options: **Enable Aberrations = on**, **Tilt Angle = 30°**, **Tilt Amount = 80 µm**,
**Backfocus Error = 40 µm**. Reconnect. Run the HocusFocus **Aberration Inspector** (a stepped run across
focuser positions).

- **Expect** the inspector to report approximately what was injected:
  - **Tilt effect ≈ 80 µm** (the injected *Tilt Amount* = the inspector's `TiltEffectMicrons`)
  - **Curvature effect ≈ 40 µm** (the injected *Backfocus Error* = `CurvatureEffectMicrons`)
  - **Tilt azimuth ≈ 30°** (the injected *Tilt Angle* = the inspector's `Phi`)
- Note the parameterization: **Tilt Angle is the azimuth φ**, *not* the inspector's `Theta` (which is the tilt
  *magnitude* angle `atan|G|` and is derived).
- With aberrations **off**, the inspector should report an essentially **flat** surface (tilt/curvature ≈ 0).
- This is the same inject⇄recover the automated capstone proves headlessly (injected 30°/80 µm/40 µm →
  recovered 29.8°/79.9 µm/41.9 µm); the manual run confirms it through the real inspector UI.

### (e) Descriptive failure when the focuser/mount is disconnected
Disconnect the **focuser** (leave the mount connected) and start an exposure. Then repeat with the **mount**
disconnected.

- **Expect:** the exposure fails with a clear message naming the missing device and why it's needed — the
  focuser drives defocus, the mount drives pointing. Not a crash, not a silent blank frame.
- Reconnect → exposures succeed again (camera state recovers from `Error`).

### (f) Resolution / pixel size / bit depth track the selected sensor
Change **Sensor Model**, reconnect the camera, and check NINA's reported camera info each time:

| Sensor | Resolution | Pixel | Bit depth |
|---|---|---|---|
| IMX455 | 9576 × 6388 | 3.76 µm | 16 |
| IMX571 | 6248 × 4176 | 3.76 µm | 16 |
| IMX533 | 3008 × 3008 | 3.76 µm | 14 |
| IMX294 | 4144 × 2822 | 4.63 µm | 14 |

- Also expect `SensorName` and electrons/ADU to follow the sensor (e.g. IMX455 ≈ 0.763 e⁻/ADU at gain 0),
  and `SensorType` = Monochrome.

---

## Rig setup dialog & virtual tilt adapter

The screw math is pinned by the automated suite — including a capstone that asks the **real** guidance helper
what to turn, turns exactly that on the simulated adapter, and asserts the residual is zero across 3/4 screws ×
screws/steppers × both rig directions. So these checks are not about the math. They are about the surface no test
can see: whether the windows open, the bindings bind, the gates gate, and **whether the glyphs the inspector
prints are the glyphs the panel takes**.

### Setup for these checks

- **Options → Hocus Focus → Camera Sim → Tilt Adapter**: turn **`Show tilt adapter panel in Imaging`** on
  (it's off by default), then **restart NINA** — see (l) for why.
- Simulated adapter defaults (panel → **Adapter configuration**): **3 screws** at **0° / 120° / 240°**,
  **Screws** (not steppers), **thread pitch 500 µm/turn**, **screw radius 30 mm**,
  **`⟳ tighten moves adapter toward: camera`**.
- The numbers below assume those defaults plus the camera defaults from **Setup** above (IMX455, focuser step
  size 2.0 µm/step, optimal position 5000).

### (g) The gear button opens the rig setup dialog
**Equipment → Camera** → select **"Hocus Focus Simulator"** → click the **gear** button beside the dropdown.

- **Expect:** a non-resizable tool window titled **"Hocus Focus Simulator Setup"**, containing a bold **Rig**
  header, a one-line explainer, and exactly **six** rows — **Sensor Model**, **Aperture** (mm), **Focal Length**
  (mm), **Central Obstruction** (checkbox), **Obstruction Fraction**, **Optical Throughput**. **Nothing else** —
  the dialog is sensor + optics only. The virtual tilt adapter is **not** here: it is rig state NINA latches at
  connect, whereas the adapter is worked mid-session, so the adapter lives in the Imaging dockable (its panel)
  and in **Options → Hocus Focus → Camera Sim → Tilt Adapter** (its configuration).
- Values edit and persist: set Aperture to **120**, close, reopen → **120**, and the Camera Sim options page
  agrees (check (i)).
- **Aperture and Focal Length clamp on commit rather than showing a validation border** — type **99999** into
  Aperture, tab out, and the box visibly **snaps to 2000** (focal length caps at **20000**). That's the intended
  behaviour, not a missing rule: the box has to accept an empty value to mean "infer it" (check (p)), and a
  `ValidationRule` runs on the raw text before the converter, so it would reject the blank box. Clamping is what
  replaced it.
- **Obstruction Fraction (0–0.9) and Optical Throughput (0–1) do still use a validation border** — the two
  behave differently on purpose, so don't report the difference as an inconsistency.
- **Obstruction Fraction greys out** when **Central Obstruction** is unchecked — it's inert then.
- The gear stays clickable while connected, and clicking it twice opens a **second** window bound to the same
  options. NINA's own `SimulatorCamera` behaves identically — expected, not a bug.

### (h) Sensor Model locks while connected — the rest of the rig does not
- **Connected:** **Sensor Model** is **disabled** (its tooltip says why: NINA latches resolution, pixel size and
  bit depth at connect). The other five rig options stay **editable**.
- **That's by design, not a gap.** Those five are read per exposure from the render snapshot, so they take
  effect on the **next frame with no reconnect**. Prove it: connected, take an exposure, change **Optical
  Throughput 0.85 → 0.1**, take another → visibly fainter stars, no reconnect. Only the geometry NINA latches
  needs the lock.
- **Disconnected:** Sensor Model is editable again.

### (i) Rig options are read-only in plugin Options
**Options → Hocus Focus → Camera Sim**:

- **Aperture**, **Focal Length**, **Central Obstruction**, **Obstruction Fraction**, **Optical Throughput**
  (Optics group) and **Sensor Model** (Sensor & Filter group) render **greyed out but readable**, showing the
  live values.
- **Expect the tooltip** on each: *"Rig settings are edited in the camera's setup dialog — click the gear icon
  next to the camera in the Equipment > Camera pane. They are shown here for reference only."*
- Change Aperture in the setup dialog → the read-only box here follows immediately (one options object, two
  views).
- Everything else on the page (filter, sky, seeing, gain, pedestal, temperature, ASTAP path, limiting mag,
  rotation, seed, optimal position, step size, aberrations) stays **editable**.

### (j) The aberration group collapses when aberrations are off
**Options → Hocus Focus → Camera Sim → Aberrations**:

- **Enable Aberrations off** → the checkbox stays put; everything below it (Tilt Angle, Tilt Amount, Backfocus
  Error, optical-axis offsets) is **gone — collapsed, not greyed**. The page gets shorter.
- Toggle back on → the group returns in place. No restart, no reopen.
- The **panel** deliberately behaves differently: with aberrations off it shows the banner *"Aberrations are
  disabled — the plane below has no effect on rendered frames"* plus an **Enable** button, and stays operable
  (editing an inert plane is legal — it just doesn't render).

### (k) The tilt-adapter panel renders in the dockable, and its configuration also on the options page
The Imaging dockable hosts the **whole panel**. **Options → Hocus Focus → Camera Sim → Tilt Adapter** hosts the
**same "Adapter configuration" markup** (one template, two hosts) — rendered inline there, always expanded, with
no expander around it. Check both.

- **Expect, out of the box:**
  - state strip `Plane:  0.0 µm @ 0°   ·   Backfocus 0.0 µm` with a green **`✓ ≈ flat`**
  - `Amount per click  [0.25 | turns]` and the dim hint **`≈ 125 µm axial`** (0.25 × 500 µm/turn)
  - three rows — `Screw 1 · 0.0°`, `Screw 2 · 120.0°`, `Screw 3 · 240.0°` — each with a `⟲` and a `⟳` button,
    and **no movement-type selector** (that's 4-screw only)
  - `Net (turns):  1: 0.00  ·  2: 0.00  ·  3: 0.00` with **Re-zero**
  - two **collapsed** expanders: `Injected aberration` and
    `Adapter configuration (3 screws · Screws · 500 µm/turn · R 30 mm · ⟳ → camera · …)`
- **Both hosts share the state that matters.** They read and write one options object, so the adapter *geometry*
  is shared: set **Screw radius = 40 mm** on the options page and the dockable's `Adapter configuration` summary
  follows live (and vice versa), with no restart. The same holds for the injected plane.
  **Expected NOT to follow — do not report these:** `Net (turns)`, **Undo**, and `Amount per click` are per-host
  (each host has its own view-model), and only the dockable has them at all. Net position is a *display* odometer
  over the shared plane, not part of it — `Re-zero` re-bases only that host's counter and never touches the rig.
- Set **Screw count = 4** → a `Corner | Side | Backfocus` segmented radio appears above four rows, each naming
  its coupled partner (`3 opposes`, `4 opposes`, …), and **Screw 3/4 angle** render **dimmed and derived**
  (+180° of screws 1/2), not editable. Set it back to 3 → selector gone.

### (l) The Imaging dockable is gated by the option
With **`Show tilt adapter panel in Imaging` off**:

- **Expect:** the panel is **not open** at startup, and clicking the **Simulator Tilt Adapter** sidebar button
  **does nothing** — no open, and no flash of opening then closing.
- Turn the option **on** while NINA runs → the button opens the panel normally. Turn it **off** with the panel
  open → it closes immediately. Neither needs a restart.

> **Two accepted NINA limitations. Both are expected, and neither is a bug — do not report them as one.**
>
> 1. **The 30×30 sidebar button always remains and cannot be removed.** Not with the option off, not after a
>    restart, not ever. NINA builds the dockable list once at startup into a plain non-observable list and
>    exposes no supported way to remove an entry. All the plugin can do is make the button **inert**, which is
>    exactly what the check above verifies.
> 2. **The button only appears after a NINA restart** following the plugin update — same root cause. The first
>    time you run this build, it will not be in the sidebar until you restart, no matter what the option says.

### (m) The glyph contract holds
The one thing no automated test can see is what is actually painted on the buttons. Read this one carefully.

- **Buttons carry rotation.** In **Screws** mode every row shows **`⟲`** and **`⟳`** (drawn as the plugin's
  rotation icons — the same glyphs the inspector's guidance prints).
- **Switch Adjustment type → Stepper Motors.** The buttons become **`−`** and **`+`**, the amount unit becomes
  **`steps`**, the default amount jumps to **10**, the net strip reads steps — and **no rotation glyph appears
  anywhere on the panel**. Motors abstract rotation away.
- **Tooltips and feedback carry motion.** Hover `⟳` on Screw 2 (Screws mode, defaults) → *"⟳ 0.25 turns —
  screw 2 moves toward the camera (⬇); tilt tips accordingly."* Click it → the last-action line reads
  `Last: Screw 2 ⟳ 0.25 (S2 ⬇) — tilt 0.0 → … µm` with an **Undo**.
- **⬆/⬇ must NEVER appear as a rotation** — not on a button, not in a "turn it this way" instruction. An
  up/down arrow where a turn direction belongs is a real bug: it is precisely the confusion the two-vocabulary
  contract exists to prevent.
- **Direction comes only from the button.** Type **−0.5** into **Amount per click** → it must be **rejected**
  (the box is strictly positive, 0.001–10000). There is no signed amount to contradict the glyph you clicked.
- Flip **`⟳ tighten moves adapter toward`** from `camera` to `objective`. The **same `⟳` button** now reports
  **⬆** where it reported ⬇. The glyph on the button must not change — only the motion it causes does.

### (n) The coherence badge and the copy commands
Expand **Adapter configuration**.

- **On a fresh profile expect `⚠ differs from adapter settings`**, with a tooltip naming the differing fields:
  **Screw 1 angle, Screw 2 angle, Screw 3 angle, Thread pitch, Screw radius**. That is correct — the real Tilt
  Adapter options ship uncalibrated (angles NaN, pitch and radius −1), so there is nothing to match yet.
- **Copy to adapter settings…** → an **inline** warning (never a modal) naming exactly what it will overwrite →
  **Overwrite** → the badge flips to **`matches adapter ✓`**. Verify at **Options → Hocus Focus → Tilt
  Adapter**: 3 screws at 0/120/240°, 500 µm/turn, R 30 mm, marked as a **manual** calibration.
- **Cancel** leaves the real settings untouched. **Copy from adapter settings** goes the other way and never
  writes the real settings.
- Diverge on purpose — set the sim's **screw radius to 40 mm** → badge returns to `⚠ differs`, tooltip lists
  **Screw radius**. Tolerances: angles **±2°**, pitch/radius **±5%**.
- **This badge is the loop's precondition.** The inspector guides from the *real* adapter settings while the
  simulator obeys the sim's. If they differ the loop will not converge, and the math will not be at fault.

### (o) Options-page layout for the adapter config — please report what you see
Needs a human at a real display. The old version of this check asked about the **setup dialog** growing when
`Adapter configuration` was expanded inside it (`ResizeMode.NoResize`, no `ScrollViewer`). **That is moot**: the
adapter is no longer in that dialog, which is now six rows and always compact. The question moved with the markup.

- **Options → Hocus Focus → Camera Sim → Tilt Adapter** renders the config **inline and always expanded** — a
  10-row grid, the screw diagram, the coherence badge and the Copy buttons — under the section header, below
  `Show tilt adapter panel in Imaging`.
- **Report back:** does that section read as part of the page (label column aligned with the sections above it,
  diagram not absurdly wide, page still scrolls to the Copy buttons)? It is the one place the config renders
  *without* an expander around it, so it is the layout most likely to need a tweak.
- The dockable is user-resizable and keeps its collapsed expander, so this is specific to the options page.

### (p) Optics default to the profile
With **Aperture** and **Focal Length** blank in the setup dialog, both boxes show a greyed-out number (40%
opacity) — the value the next exposure will actually use, inferred from the active profile.

- **Expect** on a **430 mm f/5** profile: **430** and **86.0**. Aperture is inferred from the profile's focal
  ratio, not stored — 430/5.
- **Expect** on a profile whose telescope settings are unset (NINA stores `NaN` for both): **980** and
  **140.0** — the built-in default rig, a 980 mm f/7. There is no configuration in which the boxes read blank
  and the camera renders at nothing.
- Typing a value makes it **solid**; clearing the box returns it to **grey** and to the inferred number. Edit the
  profile's focal length with the dialog open → the grey number follows without a reopen.
- **The Camera Sim options page shows the same two numbers**, read-only (check (i)), and **never `-1`**. `-1` is
  the stored "unset" sentinel; if it reaches a box, the converter is missing — that's a finding.

### (q) The setup dialog still opens
Click the camera **gear** button. The dialog must appear.

- **This check has no interesting pass state — it exists entirely for its failure mode.** If a `{StaticResource}`
  key in `SetupDataTemplates.xaml` is unresolvable, `WindowService.Show` swallows the `XamlParseException` and
  **the only symptom is nothing happening**: no dialog, no error, no crash. A dead gear button is the whole
  signal.
- If it doesn't open, check the NINA log for `WindowService.cs|Show|41` — that line is the swallow site and names
  the key.
- The automated suite pins every `{StaticResource}` key in the plugin's XAML against the merged resource graph,
  so this should not regress silently. Click it anyway: the test resolves keys, not the runtime dialog.

---

## Autofocus end to end

### (r) Autofocus completes without timing out
Run a real **Auto Focus** on the IMX455 at a star-rich pointing. This check exists because a user's run **timed
out at the 10-minute limit**: at 61 MP the frame was rendered single-threaded *inside* the download, so every
point cost focuser move ~3 s + exposure 5 s + **download ~35 s** + detection ~10 s.

- **Expect: download is now essentially free.** The progress bar should pass straight through it rather than sit
  there for ~35 s. The render now starts when the **exposure** does and runs on every core, so by the time NINA
  asks for the frame it is normally already finished and the download is just a handover. A point should cost
  about **expose + star detection**, with download no longer a term worth counting.
- **Expect: the run completes** and converges (check (a)), rather than dying on the timeout.
- **Frames at one focuser position must differ.** Take two exposures without touching anything and blink or
  subtract them — the noise must change. It previously didn't: every exposure at a given position was
  byte-identical, which meant **Frames per point > 1** was averaging a frame with itself and buying exactly no
  SNR. That average now does real work.
- On a slower box, or with a very short exposure, download costs only whatever render time is left over *after*
  the exposure — not the whole render. What it must never do is go back to a flat ~35 s regardless of exposure
  length; that would mean the prefetch isn't running.
- **For reference:** one 61 MP IMX455 frame at the reporting user's settings renders in **~1.0–1.5 s** on a
  24-core box (Debug build), down from a measured **13.1 s**. The win is parallelism, so a 4–8 core machine
  gives back some of it — expect a few seconds there, not 35.

---

## The headline loop — inject, inspect, turn, re-inspect

**This is the reason the feature exists.** Everything above is scaffolding for it. The capstone proves the
*math* closes; only a human can confirm the loop closes **through the UI** — that what the inspector *prints* is
what the panel *applies*, with no sign flip and no arithmetic in between. A presentation bug sails straight
through an automated round-trip and gets caught only here.

> **Precondition — do this first.** The inspector prints per-screw numbers only when the real adapter is
> calibrated. On a fresh profile it isn't, and **the guidance cells will be blank**. One click fixes it: panel →
> **Adapter configuration** → **Copy to adapter settings…** → **Overwrite**. The badge must read
> **`matches adapter ✓`** before you start (check (n)).

### The tilt loop

1. **Inject.** Camera Sim options: **Enable Aberrations = on**, **Tilt Angle = 30°**, **Tilt Amount = 80 µm**,
   **Backfocus Error = 0**. (The panel's **Injected aberration** expander edits the same three values.)
   The state strip must read `Plane:  80.0 µm @ 30°   ·   Backfocus 0.0 µm` with **no** `✓ ≈ flat`.
2. **Inspect.** Run the **Aberration Inspector**. It should recover ≈80 µm at ≈30° (check (d)) and its guidance
   panel should now print a number in every screw cell.
3. **Read.** Note the **Total** row for screws 1/2/3. With backfocus at 0 the Total and Tilt rows agree.
   With the default rig expect roughly **`0.11 ⟳ · 0.22 ⟲ · 0.11 ⟳`** — two equal turns one way and one about
   double the other way is the signature of a single-axis tilt on an evenly-spaced 3-screw adapter. The exact
   cells follow what the inspector actually measured; don't chase them.
4. **Turn.** For each screw: type the magnitude into **Amount per click** and click **the same glyph the
   inspector printed**. Transcription, not translation — **if you find yourself converting a sign, that is the
   finding**, and it's the whole point of doing this by hand.
5. **Watch.** After each click the state strip's tilt number must **shrink**. If it grows, hit **Undo** and
   re-read the row: a wrong-direction click is the classic tilt-adapter failure this loop exists to catch.
6. **Re-inspect.** Re-run the inspector.
   - **Expect: tilt ≈ 0** — down from 80 µm to ~1 µm or so. The residual is 2-decimal rounding on the guidance
     cells plus the inspector's own measurement error, not a sign problem.
   - **Expect `✓ ≈ flat`** on the state strip once tilt **and** |backfocus| are both **< 1 µm**. One more round
     of guidance → clicks mops up any residual.
   - **Backfocus and Optimal Focuser Position must not move** — a pure-tilt correction on evenly-spaced screws
     sums to zero piston. If backfocus drifts, that's a finding.
   - **Rounds must converge, not oscillate.** Growth or ping-ponging = a sign bug; stop and report.

> **Tip:** if 2-decimal rounding keeps you just above the 1 µm flat threshold, drop the sim's **thread pitch** to
> **100 µm/turn** (then 0.01 turns = 1 µm) and **Copy to adapter settings…** again so the badge stays
> `matches adapter ✓`.

### The backfocus variant — it must null, not double

Worth doing by hand: **the sign here was wrong in an earlier draft and was caught in review.** The wrong sign
doesn't wobble — it *doubles* the error, on both rig directions.

1. **Inject.** **Tilt Amount = 0**, **Backfocus Error = 40 µm**, aberrations on. Strip: `Backfocus +40.0 µm`.
2. **Inspect.** Run the inspector and read its **Backfocus** row. Curvature is rotationally symmetric, so all
   three screws get the **same** cell — with the default rig, ≈ **`0.15 ⟲`**.
3. **Apply.** Type the magnitude, click that glyph once on **each** of Screw 1, 2 and 3. (On a 4-screw rig this
   is one click in **Backfocus** mode instead.)
4. **Expect:**
   - **Backfocus 40.0 → ≈0** (a µm or so of rounding residual), and `✓ ≈ flat` once it's under 1 µm.
   - **Tilt stays 0** — an all-screws move is pure piston.
   - **Optimal Focuser Position drops by a few tens of steps** (5000 → ≈4964 with the defaults). That is
     **correct**: the sensor really moved axially, so best focus moved with it, exactly as it would on a real
     rig. Re-run **Auto Focus** and it should converge on the new position.
- **The failure to watch for: backfocus grows to ≈80 µm — double the injection, not zero.** That is the exact
  bug this check exists for. If you see it, stop and report it.

---

## Other things worth eyeballing

- **Saturation is physical.** At D=100/f/8, gain 100, a mag-10 star saturates in ~6 s; in a 60 s L sub it
  renders as a saturated core with correct unsaturated wings. A ~mag-13 star is the "bright but unsaturated"
  exemplar (peak ≈ 42 kADU).
- **Bias/dark/flat for free.** A 0 s dark should sit at ≈ the pedestal (500 ADU at 16-bit) plus read noise —
  the frames are calibratable.
- **Repeatability is per-sequence, not per-exposure.** Two back-to-back exposures at identical settings are
  **not** bit-identical any more — each one gets its own noise realization, which is what makes averaging
  worthwhile (check (r)). What replays is the *sequence*: reconnect the camera and take the same exposures at the
  same positions with the same **Noise Seed**, and you get the same frames back, because the per-exposure counter
  resets on **Connect**. Still deterministic regardless of CPU core count — the render is parallel, but its
  partition is fixed, so core count never changes a pixel.
- **No ASTAP DB / pointing outside coverage** ⇒ starless frame + a descriptive warning in the NINA log
  (the exposure still succeeds).
- **Undo is single-level and free.** It reverts the last click's tilt, backfocus, focuser position **and** net
  counters. Two clicks back is not undoable — that's deliberate; this is a misclick escape, not an edit history.
- **Re-zero re-bases the counter display only.** It must never move a screw or change the plane: click it and
  the state strip must not flinch. Counters are stored in axial µm, so editing thread pitch mid-session
  **re-scales** the net strip rather than corrupting it — set 500 → 250 µm/turn and the numbers should double.
- **Extreme states stay legal.** Drive tilt past **500 µm** → a passive `extreme tilt — expect heavy donuts`
  badge, no dialog. Past **±10 000 µm** the value clamps and the last-action line appends `(clamped)`.
- **Degenerate config is non-blocking.** Blank the thread pitch or radius → a banner
  (*"Set thread pitch and screw radius to enable the adapter"*), the rows disable, and **Adapter configuration**
  force-expands. Never a crash, never a modal.

## If something looks wrong

The physics is pinned by the automated suite, so a manual-only discrepancy usually means a wiring/config issue:
check the focal length actually in use (profile vs override), the ASTAP path + which cell set is installed,
that you reconnected after changing the sensor, and the NINA log for the camera-simulator warnings.

The same holds for the adapter — the screw math is pinned by the capstone, so a manual-only discrepancy is
almost always wiring or config rather than geometry. In rough order of likelihood:

1. **The badge doesn't say `matches adapter ✓`.** The inspector guides from the *real* adapter settings and the
   simulator obeys the sim's; while they differ the loop cannot converge. Read the badge tooltip — it names the
   fields.
2. **Guidance cells are blank.** The real adapter isn't calibrated. `Copy to adapter settings…` → `Overwrite`.
3. **The panel isn't in the sidebar.** Restart NINA. The button appears only after a restart following the
   plugin update — and the button itself can never be removed (both are NINA limitations; see (l)).
4. **Nothing changes in the rendered frame.** `Enable Aberrations` is off — the panel says so in a banner. The
   plane is still being edited; it just isn't being rendered.
5. **Tilt grows instead of shrinking.** Undo, then re-read the row. Check you clicked the glyph the inspector
   printed rather than its opposite, and that the sim's `⟳ tighten moves adapter toward` matches the real
   adapter's. A genuine wrong-direction bug would show up as the **backfocus doubling** check failing too — if
   that one passes and only tilt diverges, suspect the transcription, not the code.
