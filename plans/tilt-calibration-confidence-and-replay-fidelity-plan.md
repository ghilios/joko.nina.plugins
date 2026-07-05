# Tilt Calibration — Confidence Surfacing & Replay Fidelity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface the tilt calibration's already-computed confidence (SNR, screw-direction σ, and a new pitch ±) in the wizard UI and persist it; and persist the measurement context so a saved run can be replayed faithfully (and drift warned).

**Architecture:** Two phases. Phase 1 (presentation + persistence) adds a per-screw-move pitch uncertainty to the pure `TiltCalibrationCalculator`, retains the `TiltCalibrationConfidence` on the VM, exposes always-on display properties, renders them in the "Calibration Complete" panel, and persists a confidence block in `metadata.json`. Phase 2 (reproducibility) adds a `MeasurementContext` block to `TiltCalibrationMetadata`, captures it at save, and — on replay — restores the sensor-model focuser-size/f-ratio via a transient override and warns when the current profile drifts from the captured context. Both phases follow the plugin's existing patterns; no profile mutation.

**Tech Stack:** C# / .NET 8 (windows), NUnit 4 + NSubstitute tests, CommunityToolkit.Mvvm, WPF/XAML, Newtonsoft.Json (camelCase). Design record: `docs/tilt-calibration-error-bounds-design.md` (§6, §7).

**Design decisions locked (user-approved):** always-show a compact confidence line, escalate to the existing red "do not apply" warning at SNR<2; show a single global screw-direction σ (not per-screw — dof=1); show pitch ±; gate pitch reliability on the physical delta ratio; keep the hard SNR<2 apply-block.

**Conventions:** run the full suite after every change: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`. Commit with the privacy email per CLAUDE.md. On WSL, `dotnet` is `"/mnt/c/Program Files/dotnet/dotnet.exe"`.

## File Structure

**Phase 1**
- Modify `Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltCalibrationCalculator.cs` — add `RecoverHardwareDetailed` + `TiltCalibrationResult.PitchUncertaintyMicrons`.
- Modify `Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltCalibrationMetadata.cs` — add confidence fields to `TiltCalibrationResultRecord`; bump `CurrentSchemaVersion`.
- Modify `Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltAdapterWizardVM.cs` — retain confidence + pitch σ; add display properties; persist in `FinalizeMetadata`.
- Modify `Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/DataTemplates.xaml` — always-on confidence line + pitch ±.
- Tests: `Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterWizard/{TiltCalibrationCalculatorTests,TiltCalibrationMetadataTests,TiltAdapterWizardVMTests}.cs`.

**Phase 2**
- Modify `TiltCalibrationMetadata.cs` — add `MeasurementContext` class + property.
- Modify `TiltAdapterWizardVM.cs` — `CaptureMeasurementContext` helper + wire into `BuildInitialMetadata`; replay drift warning; transient focuser/fRatio override.
- Modify `AutoFocus/InspectorVM.cs` — consult transient focuser-size/f-ratio overrides at the sensor-model call.
- Tests: `TiltCalibrationMetadataTests.cs`, `TiltAdapterWizardVMTests.cs`.

---

# Phase 1 — Confidence surfacing & persistence

### Task 1: Pitch uncertainty in the pure calculator

**Files:**
- Modify: `Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltCalibrationCalculator.cs` (`RecoverHardwareMicrons` 248-279; `TiltCalibrationResult` 69-90; `Calibrate` 297-322)
- Test: `Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterWizard/TiltCalibrationCalculatorTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `TiltCalibrationCalculatorTests.cs`:

