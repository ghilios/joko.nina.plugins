# Backfocus Correction Gain Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the Aberration Inspector's backfocus guidance understating the required move by ~7×. Measure the backfocus correction gain γ from the calibration run's own AllInward piston, persist it, and **apply** the gain-corrected move — with the uncorrected figure kept visible for comparison and the amount adjustable at the point of applying.

**Architecture:** γ is computed in `TiltCalibrationCalculator` (pure, shared by wizard and TestApp) from the drift-corrected AllInward curvature delta, sanity-banded, and surfaced on `TiltCalibrationResult` + metadata. `TiltAdapterOptions` persists the last accepted γ with its provenance and offers a manual override. `TiltScrewGeometry` gains a gain-aware backfocus overload; the existing gain-1.0 path is untouched so the comparison figure stays computable. The corrected figure owns the guidance table, and the approval dialog carries a preset selector for applying less than all of it.

**Tech Stack:** C# / .NET 8 (WPF plugin), NUnit 4.4 (`dotnet.exe test` via WSL interop), Newtonsoft JSON metadata.

**Read first (execution session):** `docs/backfocus-correction-gain-design.md` (the spec — §2 for the mechanism, §4 for the measurement and its validation, §5 for the design, §7 for what must not be "simplified", §8 for the apply-time UX), `docs/tilt-calibration-pitch-nonlinearity-design.md` §4 and §8 (the compounding curvature under-read), `.claude/docs/mvvm-patterns.md`, `.claude/docs/options-system.md`, `.claude/docs/wpf-xaml.md`, `.claude/docs/documentation-style.md`.

**Locked decisions (from the investigation — do not relitigate mid-execution):**
- **The corrected figure is what gets applied.** It owns the guidance table's Backfocus and Total rows; the uncorrected figure appears as a single comparison line beneath, never as a parallel column (two per-screw Totals would leave a hand-turning user with no recommendation). Design §8.2.
- **Automatic Adjustment applies 100% of the same resolved figure**, with **no** new policy setting. `RunAutomaticAdjustmentAsync` always awaits the approval prompt, so the apply-time control covers it. Do **not** add an `AutomationBackfocusPolicy` enum — it would break the single-pipeline property that stops the planner diverging from the approved table. Design §8.5.
- **Apply-time choice is four fixed presets** in the approval dialog (Corrected / Half / Quarter / Uncorrected), default Corrected, **never persisted**. Design §8.3.
- **Out-of-range is judged in millimetres of plate travel, not the per-command cap** — 1.0 mm fixed advisory threshold, never blocking. The cap is a chunk size the planner already satisfies. Design §8.4.
- Sanity band for a **measured** γ is **[0.05, 0.5]**; outside it γ is treated as unmeasured and is **not** persisted. The **manual override** range is **[0.05, 1.0]**, with 1.0 documented as restoring legacy behaviour — a measurement band and a user assertion are different things.
- γ is drift-corrected against **mid(Baseline, ReBaseline1)** — the same reference `PistonImpliedMicronsPerStep` already uses. Uncorrected it reads 59% high; this is not optional.
- γ is a **dimensionless gain** (µm of curvature effect per µm of plate travel), reported separately from the pitch. Do not fold it into `unitMicrons`.
- The **tilt** term keeps gain 1.0. It is correct there and is locked by the synthetic round-trip test from PR #178.
- `MeasureCurvatureDuringCalibration` **default flips to ON**.
- Metadata `CurrentSchemaVersion` 3→4, all new fields additive with NaN/empty defaults.

**Branch:** all work on `ghilios/backfocus-correction-gain` off `develop`. Never push `develop`. Commits use:
```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "<message>"
```
Test command template (WSL → Windows dotnet; timeout 600000, never pipe through `tail`):
```bash
dotnet.exe test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~<FixtureName>"
```

