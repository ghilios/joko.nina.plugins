# Tilt Calibration & Sensor Model UX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the approved spec `docs/tilt-calibration-ux-design.md`: surface the sweep-cost settings (Signal Amplification, Center Focuser First — new default off) in both the Aberration Inspector and the Tilt Adapter Wizard, give screw/stepper guidance explicit CW/CCW and signed-step directions, make the wizard's curvature-direction measurement opt-in (4-step default, 6-step optional), and add manual calibration entry.

**Architecture:** All changes live in the existing HocusFocus plugin (C#/WPF, MEF + CommunityToolkit.Mvvm). Pure math/formatting goes in static helpers (`TiltCalibrationCalculator`, `TiltScrewGeometry`, `TiltAdapterGuidanceVM`, internal statics on the VMs) so it is unit-testable without WPF. Persisted settings follow the existing `PluginOptionsAccessor` pattern. UI is XAML edits to `AutoFocus/DataTemplates.xaml` and `TiltAdapterWizard/DataTemplates.xaml`.

**Tech Stack:** .NET 8 (`net8.0-windows7.0`, builds on Linux/WSL via EnableWindowsTargeting), NUnit 4.4 + NSubstitute, WPF XAML.

---

## Context for a zero-context engineer

- **Repo root:** `/home/ghilios/src/hocus-focus`. Work on branch `ghilios/tilt-calibration-ux` (already exists; the spec is committed there). **Never push to `develop`.**
- **Build + full test suite** (run after every task; it also compiles the XAML since the sln includes the plugin project):
  ```bash
  dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo
  ```
  Filtered run for a single fixture:
  ```bash
  dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~TiltCalibrationCalculatorTests"
  ```
- **Committing** (GitHub email privacy — both author and committer must use the noreply address):
  ```bash
  GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
    git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "<message>"
  ```
- **Domain terminology (from the spec — keep these distinct in ALL text you write):**
  - *Screw motion:* clockwise (CW) always advances/tightens a screw. Fixed hardware fact, never a setting. UI text says "clockwise/counter-clockwise (tighten/loosen)".
  - *Adapter motion:* "inward" = the adapter's moving plate travels toward the telescope objective; "outward" = toward the camera. Whether a CW turn produces inward or outward adapter motion is rig-specific — that is the new setting.
  - `ScrewInwardCurvatureSign` (existing, persisted): the measured sign of the mean-focus/curvature response to a CW ("prompt-direction") screw turn. `+1` = CW turns raise the curvature effect, `−1` = lower it. The wizard's Baseline→AllInward steps measure it; this plan adds a manual/mechanical way to set it.
- **The 6-step wizard flow (existing):** Baseline (a) → AllInward (b) → ReBaseline1 (c) → Screw1 (d) → ReBaseline2 (e) → Screw2 (f). Steps a→b exist only to measure the sign. The 4-step flow removes a-as-separate-reference and b: Baseline → Screw1 → ReBaseline2 → Screw2, with the Baseline reading serving as the screw-1 reference (the role `ReBaseline1` plays in the 6-step flow).
- **Key files:**
  - `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorOptions.cs` — inspector persisted options
  - `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorVM.cs` — sensor-model runs, tilt guidance
  - `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/TiltScrewGuidanceRow.cs` — `TiltAdapterGuidanceVM` + formatters
  - `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/DataTemplates.xaml` — inspector UI
  - `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltAdapterWizardVM.cs` — wizard state machine
  - `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltAdapterOptions.cs` (+ `Interfaces/ITiltAdapterOptions.cs`) — wizard persisted options
  - `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltCalibrationCalculator.cs` — pure calibration math
  - `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltScrewGeometry.cs` — pure screw geometry
  - `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltCalibrationMetadata.cs` — saved-run metadata
  - `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/DataTemplates.xaml` — wizard UI
  - `Joko.NINA.Plugins/TestApp/TiltCalibrationRunner.cs` — headless validator
  - Tests live in `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/` (ProjectReference to the plugin; test doubles in `TestDoubles/`, notably `InMemoryPluginOptionsAccessor`).
- **Line numbers below are pre-change positions.** Each task's edits shift later lines; anchors are given as unique code snippets so you can locate them regardless.

---

### Task 1: InspectorOptions — fix TimeoutSeconds persistence key; flip CenterFocuserBeforeRun default to off

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorOptions.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/InspectorOptionsTests.cs`

- [ ] **Step 1: Write the failing tests**

In `InspectorOptionsTests.cs` (fixture already has a `Build()` helper returning `(InspectorOptions options, InMemoryPluginOptionsAccessor store, IProfileService profile)`), add:

```csharp
    [Test]
    public void TimeoutSeconds_PersistsUnderItsOwnKey() {
        var (options, store, _) = Build();
        options.TimeoutSeconds = 120;
        Assert.Multiple(() => {
            Assert.That(store.GetValueInt32(nameof(InspectorOptions.TimeoutSeconds), -999), Is.EqualTo(120));
            Assert.That(store.GetValueInt32(nameof(InspectorOptions.StepCount), -999), Is.EqualTo(-999),
                "TimeoutSeconds must not clobber the StepCount key");
        });
    }

    [Test]
    public void CenterFocuserBeforeRun_DefaultsToOff() {
        var (options, _, _) = Build();
        Assert.That(options.CenterFocuserBeforeRun, Is.False);
    }
```

Also update the existing `Defaults_AreLoadedFromAccessor` test: change the line
`Assert.That(options.CenterFocuserBeforeRun, Is.True);` to `Assert.That(options.CenterFocuserBeforeRun, Is.False);`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~InspectorOptionsTests"`
Expected: FAIL — `TimeoutSeconds_PersistsUnderItsOwnKey` (StepCount key clobbered) and `CenterFocuserBeforeRun_DefaultsToOff` (currently true).

- [ ] **Step 3: Implement**

In `InspectorOptions.cs`:

1. Line 52, change the default:
```csharp
            centerFocuserBeforeRun = optionsAccessor.GetValueBoolean(nameof(CenterFocuserBeforeRun), false);
```
2. In `ResetDefaults()` (line 87), change `CenterFocuserBeforeRun = true;` to `CenterFocuserBeforeRun = false;`.
3. The field declaration + doc comment (lines 158–161) — replace with:
```csharp
        // When on, a quick standard autofocus is run before each live sensor-model / tilt sweep to center
        // the focuser at best focus, so the sweep brackets focus symmetrically (fewer extreme one-sided defocus
        // frames that fail to align). Off by default — it adds a full AF run to every sweep. No effect on replay.
        private bool centerFocuserBeforeRun = false;
```
4. In the `TimeoutSeconds` setter (line 194), fix the key:
```csharp
                    optionsAccessor.SetValueInt32(nameof(TimeoutSeconds), timeoutSeconds);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~InspectorOptionsTests"`
Expected: PASS. Then run the full suite: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` — fix any other test that asserted the old default (search the Tests project for `CenterFocuserBeforeRun`).

- [ ] **Step 5: Commit**

```bash
git add -A
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "fix(inspector): TimeoutSeconds persisted under StepCount key; default Center Focuser First off"
```

---

### Task 2: Direction-mapping constant in TiltScrewGeometry (USER CHECKPOINT)

The spec requires the mechanical-setting ↔ stored-sign mapping to be **verified empirically, not derived on paper**. This task pins it as a single named constant.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltScrewGeometry.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterWizard/TiltScrewGeometryTests.cs`

- [ ] **Step 1: STOP — ask the user (blocking checkpoint)**

Ask the user these two questions about a rig they have **measured** with the 6-step wizard:

1. On that adapter, does turning the screws **clockwise** move the adapter plate **toward the objective** or **toward the camera**?
2. What is the measured curvature sign on that rig? (Visible as the "Screws Inward / Curvature ↑ or ↓" row in the wizard's saved-calibration panel — ↑ = `+1`, ↓ = `−1`; or as `calibration.curvatureSign` in a saved run's `metadata.json`.)

Set the constant so the two answers agree: if CW→objective pairs with measured `+1` (or CW→camera pairs with `−1`), the constant is `+1`; if CW→objective pairs with measured `−1`, the constant is `−1`. **Do not guess. Do not proceed without the user's answer.** Record the rig + date in the comment.

- [ ] **Step 2: Write the failing test**

In `TiltScrewGeometryTests.cs` add (adjust the two `+1/−1` expectations if the user's answer flipped the constant — the test documents the verified truth):

```csharp
    [Test]
    public void CurvatureSignForCwDirection_MatchesEmpiricalAnchor() {
        Assert.Multiple(() => {
            // Pinned to the empirically verified mapping (see TiltScrewGeometry comment). If this
            // fails after an intentional flip, update BOTH the constant comment and this test.
            Assert.That(TiltScrewGeometry.CurvatureSignWhenCwMovesAdapterTowardObjective, Is.EqualTo(1));
            Assert.That(TiltScrewGeometry.CurvatureSignForCwDirection(cwMovesAdapterTowardObjective: true), Is.EqualTo(1));
            Assert.That(TiltScrewGeometry.CurvatureSignForCwDirection(cwMovesAdapterTowardObjective: false), Is.EqualTo(-1));
            Assert.That(TiltScrewGeometry.CwMovesAdapterTowardObjectiveForSign(1), Is.True);
            Assert.That(TiltScrewGeometry.CwMovesAdapterTowardObjectiveForSign(-1), Is.False);
            // Default assumption: CW moves the adapter outward (toward the camera).
            Assert.That(TiltScrewGeometry.DefaultScrewInwardCurvatureSign,
                Is.EqualTo(TiltScrewGeometry.CurvatureSignForCwDirection(false)));
        });
    }
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~TiltScrewGeometryTests"`
Expected: FAIL with "does not contain a definition for `CurvatureSignWhenCwMovesAdapterTowardObjective`" (compile error counts as the failing state).

- [ ] **Step 4: Implement**

Append inside the `TiltScrewGeometry` class (it is a static geometry helper class in namespace `NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard`):

```csharp
        // ---- Adapter direction ⇄ curvature sign --------------------------------------------------
        //
        // Clockwise (tighten) always advances a screw; the rig-specific unknown is whether that
        // advance moves the adapter's plate toward the telescope objective ("inward") or toward the
        // camera ("outward"). ScrewInwardCurvatureSign stores the measurable consequence: the sign
        // of the mean best-focus / curvature-effect response to a CW turn (+1 = raises it).
        //
        // EMPIRICAL ANCHOR (verified <DATE> against <RIG> — fill in from the Task 2 checkpoint):
        // a plate moving toward the objective forces the focuser to re-focus outward, raising the
        // mean best-focus position, which ComputeCurvatureSign records as +1.
        public const int CurvatureSignWhenCwMovesAdapterTowardObjective = 1;

        /// <summary>The stored curvature sign implied by the mechanical setting.</summary>
        public static int CurvatureSignForCwDirection(bool cwMovesAdapterTowardObjective) =>
            cwMovesAdapterTowardObjective
                ? CurvatureSignWhenCwMovesAdapterTowardObjective
                : -CurvatureSignWhenCwMovesAdapterTowardObjective;

        /// <summary>The mechanical reading of a stored curvature sign (sign must be non-zero).</summary>
        public static bool CwMovesAdapterTowardObjectiveForSign(int curvatureSign) =>
            curvatureSign * CurvatureSignWhenCwMovesAdapterTowardObjective > 0;

        // Default assumption when the direction was never measured or chosen: CW moves the adapter
        // toward the camera (outward) — the common push-screw design; matches the tilt-domain doc
        // ("turning a screw inward pushes that corner of the sensor away from the telescope").
        public static int DefaultScrewInwardCurvatureSign => CurvatureSignForCwDirection(false);
```

Replace `<DATE>` / `<RIG>` with the user's checkpoint answer, and flip the constant to `-1` (and the test's expectations) if that is what the answer implies.

- [ ] **Step 5: Run tests, then commit**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~TiltScrewGeometryTests"` → PASS, then the full suite → PASS.

```bash
git add -A
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(tilt): pin empirically verified adapter-direction <-> curvature-sign mapping"
```

---

### Task 3: TiltAdapterOptions — new persisted options and new default for the sign

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/ITiltAdapterOptions.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltAdapterOptions.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterWizard/TiltAdapterOptionsTests.cs`

- [ ] **Step 1: Write the failing tests**

In `TiltAdapterOptionsTests.cs`, update `Defaults_AreLoadedFromAccessor`: change
`Assert.That(options.ScrewInwardCurvatureSign, Is.EqualTo(0));` to
`Assert.That(options.ScrewInwardCurvatureSign, Is.EqualTo(TiltScrewGeometry.DefaultScrewInwardCurvatureSign));`
and add inside the same `Assert.Multiple`:

```csharp
            Assert.That(options.ScrewInwardCurvatureSignIsMeasured, Is.False);
            Assert.That(options.MeasureCurvatureDuringCalibration, Is.False);
            Assert.That(options.CalibrationIsManual, Is.False);
```

Add a round-trip test:

```csharp
    [Test]
    public void NewOptions_RoundTripThroughAccessor() {
        var (options, store, _) = Build();
        options.ScrewInwardCurvatureSignIsMeasured = true;
        options.MeasureCurvatureDuringCalibration = true;
        options.CalibrationIsManual = true;
        Assert.Multiple(() => {
            Assert.That(store.GetValueBoolean(nameof(TiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured), false), Is.True);
            Assert.That(store.GetValueBoolean(nameof(TiltAdapterOptions.MeasureCurvatureDuringCalibration), false), Is.True);
            Assert.That(store.GetValueBoolean(nameof(TiltAdapterOptions.CalibrationIsManual), false), Is.True);
        });
    }
```

- [ ] **Step 2: Run tests to verify they fail** (compile error on the new members)

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~TiltAdapterOptionsTests"`

- [ ] **Step 3: Implement**

In `ITiltAdapterOptions.cs`, replace line 31 (`int ScrewInwardCurvatureSign { get; set; } // +1 or -1; 0 = not yet calibrated`) with:

```csharp
        // Sign of the curvature/backfocus response to a CW ("inward") screw turn: +1 = CW turns
        // raise the curvature effect, -1 = lower it. Defaults to the assumed mechanical direction
        // (TiltScrewGeometry.DefaultScrewInwardCurvatureSign); a 6-step wizard run measures it.
        int ScrewInwardCurvatureSign { get; set; }

        // True only when a wizard run measured ScrewInwardCurvatureSign (Baseline -> AllInward
        // steps). Cleared when the user edits the direction manually or applies a manual entry.
        bool ScrewInwardCurvatureSignIsMeasured { get; set; }

        // Include the 2 curvature-direction steps (Baseline + AllInward) in a calibration run:
        // 6 steps instead of 4. Off by default — the assumed/manual direction is used instead.
        bool MeasureCurvatureDuringCalibration { get; set; }

        // True when the current calibration came from Manual Calibration Entry, not a wizard run.
        bool CalibrationIsManual { get; set; }
```

In `TiltAdapterOptions.cs`:

1. In `InitializeOptions()`, replace the `screwInwardCurvatureSign` line and append the new loads:
```csharp
            screwInwardCurvatureSign = optionsAccessor.GetValueInt32(nameof(ScrewInwardCurvatureSign), TiltScrewGeometry.DefaultScrewInwardCurvatureSign);
            screwInwardCurvatureSignIsMeasured = optionsAccessor.GetValueBoolean(nameof(ScrewInwardCurvatureSignIsMeasured), false);
            measureCurvatureDuringCalibration = optionsAccessor.GetValueBoolean(nameof(MeasureCurvatureDuringCalibration), false);
            calibrationIsManual = optionsAccessor.GetValueBoolean(nameof(CalibrationIsManual), false);
```
2. After the `ScrewInwardCurvatureSign` property (line 183), add three properties following the exact existing pattern:
```csharp
        private bool screwInwardCurvatureSignIsMeasured;

        public bool ScrewInwardCurvatureSignIsMeasured {
            get => screwInwardCurvatureSignIsMeasured;
            set {
                if (screwInwardCurvatureSignIsMeasured != value) {
                    screwInwardCurvatureSignIsMeasured = value;
                    optionsAccessor.SetValueBoolean(nameof(ScrewInwardCurvatureSignIsMeasured), screwInwardCurvatureSignIsMeasured);
                    RaisePropertyChanged();
                }
            }
        }

        private bool measureCurvatureDuringCalibration;

        public bool MeasureCurvatureDuringCalibration {
            get => measureCurvatureDuringCalibration;
            set {
                if (measureCurvatureDuringCalibration != value) {
                    measureCurvatureDuringCalibration = value;
                    optionsAccessor.SetValueBoolean(nameof(MeasureCurvatureDuringCalibration), measureCurvatureDuringCalibration);
                    RaisePropertyChanged();
                }
            }
        }

        private bool calibrationIsManual;

        public bool CalibrationIsManual {
            get => calibrationIsManual;
            set {
                if (calibrationIsManual != value) {
                    calibrationIsManual = value;
                    optionsAccessor.SetValueBoolean(nameof(CalibrationIsManual), calibrationIsManual);
                    RaisePropertyChanged();
                }
            }
        }
```

- [ ] **Step 4: Run tests to verify they pass**, then the full suite. Any existing test asserting `ScrewInwardCurvatureSign == 0` defaults must be updated to the new default (search Tests for `ScrewInwardCurvatureSign`).

- [ ] **Step 5: Commit**

```bash
git add -A
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(tilt): persisted options for assumed direction, opt-in curvature steps, manual-entry provenance"
```

---

### Task 4: TiltCalibrationCalculator — optional curvature measurement + manual angle placement

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltCalibrationCalculator.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterWizard/TiltCalibrationCalculatorTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `TiltCalibrationCalculatorTests.cs` (the fixture has `SingleScrewReading(angleDeg, axialMicrons, n, meanFocuser)` and constants `PixelSize/ImgW/ImgH/FStep/RadiusMm` — reuse them; follow an existing `Calibrate` test for how a full `TiltCalibrationInputs` is built):

```csharp
    [Test]
    public void Calibrate_WithoutCurvatureMeasurement_PassesFallbackSignThrough() {
        // 4-step run: Baseline/AllInward were never measured. The baseline reading is supplied in
        // the ReBaseline1 slot (it is the screw-1 reference); Baseline/AllInward stay default.
        var baseline = new TiltGradient(0, 0, 1000);
        var inputs = new TiltCalibrationInputs {
            ScrewCount = 3,
            HasCurvatureMeasurement = false,
            FallbackCurvatureSign = -1,
            ReBaseline1 = baseline,
            Screw1 = SingleScrewReading(0, 400, 3),
            ReBaseline2 = baseline,
            Screw2 = SingleScrewReading(120, 400, 3),
            ImageWidthPixels = ImgW,
            ImageHeightPixels = ImgH,
            PixelSizeMicrons = PixelSize,
            FocuserStepMicrons = FStep,
            ScrewRadiusMillimeters = RadiusMm,
            CalibrationAppliedAmount = 1.0,
            IsStepperAdjustment = false
        };
        var result = TiltCalibrationCalculator.Calibrate(inputs);
        Assert.Multiple(() => {
            Assert.That(result.CurvatureSign, Is.EqualTo(-1));
            Assert.That(result.Screw1AngleDegrees, Is.EqualTo(0).Within(1e-6));
            Assert.That(result.Screw2AngleDegrees, Is.EqualTo(120).Within(1e-6));
            Assert.That(result.MeasuredHardwareMicrons, Is.EqualTo(400).Within(1e-6));
        });
    }

    [Test]
    public void ComputeConfidence_WithoutCurvatureMeasurement_UsesOnlyRebaseline2Drift() {
        var baseline = new TiltGradient(0, 0, 1000);
        var drifted = new TiltGradient(0.01, 0, 1000); // ReBaseline2 drifts by 0.01 from ReBaseline1
        var inputs = new TiltCalibrationInputs {
            ScrewCount = 3,
            HasCurvatureMeasurement = false,
            ReBaseline1 = baseline,
            Screw1 = new TiltGradient(0.10, 0, 1000),
            ReBaseline2 = drifted,
            Screw2 = new TiltGradient(0.01, 0.10, 1000)
        };
        var confidence = TiltCalibrationCalculator.ComputeConfidence(inputs);
        Assert.Multiple(() => {
            // signal = mean(|0.10|, |0.10|) = 0.10; noise = |ReBaseline2 - ReBaseline1| = 0.01
            Assert.That(confidence.ScrewMoveSignal, Is.EqualTo(0.10).Within(1e-9));
            Assert.That(confidence.NoiseEstimate, Is.EqualTo(0.01).Within(1e-9));
            Assert.That(confidence.SignalToNoise, Is.EqualTo(10.0).Within(1e-9));
            Assert.That(confidence.AllInwardTiltResidual, Is.NaN);
            Assert.That(confidence.Rebaseline1Drift, Is.NaN);
            Assert.That(confidence.Rebaseline2Drift, Is.EqualTo(0.01).Within(1e-9));
            Assert.That(confidence.IsReliable, Is.True);
        });
    }

    [TestCase(30.0, true, 3, 30.0, 150.0, 270.0, double.NaN)]
    [TestCase(30.0, false, 3, 30.0, 270.0, 150.0, double.NaN)]
    [TestCase(350.0, true, 4, 350.0, 80.0, 170.0, 260.0)]
    [TestCase(10.0, false, 4, 10.0, 280.0, 190.0, 100.0)]
    public void ComputeManualScrewAngles_PlacesEqualSpacingWithWinding(
        double screw1, bool clockwise, int screwCount, double e1, double e2, double e3, double e4) {
        var (s1, s2, s3, s4) = TiltCalibrationCalculator.ComputeManualScrewAngles(screw1, clockwise, screwCount);
        Assert.Multiple(() => {
            Assert.That(s1, Is.EqualTo(e1).Within(1e-9));
            Assert.That(s2, Is.EqualTo(e2).Within(1e-9));
            Assert.That(s3, Is.EqualTo(e3).Within(1e-9));
            if (double.IsNaN(e4)) Assert.That(s4, Is.NaN);
            else Assert.That(s4, Is.EqualTo(e4).Within(1e-9));
        });
    }
```

Note on the 4-screw expectations: with step +90° CW, screws land at s1, s1+90, s1+180, s1+270 (normalized); with −90° CCW at s1, s1−90, s1−180, s1−270. Opposite screws are 180° apart in both cases.

- [ ] **Step 2: Run to verify failure** (compile errors on `HasCurvatureMeasurement`, `FallbackCurvatureSign`, `ComputeManualScrewAngles`).

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~TiltCalibrationCalculatorTests"`

- [ ] **Step 3: Implement**

In `TiltCalibrationCalculator.cs`:

1. In `TiltCalibrationInputs` (after the `IsStepperAdjustment` property, line 54), add:
```csharp
        /// <summary>False for a 4-step run that skipped the curvature-direction steps: Baseline and
        /// AllInward were never measured (leave them default) and the baseline reading is supplied
        /// in the ReBaseline1 slot, which is the screw-1 reference in both flows.</summary>
        public bool HasCurvatureMeasurement { get; set; } = true;

        /// <summary>Curvature sign carried into the result when HasCurvatureMeasurement is false
        /// (the configured/assumed ScrewInwardCurvatureSign; 0 = unknown).</summary>
        public int FallbackCurvatureSign { get; set; }
```
2. In `ComputeConfidence` (lines 125–150), replace the noise computation (keep the `s1/s2/signal` lines) with:
```csharp
            double drift2 = Magnitude(inputs.ReBaseline2.A - inputs.ReBaseline1.A, inputs.ReBaseline2.B - inputs.ReBaseline1.B);
            double allInward = double.NaN;
            double drift1 = double.NaN;
            double noise;
            if (inputs.HasCurvatureMeasurement) {
                allInward = Magnitude(inputs.AllInward.A - inputs.Baseline.A, inputs.AllInward.B - inputs.Baseline.B);
                drift1 = Magnitude(inputs.ReBaseline1.A - inputs.Baseline.A, inputs.ReBaseline1.B - inputs.Baseline.B);
                noise = Math.Sqrt((allInward * allInward + drift1 * drift1 + drift2 * drift2) / 3.0);
            } else {
                // 4-step run: the only available noise probe is the single re-baseline drift.
                noise = drift2;
            }
```
   (The subsequent `snr`, `angleUncertainty`, and result-object lines are unchanged — they already use `allInward`, `drift1`, `drift2`, `noise`.)
3. In `Calibrate` (line 274), replace the `CurvatureSign = ...` line with:
```csharp
                CurvatureSign = inputs.HasCurvatureMeasurement
                    ? ComputeCurvatureSign(inputs.AllInward.MeanFocuserPosition, inputs.Baseline.MeanFocuserPosition)
                    : inputs.FallbackCurvatureSign,
```
4. Add the manual-placement helper (near `ComputeScrewAngles`):
```csharp
        /// <summary>
        /// Places all screws from a manually entered screw-1 position angle: equal spacing (120° for
        /// 3 screws; 90° for 4, opposite screws 180° apart), numbered clockwise or counter-clockwise
        /// around the IMAGE (mirrors/diagonals can flip the physical winding). s4 = NaN for 3 screws.
        /// </summary>
        public static (double s1, double s2, double s3, double s4) ComputeManualScrewAngles(
            double screw1Deg, bool clockwise, int screwCount) {
            double step = (screwCount == 3 ? 120.0 : 90.0) * (clockwise ? 1.0 : -1.0);
            double s1 = NormalizeAngle(screw1Deg);
            double s2 = NormalizeAngle(s1 + step);
            double s3 = NormalizeAngle(s1 + 2 * step);
            double s4 = screwCount == 4 ? NormalizeAngle(s1 + 3 * step) : double.NaN;
            return (s1, s2, s3, s4);
        }
```
5. Update the `TiltCalibrationInputs` class doc comment (lines 35–39) to mention both flows: append `In the 4-step flow (no curvature steps) Baseline/AllInward are unset, HasCurvatureMeasurement is false, and the baseline reading occupies the ReBaseline1 slot.` to the summary.

- [ ] **Step 4: Run tests** — the fixture filter, then the full suite. PASS required.

- [ ] **Step 5: Commit**

```bash
git add -A
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(tilt): calculator supports 4-step runs (sign passthrough) and manual screw placement"
```

---

### Task 5: Directional guidance — CW/CCW totals, signed steps, direction legend

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/TiltScrewGuidanceRow.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorVM.cs` (RebuildTiltGuidance)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/DataTemplates.xaml` (legend line)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/TiltAdapterGuidanceVMTests.cs`

- [ ] **Step 1: Write the failing tests**

In `TiltAdapterGuidanceVMTests.cs` add (and UPDATE any existing `FormatTotal` tests asserting the old `"IN"/"OUT"` wording to the new expectations below):

```csharp
    [Test]
    public void FormatTotal_Screws_UsesClockwiseWording() {
        Assert.Multiple(() => {
            Assert.That(TiltAdapterGuidanceVM.FormatTotal(1.25, steps: false, hasDirection: true), Is.EqualTo("1.25 turns CW"));
            Assert.That(TiltAdapterGuidanceVM.FormatTotal(-0.5, steps: false, hasDirection: true), Is.EqualTo("0.50 turns CCW"));
            Assert.That(TiltAdapterGuidanceVM.FormatTotal(0.75, steps: false, hasDirection: false), Is.EqualTo("0.75 turns"));
            Assert.That(TiltAdapterGuidanceVM.FormatTotal(0.001, steps: false, hasDirection: true), Is.EqualTo("—"));
        });
    }

    [Test]
    public void FormatTotal_Steppers_UsesSignedSteps() {
        Assert.Multiple(() => {
            Assert.That(TiltAdapterGuidanceVM.FormatTotal(35.2, steps: true, hasDirection: true), Is.EqualTo("+35 steps"));
            Assert.That(TiltAdapterGuidanceVM.FormatTotal(-35.2, steps: true, hasDirection: true), Is.EqualTo("−35 steps"));
            Assert.That(TiltAdapterGuidanceVM.FormatTotal(12.0, steps: true, hasDirection: false), Is.EqualTo("12 steps"));
            Assert.That(TiltAdapterGuidanceVM.FormatTotal(0.2, steps: true, hasDirection: true), Is.EqualTo("—"));
        });
    }

    [Test]
    public void BuildDirectionLegend_DescribesAdapterMotionAndProvenance() {
        int cwInwardSign = TiltScrewGeometry.CurvatureSignForCwDirection(true);
        int cwOutwardSign = TiltScrewGeometry.CurvatureSignForCwDirection(false);
        Assert.Multiple(() => {
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: false, cwOutwardSign, signIsMeasured: true),
                Is.EqualTo("⬆ = clockwise (adapter moves toward the camera)"));
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: false, cwInwardSign, signIsMeasured: true),
                Is.EqualTo("⬆ = clockwise (adapter moves toward the objective)"));
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: true, cwOutwardSign, signIsMeasured: false),
                Is.EqualTo("⬆ = + steps (adapter moves toward the camera) (assumed — set or measure in the Tilt Adapter Wizard)"));
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: false, 0, signIsMeasured: false), Is.Empty);
        });
    }