```csharp
[Test]
public void PitchUncertainty_IsHalfTheDifferenceOfPerScrewMoves() {
    // Two single-screw moves of clearly unequal magnitude along different axes.
    var inputs = new TiltCalibrationInputs {
        ScrewCount = 3,
        ReBaseline1 = new TiltGradient(0, 0, 0),
        Screw1 = new TiltGradient(20, 0, 0),      // move along A
        ReBaseline2 = new TiltGradient(0, 0, 0),
        Screw2 = new TiltGradient(0, 10, 0),      // move along B
        ImageWidthPixels = 6248, ImageHeightPixels = 4176,
        PixelSizeMicrons = 3.76, FocuserStepMicrons = 3.6,
        ScrewRadiusMillimeters = 44, CalibrationAppliedAmount = 1.0, IsStepperAdjustment = false
    };
    var (measured, d1, d2) = TiltCalibrationCalculator.RecoverHardwareDetailed(inputs);
    Assert.Multiple(() => {
        Assert.That(measured, Is.EqualTo(0.5 * (d1 + d2)).Within(1e-9));
        var result = TiltCalibrationCalculator.Calibrate(inputs);
        Assert.That(result.PitchUncertaintyMicrons, Is.EqualTo(Math.Abs(d1 - d2) / 2.0).Within(1e-9));
        Assert.That(result.PitchUncertaintyMicrons, Is.GreaterThan(0));
    });
}

[Test]
public void PitchUncertainty_IsNaN_WhenHardwareUncomputable() {
    var inputs = new TiltCalibrationInputs {
        ScrewCount = 3, ScrewRadiusMillimeters = 0 /* invalid */,
        CalibrationAppliedAmount = 1.0, PixelSizeMicrons = 3.76, FocuserStepMicrons = 3.6,
        ImageWidthPixels = 6248, ImageHeightPixels = 4176
    };
    Assert.That(TiltCalibrationCalculator.Calibrate(inputs).PitchUncertaintyMicrons, Is.NaN);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `"/mnt/c/Program Files/dotnet/dotnet.exe" test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter PitchUncertainty`
Expected: FAIL — `RecoverHardwareDetailed` and `PitchUncertaintyMicrons` don't exist (compile error).

- [ ] **Step 3: Add the field to `TiltCalibrationResult`**

In `TiltCalibrationCalculator.cs`, inside `TiltCalibrationResult` (after `MoveMagnitudeRatio`, ~line 85):

```csharp
        /// <summary>Half the absolute difference of the two single-screw recovered pitches (µm/turn or µm/step).
        /// This is the physical-space 1σ on the recovered hardware — it captures screw-to-screw disagreement the
        /// (A,B) <see cref="MoveMagnitudeRatio"/> misses because the plane→physical conversion is anisotropic.
        /// NaN when the hardware is uncomputable.</summary>
        public double PitchUncertaintyMicrons { get; set; }
```

- [ ] **Step 4: Add `RecoverHardwareDetailed` and delegate `RecoverHardwareMicrons` to it**

Replace the body of `RecoverHardwareMicrons` (248-279) so the math lives in a detailed variant:

```csharp
        /// <summary>Per-screw recovered hardware: the average (µm/turn or µm/step) plus each screw's own recovered
        /// value (delta_i / applied). Same math as the wizard's CalculateAndSaveHardware. All three are NaN when any
        /// input is non-positive or the average is non-positive.</summary>
        public static (double measured, double delta1PerApplied, double delta2PerApplied) RecoverHardwareDetailed(TiltCalibrationInputs inputs) {
            double pixelSize = inputs.PixelSizeMicrons;
            double fStep = inputs.FocuserStepMicrons;
            double radiusMm = inputs.ScrewRadiusMillimeters;
            double applied = inputs.CalibrationAppliedAmount;
            double sensorW = inputs.ImageWidthPixels * pixelSize;
            double sensorH = inputs.ImageHeightPixels * pixelSize;
            if (radiusMm <= 0 || applied <= 0 || pixelSize <= 0 || fStep <= 0 || sensorW <= 0 || sensorH <= 0) {
                return (double.NaN, double.NaN, double.NaN);
            }
            double radiusMicrons = radiusMm * 1000.0;
            int n = inputs.ScrewCount;
            double d1A = inputs.Screw1.A - inputs.ReBaseline1.A;
            double d1B = inputs.Screw1.B - inputs.ReBaseline1.B;
            double d2A = inputs.Screw2.A - inputs.ReBaseline2.A;
            double d2B = inputs.Screw2.B - inputs.ReBaseline2.B;
            var (g1x, g1y) = TiltScrewGeometry.PlaneGradientToPhysical(d1A, d1B, fStep, sensorW, sensorH);
            var (g2x, g2y) = TiltScrewGeometry.PlaneGradientToPhysical(d2A, d2B, fStep, sensorW, sensorH);
            double delta1 = TiltScrewGeometry.CalibrationAxialMoveMicrons(g1x, g1y, n, radiusMicrons);
            double delta2 = TiltScrewGeometry.CalibrationAxialMoveMicrons(g2x, g2y, n, radiusMicrons);
            double measured = 0.5 * (delta1 + delta2) / applied;
            if (double.IsNaN(measured) || measured <= 0) {
                return (double.NaN, double.NaN, double.NaN);
            }
            return (measured, delta1 / applied, delta2 / applied);
        }

        /// <summary>Recovers the adapter hardware (µm/turn for screws, µm/step for steppers). See
        /// <see cref="RecoverHardwareDetailed"/>. NaN when uncomputable.</summary>
        public static double RecoverHardwareMicrons(TiltCalibrationInputs inputs) => RecoverHardwareDetailed(inputs).measured;