**Reference numbers** (from `D:\TiltCalibrationDebug\WithExtraBaseline`, for tests and validation):
Baseline curvature effect at r=55 mm −268.6 µm; ReBaseline1 −223.7; mid = −246.11; AllInward −208.2;
Δ = +37.88 µm over 150 steps → **0.2526 µm/step → γ = 0.1403**. Independent 5-run sweep: 0.258 µm/step → γ = 0.1433 (agree 2.1%).

---

### Task 0: Branch setup

- [ ] **Step 0.1:** `git checkout develop && git pull && git checkout -b ghilios/backfocus-correction-gain`
- [ ] **Step 0.2:** Confirm clean: `git status` → "nothing to commit".

---

### Task 1: Compute γ in the calculator

**Files:** Modify `TiltAdapterWizard/TiltCalibrationCalculator.cs`; Test `Tests/TiltAdapterWizard/TiltCalibrationCalculatorTests.cs`

- [ ] **Step 1.1: Read first.** `PistonImpliedMicronsPerStep` (the drift-correction pattern to mirror) and `TiltCalibrationInputs` — confirm whether the per-step **curvature effect at screw radius** is already an input. It is currently carried on the wizard's `StepReading` as `CurvatureEffectAtScrewRadiusMicrons` but may not be on `TiltCalibrationInputs`. If absent, add `BaselineCurvatureEffectMicrons`, `AllInwardCurvatureEffectMicrons`, `ReBaseline1CurvatureEffectMicrons` (NaN defaults). Report what you found.
- [ ] **Step 1.2: Write the failing tests** — the real run's numbers, plus the guards:
  - `BackfocusGain_DriftCorrectedFromBracketingBaselines`: baseline −268.6, reBaseline1 −223.7, allInward −208.2, applied 150, pitch 1.8 → γ = 0.1403 within 1e-3.
  - `BackfocusGain_NaNWithoutCurvatureSteps`: `HasCurvatureMeasurement = false` → NaN.
  - `BackfocusGain_OutsideSanityBand_ReturnsNaN`: `[TestCase]` values that produce γ = 0.01 and γ = 0.9 → NaN.
  - `BackfocusGain_UncorrectedWouldRead59PercentHigh`: assert the drift-corrected value differs from the naive `AllInward − Baseline` value by the expected margin, so a future "simplification" that drops the drift correction fails loudly.
- [ ] **Step 1.3: Run** `--filter "FullyQualifiedName~TiltCalibrationCalculatorTests"` → the new tests FAIL (method missing).
- [ ] **Step 1.4: Implement** `public static double BackfocusCorrectionGain(TiltCalibrationInputs inputs)` returning the dimensionless gain: drift-corrected Δcurvature ÷ (applied × unitMicrons). Return NaN when no curvature measurement, when applied/unit are non-positive, or when the result falls outside [0.05, 0.5]. Doc comment must state the §2 mechanism in brief (the gain exists only because the corrector rides the drawtube) and why the drift correction is load-bearing.
- [ ] **Step 1.5:** Add `public double BackfocusCorrectionGain { get; set; }` to `TiltCalibrationResult`, populated in `Calibrate`.
- [ ] **Step 1.6: Run** the fixture → green. **Commit** — `feat(tilt): measure the backfocus correction gain from the AllInward piston`

---

### Task 2: Gain-aware backfocus correction

**Files:** Modify `TiltAdapterWizard/TiltScrewGeometry.cs`; Test `Tests/TiltAdapterWizard/TiltScrewGeometryTests.cs`