```

Add `using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;` to the test file if missing.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~TiltAdapterGuidanceVMTests"`

- [ ] **Step 3: Implement the formatter changes**

In `TiltScrewGuidanceRow.cs`:

1. Add `using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;` to the usings.
2. Replace `FormatTotal` (lines 78–95) with:
```csharp
        /// <summary>
        /// Format the signed total adjustment with an explicit direction: clockwise/counter-clockwise
        /// for screws, signed (+/−) steps for steppers. Positive input = the calibration "inward"
        /// direction (a CW screw turn / + steps). When no direction is known
        /// (<paramref name="hasDirection"/> false) only the magnitude is shown.
        /// </summary>
        public static string FormatTotal(double inwardAmount, bool steps, bool hasDirection) {
            if (steps) {
                long rounded = (long)Math.Round(Math.Abs(inwardAmount), MidpointRounding.AwayFromZero);
                if (rounded == 0) return "—";
                if (!hasDirection) return $"{rounded} steps";
                return inwardAmount >= 0 ? $"+{rounded} steps" : $"−{rounded} steps";
            }
            if (Math.Abs(inwardAmount) < 0.005) return "—";
            string magnitude = $"{Math.Abs(inwardAmount):0.00} turns";
            if (!hasDirection) return magnitude;
            return $"{magnitude} {(inwardAmount >= 0 ? "CW" : "CCW")}";
        }
```
3. Add new members after `PitchMismatchWarning`/`HasPitchMismatch` (lines 62–63):
```csharp
        // One-line legend tying the arrows/totals to physical adapter motion; empty when unknown.
        public string DirectionLegend { get; set; } = string.Empty;
        public bool HasDirectionLegend => !string.IsNullOrEmpty(DirectionLegend);

        /// <summary>
        /// Legend for the guidance table. The adapter-motion wording comes from the configured or
        /// measured curvature sign; "(assumed)" flags a sign never measured by the wizard.
        /// </summary>
        public static string BuildDirectionLegend(bool steps, int curvatureSign, bool signIsMeasured) {
            if (curvatureSign == 0) return string.Empty;
            bool cwTowardObjective = TiltScrewGeometry.CwMovesAdapterTowardObjectiveForSign(curvatureSign);
            string inwardWord = steps ? "+ steps" : "clockwise";
            string plateWord = cwTowardObjective ? "toward the objective" : "toward the camera";
            string assumed = signIsMeasured ? string.Empty : " (assumed — set or measure in the Tilt Adapter Wizard)";
            return $"⬆ = {inwardWord} (adapter moves {plateWord}){assumed}";
        }
```