```

- [ ] **Step 5: Set `PitchUncertaintyMicrons` in `Calibrate`**

In `Calibrate` (297-322), replace `MeasuredHardwareMicrons = RecoverHardwareMicrons(inputs),` (line 316) with:

```csharp
                MeasuredHardwareMicrons = RecoverHardwareDetailed(inputs) is var (hw, hd1, hd2)
                    ? hw : double.NaN,
                PitchUncertaintyMicrons = RecoverHardwareDetailed(inputs) is var (_, pd1, pd2) && !double.IsNaN(pd1)
                    ? Math.Abs(pd1 - pd2) / 2.0 : double.NaN,
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `"/mnt/c/Program Files/dotnet/dotnet.exe" test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter PitchUncertainty`
Expected: PASS (2 tests). Then run the full suite to confirm no regressions.

- [ ] **Step 7: Commit**

```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit -am "feat(tilt): expose per-screw pitch uncertainty from the calibration calculator" \
  --author="George Hilios <322725+ghilios@users.noreply.github.com>"
```

### Task 2: Persist the confidence block in metadata.json

**Files:**
- Modify: `TiltCalibrationMetadata.cs` (`TiltCalibrationResultRecord` 53-62; `CurrentSchemaVersion` 73)
- Modify: `TiltAdapterWizardVM.cs` (`FinalizeMetadata` 1909-1922)
- Test: `TiltCalibrationMetadataTests.cs`

- [ ] **Step 1: Write the failing round-trip test**

Add to `TiltCalibrationMetadataTests.cs`:

```csharp
[Test]
public void ResultRecord_RoundTripsConfidenceFields() {
    var meta = new TiltCalibrationMetadata {
        NumberOfScrews = 3, ScrewRadiusMillimeters = 44, PixelSizeMicrons = 3.76,
        FocuserStepSizeMicrons = 3.6, CalibrationAppliedAmount = 1.0,
        Calibration = new TiltCalibrationResultRecord {
            Screw1AngleDegrees = 67.1, SignalToNoise = 2.16,
            PredictedAngleUncertaintyDeg = 24.9, PitchUncertaintyMicrons = 43.0, ConfidenceIsReliable = true
        }
    };
    var back = TiltCalibrationMetadata.Deserialize(meta.Serialize());
    Assert.Multiple(() => {
        Assert.That(back.Calibration.SignalToNoise, Is.EqualTo(2.16).Within(1e-9));
        Assert.That(back.Calibration.PredictedAngleUncertaintyDeg, Is.EqualTo(24.9).Within(1e-9));
        Assert.That(back.Calibration.PitchUncertaintyMicrons, Is.EqualTo(43.0).Within(1e-9));
        Assert.That(back.Calibration.ConfidenceIsReliable, Is.True);
    });
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `… --filter ResultRecord_RoundTripsConfidenceFields` → FAIL (fields don't exist).

- [ ] **Step 3: Add fields to `TiltCalibrationResultRecord`**

In `TiltCalibrationMetadata.cs`, inside `TiltCalibrationResultRecord` (after `MoveMagnitudeRatio`, ~line 61):

```csharp
        // ---- Confidence (persisted so a saved/replayed run carries its reliability) ----
        public double SignalToNoise { get; set; } = double.NaN;
        public double PredictedAngleUncertaintyDeg { get; set; } = double.NaN;
        public double PitchUncertaintyMicrons { get; set; } = double.NaN;
        public bool ConfidenceIsReliable { get; set; }
```

- [ ] **Step 4: Bump the schema version**

In `TiltCalibrationMetadata.cs:73` change `public const int CurrentSchemaVersion = 1;` to `= 2;`.

- [ ] **Step 5: Populate in `FinalizeMetadata`**

In `TiltAdapterWizardVM.cs`, `FinalizeMetadata` (1909-1922), extend the `TiltCalibrationResultRecord` initializer with (using the VM fields added in Task 3):

```csharp
        SignalToNoise = lastConfidence?.SignalToNoise ?? double.NaN,
        PredictedAngleUncertaintyDeg = lastConfidence?.PredictedAngleUncertaintyDeg ?? double.NaN,
        PitchUncertaintyMicrons = pitchUncertaintyMicrons,
        ConfidenceIsReliable = lastConfidence?.IsReliable ?? false,
```

(Task 3 introduces `lastConfidence` and `pitchUncertaintyMicrons`. If executing tasks strictly in order, do Task 3 before Step 5 compiles.)

- [ ] **Step 6: Run round-trip test + full suite → PASS. Commit.**

```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit -am "feat(tilt): persist calibration confidence block in metadata.json (schema v2)" \
  --author="George Hilios <322725+ghilios@users.noreply.github.com>"
