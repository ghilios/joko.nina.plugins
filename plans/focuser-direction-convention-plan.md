# Focuser Direction Convention / Curvature-Sign Measurement Fix — Implementation Plan

Executes `docs/focuser-direction-convention-design.md` (revised, user-approved layered shape):
**measured σ stays authoritative for all motion; a new display-only focuser-direction setting `k`
drives the physical-direction captions and mechanical wording.** The change is (1) the
unconditional measurement flip, (2) the matching camera-simulator piston flip, (3) a one-time
migration for measured-σ profiles, (4) the new `k` option + its presentation consumers +
invariance guards, (5) prose/doc corrections, (6) required warning-only cross-check (resolved question
Q3). Steps are ordered; each step ends green
(`dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`).

**Persisted options touched:**
- `IInspectorOptions.FocuserIncreasesTowardObjective` (new, user-facing, bool, default `false` =
  standard convention). **Per the project invariant it gets a UI control plus tooltip resource in
  `Resources/OptionsDataTemplates.xaml`** (step 5). Display-only by design: it must never be read
  by `TiltCalibrationCalculator`, `TiltScrewGeometry`, `TiltScrewTargets`, the Automatic
  Adjustment planner's target computation, or the camera simulator.
- One new persisted *internal-state* key (the migration marker, step 4). Not a user option — same
  category as `TiltDeviceShadowPositions` / `DeviceLinkedCalibrationDeviceName` /
  `CalibrationIsReliable`, which have no `OptionsDataTemplates.xaml` controls — so no UI control.

**Tests that must NOT change** (they pin the anchor and the σ-frame consumers, which this fix
deliberately leaves alone; everything here runs at the default `k = +1`, where all presentation
output is bit-identical to today):
- `TiltScrewGeometryTests.CurvatureSignForCwDirection_MatchesEmpiricalAnchor` (anchor pinned).
- `InspectorVMBehavioralTests.TiltGuidance_SigmaFlip_FlipsMotionArrowsNotTiltGlyphs` and the rest of
  the σ-flip matrix.
- `TiltScrewTargetsTests`, `SignedTotalAdjustment` tests (σ-on-backfocus-only convention).
- `SimulatedTiltAdapterVMTests.BackfocusMove_ChangesBackfocusErrorWithTheSignOfTheRigDirection` and
  `…OnOppositeRigs_MovesBackfocusInOppositeDirections` (curvature-effect response = +σ, unchanged).