- [ ] **Step 4: Wire the legend in InspectorVM**

In `InspectorVM.cs`, inside `RebuildTiltGuidance()` — after the backfocus-row block closes (the `if (curvatureSign != 0 && SensorModel?.DisplayedSensorModel != null) { ... }` block ending at line 1884) and still inside the `if (HasTiltAdapterCalibration)` block — add:

```csharp
                guidance.DirectionLegend = TiltAdapterGuidanceVM.BuildDirectionLegend(
                    steps: tiltAdapterOptions.AdjustmentType == TiltAdjustmentType.StepperMotors,
                    curvatureSign: curvatureSign,
                    signIsMeasured: tiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured);
```

Note `curvatureSign` is already in scope (declared at line 1864). `TiltAdjustmentType` is already resolvable in this file (it uses `TiltAdjustmentType.StepperMotors` at line 1905).

- [ ] **Step 5: Add the legend line to the inspector guidance XAML**

In `AutoFocus/DataTemplates.xaml`, find the numeric-guidance grid's closing tag — the `</Grid>` immediately after the `Screw4TotalAmount` TextBlock (line ~2972), just before the comment `<!--  Saved-vs-measured pitch mismatch warning  -->`. Insert between them:

```xml
                            <!--  Direction legend: what ⬆ / CW / + steps mean for this adapter  -->
                            <TextBlock
                                Margin="5,4,5,0"
                                FontStyle="Italic"
                                Opacity="0.7"
                                Text="{Binding TiltGuidance.DirectionLegend}"
                                TextWrapping="Wrap"
                                Visibility="{Binding TiltGuidance.HasDirectionLegend, Converter={StaticResource BooleanToVisibilityCollapsedConverter}}" />
```