```

### Task 3: Retain confidence on the VM and expose display properties

**Files:**
- Modify: `TiltAdapterWizardVM.cs` — fields near 115-120; `RunCalibrationMath` (1504, 1516); `RaiseHardwareSummaryChanged` 1530-1540; clear sites `Restart` 1376-1378, `ApplyManualCalibration` 862-864, `StartAsync` 995-1010.
- Test: `TiltAdapterWizardVMTests.cs`

- [ ] **Step 1: Write the failing VM test**

Add to `TiltAdapterWizardVMTests.cs` (mirror existing seed-based tests — use `SeedStepReading` + the private calibration entry the other tests use; follow the pattern already used by `StepInstructionsText_*`/seed tests in this file):

```csharp
[Test]
public void Confidence_DisplayProperties_PopulatedAfterCalibration() {
    var (vm, _, _, _) = Build(screwCount: 3);
    // Seed 6 step readings with a clean, unequal-enough pair so pitch σ > 0 (values in A,B plane units).
    vm.SeedStepReading(WizardStep.Baseline, -9.5, 16.3, 7049);
    vm.SeedStepReading(WizardStep.AllInward, -15.9, 14.5, 6957);
    vm.SeedStepReading(WizardStep.ReBaseline1, -9.5, 16.3, 7049);
    vm.SeedStepReading(WizardStep.Screw1, 11.7, 8.9, 7013);
    vm.SeedStepReading(WizardStep.ReBaseline2, -9.5, 16.3, 7049);
    vm.SeedStepReading(WizardStep.Screw2, -8.6, 40.3, 7016);
    vm.RunCalibrationForTest(); // test seam added in Step 3
    Assert.Multiple(() => {
        Assert.That(vm.HasConfidenceInfo, Is.True);
        Assert.That(vm.ConfidenceSummaryDisplay, Does.Contain("Signal-to-noise"));
        Assert.That(vm.PitchUncertaintyDisplay, Does.Contain("±"));
    });
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `… --filter Confidence_DisplayProperties_PopulatedAfterCalibration` → FAIL (members missing).

- [ ] **Step 3: Add fields, a test seam, capture, properties, and clearing**

In `TiltAdapterWizardVM.cs`:

3a. Add fields next to `measuredHardwareMicrons` (~115-120):

```csharp
        private TiltCalibrationConfidence lastConfidence;
        private double pitchUncertaintyMicrons = double.NaN;
```

3b. In `RunCalibrationMath`, replace the hardware line (1504) `double measured = TiltCalibrationCalculator.RecoverHardwareMicrons(inputs);` with:

```csharp
                var (measured, hwDelta1, hwDelta2) = TiltCalibrationCalculator.RecoverHardwareDetailed(inputs);
                pitchUncertaintyMicrons = double.IsNaN(hwDelta1) ? double.NaN : Math.Abs(hwDelta1 - hwDelta2) / 2.0;
```

3c. In `RunCalibrationMath`, capture the confidence: change line 1516 from `EvaluateCalibrationConfidence(TiltCalibrationCalculator.ComputeConfidence(new TiltCalibrationInputs {` to compute-then-store:

```csharp
            lastConfidence = TiltCalibrationCalculator.ComputeConfidence(new TiltCalibrationInputs {
                ScrewCount = screwCount,
                Baseline = measuredCurvature ? new TiltGradient(a.A, a.B, a.Mean) : default,
                AllInward = measuredCurvature ? new TiltGradient(b.A, b.B, b.Mean) : default,
                ReBaseline1 = new TiltGradient(c.A, c.B, c.Mean),
                Screw1 = new TiltGradient(d.A, d.B, d.Mean),
                ReBaseline2 = new TiltGradient(e.A, e.B, e.Mean),
                Screw2 = new TiltGradient(f.A, f.B, f.Mean),
                HasCurvatureMeasurement = measuredCurvature,
                FallbackCurvatureSign = tiltAdapterOptions.ScrewInwardCurvatureSign
            });
            EvaluateCalibrationConfidence(lastConfidence);
```

3d. Add display properties (near the other display properties, ~895-922):

```csharp
        public bool HasConfidenceInfo => lastConfidence != null;

        public bool ConfidenceIsReliable => lastConfidence?.IsReliable ?? false;

        public string ConfidenceSummaryDisplay =>
            lastConfidence == null ? string.Empty :
            $"Signal-to-noise {lastConfidence.SignalToNoise:F1} · screw-direction ±{lastConfidence.PredictedAngleUncertaintyDeg:F0}°";

        public string PitchUncertaintyDisplay =>
            double.IsNaN(pitchUncertaintyMicrons) ? string.Empty :
            $"± {pitchUncertaintyMicrons:F0} µm/{(IsStepperAdjustment ? "step" : "turn")}";
```

3e. Add the raises to `RaiseHardwareSummaryChanged` (before the `OnUIThread(...)` line, ~1539):

```csharp
            RaisePropertyChanged(nameof(HasConfidenceInfo));
            RaisePropertyChanged(nameof(ConfidenceIsReliable));
            RaisePropertyChanged(nameof(ConfidenceSummaryDisplay));
            RaisePropertyChanged(nameof(PitchUncertaintyDisplay));
```

3f. Clear in `Restart` (1376-1378), `ApplyManualCalibration` (862-864), and `StartAsync` (995-1010) — add alongside the existing `measuredHardwareMicrons = double.NaN;` resets:

```csharp
            lastConfidence = null;
            pitchUncertaintyMicrons = double.NaN;
```

3g. Add the test seam near `SeedStepReading` (1624): a public wrapper that runs the same calibration entry the Complete step uses. Confirm the exact private method name in `NextStep` (1357 calls `RunCalibrationMath(...)`); expose:

```csharp
        // Test seam: run the calibration math over the seeded step readings (mirrors NextStep -> Complete).
        internal void RunCalibrationForTest() => RunCalibrationMath(
            tiltAdapterOptions.ScrewCount, tiltAdapterOptions.ScrewRadiusMillimeters,
            profileService.ActiveProfile.CameraSettings.PixelSize, EffectiveFocuserStepMicrons(),
            calibrationAppliedAmount, IsStepperAdjustment);
```

(Signature confirmed at 1439: `RunCalibrationMath(int screwCount, double radiusMm, double pixelSize, double fStep, double appliedAmount, bool isStepper)`.)

- [ ] **Step 4: Run the VM test + full suite → PASS. Commit.**

```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit -am "feat(tilt): retain calibration confidence + pitch σ on the wizard VM" \
  --author="George Hilios <322725+ghilios@users.noreply.github.com>"
```

### Task 4: Render confidence line + pitch ± in the Complete panel

**Files:**
- Modify: `TiltAdapterWizard/DataTemplates.xaml` — `HF_TiltScrewCalibrationInfo` (append after 261); measured-hardware block (1201-1204).
- Verification: manual (XAML has no unit tests).

- [ ] **Step 1: Add the always-on confidence line** to `HF_TiltScrewCalibrationInfo` (inside its root `StackPanel`, after the curvature row's closing tag ~line 261):

```xml
                <!--  Confidence summary — always shown once a calibration exists (SNR + screw-direction σ);
                      the red do-not-apply warning (HasConfidenceWarning) still fires separately at SNR < 2.  -->
                <TextBlock Margin="0,6,0,0" TextWrapping="Wrap"
                    Text="{Binding ConfidenceSummaryDisplay}">
                    <TextBlock.Style>
                        <Style TargetType="TextBlock">
                            <Setter Property="Visibility" Value="Collapsed" />
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding HasConfidenceInfo}" Value="True">
                                    <Setter Property="Visibility" Value="Visible" />
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </TextBlock.Style>
                </TextBlock>
```

- [ ] **Step 2: Add the pitch ±** next to `MeasuredHardwareDisplay` — extend the horizontal `StackPanel` (1201-1204) with a third `TextBlock` after `HardwareDeltaDisplay`:

```xml
                    <TextBlock Margin="8,0,0,0" Opacity="0.7" Text="{Binding PitchUncertaintyDisplay}" />
```

- [ ] **Step 3: Manual verification**

Build the plugin (Debug installs it): `"/mnt/c/Program Files/dotnet/dotnet.exe" build Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Joko.NINA.Plugins.HocusFocus.csproj -c Debug --nologo`. In NINA, open the Tilt Adapter Wizard and run/replay a calibration; confirm the "Calibration Complete" panel shows "Signal-to-noise X.X · screw-direction ±NN°" and the measured hardware shows "± NN µm/turn". Confirm the red SNR<2 warning still appears when applicable.

- [ ] **Step 4: Run full suite (unchanged) → PASS. Commit.**

```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit -am "feat(tilt): show confidence line and pitch ± in the Calibration Complete panel" \
  --author="George Hilios <322725+ghilios@users.noreply.github.com>"
```

---

# Phase 2 — Replay fidelity (measurement context)

### Task 5: `MeasurementContext` metadata block

**Files:**
- Modify: `TiltCalibrationMetadata.cs` — add class + property.
- Test: `TiltCalibrationMetadataTests.cs`

- [ ] **Step 1: Write the failing round-trip test**

```csharp
[Test]
public void MeasurementContext_RoundTrips() {
    var meta = new TiltCalibrationMetadata {
        NumberOfScrews = 3, ScrewRadiusMillimeters = 44, PixelSizeMicrons = 3.76,
        FocuserStepSizeMicrons = 3.6, CalibrationAppliedAmount = 1.0,
        MeasurementContext = new TiltMeasurementContext {
            MicronsPerFocuserStep = 3.6, FocalRatio = 7, FocalLengthMm = 703,
            UseRANSAC = true, FixedSensorCenter = false, AstigmaticCurvatureEnabled = false,
            AcceptableRSquaredMin = 0.8, WeightedHyperbolicFitEnabled = true,
            MaxOutlierRejections = 3, OutlierRejectionConfidence = 0.9,
            HyperbolicFitModel = "Hybrid", SensorROI = 1.0, CornersROI = 1.0
        }
    };
    var back = TiltCalibrationMetadata.Deserialize(meta.Serialize());
    Assert.Multiple(() => {
        Assert.That(back.MeasurementContext.MicronsPerFocuserStep, Is.EqualTo(3.6).Within(1e-9));
        Assert.That(back.MeasurementContext.FocalRatio, Is.EqualTo(7).Within(1e-9));
        Assert.That(back.MeasurementContext.HyperbolicFitModel, Is.EqualTo("Hybrid"));
        Assert.That(back.MeasurementContext.UseRANSAC, Is.True);
    });
}
```

- [ ] **Step 2: Run → FAIL (`TiltMeasurementContext` missing).**

- [ ] **Step 3: Add the class + property** in `TiltCalibrationMetadata.cs`:

```csharp
    /// <summary>Snapshot of the profile/inspector inputs the tilt measurement reads from live state, so a replay
    /// can reproduce the original calibration (or warn when the current profile has drifted). HyperbolicFitModel
    /// is stored as its enum name for forward-compatibility.</summary>
    public sealed class TiltMeasurementContext {
        public double MicronsPerFocuserStep { get; set; } = double.NaN;
        public double FocalRatio { get; set; } = double.NaN;
        public double FocalLengthMm { get; set; } = double.NaN;
        public bool UseRANSAC { get; set; }
        public bool FixedSensorCenter { get; set; }
        public bool AstigmaticCurvatureEnabled { get; set; }
        public double AcceptableRSquaredMin { get; set; } = double.NaN;
        public bool WeightedHyperbolicFitEnabled { get; set; }
        public int MaxOutlierRejections { get; set; }
        public double OutlierRejectionConfidence { get; set; } = double.NaN;
        public string HyperbolicFitModel { get; set; }
        public double SensorROI { get; set; } = double.NaN;
        public double CornersROI { get; set; } = double.NaN;
    }
```

And add to `TiltCalibrationMetadata` in the replay-payload region (after `OptimizedStarDetectionSettings`, ~line 106):

```csharp
        public TiltMeasurementContext MeasurementContext { get; set; }
```

- [ ] **Step 4: Run round-trip test + full suite → PASS. Commit.**

```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit -am "feat(tilt): add MeasurementContext to tilt calibration metadata" \
  --author="George Hilios <322725+ghilios@users.noreply.github.com>"
```

### Task 6: Capture the measurement context at save

**Files:**
- Modify: `TiltAdapterWizardVM.cs` — `CaptureMeasurementContext` helper; `BuildInitialMetadata` 1046-1061.
- Test: `TiltAdapterWizardVMTests.cs`

- [ ] **Step 1: Write the failing test** (a static, dependency-injected capture helper is testable without the VM):

```csharp
[Test]
public void CaptureMeasurementContext_ReadsInspectorAndProfileValues() {
    var inspector = Substitute.For<IInspectorOptions>();
    inspector.MicronsPerFocuserStep.Returns(3.6);
    inspector.UseRANSAC.Returns(true);
    inspector.AcceptableRSquaredMin.Returns(0.8);
    inspector.SensorROI.Returns(1.0); inspector.CornersROI.Returns(1.0);
    var af = Substitute.For<IAutoFocusOptions>();
    af.WeightedHyperbolicFitEnabled.Returns(true);
    af.MaxOutlierRejections.Returns(3);
    af.OutlierRejectionConfidence.Returns(0.9);
    af.HyperbolicFitModel.Returns(HyperbolicFitModel.Hybrid);

    var ctx = TiltAdapterWizardVM.CaptureMeasurementContext(inspector, af, fRatio: 7, focalLengthMm: 703);

    Assert.Multiple(() => {
        Assert.That(ctx.MicronsPerFocuserStep, Is.EqualTo(3.6));
        Assert.That(ctx.FocalRatio, Is.EqualTo(7));
        Assert.That(ctx.HyperbolicFitModel, Is.EqualTo("Hybrid"));
        Assert.That(ctx.UseRANSAC, Is.True);
    });
}
```

- [ ] **Step 2: Run → FAIL.**

- [ ] **Step 3: Add the static helper** in `TiltAdapterWizardVM.cs` (near `BuildInitialMetadata`):

```csharp
        // Snapshot the live inputs the sensor-model measurement reads, so a replay can restore or warn on drift.
        internal static TiltMeasurementContext CaptureMeasurementContext(
            IInspectorOptions inspector, IAutoFocusOptions af, double fRatio, double focalLengthMm) {
            return new TiltMeasurementContext {
                MicronsPerFocuserStep = inspector.MicronsPerFocuserStep,
                FocalRatio = fRatio,
                FocalLengthMm = focalLengthMm,
                UseRANSAC = inspector.UseRANSAC,
                FixedSensorCenter = inspector.FixedSensorCenter,
                AstigmaticCurvatureEnabled = inspector.AstigmaticCurvatureEnabled,
                AcceptableRSquaredMin = inspector.AcceptableRSquaredMin,
                WeightedHyperbolicFitEnabled = af.WeightedHyperbolicFitEnabled,
                MaxOutlierRejections = af.MaxOutlierRejections,
                OutlierRejectionConfidence = af.OutlierRejectionConfidence,
                HyperbolicFitModel = af.HyperbolicFitModel.ToString(),
                SensorROI = inspector.SensorROI,
                CornersROI = inspector.CornersROI
            };
        }
```

- [ ] **Step 4: Wire into `BuildInitialMetadata`** (add to the initializer, after `OptimizedStarDetectionSettings = CaptureDetectionSettings(),`):

```csharp
        MeasurementContext = CaptureMeasurementContext(
            inspector.InspectorOptions, HocusFocusPlugin.AutoFocusOptions,
            profileService.ActiveProfile.TelescopeSettings.FocalRatio,
            profileService.ActiveProfile.TelescopeSettings.FocalLength),
```

`HocusFocusPlugin.AutoFocusOptions` (static `IAutoFocusOptions`) is the AF-options accessor — the same singleton pattern `CaptureDetectionSettings()` uses for `HocusFocusPlugin.StarDetectionOptions`.

- [ ] **Step 5: Run test + full suite → PASS. Commit.**

```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit -am "feat(tilt): capture measurement context when saving a calibration run" \
  --author="George Hilios <322725+ghilios@users.noreply.github.com>"
```

### Task 7: Replay — restore focuser size/f-ratio + warn on drift

**Files:**
- Modify: `AutoFocus/InspectorVM.cs` — transient overrides consulted at 578/592.
- Modify: `TiltAdapterWizardVM.cs` — `ReplayAsync` 1638-1829; a static drift-compare helper.
- Test: `TiltAdapterWizardVMTests.cs` (drift helper only; the inspector wiring is manual).

- [ ] **Step 1: Write the failing drift-helper test**

```csharp
[Test]
public void MeasurementContextDrift_ListsChangedFields() {
    var captured = new TiltMeasurementContext { MicronsPerFocuserStep = 3.6, FocalRatio = 7, UseRANSAC = true };
    var current  = new TiltMeasurementContext { MicronsPerFocuserStep = 0.26, FocalRatio = 7, UseRANSAC = true };
    var drift = TiltAdapterWizardVM.DescribeMeasurementContextDrift(captured, current);
    Assert.Multiple(() => {
        Assert.That(drift, Does.Contain("MicronsPerFocuserStep"));
        Assert.That(drift, Does.Not.Contain("FocalRatio"));
    });
}
```

- [ ] **Step 2: Run → FAIL.**

- [ ] **Step 3: Add the drift helper** in `TiltAdapterWizardVM.cs`:

```csharp
        // Human-readable list of context fields that differ between capture and the current profile (empty if none).
        internal static string DescribeMeasurementContextDrift(TiltMeasurementContext captured, TiltMeasurementContext current) {
            if (captured == null || current == null) return string.Empty;
            var diffs = new List<string>();
            void D(string name, double a, double b) { if (!(double.IsNaN(a) && double.IsNaN(b)) && Math.Abs(a - b) > 1e-9) diffs.Add($"{name} ({a:0.###} → {b:0.###})"); }
            void B(string name, bool a, bool b) { if (a != b) diffs.Add($"{name} ({a} → {b})"); }
            D("MicronsPerFocuserStep", captured.MicronsPerFocuserStep, current.MicronsPerFocuserStep);
            D("FocalRatio", captured.FocalRatio, current.FocalRatio);
            D("AcceptableRSquaredMin", captured.AcceptableRSquaredMin, current.AcceptableRSquaredMin);
            B("UseRANSAC", captured.UseRANSAC, current.UseRANSAC);
            B("FixedSensorCenter", captured.FixedSensorCenter, current.FixedSensorCenter);
            B("WeightedHyperbolicFit", captured.WeightedHyperbolicFitEnabled, current.WeightedHyperbolicFitEnabled);
            if (!string.Equals(captured.HyperbolicFitModel, current.HyperbolicFitModel, StringComparison.Ordinal))
                diffs.Add($"HyperbolicFitModel ({captured.HyperbolicFitModel} → {current.HyperbolicFitModel})");
            return string.Join(", ", diffs);
        }
```

- [ ] **Step 4: Add transient sensor-model overrides in `InspectorVM.cs`**

Add public nullable transients (near `ForceSensorCurveModelGeneration` 552):

```csharp
        // Transient replay overrides for the sensor-model inputs otherwise read live (null = use profile/inspector).
        public double? SensorModelFocuserSizeOverrideMicrons { get; set; }
        public double? SensorModelFRatioOverride { get; set; }
```

Consult them at the sensor-model call. Change line 578 to `double focuserSizeMicrons = SensorModelFocuserSizeOverrideMicrons ?? InspectorOptions.MicronsPerFocuserStep;` and line 592's `fRatio:` argument to `fRatio: SensorModelFRatioOverride ?? profileService.ActiveProfile.TelescopeSettings.FocalRatio,`.

- [ ] **Step 5: Set/clear the overrides + warn in `ReplayAsync`**

In `TiltAdapterWizardVM.cs` `ReplayAsync`, when `mode.ApplyCaptureTimeOverridePerStep` and `metadata.MeasurementContext != null`, before the replay loop (1735) set the inspector overrides from the captured context, and restore them in a `finally`:

```csharp
        var ctx = metadata.MeasurementContext;
        var prevFocuserOverride = inspector.SensorModelFocuserSizeOverrideMicrons;
        var prevFRatioOverride = inspector.SensorModelFRatioOverride;
        if (mode.ApplyCaptureTimeOverridePerStep && ctx != null) {
            if (!double.IsNaN(ctx.MicronsPerFocuserStep) && ctx.MicronsPerFocuserStep > 0)
                inspector.SensorModelFocuserSizeOverrideMicrons = ctx.MicronsPerFocuserStep;
            if (!double.IsNaN(ctx.FocalRatio) && ctx.FocalRatio > 0)
                inspector.SensorModelFRatioOverride = ctx.FocalRatio;
            var currentCtx = CaptureMeasurementContext(inspector.InspectorOptions, HocusFocusPlugin.AutoFocusOptions,
                profileService.ActiveProfile.TelescopeSettings.FocalRatio,
                profileService.ActiveProfile.TelescopeSettings.FocalLength);
            var drift = DescribeMeasurementContextDrift(ctx, currentCtx);
            if (!string.IsNullOrEmpty(drift))
                Notification.ShowWarning($"Replaying with captured measurement settings; your current profile differs: {drift}.");
        }
```

Wrap the existing replay `foreach` in `try { … } finally { inspector.SensorModelFocuserSizeOverrideMicrons = prevFocuserOverride; inspector.SensorModelFRatioOverride = prevFRatioOverride; }`.

- [ ] **Step 6: Run drift test + full suite → PASS.**

- [ ] **Step 7: Manual verification**

Build the plugin. In NINA, replay `TiltCalibration_20260705_120026` with "use captured/stored settings" under a profile whose inspector `MicronsPerFocuserStep` ≠ the captured value; confirm the drift warning lists `MicronsPerFocuserStep`, and that the calibration now uses the captured focuser size (angles unchanged — they are focuser-step-independent — but the sensor-model log shows the captured value).

- [ ] **Step 8: Commit**

```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit -am "feat(tilt): restore captured focuser size/f-ratio on replay and warn on profile drift" \
  --author="George Hilios <322725+ghilios@users.noreply.github.com>"
```

---

## Notes / follow-ups (out of scope here)

- **Full fit-option restore.** This plan restores focuser size + f-ratio and *warns* on the remaining drift (RANSAC/weighting/ROI/etc.). Transiently applying every fit option during replay (so a replay bit-reproduces capture) is a larger change — track separately if bit-exact replay is required.
- **TestApp `tilt`** could consume `MeasurementContext` + the logged tilt planes to reproduce the wizard headlessly (it currently cannot on bayered runs); separate from this plan.
