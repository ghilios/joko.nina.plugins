# Focuser Direction Convention: measured σ stays authoritative; explicit display-only k

**Status:** design, revised after user review; the layered shape in §2 is user-approved. Open
questions at the end still need answers. Builds on `docs/tilt-adapter-direction-sign-design.md`
(the confirmed measurement inversion, session `20260803-200647`) and
`docs/tilt-guidance-motion-arrows-design.md` (whose §"Two vocabularies" equivalence at line 25 this
doc corrects).

**Questions answered here:** (1) should there be a rig-level toggle configuring the focuser's
direction convention, and should it also change how the sensor model and aberration-inspector
graphics are rendered? (2) If such a toggle were added, should it *replace*
`ScrewInwardCurvatureSign`?

**Answer, in three sentences.** The correction math needs exactly one sign — the measured z-space
response σ — and the sign algebra (§1) shows the focuser convention *cancels out* of its
measurement, so the core fix is an unconditional one-line flip of `ComputeCurvatureSign` with zero
user input. A focuser toggle can never replace σ, because σ is the *product* of two independent
bits, `σ = sign(m)·sign(k)` (adapter mechanics × focuser convention), and the toggle supplies only
one factor. But the convention does **not** cancel out of the *labels* — every caption that renders
z-space into physical words currently pins `k = +1` silently — so the approved design layers them:
**σ stays measured and authoritative for all motion; `k` is added as a persisted, display-only
setting that makes the captions and the mechanical wording true instead of assumed.** The central
guarantee: a wrong `k` produces wrong labels and never wrong motion — a bounded, visible failure
instead of a silent inverted one.

---

## 1. Definitions and the sign algebra, written out

Everything below is derived from five quantities. Signs are the whole point, so nothing is asserted
without derivation.

| Symbol | Meaning | Sign convention | Property of |
|---|---|---|---|
| `P` | focuser position, in µm-equivalents | as reported by the driver | — |
| `k` | focuser convention: sensor's objective-distance is `D = S₀ + k·P + b` | `k = +1` standard (increasing P moves camera **away** from objective), `k = −1` inverted | the **focuser** (rig/profile) |
| `b` | adapter spacing contribution, µm | larger `b` = sensor **farther** from objective (toward the camera side) | — |
| `m` | plate response to a CW/+ turn: `db/dr = m` | `m > 0` ⇔ CW/+ moves the plate toward the **camera** | the **adapter** |
| `q_E` | optical response of the field-curvature mismatch to spacing: `∂(f_c,edge − f_c,center)/∂b` | pure optics — no focuser involved | the optics |