- [ ] **Step 6: Run tests** — fixture filter, then full suite (this also compiles the XAML). Update any other failing tests that asserted the old IN/OUT strings.

- [ ] **Step 7: Commit**

```bash
git add -A
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(tilt): CW/CCW and signed-step guidance totals with adapter-direction legend"
```

---

### Task 6: InspectorVM — dynamic image-count summary for Signal Amplification

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorVM.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/InspectorVMTests.cs`

- [ ] **Step 1: Write the failing tests**

In `InspectorVMTests.cs` add:

```csharp
        [Test]
        public void EstimateImagesPerRun_UsesOverridesThenProfileFallbacks() {
            Assert.Multiple(() => {
                // StepCount/FramesPerPoint unset (-1) => profile values; amp 2 doubles offset steps.
                Assert.That(InspectorVM.EstimateImagesPerRun(-1, -1, 2, 4, 1), Is.EqualTo((17, 17)));
                // Explicit overrides win over profile values.
                Assert.That(InspectorVM.EstimateImagesPerRun(3, 2, 1, 4, 1), Is.EqualTo((7, 14)));
                // Unknown when neither source is positive.
                Assert.That(InspectorVM.EstimateImagesPerRun(-1, 1, 2, 0, 1), Is.EqualTo((0, 0)));
            });
        }

        [Test]
        public void BuildSignalAmplificationSummary_DescribesImageCount() {
            var text = InspectorVM.BuildSignalAmplificationSummary(-1, -1, 2, 4, 1);
            Assert.Multiple(() => {
                Assert.That(text, Does.Contain("~17 images"));
                Assert.That(text, Does.Contain("17 focus positions"));
                Assert.That(text, Does.Contain("1 runs a regular autofocus"));
            });
            Assert.That(InspectorVM.BuildSignalAmplificationSummary(-1, -1, 2, 0, 0), Is.Empty);
        }
```

- [ ] **Step 2: Run to verify failure** (missing members).

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~InspectorVMTests"`

- [ ] **Step 3: Implement**

In `InspectorVM.cs`, next to `ApplySignalAmplification` (after line 1115), add:

```csharp
        // Estimated sweep size for one live sensor-model autofocus run at the given settings.
        // points ≈ 2·offsetSteps·amp + 1 (the engine can extend a sweep, so callers label it "~");
        // images = points × framesPerPoint. Returns (0, 0) when the inputs cannot be resolved.
        internal static (int points, int images) EstimateImagesPerRun(
            int stepCount, int framesPerPoint, int signalAmplification, int profileOffsetSteps, int profileFramesPerPoint) {
            int offsetSteps = stepCount > 0 ? stepCount : profileOffsetSteps;
            int frames = framesPerPoint > 0 ? framesPerPoint : profileFramesPerPoint;
            if (offsetSteps <= 0 || frames <= 0) return (0, 0);
            int amp = Math.Max(1, signalAmplification);
            int points = 2 * offsetSteps * amp + 1;
            return (points, points * frames);
        }

        // Prose shown beside the Signal Amplification control. Internal for tests.
        internal static string BuildSignalAmplificationSummary(
            int stepCount, int framesPerPoint, int signalAmplification, int profileOffsetSteps, int profileFramesPerPoint) {
            var (points, images) = EstimateImagesPerRun(stepCount, framesPerPoint, signalAmplification, profileOffsetSteps, profileFramesPerPoint);
            if (images <= 0) return string.Empty;
            int frames = framesPerPoint > 0 ? framesPerPoint : profileFramesPerPoint;
            return $"Each auto focus run will capture ~{images} images ({points} focus positions × {frames} exposure{(frames == 1 ? "" : "s")} each). " +
                "Higher values collect more, finer-spaced points for a steadier fit on weak signal; 1 runs a regular autofocus (fastest).";
        }

        public string SignalAmplificationSummary =>
            BuildSignalAmplificationSummary(
                inspectorOptions.StepCount,
                inspectorOptions.FramesPerPoint,
                inspectorOptions.SignalAmplification,
                profileService.ActiveProfile.FocuserSettings.AutoFocusInitialOffsetSteps,
                profileService.ActiveProfile.FocuserSettings.AutoFocusNumberOfFramesPerPoint);
```

In the testing constructor, immediately before the `this.tiltAdapterOptions = tiltAdapterOptions;` line (line ~173), add a change subscription so the prose live-updates:

```csharp
            inspectorOptions.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(IInspectorOptions.SignalAmplification) ||
                    e.PropertyName == nameof(IInspectorOptions.StepCount) ||
                    e.PropertyName == nameof(IInspectorOptions.FramesPerPoint)) {
                    RaisePropertyChanged(nameof(SignalAmplificationSummary));
                }
            };
            profileService.ProfileChanged += (s, e) => RaisePropertyChanged(nameof(SignalAmplificationSummary));
```

- [ ] **Step 4: Run tests** — fixture filter, then full suite. PASS required.

- [ ] **Step 5: Commit**

```bash
git add -A
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(inspector): dynamic image-count summary for Signal Amplification"
```

---

### Task 7: Inspector XAML — surface the two sweep settings above the Options expander

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/DataTemplates.xaml`

No unit tests possible for XAML; the full-suite build compiles it (`dotnet test` builds the sln).

- [ ] **Step 1: Update the CenterFocuserBeforeRun tooltip** (line 31) — the default changed. Replace the resource with:

```xml
    <TextBlock x:Key="CenterFocuserBeforeRun_Tooltip" Text="When on, a quick standard AutoFocus is run before each live sensor-model / tilt calibration sweep to center the focuser at best focus. The detailed sweep then brackets focus symmetrically, which reduces extreme one-sided defocus frames that fail to align. Off by default; it adds a full AutoFocus run to every sweep. Has no effect when replaying saved frames." />
```

- [ ] **Step 2: Add the always-visible sweep-settings block**

The buttons grid is `<Grid Grid.Row="0">` opening at line 1181 with its own `<Grid.RowDefinitions>` (two `Auto` rows at lines 1183–1184 — verify the actual count when editing). Append one more `<RowDefinition Height="Auto" />` to that inner list, then insert the following block immediately before the buttons grid's closing `</Grid>` (line ~1394, directly before the `<Expander Grid.Row="1" ... Header="Options">`). Set `Grid.Row` to the new last row index (expected `2`; adjust if the count differs):

```xml
                    <!--  Sweep-cost settings: kept out of the Options expander because they directly
                          control how long every sensor-model / tilt-calibration run takes.  -->
                    <Grid Grid.Row="2" Margin="0,4,0,0">
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="Auto" />
                        </Grid.RowDefinitions>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>
                        <TextBlock
                            Grid.Row="0"
                            Grid.Column="0"
                            Margin="5"
                            VerticalAlignment="Center"
                            Text="Signal Amplification"
                            ToolTip="{StaticResource SignalAmplification_Tooltip}" />
                        <ninactrl:UnitTextBox
                            Grid.Row="0"
                            Grid.Column="1"
                            MinWidth="40"
                            VerticalAlignment="Center"
                            HorizontalContentAlignment="Left"
                            VerticalContentAlignment="Center"
                            Foreground="{StaticResource PrimaryBrush}"
                            TextAlignment="Left"
                            ToolTip="{StaticResource SignalAmplification_Tooltip}"
                            Unit="x">
                            <Binding
                                Mode="TwoWay"
                                Path="InspectorOptions.SignalAmplification"
                                UpdateSourceTrigger="LostFocus" />
                        </ninactrl:UnitTextBox>
                        <TextBlock
                            Grid.Row="0"
                            Grid.Column="2"
                            Margin="10,5,5,5"
                            VerticalAlignment="Center"
                            FontStyle="Italic"
                            Opacity="0.7"
                            Text="{Binding SignalAmplificationSummary}"
                            TextWrapping="Wrap" />
                        <TextBlock
                            Grid.Row="1"
                            Grid.Column="0"
                            Margin="5"
                            VerticalAlignment="Center"
                            Text="Center Focuser First"
                            ToolTip="{StaticResource CenterFocuserBeforeRun_Tooltip}" />
                        <CheckBox
                            Grid.Row="1"
                            Grid.Column="1"
                            Margin="5,0,0,0"
                            HorizontalAlignment="Left"
                            VerticalAlignment="Center"
                            IsChecked="{Binding InspectorOptions.CenterFocuserBeforeRun}"
                            ToolTip="{StaticResource CenterFocuserBeforeRun_Tooltip}" />
                        <TextBlock
                            Grid.Row="1"
                            Grid.Column="2"
                            Margin="10,5,5,5"
                            VerticalAlignment="Center"
                            FontStyle="Italic"
                            Opacity="0.7"
                            Text="Runs one standard autofocus to center the focuser before each measurement sweep. Turn on if your focuser drifts between runs or starts far from best focus; leave off to save time."
                            TextWrapping="Wrap" />
                    </Grid>
```

- [ ] **Step 3: Remove the two rows from the Options expander**

Inside the Options expander (opens line 1395):
1. Delete the "Signal Amplification" label + `UnitTextBox` (lines 1760–1782) and the "Center Focuser First" label + `StackPanel`/CheckBox (lines 1784–1800) — all six elements are `Grid.Row="7"`.
2. In the expander grid's `<Grid.RowDefinitions>` (lines 1410–1421: nine plain `Auto` rows + one styled `HideOnInterpolationOff` row), delete ONE plain `<RowDefinition Height="Auto" />` so eight plain rows + the styled row remain.
3. Renumber the two elements that referenced later rows: the `Experimental` expander `Grid.Row="8"` → `Grid.Row="7"`; any active (non-commented) element with `Grid.Row="9"` → `Grid.Row="8"` (the interpolation row's controls are commented out — check before assuming).

- [ ] **Step 4: Build + full suite**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
Expected: PASS (XAML compiles; no test changes in this task).

- [ ] **Step 5: Commit**

```bash
git add -A
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(inspector): surface Signal Amplification and Center Focuser First above Options with prose"
```

---

### Task 8: TiltAdapterWizardVM — 4-step sequencing, direction property, manual entry, sweep summary, reworded prompts

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltAdapterWizardVM.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltCalibrationMetadata.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterWizard/TiltAdapterWizardVMTests.cs`

- [ ] **Step 1: Write the failing tests for the new statics**

In `TiltAdapterWizardVMTests.cs` add (namespace `NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterWizard`; the statics need no VM instance):

