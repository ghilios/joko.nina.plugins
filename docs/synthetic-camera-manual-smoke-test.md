# Synthetic Star-Field Camera — Manual Smoke Test (Windows / NINA)

Executes step 23 of `plans/synthetic-camera-plan.md`. Everything else in the plan is covered by the automated
suite (1956 tests, incl. two capstones that render frames and recover star positions/HFR and the injected
aberration surface through HocusFocus's own detector). This checklist covers what only a human at a running
NINA can confirm: that the device shows up, connects, and behaves correctly in the real app.

## Prerequisites

1. **Build + install the plugin.** `dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug` — the
   project's PostBuild step copies the assembly + dependencies to `%localappdata%\NINA\Plugins\3.0.0\Hocus Focus`.
   Restart NINA.
2. **ASTAP star database** (optional but needed for stars). Install an ASTAP DB (`.1476` or `.290` cell files,
   e.g. the `g17`/`h17`/`h18` Gaia sets) — by default under `C:\Program Files\astap`. Without it the camera
   still works: it renders a **starless** frame (sky+dark+noise) and logs a warning naming the missing path.
3. **Simulator devices**: NINA's simulator **focuser** and simulator **telescope/mount**.

## Setup

- **Options → Hocus Focus → Camera Sim** tab. Defaults: IMX455, filter L, aperture 100 mm, focal length 0
  (⇒ uses the profile's `TelescopeSettings.FocalLength`), obstruction on @ 0.3, throughput 0.85, gain 100,
  pedestal 500 ADU, −10 °C, sky 20.5 mag/arcsec², seeing 2.5″, ASTAP path, limiting mag 16, rotation 0,
  noise seed 42, aberrations off.
- Set the profile's telescope focal length to **800 mm** (or set the Focal Length override) so the numbers below apply.
- **Equipment → Camera** → choose **"Hocus Focus Simulator"** → Connect.
- Connect the simulator **Focuser** and **Mount**. Slew the mount to a star-rich field (e.g. near the galactic
  plane) so the catalog returns stars.

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

## Other things worth eyeballing

- **Saturation is physical.** At D=100/f/8, gain 100, a mag-10 star saturates in ~6 s; in a 60 s L sub it
  renders as a saturated core with correct unsaturated wings. A ~mag-13 star is the "bright but unsaturated"
  exemplar (peak ≈ 42 kADU).
- **Bias/dark/flat for free.** A 0 s dark should sit at ≈ the pedestal (500 ADU at 16-bit) plus read noise —
  the frames are calibratable.
- **Repeatability.** With a fixed **Noise Seed** and identical settings, two exposures are bit-identical
  (deterministic regardless of CPU core count).
- **No ASTAP DB / pointing outside coverage** ⇒ starless frame + a descriptive warning in the NINA log
  (the exposure still succeeds).

## If something looks wrong

The physics is pinned by the automated suite, so a manual-only discrepancy usually means a wiring/config issue:
check the focal length actually in use (profile vs override), the ASTAP path + which cell set is installed,
that you reconnected after changing the sensor, and the NINA log for the camera-simulator warnings.