The optical focal surface sits at objective-distance `F₀ + f_c(field, b)`; a field point is in focus
when `D = F₀ + f_c`, so the fitted best-focus surface (the sensor model's z-axis, in focuser units) is:

```
z(field) = (F₀ + f_c(field, b) − S₀ − b) / k
```

Four consequences, each used below:

**(a) Toward the objective ⇔ best-focus position rises (standard k).**
`∂z/∂b = (q − 1)/k` where `q = ∂f_c/∂b` locally; the geometric term dominates (`|q| ≪ 1`, checked
against the session in §1.1), so `∂z/∂b ≈ −1/k`. For `k = +1`: `b` down (toward objective) ⇒ `z` up.
This is the corrected equivalence — `docs/tilt-guidance-motion-arrows-design.md:25` states the
inverse ("toward the objective ⇔ the local best-focus position decreases") and is wrong prose.

**(b) The stored sign σ is the product of two independent bits: `σ = sign(m)·sign(k)`.**
`ScrewInwardCurvatureSign` is *defined* (TiltScrewGeometry.cs:140–150) as the sign of the
curvature-effect response to a CW turn, where the curvature effect `E` is the **fitted** (z-space)
quantity `CurvatureAt(...)`. Since `E_z = (f_c,edge − f_c,center)/k`:

```
dE_z/dr = q_E · m / k      ⇒      σ = sign(q_E) · sign(m/k) = sign(m) · sign(k)
```

using `q_E > 0`, fixed by the empirical anchor (2026-07-02, measured on a standard-`k` rig: "moving
the adapter toward the objective DECREASES the curvature effect", i.e. `Δb < 0 ⇒ ΔE_z < 0` with
`k = +1`; `q_E` is optics, independent of the focuser). This factorization is the load-bearing fact
of the whole design: σ fuses the adapter bit and the focuser bit into one number, and only the
product is observable from z-space.

**(c) The all-inward probe measures `−σ`, on every rig — the measurement needs no user input.**
The 6-step wizard's a→b step turns every screw CW/+ by `r`: pure piston, `Δb = m·r`, so

```
Δz̄ = −m·r·(1 − q̄)/k      ⇒      sign(Δz̄) = −sign(m)·sign(k) = −σ        (for q̄ < 1)
```

`m` and `k` enter both σ and the probe only through their product, so the focuser convention
**cancels for the math**. The correct measurement is therefore unconditional:

```
σ = −sign(allScrewsMean − baselineMean)
```

`ComputeCurvatureSign` (TiltCalibrationCalculator.cs:183–185) currently returns
`sign(allScrewsMean − baselineMean)` = `−σ`: inverted on **every** rig, not just standard ones. This
kills hypothesis 2 of the prior doc ("this rig's focuser is inverted; the code is right in
general"): there is no focuser convention under which the current formula measures the sign the
consumers need, because the consumers (§1.2) consume σ in the same z-space the probe reads.

**(d) Mechanical wording needs `k`; nothing computed does.**
Inverting (b): `sign(m) = σ · sign(k)`. So translating σ into "CW moves the plate toward the
camera/objective" words requires knowing `k` — the convention cancels for the math and does **not**
cancel for the labels. The anchor constant `CurvatureSignWhenCwMovesAdapterTowardObjective = −1`
(TiltScrewGeometry.cs:150) *is* that translation, silently pinned at `k = +1` (from (b): CW toward
objective ⇒ `m < 0`, `k = +1` ⇒ `σ = −1` ✓). The constant stays as is — it is empirically anchored,
its pinned test stays, and under the layered design the presentation layer generalizes it by
looking up `σ·sign(k)` instead of `σ` (§2.2), which reduces to today's behavior at the default
`k = +1`.

### 1.1 Check against the observed session (20260803-200647)

- `bf,+150` (vendor mnemonic: increase backfocus = plate toward **camera**, `m > 0`) on a standard
  focuser (`k = +1`, NiteCrawler: 0 = fully racked = closest to objective) moved `Z0`
  5498.4 → 5136.6. Predicted `sign(Δz̄) = −sign(m)·sign(k) = −1` ✓ observed (−361.8 steps).
- Corrected measurement: `σ = −sign(−361.8) = +1`, giving `m = σ·k = +1` = "toward the camera" —
  exactly the manual setting that made corrections converge ✓.
- Magnitude sanity: 150 EAT steps and 361.8 focuser steps both correspond to ≈ 96 µm on this rig's
  step sizes — the geometric `−m/k` term at ~1:1, confirming `|q̄| ≪ 1` (the mean-focus probe is
  dominated by geometry, not optics). Caveat recorded: an optical train with `q̄ > 1` (mean focal
  shift responding *more* than µm-for-µm to spacing) would defeat any mean-focus-based direction
  probe; no such system is known, and the probe's large observed SNR is itself a per-run check.
- **Why not measure σ from the curvature-effect change directly** (it is, after all, the
  definition)? The session shows why: `Kx` slid monotonically −1.33e−7 → −2.10e−7 across *all six*
  steps — including pure single-screw tilt moves and re-baselines that changed no spacing — so the
  a→b curvature change (−401 → −442 µm at screw radius) is dominated by secular drift
  (temperature/seeing/fit coupling), not the probe. The mean-focus signal (362 steps against
  re-baseline drifts of tens) is the robust channel. The prior doc's observation that the two
  channels "agree by construction" dissolves under the corrected sign: they are now *independent*
  (one geometric, one optical) — which enables the cross-check in §2.4, but only as a warning,
  since the optical channel is usually drift-buried.

### 1.2 Why every consumer is already in the cancelling frame (verified, no changes needed)

- `TiltScrewTargets.ComputePerScrewTargets` (TiltScrewTargets.cs:72–86): backfocus rotation
  `= σ·(−E_r)/unit`. Required rotation from (b): `r = −E_z/(dE_z/dr) ⇒ sign(r) = −σ·sign(E_z)` ✓
  identical. `k`-free.
- `TiltScrewGeometry.SignedTotalAdjustment` (TiltScrewGeometry.cs:115–119): same σ-on-backfocus-only
  convention ✓.
- Tilt turns (InspectorVM.cs:2064–2068 and the calibration that feeds them): rotation→gradient
  response measured directly through the same focuser during calibration; target and measurement
  both in z-space, `k` and `m` cancel ✓.
- Automatic Adjustment planner (InspectorVM.cs:2418–2424): resolves σ exactly like the guidance
  table and feeds the same `TiltScrewTargets` ✓.
- The wizard's device-driven runs: device "+" *is* the CW of the response frame because the screw
  angles and σ are measured from device-"+" moves themselves ✓.

This is why "only the MEASUREMENT is inverted" holds exactly: the consumption graph is closed in
z-space; the single place a z-space quantity was translated with a physical-space assumption is
`ComputeCurvatureSign`.

---

## 2. The layered design (approved)

### 2.1 Why the toggle cannot replace σ — and why σ must not be derived from user-supplied bits

The user asked whether a focuser-direction toggle could *replace* `ScrewInwardCurvatureSign`. It
cannot, and the reason is §1(b): `σ = sign(m)·sign(k)` is the product of two independent bits, and
the toggle supplies only `k`. Two rigs with opposite adapters *and* opposite focusers produce the
same σ and are indistinguishable to the correction math — which is fine, because σ alone is what
the math needs.

The instinct behind the question is still pointing at a real defect. The existing user-facing
direction setting — `TiltAdapterWizardVM.CwMovesAdapterTowardObjective` (:1066–1080), the combo at
`TiltAdapterWizard/DataTemplates.xaml:711`, described in code as the "Mechanical framing of
ScrewInwardCurvatureSign" — asks the user a question about `m` but **stores σ**, with the
`m ↔ σ` conversion silently pinned at `k = +1` through the anchor dictionary. The setting as it
exists today is already `m` and `k` fused into one bit with the focuser half assumed; that fusion
is the source of the opacity the user noticed.

The tempting fix — store `m` and `k` as two user-settable bits and *derive* `σ = m·k` — is
rejected outright, and this is the decisive safety argument: post-fix, σ is **measured** with zero
user input and is correct on any focuser (§1(c)). Replacing one measured quantity with two
user-entered conventions, where *either* being wrong inverts σ and drives unattended correction
backwards, is a net safety regression in exactly the dimension this whole investigation is about.
Direction conventions are precisely the thing humans get wrong — that is how this bug was born.

### 2.2 The approved shape: measured σ for motion, explicit k for words

Layer the two quantities, keeping `k` strictly non-load-bearing:

- **MATH — unchanged, zero user input.** σ stays measured and authoritative. The unconditional
  one-line flip of `ComputeCurvatureSign` (§1(c)) is the core fix. `k` never enters the correction
  math, the planner, the wizard's measurement, or the simulator.
- **PRESENTATION — `k` becomes an explicit, persisted, display-only setting** (default: standard).
  It drives exactly the caption sites enumerated in §3, and it lets the UI display the *true*
  mechanical direction as `m = σ·k` instead of asserting it under a hidden assumption. Concretely,
  every presentation-side use of the anchor dictionary changes from
  `CwMovesAdapterTowardObjectiveForSign(σ)` to `CwMovesAdapterTowardObjectiveForSign(σ·sign(k))`,
  the tilt motion arrows become `−σ·sign(k)·turns` (InspectorVM.cs:2078), and the backfocus motion
  arrow becomes `⬆ ⇔ sign(k)·E_z > 0` (InspectorVM.cs:2104–2110) — all reducing bit-for-bit to
  today's behavior at the default `k = +1`.

**The central guarantee, and the design's acceptance criterion: a wrong `k` produces wrong LABELS
and never wrong MOTION.** A user who sets `k` backwards sees captions and arrows that contradict
what their hands and their aberration numbers tell them — a bounded, visible, self-correcting
failure. It can never re-create the silent inverted-automation failure, because no computed motion
reads `k`.

**Structural enforcement, not convention.** Two mechanisms:

1. *Placement.* `k` lives on `IInspectorOptions` (it is a focuser/rig property, like
   `MicronsPerFocuserStep` — and deliberately **not** on `ITiltAdapterOptions`, whose σ it would
   otherwise sit next to and get fused with again). The pure math layer
   (`TiltCalibrationCalculator`, `TiltScrewGeometry`, `TiltScrewTargets`) takes no options object
   and gains no `k` parameter on any computing function; the only new σ·k composition points are
   presentation call sites. Any future attempt to thread `k` into a math signature is visible in
   review.
2. *Pinned invariance tests.* New guard tests assert that with `k` toggled both ways and identical
   inputs: the measured σ from a seeded 6-step run is identical; the numeric guidance rows, glyphs
   and totals are identical; the Automatic Adjustment move plan is identical; only
   arrows/captions/wording differ. (Plan step 6.)

**Option shape:** `IInspectorOptions.FocuserIncreasesTowardObjective`, `bool`, default `false`
(= standard convention, `sign(k) = +1`), persisted per profile via the standard accessor pattern.
Per the project invariant it gets a UI control in `Resources/OptionsDataTemplates.xaml` (a two-item
ComboBox, "Increasing focuser position: Moves camera away from objective (standard) / Moves camera
toward objective (reversed)", next to the `MicronsPerFocuserStep` control, with a
`FocuserIncreasesTowardObjective_Tooltip` resource stating explicitly that it affects direction
labels and diagrams only and can never change a measurement or a correction).

### 2.3 The one deliberate exception: the manual direction combo (DECIDED — accepted 2026-08-04)

The wizard's direction combo is an *input* for the assumed-σ path (when no measurement exists).
Under the layered design its getter shows `m = σ·k`; for the combo to round-trip coherently its
setter must store `σ = m·sign(k)` — which makes `k` touch the **assumed** σ. This is the one place
the "k never influences motion" rule is bent, and it is bent deliberately:

- It only writes the *assumed* σ, which every guidance surface already flags "(assumed)" and any
  6-step measurement overwrites.
- At the default `k = +1` it is bit-identical to today's behavior; on a genuinely reversed rig it
  makes the manual path *correct* where today it is silently wrong (today's pinned `k = +1`
  conversion would store an inverted σ for an honest answer about `m`).
- Without it the UI is self-contradictory on reversed rigs: the user selects "toward the
  objective" and the readback (computed as `σ·k`) immediately displays "toward the camera".

**Decision (user, 2026-08-04): accept the bounded exception.** The combo keeps its mechanical
wording; the setter stores `σ = m·sign(k)`.

Two alternatives were considered and rejected:

- *Reword the combo to the convention-free z-space question* ("turning all screws clockwise moves
  the best-focus position: lower (typical) / higher"), keeping `k` perfectly non-load-bearing.
  Coherent, and the only option that preserves the guarantee absolutely — but it asks users exactly
  the kind of frame-dependent question this design otherwise eliminates.
- *Gate manual entry on an explicitly confirmed `k`*, so `k` is never silently defaulted on a path
  that reaches motion. Preserves the guarantee in substance while keeping the intuitive wording, at
  the cost of an extra confirmation step for users who skip the wizard.

The accepted residual risk, stated plainly so it is not rediscovered later: a user who sets `k`
wrong **and** sets direction by hand instead of running the 6-step wizard gets inverted motion. The
exposure is narrow — the measurement overwrites the assumed σ, the default `k` is bit-identical to
today, and a reversed rig is *better* off than today — but it is a real hole in the "wrong `k`
never causes wrong motion" guarantee, and it is the only one. Implementations must not widen it.

### 2.4 Retained from the prior revision (unchanged verdicts)

- **Auto-derivation from NINA:** impossible; NINA/ASCOM expose no "which way is out". `k` is
  therefore a user-set bit — acceptable *because* it is display-only.
- **A separate probe move to establish `k`:** rejected; `k` is consumed only by captions, not worth
  an automated slew. (The 6-step a→b step remains the probe for σ and needs no `k`.)
- **Confirmation prompt at calibration end (prior doc's C):** no modal; the fix is correct by
  construction, and the measured direction is already surfaced in the log
  (TiltAdapterWizardVM.cs:2651–2655) and the "measured" provenance.
- **Cross-check, warning-only (prior doc's D):** adopt optionally (Q3). When the a→b
  curvature-effect change is large relative to the re-baseline drifts *and* its sign contradicts
  the measured σ, warn in the wizard's confidence panel. Never block: the optical channel is
  usually drift-buried (§1.1) and must not veto the robust one.

**Fail-safe position:** the measurement is parameter-free; the assumed-σ default (`+1`) and its
"(assumed)" labelling are unchanged; `k` defaults to standard, and being wrong about it is visible
in captions and correctable at any time with no effect on stored calibrations or motion.

---

## 3. Rendering impact — investigated, with file:line

The question: do the sensor model and inspector graphics depend on the focuser direction
convention? Under the layered design this section doubles as the enumeration of `k`'s consumers:
**these sites, and nothing else, read `k`.**

**The fit and every computed number: no.** The sensor model is fit in pure z-space (best-focus
focuser position vs. sensor x/y — `Inspection/SensorParaboloidModel.cs:93`, solver at :330–450);
no physical-direction input exists anywhere in the pipeline. Invariant surfaces, verified:

- Contour/heat map *values, shape, colors* — `Controls/SensorModelContourMapControl.cs:142–213`
  plots `ValueAt − SensorMeanElevation` with z-axis label "Offset (microns)"; no directional claim
  in the control itself.
- Tilt tables' numbers (Position / Adj Steps / Adj Microns) — `AutoFocus/TiltModel.cs:55–74`
  (`AdjustmentRequiredSteps = corner − mean`, raw focuser units).
- Tilt history and "Backfocus Steps" — `AutoFocus/DataTemplates.xaml:3737–3792`,
  `InspectorVM.cs:1671` (raw focuser delta).
- Numeric guidance rows, ⟳/⟲ glyphs, signed stepper steps, totals — `TiltScrewTargets.cs`,
  `TiltScrewGuidanceRow.cs:103–123` (σ-frame, §1.2).
- Wizard saved-calibration "Curvature ↑/↓ (measured)" — `TiltAdapterWizardVM.cs:694–701`
  (explicitly the curvature-effect direction, z-space by definition).
- FWHM/eccentricity maps — `Controls/FWHMContourControl.cs` (arcsec/px, direction-free).
- Automatic Adjustment plan/prompt — `InspectorVM.cs:2418–2424` and downstream (σ-frame).

**The captions that translate z into physics: yes — six sites, all currently hardcoding
`k = +1`.** For each, the standard-convention reading is *correct* under the §1 algebra — i.e.
these labels independently corroborate that `ComputeCurvatureSign` was the outlier that assumed the
inverted equivalence. Under the layered design each becomes `k`-driven and thereby **correct on
every rig** rather than correct-by-assumption (this supersedes the previous revision's open
question about adding "assumes standard focuser" caveat text — with an explicit `k` the captions
become correct, so no caveat is needed):

1. `AutoFocus/DataTemplates.xaml:3638` and `:3837` — "Positive values = Move away from objective"
   (both tilt-corner tables). Positive `Adj Steps` ⇔ corner best-focus above mean ⇔ (§1(a)) corner
   sits closer to the objective **iff `k = +1`**. Becomes: "Move away from" / "Move toward"
   selected by `k`.
2. `AutoFocus/DataTemplates.xaml:3265` — "Telescope is up, Sensor is down" caption over the
   sensor-model surface/contour view. Up = higher best-focus ⇔ closer to the telescope iff
   `k = +1`. Caption text selected by `k`.
3. `Controls/TiltModelControl.cs:142–143` — `ScreenLabel("Telescope", …)` / `ScreenLabel("Sensor", …)`
   on the 3D tilt visualization (z = focuser delta, :97). Same claim as (2); labels swap with `k`
   (new dependency property bound from the options).
4. `AutoFocus/InspectorVM.cs:1671–1676` + `AutoFocus/DataTemplates.xaml:2859–2861` — "Backfocus
   Error … (Move sensor TOWARDS/AWAY FROM flattener)". Outer above inner ⇒ `E_z > 0` ⇒ (`q_E > 0`)
   reduce spacing **iff `k = +1`**. The TOWARDS/AWAY FROM selection becomes `k`-aware.
5. `Inspection/SensorModelAberrationResult.cs:393–401` — "Try REMOVING/ADDING spacers"
   (`E_z > 0 ⇒ REMOVING`). Same physics as (4); the direction word becomes `k`-aware (a focuser
   sign parameter threaded into `Update(...)`).
6. The mechanical vocabulary of the guidance/wizard, all routed through the anchor dictionary
   (`TiltScrewGeometry.cs:150–167`): tilt motion arrows `InspectorVM.cs:2078`
   (`−σ·turns` → `−σ·sign(k)·turns`), backfocus motion arrow `:2104–2110`
   (`E_z > 0` → `sign(k)·E_z > 0`), legend text `TiltScrewGuidanceRow.cs:88–90` (text unchanged —
   "⬆ = adapter moves toward the objective" remains the definition; the *selection* of ⬆ vs ⬇ is
   what becomes `k`-aware), wizard direction combo
   `TiltAdapterWizard/DataTemplates.xaml:711–732` + `TiltAdapterWizardVM.cs:1066–1080` (getter
   `m = σ·k`; setter per §2.3), and the calibration log's OBJECTIVE/CAMERA verdict
   `TiltAdapterWizardVM.cs:2654–2655` (keep printing raw σ; the mechanical word uses `σ·k` and says
   so). Given the default `k = +1`, every one of these renders exactly as today.

**Prose that must be fixed regardless (it is wrong, not merely scoped):**
- `docs/tilt-guidance-motion-arrows-design.md:25` — the inverted equivalence; re-justify the arrow
  formulas from §1(d): a CW/+ turn moves the plate toward the camera iff `σ·k = +1`, hence
  motion-toward-objective is `−σ·sign(k)·turns` for tilt and `sign(k)·E_z > 0` for backfocus, with
  the shipped `k = +1` default reproducing the current formulas.
- `.claude/docs/tilt-domain.md:26` — "pure physics, identical on every rig" overstates: identical
  given the focuser-direction setting (previously: given the silent standard assumption).
- `documentation/docs/overview/tilt-adapter-wizard.md:58–62` — says the 6-step run determines the
  sign "from the curvature change"; it is measured from the mean best-focus change (and after this
  fix, with the corrected sense). Also document the new focuser-direction setting and that the
  direction combo now reflects it.
- `documentation/docs/overview/tilt-aberration-inspector.md:111–112` — "the same on every rig",
  same rewording as tilt-domain.md.

---

## 4. The camera simulator must flip with the measurement (unchanged finding)

`SimulatedTiltInjection.Fold` (CameraSimulator/TiltAdapter/SimulatedTiltInjection.cs:72–93) folds an
all-screws piston into **two** responses:

- `OptimalFocuserPosition += pistonMicrons/step` (:77) — the geometric mean-focus shift. With
  `pistonMicrons = σ·δ` for a CW move, the sim's mean focus moves by `+σ·δ`; real physics (§1(c))
  moves it by `−σ·δ`. The sim was built to satisfy the buggy measurement and **must flip this line**
  (to `−pistonMicrons`) in the same change, or a simulated 6-step run will measure `−σ_config`
  after the calculator fix.
- `BackfocusErrorMicrons += pistonMicrons·(hw²+hh²)/R²` (:86–91) — the curvature-effect response,
  `+σ·δ` per the definition of σ. **Correct as is** (`q_E > 0` behavior); unchanged.

The sim is pure z-space and never reads `k` — that stays true under the layered design (the sim is
on the "math" side of the wall). The existing sim tests only pin the *antisymmetry* of the focuser
shift across σ (`SimulatedTiltAdapterVMTests.BackfocusMove_OnOppositeRigs_ShiftsTheFocuserInOppositeDirections`,
:210–228), so the flip keeps them green; the absolute direction was never pinned — a gap this change
closes with a new test. The curvature-response pins
(`BackfocusMove_ChangesBackfocusErrorWithTheSignOfTheRigDirection`, :172–182 and
`…OnOppositeRigs_MovesBackfocusInOppositeDirections`, :188–207) are unaffected.

---

## 5. Migration (unchanged finding)

Profiles where `ScrewInwardCurvatureSignIsMeasured == true` hold a deterministically inverted value:
every measured σ ever written came from the inverted formula, and any user who *manually* corrected
the direction afterwards (as this user did) went through the `CwMovesAdapterTowardObjective` setter,
which clears `IsMeasured` (TiltAdapterWizardVM.cs:1074–1077). Therefore:

- **Auto-negate σ once** for profiles with `IsMeasured == true`, guarded by a new persisted one-time
  marker so it can never double-apply; log at Info and raise a one-time notification naming the
  profile and both values.
- `IsMeasured == false` (assumed or manually set) — **untouched**: the stored value is the user's
  or the default's, not the buggy formula's.
- The new `k` option needs no migration: it is introduced at its default (standard), which
  reproduces today's rendering everywhere.
- Replays: `TiltCalibrationMetadata` stores raw step readings and replays recompute through
  `RunCalibrationMath` (TiltAdapterWizardVM.cs:3106/3117), so replaying an old saved run after the
  fix yields the *correct* sign with no metadata change.
- Known cosmetic side effect: the wizard's displayed "physical" screw angles
  (`PhysicalToStoredAngle`, consumed at TiltAdapterWizardVM.cs:537–540) rotate 180° for migrated
  profiles, because the display converts stored angles with the now-flipped σ. Guidance and
  automation never consume physical angles, so this is display-only — but see Q2.
- Downgrade hazard (accepted, documented): a migrated profile re-measured on an *old* plugin
  version gets the inverted sign again; nothing in this design can defend against running old code.

The migration marker is persisted internal state, not a user option — same category as
`TiltDeviceShadowPositions` / `DeviceLinkedCalibrationDeviceName` / `CalibrationIsReliable`, none of
which have (or should have) a control in `Resources/OptionsDataTemplates.xaml`. The one user-facing
persisted option this design adds — `FocuserIncreasesTowardObjective` — carries the full invariant:
control plus tooltip in `Resources/OptionsDataTemplates.xaml` (§2.2, plan step 5).

---

## 6. Open questions for the user

- **Q1 — RESOLVED (user, 2026-08-04).** The manual direction combo's `k`-aware round-trip is
  accepted as a bounded exception: the combo keeps its mechanical wording and its setter stores
  `σ = m·sign(k)`, touching only the *assumed* σ. See §2.3 for the rejected alternatives and the
  accepted residual risk.
- **Q2 — `PhysicalToStoredAngle` absolute offset** (TiltScrewGeometry.cs:169–180): its 0°-vs-180°
  assignment per σ was justified with the same family of reasoning as the inverted equivalence.
  Checked against §1: the stored (response-frame) angle is the direction of local z-increase for a
  CW turn, which works out to `θ_physical + Δ_mech + (180° iff σ = +1)` — a function of **σ and the
  adapter's lever mechanics `Δ_mech`**, with `k` entering only through σ. So, contrary to first
  appearances, storing `k` does **not** settle this: the unverified bit is the lever mechanics
  (does tightening a screw at θ locally raise or lower the plate at θ — the tilt-domain doc's
  pivot description suggests it may flip), not the focuser. What the explicit `k` buys is precise
  mechanical wording around the setting once the mechanics bit is verified. Wizard-measured
  calibrations are immune (angles measured directly); only **Manual Calibration Entry** and the
  physical-angle *display* consume it. Decisive check, no code: on your calibrated rig, do the
  wizard's displayed physical screw angles match where the screws actually sit? If yes, leave it;
  if they read 180° off, that is a separate one-line fix plus its own migration question. This
  design deliberately does not touch it.
- **Q3 — RESOLVED (user, 2026-08-04): adopt it.** The warning-only σ-vs-curvature-change
  disagreement check goes in the wizard's confidence panel (§2.4), not log-only. It is a required
  step of the plan, not an optional one. Warning-only is the point: it must never gate or alter the
  measured σ, only surface a disagreement for the user to judge. Note the known weakness recorded
  in §2.4 — the check is often drift-buried (the reference session's `Kx` slid monotonically across
  all six steps), so a quiet panel is weak evidence of agreement, not proof of it.