```csharp
    [Test]
    public void GetMeasurementSteps_FourStepFlowSkipsCurvatureSteps() {
        Assert.Multiple(() => {
            Assert.That(TiltAdapterWizardVM.GetMeasurementSteps(measureCurvature: true), Is.EqualTo(new[] {
                WizardStep.Baseline, WizardStep.AllInward, WizardStep.ReBaseline1,
                WizardStep.Screw1, WizardStep.ReBaseline2, WizardStep.Screw2 }));
            Assert.That(TiltAdapterWizardVM.GetMeasurementSteps(measureCurvature: false), Is.EqualTo(new[] {
                WizardStep.Baseline, WizardStep.Screw1, WizardStep.ReBaseline2, WizardStep.Screw2 }));
        });
    }

    [Test]
    public void StepInstructionsText_Screws_UsesClockwiseWordingAndAmount() {
        Assert.Multiple(() => {
            Assert.That(TiltAdapterWizardVM.StepInstructionsText(WizardStep.Baseline, 3, isStepper: false, appliedAmount: 1.0),
                Does.Contain("consistent clockwise order").And.Contain("starting position"));
            Assert.That(TiltAdapterWizardVM.StepInstructionsText(WizardStep.AllInward, 3, false, 1.0),
                Does.Contain("ALL screws CLOCKWISE (tighten) exactly 1 full turn"));
            Assert.That(TiltAdapterWizardVM.StepInstructionsText(WizardStep.ReBaseline1, 3, false, 1.0),
                Does.Contain("COUNTER-CLOCKWISE (loosen) exactly 1 full turn"));
            Assert.That(TiltAdapterWizardVM.StepInstructionsText(WizardStep.Screw1, 4, false, 1.0),
                Does.Contain("screw 1 CLOCKWISE").And.Contain("screw 3 COUNTER-CLOCKWISE"));
            Assert.That(TiltAdapterWizardVM.StepInstructionsText(WizardStep.Screw2, 3, false, 1.5),
                Does.Contain("screw 2 CLOCKWISE exactly 1.5 turns"));
        });
    }

    [Test]
    public void StepInstructionsText_Steppers_UsesSignedSteps() {
        Assert.Multiple(() => {
            Assert.That(TiltAdapterWizardVM.StepInstructionsText(WizardStep.AllInward, 3, isStepper: true, appliedAmount: 2.0),
                Does.Contain("+2 steps to EVERY motor"));
            Assert.That(TiltAdapterWizardVM.StepInstructionsText(WizardStep.ReBaseline2, 3, true, 2.0),
                Does.Contain("−2 steps to motor 1"));
            Assert.That(TiltAdapterWizardVM.StepInstructionsText(WizardStep.Screw1, 4, true, 2.0),
                Does.Contain("+2 steps to motor 1").And.Contain("−2 steps to motor 3"));
        });
    }

    [Test]
    public void BuildSweepSummary_CountsSweepsAndImages() {
        // 4 steps × 2 averaged measurements = 8 sweeps; profile: 4 offset steps, 1 frame, amp 2 => 17 images/run.
        var text = TiltAdapterWizardVM.BuildSweepSummary(4, 2, -1, -1, 2, 4, 1);
        Assert.Multiple(() => {
            Assert.That(text, Does.Contain("~17 images"));
            Assert.That(text, Does.Contain("8 sweeps"));
            Assert.That(text, Does.Contain("136 images total"));
        });
        Assert.That(TiltAdapterWizardVM.BuildSweepSummary(4, 1, -1, -1, 2, 0, 0), Is.Empty);
    }
```

- [ ] **Step 2: Run to verify failure** (missing statics).

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~TiltAdapterWizardVMTests"`

- [ ] **Step 3: Implement sequencing**

In `TiltAdapterWizardVM.cs`:

1. Replace the static `MeasurementSteps` field (lines 118–122) with:
```csharp
        // The measurement steps in capture order. The 4-step flow (default) skips the two
        // curvature-direction steps; the Baseline reading then serves as the screw-1 reference
        // (the ReBaseline1 role of the 6-step flow).
        internal static WizardStep[] GetMeasurementSteps(bool measureCurvature) =>
            measureCurvature
                ? new[] { WizardStep.Baseline, WizardStep.AllInward, WizardStep.ReBaseline1,
                          WizardStep.Screw1, WizardStep.ReBaseline2, WizardStep.Screw2 }
                : new[] { WizardStep.Baseline, WizardStep.Screw1, WizardStep.ReBaseline2, WizardStep.Screw2 };

        // Captured at run start (StartAsync/ReplayAsync) so toggling the option mid-run is inert.
        private WizardStep[] activeMeasurementSteps = GetMeasurementSteps(measureCurvature: false);
```
2. `IsOnMeasurementStep` (line 283) → `public bool IsOnMeasurementStep => activeMeasurementSteps.Contains(currentStep);`
3. In `StartAsync()` (before `CurrentStep = WizardStep.Baseline;`, line 713), add:
```csharp
            activeMeasurementSteps = GetMeasurementSteps(tiltAdapterOptions.MeasureCurvatureDuringCalibration);
```
4. Rewrite `NextStep()` (lines 1021–1041) — sequence advance is index-based now:
```csharp
        private void NextStep() {
            int idx = Array.IndexOf(activeMeasurementSteps, currentStep);
            WizardStep next = (idx < 0 || idx == activeMeasurementSteps.Length - 1)
                ? WizardStep.Complete
                : activeMeasurementSteps[idx + 1];

            if (next == WizardStep.Complete) {
                RunCalibrationMath(
                    tiltAdapterOptions.ScrewCount,
                    tiltAdapterOptions.ScrewRadiusMillimeters,
                    profileService.ActiveProfile.CameraSettings.PixelSize,
                    EffectiveFocuserStepMicrons(),
                    calibrationAppliedAmount,
                    IsStepperAdjustment);
                RebuildDiagram();
                FinalizeMetadata();
            }

            HasMeasurementConsistencyWarning = false;
            MeasurementConsistencyWarningText = string.Empty;
            StatusText = string.Empty;
            ClearMeasurementFailureChoice();
            CurrentStep = next;
        }
```

- [ ] **Step 4: Implement the calibration-math branching**

Rewrite the top of `RunCalibrationMath` (lines 1113–1123). The rule: curvature was measured iff an `AllInward` reading exists (works for live runs and replays of old 6-step runs alike). In the 4-step flow the Baseline reading takes the `c` role:

```csharp
        private void RunCalibrationMath(int screwCount, double radiusMm, double pixelSize, double fStep,
            double appliedAmount, bool isStepper) {
            bool measuredCurvature = stepReadings.ContainsKey(WizardStep.AllInward);
            var a = Reading(WizardStep.Baseline);
            var b = Reading(WizardStep.AllInward);
            var c = measuredCurvature ? Reading(WizardStep.ReBaseline1) : Reading(WizardStep.Baseline);
            var d = Reading(WizardStep.Screw1);
            var e = Reading(WizardStep.ReBaseline2);
            var f = Reading(WizardStep.Screw2);

            // Curvature (backfocus) sign from baseline (a) → all-inward (b) mean focus, only when
            // those steps ran; otherwise the configured/assumed sign is left untouched.
            if (measuredCurvature) {
                tiltAdapterOptions.ScrewInwardCurvatureSign = TiltCalibrationCalculator.ComputeCurvatureSign(b.Mean, a.Mean);
                tiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured = true;
            }
```

After `tiltAdapterOptions.IsCalibrated = true;` (line 1138) add:
```csharp
            tiltAdapterOptions.CalibrationIsManual = false;
```

Both `TiltCalibrationInputs` constructions in this method (the hardware-recovery one at lines 1152–1167 and the confidence one at lines 1180–1188) get three edits each:
- `ReBaseline1 = new TiltGradient(c.A, c.B, c.Mean),` (unchanged text — `c` now carries the right reading in both flows), and add:
```csharp
                    HasCurvatureMeasurement = measuredCurvature,
                    FallbackCurvatureSign = tiltAdapterOptions.ScrewInwardCurvatureSign,
```

Rewrite `EvaluateRebaselineDrift` (lines 1234–1256) — drift1 only exists in the 6-step flow:

```csharp
        private void EvaluateRebaselineDrift() {
            bool measuredCurvature = stepReadings.ContainsKey(WizardStep.AllInward);
            var a = Reading(WizardStep.Baseline);
            var c = measuredCurvature ? Reading(WizardStep.ReBaseline1) : Reading(WizardStep.Baseline);
            var d = Reading(WizardStep.Screw1);
            var e = Reading(WizardStep.ReBaseline2);
            var f = Reading(WizardStep.Screw2);

            double drift1 = measuredCurvature
                ? TiltCalibrationCalculator.RebaselineDriftRatio(c.A - a.A, c.B - a.B, d.A - c.A, d.B - c.B)
                : double.NaN;
            double drift2 = TiltCalibrationCalculator.RebaselineDriftRatio(e.A - c.A, e.B - c.B, f.A - e.A, f.B - e.B);

            var parts = new List<string>(2);
            if (!double.IsNaN(drift1) && drift1 > RebaselineDriftWarnThreshold) {
                parts.Add($"re-baseline 1 drifted {drift1 * 100.0:F0}% of the screw-1 move");
            }
            if (!double.IsNaN(drift2) && drift2 > RebaselineDriftWarnThreshold) {
                parts.Add($"re-baseline 2 drifted {drift2 * 100.0:F0}% of the screw-2 move");
            }
            HasRebaselineDriftWarning = parts.Count > 0;
            RebaselineDriftWarningText = parts.Count == 0
                ? string.Empty
                : "Re-baseline drift detected: " + string.Join("; ", parts) +
                    ". The undo between moves left residual tilt (backlash or an uneven turn); consider recalibrating.";
        }
```

- [ ] **Step 5: Implement the reworded, testable step instructions**

Replace the `StepInstructions` property (lines 500–530) with a delegating property + static, and extend `BaselineRecoveryText`:

```csharp
        public string StepInstructions =>
            StepInstructionsText(currentStep, tiltAdapterOptions.ScrewCount, IsStepperAdjustment, calibrationAppliedAmount);

        // Formats the calibration move amount: "1 full turn" / "1.5 turns" for screws, whole "+N"
        // magnitude for steppers (sign is added by the caller's wording).
        internal static string FormatAppliedAmount(bool isStepper, double amount) =>
            isStepper ? $"{amount:0.##}" : (amount == 1.0 ? "1 full turn" : $"{amount:0.##} turns");

        // All wizard prompts. Screw motion is worded as CLOCKWISE/COUNTER-CLOCKWISE (tighten/loosen)
        // — never "inward/outward", which this plugin reserves for adapter-plate motion. Stepper
        // prompts use signed steps; "+" is the direction the guidance later reports as positive.
        internal static string StepInstructionsText(WizardStep step, int screwCount, bool isStepper, double appliedAmount) {
            string amt = FormatAppliedAmount(isStepper, appliedAmount);
            bool four = screwCount == 4;
            switch (step) {
                case WizardStep.Baseline:
                    return "Label your screws 1, 2, and 3 (or 1–4 for a 4-screw adapter) in a consistent clockwise order. " +
                        "Screw 1 does NOT need to be at any particular clock position — the wizard determines each screw's actual " +
                        "position from the measurements.\n\nEnsure all screws are at their starting position, then click Run Measurement to take a baseline reading.";
                case WizardStep.AllInward:
                    return isStepper
                        ? $"Apply +{amt} steps to EVERY motor, then click Run Measurement."
                        : $"Turn ALL screws CLOCKWISE (tighten) exactly {amt} each, then click Run Measurement.";
                case WizardStep.ReBaseline1:
                    return isStepper
                        ? $"Apply −{amt} steps to every motor, returning to the baseline position, then click Run Measurement."
                        : $"Turn ALL screws back COUNTER-CLOCKWISE (loosen) exactly {amt} each, returning to the baseline position, then click Run Measurement.";
                case WizardStep.Screw1:
                    if (isStepper) {
                        return four
                            ? $"Apply +{amt} steps to motor 1 and −{amt} steps to motor 3, then click Run Measurement."
                            : $"Apply +{amt} steps to motor 1, then click Run Measurement.";
                    }
                    return four
                        ? $"Turn screw 1 CLOCKWISE and screw 3 COUNTER-CLOCKWISE exactly {amt} each, then click Run Measurement."
                        : $"Turn screw 1 CLOCKWISE exactly {amt}, then click Run Measurement.";
                case WizardStep.ReBaseline2:
                    if (isStepper) {
                        return four
                            ? $"Apply −{amt} steps to motor 1 and +{amt} steps to motor 3, returning to the baseline position, then click Run Measurement."
                            : $"Apply −{amt} steps to motor 1, returning to the baseline position, then click Run Measurement.";
                    }
                    return four
                        ? $"Turn screw 1 back COUNTER-CLOCKWISE and screw 3 back CLOCKWISE exactly {amt} each, returning to the baseline position, then click Run Measurement."
                        : $"Turn screw 1 back COUNTER-CLOCKWISE exactly {amt}, returning to the baseline position, then click Run Measurement.";
                case WizardStep.Screw2:
                    if (isStepper) {
                        return four
                            ? $"Apply +{amt} steps to motor 2 and −{amt} steps to motor 4, then click Run Measurement."
                            : $"Apply +{amt} steps to motor 2, then click Run Measurement.";
                    }
                    return four
                        ? $"Turn screw 2 CLOCKWISE and screw 4 COUNTER-CLOCKWISE exactly {amt} each, then click Run Measurement."
                        : $"Turn screw 2 CLOCKWISE exactly {amt}, then click Run Measurement.";
                case WizardStep.Complete:
                    return isStepper
                        ? "Return all motors to their original position."
                        : "Restore all screws to their original position.";
                default:
                    return string.Empty;
            }
        }
```