- [ ] **Step 2.1: Read** `ScrewCorrectionMicrons` and `SignedTotalAdjustment`. Note that with a centred apex (`fixedSensorCenter = true`, so x0 = y0 = 0) and isotropic curvature (astigmatic disabled, kx = ky), every screw at the same radius gets an **identical** backfocus correction — so the gain-corrected figure is one number, not four. Confirm this before designing the API.
- [ ] **Step 2.2: Write failing tests** proving (a) the existing gain-1.0 result is unchanged when no gain is supplied, and (b) supplying γ = 0.1403 scales the backfocus component by 1/γ ≈ 7.13 while leaving the **tilt** component bit-identical.
- [ ] **Step 2.3: Implement** an additive overload carrying γ. Do **not** modify the existing signature's behaviour — both numbers must remain computable side by side. The tilt term must not be touched.
- [ ] **Step 2.4: Run** → green. **Commit** — `feat(tilt): gain-aware backfocus correction alongside the existing one`

---

### Task 3: Persist γ, its provenance, and a manual override

**Files:** Modify `Interfaces/ITiltAdapterOptions.cs`, `TiltAdapterWizard/TiltAdapterOptions.cs`, `TiltAdapterWizard/DataTemplates.xaml`; Test `Tests/TiltAdapterWizard/TiltAdapterOptionsTests.cs`

- [ ] **Step 3.1:** Add `LastMeasuredBackfocusGain` (double, default NaN), `BackfocusGainOverrideEnabled` (bool, default false) and `BackfocusGainOverride` (double, default NaN), following the `LastMeasuredStepperStepSizeMicrons` accessor pattern verbatim. Use an explicit enabled flag rather than a NaN sentinel — the UI is a checkbox plus an always-visible-but-disabled TextBox (design §8.6), which keeps `DataTemplates.xaml` converter-free.
- [ ] **Step 3.2:** Add a resolver — override, else last-measured, else NaN — returning both the value and a provenance enum (`Manual`, `MeasuredThisRun`, `Persisted`, `Unavailable`). Unit-test each branch.
- [ ] **Step 3.3:** UI: checkbox + numeric override field in the wizard's hardware section, next to the measured-pitch controls, enabled via `Style` + `DataTrigger` on the bool. Range 0.05–1.0. Every local `TextBlock.Style` must carry `BasedOn="{StaticResource StandardTextBlock}"` (THEME HAZARD note at the top of `DataTemplates.xaml`). Caption per design §8.6.
- [ ] **Step 3.4:** `RunCalibrationMath` writes `LastMeasuredBackfocusGain` only when the computed γ is non-NaN (i.e. passed the sanity band).
- [ ] **Step 3.5: Run** options + wizard filters → green. **Commit** — `feat(tilt): persist the backfocus gain with provenance and a manual override`

---

### Task 4: Show both figures in the guidance

**Files:** Modify `AutoFocus/InspectorVM.cs`, the tilt-guidance VM and its DataTemplates; Test `Tests/AutoFocus/InspectorVMBehavioralTests.cs`

- [ ] **Step 4.1: Read** `ComputePerScrewTargets` and how `Screw*BackfocusAmount` reaches the guidance table, before designing the presentation.
- [ ] **Step 4.2:** The **corrected** figure populates the existing Backfocus and Total rows. Add one comparison line beneath the table: `Backfocus is gain-corrected: γ = 0.140 (measured) · uncorrected: +137 steps`. Do **not** add a parallel per-screw column — design §8.2 explains why.
- [ ] **Step 4.3:** When γ is unavailable, show only today's figure and label it a **lower bound** — do not silently imply it is the answer.
- [ ] **Step 4.4:** Add the spacer advisory when the corrected move is **≥ 1.0 mm of plate travel** (fixed constant). Do **not** trigger on `TiltDeviceMaxStepsPerCommand` — that cap is a chunk size `TiltMovePlanner.ApplyCap` already satisfies, and firing on it would cry wolf. Advisory only, never blocking; the existing `TiltDeviceMaxExcursionSteps` limit stays the sole blocker. Text per design §8.4. On the reference rig this fires at 1.75 mm.
- [ ] **Step 4.5: Write VM tests:** both figures present with a measured γ; lower-bound labelling with no γ; the spacer note firing past the cap.
- [ ] **Step 4.6: Run** inspector + wizard filters → green. **Commit** — `feat(inspector): show the gain-corrected backfocus move alongside the uncorrected one`