- `SimulatedTiltAdapterVMTests.BackfocusMove_OnOppositeRigs_ShiftsTheFocuserInOppositeDirections`
  (pins antisymmetry only; stays green across step 3's flip).

---

## Step 1 — Flip the measurement

**File:** `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltCalibrationCalculator.cs:183–185`

Change `ComputeCurvatureSign` to return the corrected sense (all-inward CW/+ *lowering* the mean
best-focus position ⇒ σ = +1), keeping the signature and the tie-breaking direction (a zero delta
still yields +1, matching today's degenerate behavior):

- from: `return (allScrewsMean - baselineMean) >= 0 ? 1 : -1;`
- to:   `return (allScrewsMean - baselineMean) <= 0 ? 1 : -1;`

Replace the one-line doc comment with the derivation summary (design §1): σ is defined as the
fitted curvature-effect response to a CW turn; `σ = sign(m)·sign(k)`, the probe measures
`sign(Δz̄) = −sign(m)·sign(k)`; the focuser convention cancels, so the flip is correct on standard
and inverted focusers alike, with zero user input; cite the design doc and session
`20260803-200647`. Do **not** swap the argument order to achieve the flip (booby trap), and do not
touch the callers — `RunCalibrationMath` (TiltAdapterWizardVM.cs:2644) and `Calibrate`
(TiltCalibrationCalculator.cs:326–328) inherit the fix; the log line at
TiltAdapterWizardVM.cs:2651–2655 already prints the raw Δ plus the verdict (its mechanical wording
becomes `k`-aware later, in step 6).

**Tests (same commit):**
- `Tests/TiltAdapterWizard/TiltCalibrationCalculatorTests.cs:99–103` — rename
  `ComputeCurvatureSign_PositiveWhenAllScrewsMeanHigher` to
  `ComputeCurvatureSign_PositiveWhenAllScrewsMeanLower`; expectations become
  `(1100, 1000) → −1`, `(900, 1000) → +1`, `(1000, 1000) → +1`.
- `:264` and `:306` (`Calibrate…RecoversAnglesPitchAndSign` fixtures, AllInward mean 1075 > baseline
  1000): expectation `CurvatureSign == −1`, and update the inline comment at :289 ("raised mean
  focus -> +1" is now "-> −1").
- `:411/:426` (fallback path, 4-step) — unchanged (fallback is the configured sign, untouched).
- **Add** a session-regression test pinning the incident:
  `ComputeCurvatureSign_Session20260803_BaselineHigherMeansTowardCamera` —
  `ComputeCurvatureSign(5136.6, 5498.4) == +1`, with a comment that +1 reads "toward the camera"
  on a standard focuser (`m = σ·k`) and is the value that made corrections converge.

## Step 2 — Wizard VM tests that seed six-step runs

**File:** `Tests/TiltAdapterWizard/TiltAdapterWizardVMTests.cs`

- `:1165–1184 CompletingSixStepRun_WritesMeasuredCurvatureSignFromMeans` — AllInward 990 < baseline
  1000 now yields **+1**; update the received-assignment assertion (`:1184`) and the comment
  (`:1169`).
- `:1145` (the other seeded run, AllInward 1010): update any sign assertion accordingly (baseline
  1000 → 1010 rise now yields −1).
- `:1207–1208` (4-step leaves sign untouched) — unchanged.
- Sweep the two wizard test files for any other assertion coupling a seeded mean-focus delta to a
  stored sign (`grep -n "SeedStepReading\|ScrewInwardCurvatureSign = " Tests/TiltAdapterWizard/*.cs`).

## Step 3 — Camera simulator: flip the geometric piston, pin the absolute direction

**File:** `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/TiltAdapter/SimulatedTiltInjection.cs`

- `:77` — `options.OptimalFocuserPosition += (int)Math.Round(pistonMicrons / …)` becomes
  `-= …` (equivalently `+= −pistonMicrons/…`): a plate move toward the camera must *lower* the mean
  best-focus position (design §1(c)/§4). `:86–91` (`BackfocusErrorMicrons`, the curvature-effect
  response `+σ·δ`) is **unchanged**.
- Rewrite the sign-critical comments to the corrected physics: the block at `:41–54` ("THE SIGN,
  stated once") and `:74–85` (the OptimalFocuserPosition/backfocus rationale), and the
  `PistonDirectionSign` remarks in `SimulatedTiltAdapter.cs:59–60` and `:91–99` (the constant and
  its value are unchanged; only the explanation of where the geometric shift's extra minus lives
  moves into `Fold`). The simulator remains `k`-free (design §4): it must not gain a
  focuser-direction knob in this change.

**Tests:**
- **Add** `SimulatedTiltAdapterVMTests.BackfocusMove_CwOnPositiveRig_LowersOptimalFocuserPosition`
  (σ = +1, `TurnCommand(⟳)` ⇒ `OptimalFocuserPosition < start`) — the absolute-direction pin that
  was missing; note in its doc comment that it is what makes a simulated 6-step run measure
  σ correctly post-fix.
- Confirm `BackfocusMove_OnOppositeRigs_ShiftsTheFocuserInOppositeDirections` (antisymmetry) and
  the convergence tests (`InspectorBackfocusGuidance_…`) stay green — they must, per design §4; if
  any fails, stop and re-derive rather than adjusting the test.
- Sweep `Tests/CameraSimulator/SimulatedTiltActuatorTests.cs` and
  `SimulatedTiltAdapterCapstoneTests.cs` for assertions on `OptimalFocuserPosition` direction
  (none found in the survey; the capstone's `PistonDirectionSign * delta.PistonMicrons` assertion
  at `:96` concerns the adapter's response frame and is unchanged).

## Step 4 — One-time migration of measured signs

**File:** `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltAdapterOptions.cs`

In `InitializeOptions()` (runs at construction and on every profile change, so it is per-profile),
after reading `screwInwardCurvatureSign` / `screwInwardCurvatureSignIsMeasured`:

- New persisted key (raw accessor, internal state, **no interface property, no UI control** — see
  header): `CurvatureSignMeasurementMigrated` (bool, default false).
- If `!migrated`:
  - if `screwInwardCurvatureSignIsMeasured && screwInwardCurvatureSign != 0`: negate the sign,
    write it back through the accessor, `Logger.Info` both values, and raise a one-time
    `Notification.ShowInformation` explaining that the measured adapter direction was corrected
    (cite the wizard's "Direction was measured" provenance so the user can re-verify).
  - set the marker true (also when nothing needed negating, so the check never re-runs).
- `IsMeasured == false` values are never touched (manually-set or assumed — design §5).

**Tests:** `Tests/TiltAdapterWizard/TiltAdapterOptionsTests.cs` —
- measured +1 → loads as −1, marker set, second construction does not negate again;
- measured value negated only once across a simulated profile change;
- unmeasured value untouched, marker still set;
- sign 0 untouched.
(These need the injectable `IPluginOptionsAccessor` ctor that already exists at
TiltAdapterOptions.cs:29.)

**Documentation of side effect:** the wizard's displayed physical screw angles rotate 180° for
migrated profiles (design §5, open question Q2) — add one sentence to the migration notification
text suggesting the user glance at the wizard's screw diagram.

## Step 5 — Add the focuser-direction setting `k` (persisted option + UI)

**Files:**
- `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/IInspectorOptions.cs` — add
  `bool FocuserIncreasesTowardObjective { get; set; }` with a doc comment stating the contract:
  *display-only; consumed exclusively by direction captions and mechanical wording; must never be
  passed into the TiltAdapterWizard math layer, the planner's target computation, or the
  simulator* (the structural guarantee of design §2.2).
- `AutoFocus/InspectorOptions.cs` — backing field + accessor persistence (default `false` =
  standard, `sign(k) = +1`), load in `InitializeOptions`, reset in `ResetDefaults`.
- `Resources/OptionsDataTemplates.xaml` — **the invariant control**: a labeled two-item ComboBox
  next to the `MicronsPerFocuserStep` pass-through (~:3179), label "Increasing focuser position",
  items "Moves camera away from objective (standard)" / "Moves camera toward objective
  (reversed)", `SelectedValuePath=Tag` bound to `InspectorOptions.FocuserIncreasesTowardObjective`
  (mirroring the wizard combo's bool-Tag pattern at TiltAdapterWizard/DataTemplates.xaml:711–732);
  plus a `FocuserIncreasesTowardObjective_Tooltip` TextBlock resource near the other tooltips
  stating it affects direction labels and diagrams only and can never change a measurement or a
  correction.

**Tests:** options round-trip + default in the inspector-options tests (follow the existing
`MicronsPerFocuserStep` precedent).

## Step 6 — Presentation layer consumes `k`; invariance guards

Introduce one tiny presentation helper (suggested: `FocuserSign` resolved once per call site as
`inspectorOptions.FocuserIncreasesTowardObjective ? -1 : +1`) and wire the six caption groups from
design §3 — **and nothing else**:

1. `AutoFocus/DataTemplates.xaml:3638` / `:3837` — caption becomes k-selected: "Positive values =
   Move away from objective" ⟷ "…Move toward objective" (DataTrigger on the bound option or a
   converter).
2. `AutoFocus/DataTemplates.xaml:3265` — "Telescope is up, Sensor is down" ⟷ inverted wording,
   same mechanism.
3. `Controls/TiltModelControl.cs:142–143` — new bool DP (e.g. `FocuserIncreasesTowardObjective`)
   bound from the options; swap the "Telescope"/"Sensor" `ScreenLabel` placement when set.
4. `AutoFocus/InspectorVM.cs:1671–1676` — `BackfocusDirection` selection becomes
   `delta > 0 ⇒ (k = +1 ? "TOWARDS" : "AWAY FROM")` etc.; raise on option change.
5. `Inspection/SensorModelAberrationResult.cs:392–404` — `AnalyzeCurvature` direction word:
   thread a `focuserSign` parameter (default `+1`) through `Update(...)` from the calling VM;
   `REMOVING ⇔ sign(k)·E_z > 0`.
6. Mechanical vocabulary via the anchor dictionary — presentation call sites compose `σ·sign(k)`
   before the existing lookup (the dictionary itself and its pinned test are untouched):
   - tilt motion arrows `InspectorVM.cs:2078`: `−resolvedSign·turns` → `−resolvedSign·focuserSign·turns`;
   - backfocus motion arrow `InspectorVM.cs:2104–2110`: `towardObjective = focuserSign·curvatureEffectMicrons > 0`;
   - wizard combo `TiltAdapterWizardVM.cs:1066–1080`: getter
     `CwMovesAdapterTowardObjectiveForSign(σ·focuserSign)`; setter
     `σ = CurvatureSignForCwDirection(value)·focuserSign` — **the one deliberate assumed-σ
     exception. Q1 is RESOLVED (user, 2026-08-04): implement this bounded exception; the combo keeps
     its mechanical wording. Do not widen it — this is the single sanctioned path on which `k` can
     reach motion** (design §2.3);
   - calibration log `TiltAdapterWizardVM.cs:2654–2655`: keep printing raw σ; the
     OBJECTIVE/CAMERA word uses `σ·focuserSign` and the line says "per the focuser direction
     setting";
   - `RebuildTiltGuidance`/`FillNumericGuidance` re-run when the option changes (subscribe like the
     existing `tiltAdapterOptions.PropertyChanged` path; use the non-blocking Post marshaling per
     `.claude/docs/mvvm-patterns.md`).

The wizard VM reaches the option via its existing `Inspector` reference (the wizard XAML already
binds `Inspector.InspectorOptions.CenterFocuserBeforeRun`, TiltAdapterWizard/DataTemplates.xaml:651).

**Invariance guard tests (the structural half of design §2.2 — must be added, not optional):**
- Wizard: identical seeded 6-step run with `k` toggled both ways ⇒ identical stored
  `ScrewInwardCurvatureSign`.
- Inspector: identical model + calibration with `k` toggled ⇒ identical numeric guidance rows,
  glyphs, totals; tilt/backfocus *arrows* flip; legend text unchanged.
- Planner (`InspectorVMAutomaticAdjustmentTests`): identical move plan across `k`.
- Simulator: no `k` dependency exists to test by construction (it reads only
  `ICameraSimulatorOptions`); assert nothing new needed beyond step 3's pins.
- Caption matrix: the six sites render the standard wording at `k = +1` (bit-identical to today)
  and the swapped wording at `k = −1`.

## Step 7 — Prose and documentation corrections

- `docs/tilt-guidance-motion-arrows-design.md:25` — replace the inverted equivalence with the
  corrected justification (design §3 "prose that must be fixed"): a CW/+ turn moves the plate
  toward the camera iff `σ·k = +1`; motion-toward-objective is `−σ·sign(k)·turns` (tilt) and
  `sign(k)·E_z > 0` (backfocus); the shipped `k = +1` default reproduces the previous formulas.
- `.claude/docs/tilt-domain.md:26` — scope "pure physics, identical on every rig" to "given the
  focuser-direction setting"; add a pointer to the new design doc.
- `documentation/docs/overview/tilt-adapter-wizard.md:58–62` — the direction is measured from the
  **mean best-focus change** of the all-screws step (not "the curvature change"), with the
  corrected sense in user terms (all-screws-clockwise lowering best-focus ⇒ clockwise moves the
  adapter toward the camera on a standard focuser); document that the direction combo now reflects
  the focuser-direction setting.
- `documentation/docs/overview/tilt-aberration-inspector.md:111–112` — "the same on every rig" →
  scoped to the focuser-direction setting; document the new option and its display-only guarantee.
- Document the new option wherever the inspector's options are listed in the manual.
- `docs/tilt-adapter-direction-sign-design.md` — add a short "Resolved" header pointing at
  `docs/focuser-direction-convention-design.md` (hypothesis 1 confirmed in strengthened,
  convention-independent form; the σ = m·k factorization explains why the old setting felt
  opaque).
- Grep sweep before closing: `grep -rn "best-focus position decreases" docs .claude/docs documentation`
  and `grep -rn "toward the objective" documentation/docs` — fix any stragglers consistent with the
  above.
- Run `mkdocs build --strict` for the manual changes.

## Step 8 (REQUIRED — Q3 resolved "adopt", user 2026-08-04) — warning-only cross-check

**File:** `TiltAdapterWizardVM.cs`, `RunCalibrationMath` (after the `measuredCurvature` block at
:2643–2658).

Compute the a→b curvature-effect delta (`CurvatureEffectAtScrewRadiusMicrons`, already logged at
:2653) and the re-baseline curvature-effect drifts as the noise scale. If `|ΔE| > 2×` the drift
scale **and** `sign(ΔE) != σ_measured`, log a Warning and surface it through the existing
confidence/warning banner path (`EvaluateCalibrationConfidence` / `HasConfidenceWarning`). Never
block, never modal, never change the stored sign — and never read `k` (both channels are z-space).
Test with seeded readings: agreeing channels → no warning; contradicting-but-small ΔE → no
warning; contradicting-and-large ΔE → warning. **Skip this step entirely unless the user answers
Q3 "yes".**

## Step 9 — Verification

1. Full suite: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` (expect the
   flipped-expectation tests from steps 1–2, the new pins from steps 1/3/4/6, and everything in the
   "must NOT change" list untouched and green).
2. `mkdocs build --strict` (step 7).
3. Simulated end-to-end sanity (manual or scripted via the simulator): configure
   `SimScrewInwardCurvatureSign = +1`, run a 6-step wizard calibration against the simulator, and
   confirm the stored `ScrewInwardCurvatureSign` comes out **+1** with "measured" provenance — the
   closed-loop statement that measurement and physics now agree. Repeat with −1. Toggle the new
   focuser-direction setting during review and confirm only captions/arrows change (numbers,
   glyphs, and any planned moves identical).
4. On the real rig at next opportunity: re-run the 6-step calibration; the log line
   (`TiltAdapterWizardVM.cs:2651`) should report a mean-focus **drop** on the all-inward step and
   conclude "toward the CAMERA" (at the default standard focuser setting), matching the
   manually-set value that already converges. No migration fires (the user's profile has
   `IsMeasured == false` after their manual fix); verify a copy of a profile with a stale measured
   value migrates once and logs it.