Update `BaselineRecoveryText` (lines 547–562) to the same vocabulary — new signature `internal static string BaselineRecoveryText(WizardStep step, int screwCount, bool isStepper, double appliedAmount)`:

```csharp
        internal static string BaselineRecoveryText(WizardStep step, int screwCount, bool isStepper, double appliedAmount) {
            string amt = FormatAppliedAmount(isStepper, appliedAmount);
            bool four = screwCount == 4;
            switch (step) {
                case WizardStep.AllInward:
                    return isStepper
                        ? $"Apply −{amt} steps to every motor, returning to the baseline position."
                        : $"Turn ALL screws back COUNTER-CLOCKWISE exactly {amt} each, returning to the baseline position.";
                case WizardStep.Screw1:
                    if (isStepper) {
                        return four
                            ? $"Apply −{amt} steps to motor 1 and +{amt} steps to motor 3, returning to the baseline position."
                            : $"Apply −{amt} steps to motor 1, returning to the baseline position.";
                    }
                    return four
                        ? $"Turn screw 1 back COUNTER-CLOCKWISE and screw 3 back CLOCKWISE exactly {amt} each, returning to the baseline position."
                        : $"Turn screw 1 back COUNTER-CLOCKWISE exactly {amt}, returning to the baseline position.";
                case WizardStep.Screw2:
                    if (isStepper) {
                        return four
                            ? $"Apply −{amt} steps to motor 2 and +{amt} steps to motor 4, returning to the baseline position."
                            : $"Apply −{amt} steps to motor 2, returning to the baseline position.";
                    }
                    return four
                        ? $"Turn screw 2 back COUNTER-CLOCKWISE and screw 4 back CLOCKWISE exactly {amt} each, returning to the baseline position."
                        : $"Turn screw 2 back COUNTER-CLOCKWISE exactly {amt}, returning to the baseline position.";
                default:
                    return "All screws should already be at the baseline (starting) position.";
            }
        }
```

Update its caller `BaselineRecoveryInstructions` (line 433):
```csharp
        public string BaselineRecoveryInstructions =>
            BaselineRecoveryText(currentStep, tiltAdapterOptions.ScrewCount, IsStepperAdjustment, calibrationAppliedAmount);
```

The prompts now embed the applied amount, so editing it must refresh them — in the `CalibrationAppliedAmount` setter (lines 576–585), after the existing `RaisePropertyChanged(nameof(CalibrationAppliedAmountDisplay));` add:
```csharp
                    RaisePropertyChanged(nameof(StepInstructions));
                    RaisePropertyChanged(nameof(BaselineRecoveryInstructions));
```

- [ ] **Step 6: Implement direction property, provenance, manual entry, sweep summary**

1. Replace `CurvatureSignDescription` (lines 347–350) with:
```csharp
        public string CurvatureSignDescription {
            get {
                int sign = tiltAdapterOptions.ScrewInwardCurvatureSign;
                if (sign == 0) return string.Empty;
                string arrow = sign == 1 ? "↑" : "↓";
                return tiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured ? $"{arrow} (measured)" : $"{arrow} (assumed)";
            }
        }
```
2. Add new properties near `IsStepperAdjustment` (line 587):
```csharp
        // Mechanical framing of ScrewInwardCurvatureSign: does a CW screw turn (or +steps) move the
        // adapter plate toward the objective? Editing writes the sign (and marks it assumed); a
        // 6-step wizard measurement overwrites the sign and this re-reads it.
        public bool CwMovesAdapterTowardObjective {
            get {
                int sign = tiltAdapterOptions.ScrewInwardCurvatureSign;
                if (sign == 0) sign = TiltScrewGeometry.DefaultScrewInwardCurvatureSign;
                return TiltScrewGeometry.CwMovesAdapterTowardObjectiveForSign(sign);
            }
            set {
                int sign = TiltScrewGeometry.CurvatureSignForCwDirection(value);
                if (tiltAdapterOptions.ScrewInwardCurvatureSign != sign) {
                    tiltAdapterOptions.ScrewInwardCurvatureSign = sign;
                    tiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured = false;
                    RaisePropertyChanged();
                }
            }
        }

        public string CwDirectionLabel => IsStepperAdjustment
            ? "Applying + steps moves the adapter"
            : "Turning screws clockwise moves the adapter";

        public string CurvatureSignProvenance => tiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured
            ? "Direction was measured by a calibration run."
            : "Direction is assumed — enable the measurement below (or run a 6-step calibration) to verify it.";
```
3. Manual entry state + command. Add fields/properties near `CalibrationAppliedAmount` (line 576):
```csharp
        private double manualScrew1AngleDegrees;

        // Manual Calibration Entry: screw 1 position angle in degrees, image space, 0° = straight
        // up (12 o'clock), increasing clockwise — the plugin-wide convention.
        public double ManualScrew1AngleDegrees {
            get => manualScrew1AngleDegrees;
            set {
                if (manualScrew1AngleDegrees != value) {
                    manualScrew1AngleDegrees = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool manualNumberingClockwise = true;

        // Whether screws 2..n proceed clockwise from screw 1 in the IMAGE (mirrors can flip it).
        public bool ManualNumberingClockwise {
            get => manualNumberingClockwise;
            set {
                if (manualNumberingClockwise != value) {
                    manualNumberingClockwise = value;
                    RaisePropertyChanged();
                }
            }
        }

        // Applies a manually entered calibration: same persisted state a wizard run writes, tagged
        // manual. The curvature sign is whatever the direction setting above holds (assumed).
        internal void ApplyManualCalibration() {
            int n = tiltAdapterOptions.ScrewCount;
            var (s1, s2, s3, s4) = TiltCalibrationCalculator.ComputeManualScrewAngles(manualScrew1AngleDegrees, manualNumberingClockwise, n);
            tiltAdapterOptions.Screw1AngleDegrees = s1;
            tiltAdapterOptions.Screw2AngleDegrees = s2;
            tiltAdapterOptions.Screw3AngleDegrees = s3;
            tiltAdapterOptions.Screw4AngleDegrees = s4;
            tiltAdapterOptions.CalibratedScrewCount = n;
            tiltAdapterOptions.IsCalibrated = true;
            tiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured = false;
            tiltAdapterOptions.CalibrationIsManual = true;
            RebuildDiagram();
        }
```
   Declare `public ICommand ApplyManualCalibrationCommand { get; }` with the other commands (line 564+) and initialize it in the constructor next to the others (line ~178): `ApplyManualCalibrationCommand = new RelayCommand(ApplyManualCalibration);`
   In the constructor, after `ApplyDevice(tiltAdapterOptions.DeviceName);` (line 226), pre-fill from an existing calibration:
```csharp
            if (!double.IsNaN(tiltAdapterOptions.Screw1AngleDegrees)) {
                manualScrew1AngleDegrees = tiltAdapterOptions.Screw1AngleDegrees;
            }
```
4. Sweep summary statics + property (place near `GetMeasurementSteps`):
```csharp
        // Prose for the wizard's Signal Amplification row: per-sweep image estimate plus the total
        // for the whole calibration at current settings. Internal for tests.
        internal static string BuildSweepSummary(int stepsInRun, int measurementAverage,
            int stepCount, int framesPerPoint, int signalAmplification, int profileOffsetSteps, int profileFramesPerPoint) {
            var (points, imagesPerRun) = InspectorVM.EstimateImagesPerRun(
                stepCount, framesPerPoint, signalAmplification, profileOffsetSteps, profileFramesPerPoint);
            if (imagesPerRun <= 0) return string.Empty;
            int frames = framesPerPoint > 0 ? framesPerPoint : profileFramesPerPoint;
            int sweeps = stepsInRun * Math.Max(1, measurementAverage);
            return $"Every calibration step runs a full autofocus sweep of ~{imagesPerRun} images ({points} focus positions × {frames} exposure{(frames == 1 ? "" : "s")}). " +
                $"At the current settings this calibration will take {sweeps} sweeps ≈ {sweeps * imagesPerRun} images total. " +
                "Increase for more signal on faint stars; decrease to run faster (1 = a regular autofocus).";
        }

        public string WizardSweepSummary {
            get {
                var inspectorOptions = inspector.InspectorOptions;
                if (inspectorOptions == null) return string.Empty;
                return BuildSweepSummary(
                    GetMeasurementSteps(tiltAdapterOptions.MeasureCurvatureDuringCalibration).Length,
                    tiltAdapterOptions.MeasurementAverageCount,
                    inspectorOptions.StepCount,
                    inspectorOptions.FramesPerPoint,
                    inspectorOptions.SignalAmplification,
                    profileService.ActiveProfile.FocuserSettings.AutoFocusInitialOffsetSteps,
                    profileService.ActiveProfile.FocuserSettings.AutoFocusNumberOfFramesPerPoint);
            }
        }
```
5. Change notifications. In the constructor's `tiltAdapterOptions.PropertyChanged` handler:
   - Extend the `ScrewInwardCurvatureSign` branch (lines 182–185) to also raise `CwMovesAdapterTowardObjective` and `CurvatureSignProvenance`.
   - Add a new branch:
```csharp
                if (e.PropertyName == nameof(ITiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured)) {
                    RaisePropertyChanged(nameof(CurvatureSignDescription));
                    RaisePropertyChanged(nameof(CurvatureSignProvenance));
                }
                if (e.PropertyName == nameof(ITiltAdapterOptions.MeasureCurvatureDuringCalibration) ||
                    e.PropertyName == nameof(ITiltAdapterOptions.MeasurementAverageCount)) {
                    RaisePropertyChanged(nameof(WizardSweepSummary));
                }
                if (e.PropertyName == nameof(ITiltAdapterOptions.CalibrationIsManual)) {
                    RaisePropertyChanged(nameof(IsCalibrationValid));
                }
```
   - In the `AdjustmentType` branch (lines 202–214), also raise `CwDirectionLabel` and `StepInstructions` and `BaselineRecoveryInstructions` (adjustment type changes the prompt wording).
   - After the `profileService.ProfileChanged` handler body (lines 217–223), add `RaisePropertyChanged(nameof(WizardSweepSummary));`.
   - Subscribe to the inspector options for live prose updates, after the tiltAdapterOptions handler (line ~215):
```csharp
            if (inspector.InspectorOptions != null) {
                inspector.InspectorOptions.PropertyChanged += (s, e) => OnUIThread(() => {
                    if (e.PropertyName == nameof(IInspectorOptions.SignalAmplification) ||
                        e.PropertyName == nameof(IInspectorOptions.StepCount) ||
                        e.PropertyName == nameof(IInspectorOptions.FramesPerPoint)) {
                        RaisePropertyChanged(nameof(WizardSweepSummary));
                    }
                });
            }
```

- [ ] **Step 7: Replay support for 4-step runs**

1. In `TiltCalibrationMetadata.cs`, after the `StepOrder` array (lines 76–78), add:
```csharp
        /// <summary>The 4 measurement steps of a run captured without the curvature-direction steps.</summary>
        public static readonly string[] StepOrderWithoutCurvature = {
            "Baseline", "Screw1", "ReBaseline2", "Screw2"
        };
```
2. In `TiltAdapterWizardVM.ReplayAsync`, the three uses of `MeasurementSteps` (lines 1322, 1331, 1381) switch to a locally derived array. After the `byStep` dictionary is built (line ~1321), insert:
```csharp
            // A run saved without the curvature steps replays as a 4-step run.
            bool replayHasCurvatureSteps = byStep.ContainsKey(WizardStep.AllInward.ToString());
            var replaySteps = GetMeasurementSteps(replayHasCurvatureSteps);
            activeMeasurementSteps = replaySteps;
```
   then replace `foreach (var step in MeasurementSteps)` (validation loop, line 1322) and the replay loop (line 1381) with `foreach (var step in replaySteps)`, and `MeasurementSteps.First()` (line 1331) with `replaySteps.First()`.

- [ ] **Step 8: Run tests** — `--filter "FullyQualifiedName~TiltAdapterWizard"`, then the full suite. Existing wizard tests that assert the old instruction strings or `BaselineRecoveryText(step, screwCount)` signature must be updated to the new wording/signature (search Tests for `BaselineRecoveryText` and `INWARD`).

- [ ] **Step 9: Commit**

```bash
git add -A
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(wizard): 4-step default flow, CW/CCW prompts, manual entry, direction setting, sweep summary"
```

---

### Task 9: Wizard XAML — Measurement section, direction controls, manual entry, provenance tags

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/DataTemplates.xaml`

- [ ] **Step 1: Add the "Measurement" section**

Insert after the "Focuser step size" `UniformGrid` closes (line 302) and before the `<!--  Calibrate + Replay side by side  -->` comment (line 304):

```xml
                    <!--  Measurement cost & direction settings (shared Signal Amplification /
                          Center Focuser First live in InspectorOptions — same values as the
                          Aberration Inspector's controls)  -->
                    <TextBlock
                        Margin="0,10,0,5"
                        FontWeight="Bold"
                        Text="Measurement" />
                    <Grid Margin="0,2">
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="Auto" />
                        </Grid.RowDefinitions>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="*" />
                        </Grid.ColumnDefinitions>

                        <TextBlock
                            Grid.Row="0"
                            Grid.Column="0"
                            Margin="0,2,5,2"
                            VerticalAlignment="Center"
                            Text="Signal Amplification"
                            ToolTip="Increases the resolution and signal of sensor-model / tilt calibration runs by capturing more, finer-spaced focuser points. Set to 1 to disable. Applies to live captures only." />
                        <ninactrl:UnitTextBox
                            Grid.Row="0"
                            Grid.Column="1"
                            MinWidth="40"
                            VerticalAlignment="Center"
                            HorizontalContentAlignment="Left"
                            VerticalContentAlignment="Center"
                            Foreground="{StaticResource PrimaryBrush}"
                            TextAlignment="Left"
                            Unit="x">
                            <Binding
                                Mode="TwoWay"
                                Path="Inspector.InspectorOptions.SignalAmplification"
                                UpdateSourceTrigger="LostFocus" />
                        </ninactrl:UnitTextBox>
                        <TextBlock
                            Grid.Row="0"
                            Grid.Column="2"
                            Margin="8,2,0,2"
                            VerticalAlignment="Center"
                            FontStyle="Italic"
                            Opacity="0.7"
                            Text="{Binding WizardSweepSummary}"
                            TextWrapping="Wrap" />

                        <TextBlock
                            Grid.Row="1"
                            Grid.Column="0"
                            Margin="0,2,5,2"
                            VerticalAlignment="Center"
                            Text="Center Focuser First"
                            ToolTip="When on, a quick standard AutoFocus is run before each live sweep to center the focuser at best focus. Off by default. No effect when replaying saved frames." />
                        <CheckBox
                            Grid.Row="1"
                            Grid.Column="1"
                            HorizontalAlignment="Left"
                            VerticalAlignment="Center"
                            IsChecked="{Binding Inspector.InspectorOptions.CenterFocuserBeforeRun}" />
                        <TextBlock
                            Grid.Row="1"
                            Grid.Column="2"
                            Margin="8,2,0,2"
                            VerticalAlignment="Center"
                            FontStyle="Italic"
                            Opacity="0.7"
                            Text="Adds one standard autofocus before every measurement sweep to re-center focus. Turn on if focus drifts between steps (e.g., temperature) or your focuser starts far from focus."
                            TextWrapping="Wrap" />

                        <TextBlock
                            Grid.Row="2"
                            Grid.Column="0"
                            Margin="0,2,5,2"
                            VerticalAlignment="Center"
                            Text="{Binding CwDirectionLabel}"
                            ToolTip="Whether tightening moves the adapter toward the camera or the objective depends on its design (push vs pull screws). Sets the direction of backfocus/curvature guidance." />
                        <ComboBox
                            Grid.Row="2"
                            Grid.Column="1"
                            MinWidth="180"
                            VerticalAlignment="Center"
                            SelectedValuePath="Tag"
                            SelectedValue="{Binding CwMovesAdapterTowardObjective}">
                            <ComboBox.Style>
                                <Style BasedOn="{StaticResource {x:Type ComboBox}}" TargetType="ComboBox">
                                    <Setter Property="IsEnabled" Value="True" />
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding TiltAdapterOptions.MeasureCurvatureDuringCalibration}" Value="True">
                                            <Setter Property="IsEnabled" Value="False" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </ComboBox.Style>
                            <ComboBoxItem Content="Toward the camera — outward">
                                <ComboBoxItem.Tag>
                                    <s:Boolean>False</s:Boolean>
                                </ComboBoxItem.Tag>
                            </ComboBoxItem>
                            <ComboBoxItem Content="Toward the objective — inward">
                                <ComboBoxItem.Tag>
                                    <s:Boolean>True</s:Boolean>
                                </ComboBoxItem.Tag>
                            </ComboBoxItem>
                        </ComboBox>
                        <TextBlock
                            Grid.Row="2"
                            Grid.Column="2"
                            Margin="8,2,0,2"
                            VerticalAlignment="Center"
                            FontStyle="Italic"
                            Opacity="0.7"
                            Text="{Binding CurvatureSignProvenance}"
                            TextWrapping="Wrap" />

                        <TextBlock
                            Grid.Row="3"
                            Grid.Column="0"
                            Margin="0,2,5,2"
                            VerticalAlignment="Center"
                            Text="Measure direction"
                            ToolTip="Include the two curvature-direction steps in the calibration to determine the adapter direction automatically." />
                        <CheckBox
                            Grid.Row="3"
                            Grid.Column="1"
                            HorizontalAlignment="Left"
                            VerticalAlignment="Center"
                            IsChecked="{Binding TiltAdapterOptions.MeasureCurvatureDuringCalibration}" />
                        <TextBlock
                            Grid.Row="3"
                            Grid.Column="2"
                            Margin="8,2,0,2"
                            VerticalAlignment="Center"
                            FontStyle="Italic"
                            Opacity="0.7"
                            Text="Adds 2 extra measurement steps (6 instead of 4 — about 50% longer) to determine the direction automatically. Turn on if you don't know how your adapter behaves, or to verify the setting above."
                            TextWrapping="Wrap" />
                    </Grid>
```

Note: the tooltips above are literal text (not `{StaticResource ...}`) on purpose — the `*_Tooltip` resources live in `AutoFocus/DataTemplates.xaml`, which is NOT merged into this file. Also confirm `ninactrl:` is declared in this file's root `ResourceDictionary` element; it is not today, so add the xmlns attribute — copy the exact `xmlns:ninactrl="..."` declaration string from the root element of `AutoFocus/DataTemplates.xaml` (do not guess the assembly name).

- [ ] **Step 2: Add the "Manually entered" tag to the saved-calibration panel**

Inside the saved-calibration `StackPanel` (opens line 373), before the `<Grid Margin="0,10,0,0" ...>` (line 385), insert:

```xml
                        <TextBlock
                            Margin="0,10,0,0"
                            FontStyle="Italic"
                            Opacity="0.7"
                            Text="Manually entered calibration (not measured by the wizard)."
                            TextWrapping="Wrap">
                            <TextBlock.Style>
                                <Style TargetType="TextBlock">
                                    <Setter Property="Visibility" Value="Collapsed" />
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding TiltAdapterOptions.CalibrationIsManual}" Value="True">
                                            <Setter Property="Visibility" Value="Visible" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </TextBlock.Style>
                        </TextBlock>