---

### Task 4b: Apply-time preset selector in the approval dialog

**Files:** Modify `TiltAdapterDevices/Prompt/TiltDeviceAdjustmentPromptVM.cs`, `TiltDeviceAdjustmentPromptControl.xaml`, `TiltDeviceAdjustmentChoice.cs`, and the replanner path through `InspectorVM.BuildPlanPreview` / `TiltDevicePlanPreviewBuilder.cs`; Test `Tests/TiltAdapterDevices/Prompt/*`

- [ ] **Step 4b.1: Read** how `ApplyTilt`/`ApplyBackfocus` re-invoke the replanner today, so the preset follows the same WYSIWYG contract — the move list must always match the selection.
- [ ] **Step 4b.2:** Add the four-preset ComboBox beside the Backfocus checkbox (design §8.3), default Corrected, recomputed per invocation and never persisted. Carry the chosen scale on `TiltDeviceAdjustmentChoice`.
- [ ] **Step 4b.3:** Collapse the ComboBox when γ is unavailable, showing the lower-bound caption instead.
- [ ] **Step 4b.4:** Make the spacer advisory in the dialog track the **selected** preset, not the full corrected figure.
- [ ] **Step 4b.5: Write tests:** each preset produces the expected scaled move list; the choice reaches the planner; the advisory follows the selection; γ-unavailable collapses the control.
- [ ] **Step 4b.6: Run** the prompt + inspector filters → green. **Commit** — `feat(tilt): choose how much of the backfocus correction to apply`

### Task 5: Metadata and TestApp

**Files:** Modify `TiltAdapterWizard/TiltCalibrationMetadata.cs`, `TestApp/TiltCalibrationRunner.cs`; Tests as appropriate

- [ ] **Step 5.1:** `CurrentSchemaVersion` 3→4. `TiltCalibrationResultRecord` gains `BackfocusCorrectionGain` (NaN default); per-step records gain the curvature-effect fields if Task 1 needed them. Extend the existing round-trip tests the way Task 4 of the previous plan did — assert the new fields, don't just set them.
- [ ] **Step 5.2:** TestApp `tilt` prints γ, its drift-corrected inputs, and the implied backfocus move next to the existing piston line. Extend the JSON additively.
- [ ] **Step 5.3: Run** metadata + calculator filters → green. **Commit** — `feat(testapp): report the backfocus correction gain in the tilt validator`

---

### Task 6: Default the piston step ON, with explanatory copy

**Files:** Modify `TiltAdapterWizard/TiltAdapterOptions.cs`, `TiltAdapterWizard/DataTemplates.xaml`, `documentation/docs/overview/tilt-adapter-wizard.md`

- [ ] **Step 6.1:** Flip `MeasureCurvatureDuringCalibration` default to `true`. Update every test that assumed the 4-step default flow — expect several; check `GetMeasurementSteps` callers and the `NextStep_*` walks.
- [ ] **Step 6.2:** Apply the tooltip strings and manual passage from **Appendix A** below (already drafted against the house style guide). Use them as written — two lines were corrected after drafting to match this plan's locked decisions, and reverting to the originals would reintroduce claims the design explicitly rejects.
- [ ] **Step 6.3:** Reword the three places that assume a default-OFF **Measure direction**, all identified during drafting:
  - `documentation/docs/overview/tilt-adapter-wizard.md` ~line 203 — the measured-pitch admonition says "Measure direction is **off by default**, so a default run shows neither this line nor its warning".
  - `documentation/docs/overview/tilt-adapter-wizard.md` ~line 67 — the calibration-loop paragraph's "To measure it, turn on **Measure direction**" framing.
  - `TiltAdapterWizard/DataTemplates.xaml` — the italic caption beside the checkbox, "Turn on if you don't know how your adapter behaves, or to verify the setting above".
  Also re-check `motorized-tilt-adapter.md`, which was corrected once already for the ReBaseline3 default.