```

(The curvature row's "(measured)/(assumed)" annotation comes for free — it binds `CurvatureSignDescription`, which Task 8 extended.)

- [ ] **Step 3: Add the Manual Calibration Entry expander**

After the saved-calibration `StackPanel` closes (line 401) and before Panel A's closing `</StackPanel>` (line 402), insert:

```xml
                    <!--  Manual calibration entry: writes the same persisted state a wizard run
                          does (screw angles + calibrated flags), tagged as manually entered.  -->
                    <Expander Margin="0,10,0,0" Header="Manual Calibration Entry">
                        <StackPanel Margin="0,5,0,0">
                            <TextBlock
                                Margin="0,0,0,5"
                                FontStyle="Italic"
                                Opacity="0.7"
                                Text="If you already know where screw 1 sits, enter it here and skip the wizard. Angles and numbering direction are in image space — mirrors or diagonals in the optical train can flip them relative to the physical adapter. If guidance moves tilt the wrong way, flip the numbering direction. Set the adapter direction in the Measurement settings above if you know it."
                                TextWrapping="Wrap" />
                            <UniformGrid Margin="0,2" VerticalAlignment="Center" Columns="2">
                                <TextBlock VerticalAlignment="Center" Text="Screw 1 angle (°)"
                                    ToolTip="Position angle of screw 1 in the image: 0° = straight up (12 o'clock), increasing clockwise. Remaining screws are placed at equal spacing (120° for 3 screws, 90° for 4)." />
                                <TextBox MinWidth="60" Text="{Binding ManualScrew1AngleDegrees, UpdateSourceTrigger=LostFocus}" />
                            </UniformGrid>
                            <UniformGrid Margin="0,2" VerticalAlignment="Center" Columns="2">
                                <TextBlock VerticalAlignment="Center" Text="Numbering direction"
                                    ToolTip="Whether screws 2 and up proceed clockwise or counter-clockwise from screw 1, as seen in the image." />
                                <ComboBox MinWidth="120" SelectedValuePath="Tag" SelectedValue="{Binding ManualNumberingClockwise}">
                                    <ComboBoxItem Content="Clockwise">
                                        <ComboBoxItem.Tag>
                                            <s:Boolean>True</s:Boolean>
                                        </ComboBoxItem.Tag>
                                    </ComboBoxItem>
                                    <ComboBoxItem Content="Counter-clockwise">
                                        <ComboBoxItem.Tag>
                                            <s:Boolean>False</s:Boolean>
                                        </ComboBoxItem.Tag>
                                    </ComboBoxItem>
                                </ComboBox>
                            </UniformGrid>
                            <Button
                                Margin="0,8,0,0"
                                HorizontalAlignment="Left"
                                Command="{Binding ApplyManualCalibrationCommand}">
                                <TextBlock
                                    Margin="10,5,10,5"
                                    Foreground="{StaticResource ButtonForegroundBrush}"
                                    Text="Apply" />
                            </Button>
                        </StackPanel>
                    </Expander>
```

- [ ] **Step 4: Build + full suite**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
Expected: PASS. (XAML compile errors surface here; the most likely one is a missing `ninactrl` xmlns — see the note in Step 1.)

- [ ] **Step 5: Commit**

```bash
git add -A
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(wizard): Measurement settings section, adapter-direction control, manual calibration entry UI"
```

---

### Task 10: TestApp TiltCalibrationRunner — accept 4-step runs

**Files:**
- Modify: `Joko.NINA.Plugins/TestApp/TiltCalibrationRunner.cs`

The headless validator currently hard-requires all 6 steps. Make it accept either flow. (No unit tests exist for this Exe-resident file; the linked-in tests don't cover it. Verify by build + a careful read-through.)

- [ ] **Step 1: Implement**

1. In `MapRunsToSteps` (lines 272–316): compute the expected step set instead of always using `StepOrder`:
```csharp
            // Runs saved by the 4-step wizard flow have no AllInward/ReBaseline1 steps.
            string[] expectedSteps;
            if (metadata.RunStepMapping != null && metadata.RunStepMapping.Count > 0) {
                expectedSteps = metadata.RunStepMapping.Any(m => string.Equals(m.Step, "AllInward", StringComparison.OrdinalIgnoreCase))
                    ? StepOrder
                    : TiltCalibrationMetadata.StepOrderWithoutCurvature;
            } else {
                if (runFolders.Count == StepOrder.Length) {
                    expectedSteps = StepOrder;
                } else if (runFolders.Count == TiltCalibrationMetadata.StepOrderWithoutCurvature.Length) {
                    expectedSteps = TiltCalibrationMetadata.StepOrderWithoutCurvature;
                } else {
                    throw new InvalidOperationException(
                        $"Expected {StepOrder.Length} run folders ({string.Join(", ", StepOrder)}) or " +
                        $"{TiltCalibrationMetadata.StepOrderWithoutCurvature.Length} ({string.Join(", ", TiltCalibrationMetadata.StepOrderWithoutCurvature)}) under {datasetDir}, " +
                        $"but found {runFolders.Count}. Provide an explicit 'runStepMapping' in the metadata to disambiguate.");
                }
            }
```
   Then use `expectedSteps` in place of `StepOrder` for the no-mapping ordered list (`ordered = runFolders.Select((f, i) => (expectedSteps[i], f)).ToList();`) and in the required-steps validation loop (`foreach (var required in expectedSteps)`).
2. In `RunImpl`, both `TiltCalibrationInputs` constructions (lines ~207–225 and the paraboloid one at ~240–258) must handle the short flow. Before the first construction add:
```csharp
            bool hasCurvatureSteps = byStep.ContainsKey("AllInward");
```
   and in each initializer replace the `Baseline`/`AllInward`/`ReBaseline1` lines with:
```csharp
                Baseline = hasCurvatureSteps ? byStep["Baseline"].Gradient : default,
                AllInward = hasCurvatureSteps ? byStep["AllInward"].Gradient : default,
                ReBaseline1 = hasCurvatureSteps ? byStep["ReBaseline1"].Gradient : byStep["Baseline"].Gradient,
                HasCurvatureMeasurement = hasCurvatureSteps,
                FallbackCurvatureSign = metadata.Calibration?.CurvatureSign ?? 0,
```
   (same for the `pbyStep` paraboloid block, keeping `IsStepperAdjustment = inputs.IsStepperAdjustment` etc. as-is). The paraboloid loop iterates `orderedRuns` — that is already only the discovered steps, so nothing else changes.
3. In `WriteReport`, where `calibration.CurvatureSign` is printed (lines ~740 and ~816), annotate when it was not measured — append the string `" (not measured — carried from metadata)"` when `!inputs.HasCurvatureMeasurement` (the method already receives `inputs`; check the signature and thread it through if not).
4. Update `PrintUsage` (lines 873–881): change "folder containing the 6 AF runs (Baseline, AllInward, ReBaseline1, Screw1, ReBaseline2, Screw2)" to "folder containing the 6 AF runs (Baseline, AllInward, ReBaseline1, Screw1, ReBaseline2, Screw2) — or 4 (Baseline, Screw1, ReBaseline2, Screw2) for a run saved without the curvature-direction steps".

- [ ] **Step 2: Build + full suite**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
Expected: PASS (TestApp compiles as part of the sln).

- [ ] **Step 3: Commit**

```bash
git add -A
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(testapp): tilt calibration validator accepts 4-step runs"
```

---

### Task 11: Documentation updates

**Files:**
- Modify: `documentation/docs/overview/tilt-adapter-wizard.md`
- Modify: `documentation/docs/overview/tilt-aberration-inspector.md`
- Modify: `documentation/docs/overview/sensor-model.md`
- Modify: `documentation/docs/quick-start.md`

Read `.claude/docs/documentation-style.md` before editing. Keep the house voice; do not add "Honest limit"-style confessional headings; no raw pipes inside table-cell math.

- [ ] **Step 1: tilt-adapter-wizard.md**

In "The calibration loop" (lines 38–58):
1. Replace the loop description paragraph (lines 40–46) with one that states: the default run is **4 steps** (baseline, screw 1, re-baseline, screw 2) with prompts worded as **clockwise/counter-clockwise turns** (signed +/− steps for stepper adapters); turning on **Measure direction** adds 2 steps (baseline + all-screws-clockwise) that measure `ScrewInwardCurvatureSign` — otherwise the sign comes from the **adapter direction setting** ("Turning screws clockwise moves the adapter: toward the camera / toward the objective", default toward the camera) and guidance marks it "(assumed)". Keep the existing "Measurements above 1 averages repeats" sentence.
2. Update the tip block (lines 48–58): Center Focuser First is now **off by default** and both it and Signal Amplification are editable **directly in the wizard's Measurement section** (as well as in the inspector); remove "(the default)" after Center Focuser First.
3. Add a new `## Manual calibration entry` section (after the calibration-loop section) documenting: enter the screw-1 angle (0° = top of image, clockwise-positive), pick the numbering direction (clockwise/counter-clockwise **in the image** — mirrors can flip it), Apply writes the same calibration state the wizard produces, tagged "manually entered"; the adapter-direction setting supplies the curvature sign.

- [ ] **Step 2: tilt-aberration-inspector.md**

1. In the "Inspector options" table, update the **Signal Amplification** row (line 141) and **Center Focuser First** row (line 142): note both controls now sit **above the Options expander** with explanatory text (including a live image-count estimate), and Center Focuser First's default is **off** ("| **Center Focuser First** | off | on/off | ..."). Rewrite the quoted description to match the new tooltip (drop "(default)").
2. Search this page for guidance wording that says IN/OUT or "inward/outward" describing **screw turns** (`grep -n "IN/OUT\|inward" documentation/docs/overview/tilt-aberration-inspector.md`) and update: totals now read like `1.25 turns CW` (screws) or `+35 steps` (steppers), a legend line explains what ⬆ means for the adapter ("adapter moves toward the camera/objective"), and an "(assumed)" marker appears until the direction is measured.

- [ ] **Step 3: sensor-model.md**

At the end of the "From fit to physical numbers" paragraph (lines 301–307), after "…(see the [Tilt Adapter Wizard](tilt-adapter-wizard.md))", add one sentence: "The direction of those all-screws moves comes from the adapter-direction setting (or the wizard's optional direction measurement); if the backfocus guidance seems inverted, flip that setting."

- [ ] **Step 4: quick-start.md**

In section 5 (lines 77–91), update item 2 to mention the default 4-step loop and the opt-in direction measurement, and add an item 3: "Know your adapter already? Use **Manual Calibration Entry** in the wizard to type in the screw-1 angle instead of running the loop." Keep the arrow-link lines unchanged.

- [ ] **Step 5: Verify + commit**

Run `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` (unchanged code, but keep the invariant). Optionally run the repo's `adversarial-doc-review` workflow on the four pages.

```bash
git add -A
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "docs: 4-step wizard flow, surfaced sweep settings, directional guidance, manual entry"
```

---

### Task 12: Final verification

- [ ] **Step 1: Full suite** — `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` → all green.
- [ ] **Step 2: Spec coverage check** — re-read `docs/tilt-calibration-ux-design.md` section by section and confirm each requirement maps to a landed change (Feature 1 → Tasks 1, 6, 7, 8, 9; Feature 2 → Tasks 5, 8; Feature 3 → Tasks 2, 3, 4, 8, 9, 10; Feature 4 → Tasks 4, 8, 9; incidental fix → Task 1; docs → Task 11).
- [ ] **Step 3: Grep for leftovers** — `grep -rn "INWARD\|OUTWARD" Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus --include="*.cs" | grep -v obj/` should show no remaining screw-motion prompts using the old vocabulary (adapter-motion usages in comments are fine).
- [ ] **Step 4: Push the branch and open a PR** against `develop` (never push develop directly):
```bash
git push -u origin ghilios/tilt-calibration-ux
gh pr create --base develop --title "Tilt calibration & sensor model UX improvements" --body "Implements docs/tilt-calibration-ux-design.md: surfaced sweep-cost settings (Center Focuser First now default-off), CW/CCW + signed-step guidance with adapter-direction legend, opt-in curvature measurement (4-step default wizard), manual calibration entry, TimeoutSeconds persistence-key fix.

🤖 Generated with [Claude Code](https://claude.com/claude-code)"
```