- [ ] **Step 6.4: Run** the full wizard filter → green. `mkdocs build --strict` clean. **Commit** — `feat(tilt): measure direction by default, and explain what the extra runs buy`

---

### Task 7: Validate against the captured datasets

**Files:** Test only

- [ ] **Step 7.1:** Add a run-literal regression test pinning γ = 0.1403 from `WithExtraBaseline`'s stored curvature effects through the production `Calibrate` path.
- [ ] **Step 7.2:** Add a test documenting the independent cross-check: the 5-run piston sweep gives 0.258 µm/step against AllInward's 0.2526, agreeing to 2.1%. Pin both constants with a comment citing the design doc, so a future change that breaks the agreement is visible.
- [ ] **Step 7.3:** Verify the end-to-end number: the reference rig's −246.11 µm curvature effect with γ = 0.1403 yields 974 steps ≈ 1.75 mm, against today's 137 steps ≈ 0.25 mm.
- [ ] **Step 7.4: Run** → green. **Commit** — `test(tilt): pin the backfocus gain against both captured datasets`

---

### Task 8: Documentation

- [ ] **Step 8.1:** In `tilt-adapter-wizard.md` and the inspector's backfocus documentation, explain that plate travel and curvature change are **not** one-for-one, that the ratio is rig-specific and measured, and how to read the corrected figure and its comparison line.
- [ ] **Step 8.1b:** Add a tooltip/manual note to the **"Backfocus Error"** readout stating it is focal-surface sag, **not** plate travel, and that γ is the conversion. After this change users will see "Backfocus Error: 364 µm" a few rows above a 1.75 mm move instruction and will reasonably ask why they differ. Also update `motorized-tilt-adapter.md` (~line 96) for the new amount selector, and `tilt-adapter-wizard.md` (~line 111) whose "reads it as a spacing error and derives a backfocus correction" wording encodes the 1:1 assumption.
- [ ] **Step 8.2:** State plainly that the recommendation remains a **floor** until the vertex-estimator work lands, because the curvature magnitude is separately under-read (design doc §6). Cross-reference `tilt-calibration-pitch-nonlinearity-design.md` §4/§8.
- [ ] **Step 8.3:** Mark the design doc's §5 items implemented with commit refs.
- [ ] **Step 8.4: Commit** — `docs(tilt): explain the backfocus correction gain and how to read both figures`

---

### Task 9: Final gate

- [ ] **Step 9.1:** Full suite: `dotnet.exe test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`. All green (`SendAsync_WritesOnABackgroundThread` may flake — re-run in isolation before dismissing).
- [ ] **Step 9.2:** Re-run the TestApp tilt replay against `D:\TiltCalibrationDebug\WithExtraBaseline` from the final tree; confirm γ prints as 0.140 and the implied move as ~974 steps.
- [ ] **Step 9.3:** Push branch, open PR to `develop` titled "Backfocus correction gain: stop understating the required move by 7×". Body explains the gain-1.0 defect, the mechanism, the measurement and its independent validation, and states explicitly that automation behaviour is unchanged. End with the standard generated-with footer.

---

## Appendix A: drafted copy for Task 6

Written against `.claude/docs/documentation-style.md`. **Two sentences were corrected after drafting**
to match this plan's locked decisions — the original draft said the guidance figures "are scaled by"
γ (this plan shows both figures instead of scaling) and that the measurement "is what makes the
recommended backfocus move trustworthy" (per design §6 it remains a floor until the vertex-estimator
work lands). Do not restore those phrasings.

### Tooltip — Measure direction (`MeasureCurvatureDuringCalibration`)

> Adds two steps (an all-screws move and a return to baseline) so the calibration measures which way a clockwise turn moves the adapter instead of assuming it. The same steps measure how much the field curvature actually changes when the plate moves, which sets the scale of the backfocus recommendation, and give an independent µm/step estimate that cross-checks the tilt-derived pitch.

### Tooltip — Measure final re-baseline (`MeasureFinalRebaseline`)

> Adds one measurement after the screw 2 move is undone, so that move is bracketed by re-baselines on both sides just as screw 1's is. Steady drift between steps then cancels out of both screw readings. On a thermally stable run it changes little; it is cheap insurance, not a guaranteed improvement.

### Manual passage — `documentation/docs/overview/tilt-adapter-wizard.md`, Measurement section

> Two settings in the **Measurement** section add autofocus runs in exchange for measuring what a minimal calibration has to assume. Both are on by default; turn either off to shorten the run. **Measure direction** adds two steps: every screw is moved the same amount in the same direction, then returned to baseline. An all-screws move is pure piston (the plate shifts without tilting), so the change in mean best-focus position tells the wizard which way a clockwise turn, or a positive step on motorized adapters, moves the plate, and guidance reports the direction as measured instead of "(assumed)". The same steps yield the **Piston-implied** pitch, a second estimate of the adapter's effective µm/step that involves no tilt fit and no screw radius, so it cross-checks the tilt-derived value.
>
> The same all-screws move also calibrates the backfocus recommendation, and this is the strongest reason to leave the setting on. Moving the plate does not change the measured field curvature one-for-one: the ratio is set by the optical design, and it varies from rig to rig. On one measured rig only about 14% of the plate motion showed up as curvature change, so a recommendation that treated the two as equal would have understated the required move by roughly a factor of seven. The all-screws step measures the ratio directly on your rig, and guidance reports the corrected move alongside the uncorrected one so you can see the difference the measurement makes. Treat the corrected figure as a lower bound and re-measure after adjusting: the curvature it is derived from is itself under-reported, so the move required is usually a little larger again.
>
> **Measure final re-baseline** adds one step at the end. The core sequence brackets screw 1's move with baseline measurements on both sides, but screw 2's move ends the run with a one-sided reference. This setting measures the position restored after the screw 2 move is undone instead of assuming it, so both screw moves are referenced to the midpoint of the re-baselines around them and steady drift between steps cancels out of the readings. On a run with real settling between steps that matters; on a clean, thermally stable run it makes little difference. One extra autofocus run is cheap insurance, not a guaranteed improvement.

---

## Self-review notes (kept for the executor)

- Spec coverage: §5.1 → Task 1; §5.2 → Task 1 (band) + Task 3 (persistence gate); §5.3 → Task 3; §5.4 → Task 3; §5.5 → Tasks 4 + 4b; §5.6 → Task 4b; §5.7 → Task 6; §8.2 → Task 4; §8.3 → Task 4b; §8.4 → Tasks 4.4 + 4b.4; §8.5 → Task 4b (explicitly by adding nothing); §8.6 → Task 3.
- Type flow: `BackfocusCorrectionGain` is introduced on `TiltCalibrationResult` (Task 1), persisted via options (Task 3), consumed by `TiltScrewGeometry`'s new overload (Task 2) through the guidance (Task 4), serialized (Task 5).
- Deliberate deviations an executor must not "fix": the corrected figure owns the table and one comparison line sits beneath it (not a second column); automation adds no policy enum; the tilt term keeps gain 1.0; γ stays separate from `unitMicrons`; the override band is wider than the measurement band on purpose.
- Steps 1.1, 2.1 and 4.1 are read-before-write checks: the curvature-effect plumbing into `TiltCalibrationInputs`, the per-screw-identical claim, and the guidance rendering path were reasoned about but not read line by line during planning.
- Highest-risk task is 6: flipping a default changes the flow length and will break tests that assume 4 steps. Budget for that rather than being surprised by it.
