# Tilt Adapter Screw Calibration Wizard

## Context

Tilt adapters have 3 or 4 screws for leveling a camera to the telescope's focal plane. AberrationInspector already measures the tilt plane but leaves the user to figure out which screws to turn. This wizard identifies each screw's angular position in image space (clockwise degrees from top) by measuring how the tilt plane changes when individual screws are adjusted. The stored angles enable future guidance components to give precise screw-by-screw correction instructions.

---

## Wizard Steps (enum `WizardStep`)

```
BaselineMeasurement(0) → ScrewNumbering(1) →
AllScrewsAdjustment(2) → AllScrewsMeasurement(3) →
Screw1Adjustment(4) → Screw1Measurement(5) →
Screw2Adjustment(6) → Screw2Measurement(7) → Complete(8)
```

`ScrewCount` and `MeasurementAverageCount` are persistent settings configured in the dockable panel **before** starting the wizard. The wizard is launched by a button on that panel, not by a first wizard step.

### Step instructions (per ScrewCount)

| Step | 3-screw | 4-screw |
|------|---------|---------|
| AllScrewsAdjustment | Turn ALL screws INWARD 1 full turn | Turn ALL screws INWARD 1 full turn |
| AllScrewsMeasurement | (measurement — no user action) | (measurement — no user action) |
| Screw1Adjustment | Return ALL screws to baseline, then turn screw 1 INWARD 1 full turn | Return ALL screws to baseline, then turn screw 1 INWARD + screw 3 OUTWARD 1 full turn each |
| Screw2Adjustment | Turn screw 1 back OUT, then turn screw 2 INWARD 1 full turn | Return screws 1+3, then turn screw 2 INWARD + screw 4 OUTWARD |
| Complete | Restore all screws to original position | Same |

---

## Phase 1 — Static UI with mock data controls ✅ COMPLETE (commit eb3d3b5)

**Goal**: The dockable panel appears in NINA, Panel A (settings + diagram) and Panel C (complete view) are fully laid out. Temporary dev buttons allow setting and clearing mock calibration data so the diagram rendering can be reviewed before any real logic is written.

**Stop and review before proceeding to Phase 2.**

### Files to create

| File | Notes |
|------|-------|
| `Interfaces/ITiltAdapterOptions.cs` | Full interface — needed for VM binding |
| `TiltAdapterWizard/TiltAdapterOptions.cs` | Full implementation — persists settings |
| `TiltAdapterWizard/TiltScrewDiagramItem.cs` | POCO — needed for diagram ItemsControl |
| `TiltAdapterWizard/TiltAdapterWizardVM.cs` | Partial — see scope below |
| `TiltAdapterWizard/DataTemplates.xaml.cs` | MEF export boilerplate |
| `TiltAdapterWizard/DataTemplates.xaml` | Panel A + Panel C only; Panel B omitted |

### Files to modify

| File | Change |
|------|--------|
| `HocusFocusPlugin.cs` | Add `TiltAdapterOptions` static property; initialize in constructor |

### VM scope for Phase 1

Implement the full class skeleton but only wire up what is needed for static rendering:

- Constructor: title, `ScrewDiagramItems` collection, `RebuildDiagram()` on load if calibrated, wire commands
- `TiltAdapterOptions` property (exposed for XAML binding)
- `ScrewDiagramItems` observable collection
- `RebuildDiagram()` — full implementation (reads angles from options, populates collection)
- `IsWizardRunning` property — hardcoded `false` for Phase 1 (Panel B hidden)
- **Dev-only commands** (clearly marked with `// DEV` comments, to be removed in Phase 3):
  - `DevSetMock3ScrewCommand` — sets `Screw1=0°, Screw2=120°, Screw3=240°, ScrewCount=3, IsCalibrated=true` then calls `RebuildDiagram()`
  - `DevSetMock4ScrewCommand` — sets `Screw1=45°, Screw2=135°, Screw3=225°, Screw4=315°, ScrewCount=4, IsCalibrated=true` then calls `RebuildDiagram()`
  - `DevClearCalibrationCommand` — sets `IsCalibrated=false`, clears angles to `NaN`, calls `RebuildDiagram()`
- `StartCommand` — stub, body is `throw new NotImplementedException()`
- All other commands — omitted

### XAML scope for Phase 1

Implement Panel A and Panel C in full. Add the three dev buttons inside Panel A (below the Start button). Omit Panel B entirely. `IsWizardRunning` being hardcoded `false` means Panel B triggers are never met anyway.

### Phase 1 verification

1. Build succeeds
2. "Tilt Adapter Wizard" panel appears in NINA's dockable panel list
3. Panel shows: ScrewCount ComboBox, MeasurementAverageCount IntBox, "Start Calibration" button (disabled/stub ok), three dev buttons, "No calibration saved." message
4. Click "Set mock 3-screw" → message disappears, diagram shows three numbered circles at top, lower-right, lower-left; angle TextBlocks show 0°, 120°, 240°
5. Click "Set mock 4-screw" → diagram updates to four circles at 45°, 135°, 225°, 315°
6. Click "Clear" → diagram disappears, "No calibration saved." message returns
7. Changing ScrewCount ComboBox to 3 hides the Screw 4 angle row; setting to 4 shows it
8. Panel C content (angles + diagram) is visually identical to Panel A sub-panel (same XAML bindings, just different panel context)

---

## Phase 2 — Wizard UI in mock mode ✅ COMPLETE (commit 932dc82)

**Goal**: The full wizard flow is navigable. A temporary "Simulate Measurement" button stands in for the real aberration inspector call, immediately marking the current measurement step as done and injecting synthetic tilt readings. The angle calculation and diagram rendering run for real with the synthetic data so the Complete screen can be reviewed.

**Stop and review before proceeding to Phase 3.**

### Changes from Phase 1

| File | Change |
|------|--------|
| `TiltAdapterWizard/TiltAdapterWizardVM.cs` | Extend with wizard navigation and mock measurement |
| `TiltAdapterWizard/DataTemplates.xaml` | Add Panel B (wizard steps) |

No new files. No changes to `HocusFocusPlugin.cs` or options files.

### VM additions for Phase 2

Add all wizard navigation state and logic, but keep measurement as a synchronous stub:

- `CurrentStep` property with full derived properties: `IsOnMeasurementStep`, `IsOnAdjustmentStep`, `IsComplete`, `CanAdvance`, `StepInstructions`
- `IsMeasuring` — hardcoded `false` for Phase 2
- `measurementDoneForCurrentStep` internal field
- Internal reading fields: `baselineReading`, `screw1Reading`, `screw2Reading`
- `StartCommand` — real implementation: set `CurrentStep = BaselineMeasurement`, `IsWizardRunning = true`, `measurementDoneForCurrentStep = false`
- `NextStepCommand` — real implementation including `CalculateAndSaveAngles()` + `RebuildDiagram()` before entering `Complete`
- `PreviousStepCommand` — real implementation
- `RestartCommand` — real implementation
- `CalculateAndSaveAngles()` — full implementation (runs real math on whatever readings are in the fields)
- `ValidateAngleSeparation()` — full implementation
- **Dev-only command** (clearly marked `// DEV`, to be removed in Phase 3):
  - `DevSimulateMeasurementCommand` — visible only on measurement steps; sets `measurementDoneForCurrentStep = true`; injects synthetic readings into `baselineReading` / `screw1Reading` / `screw2Reading` appropriate to the current step:
    - BaselineMeasurement: `baselineReading = (0.0, 0.0)`
    - Screw1Measurement: `screw1Reading = (0.05, 0.0)` (screw 1 at ~90° — right side)
    - Screw2Measurement: `screw2Reading = (0.0, -0.05)` (screw 2 at ~0° — top)
  - These values produce angle1≈90°, angle2≈0° for a 3-screw set, yielding screw3≈240° — a valid non-trivial result to verify the math
- `RunMeasurementCommand` — stub, body is `throw new NotImplementedException()`
- `CancelCommand` — omitted
- Dev buttons from Phase 1 (`DevSetMock3ScrewCommand`, etc.) — **remove**

### XAML additions for Phase 2

Add Panel B (wizard steps) with `DevSimulateMeasurementCommand` button visible only on measurement steps, alongside the (still-stubbed) "Run Measurement" button.

### Phase 2 verification

1. Build succeeds
2. "Start Calibration" launches the wizard; Panel A collapses, Panel B shows at BaselineMeasurement
3. "Next" is disabled until "Simulate Measurement" is clicked; clicking it enables "Next"
4. "Back" returns to the previous step; "Next" re-disables (measurement not yet done for that step)
5. Adjustment steps (no measurement needed) have "Next" enabled immediately; "Simulate Measurement" button is hidden
6. Advancing through all steps reaches the Complete panel
7. Complete panel shows calculated angles (≈90°, 0°, 270° for 3-screw with synthetic data) and the diagram with circles at those positions
8. No warning shown (90° separation between screws 1 and 2 is within tolerance for 3-screw expected 120° — deviation 30°, borderline; adjust synthetic data if a clean pass is preferred)
9. "Done" returns to Panel A with calibration results visible
10. "Abort Wizard" mid-wizard returns to Panel A; previous calibration (if any) is unchanged

---

## Phase 3 — Full implementation ✅ COMPLETE

**Goal**: Replace all stubs and dev scaffolding with real behavior. The wizard calls the aberration inspector, reports progress, handles cancellation, and averages multiple runs. A live graph from the inspector is displayed during and after each measurement, a per-step measurement summary table is shown below the graph, and measurement consistency is validated across multiple runs.

### Changes from Phase 2

| File | Change |
|------|--------|
| `TiltAdapterWizard/TiltAdapterWizardVM.cs` | Replace measurement stub; wire real async flow; add summary and consistency logic |
| `TiltAdapterWizard/TiltMeasurementSummaryRow.cs` | New: row model for per-step measurement summary table |
| `TiltAdapterWizard/DataTemplates.xaml` | Remove dev button; add live inspector view, consistency warning, and summary table to Panel B |
| `AutoFocus/DataTemplates.xaml` | Add `HF_InspectorLiveView` sub-DataTemplate (graph + tilt visualization only) |

### VM changes for Phase 3

- Remove all `// DEV` commands and their XAML bindings
- Add `IProgress<ApplicationStatus>` (created via `ProgressFactory.Create(applicationStatusMediator, "Tilt Adapter Wizard")` in the constructor)
- `IsMeasuring` — real property, set true at start of measurement, false on completion or failure
- `RunMeasurementCommand` — real async implementation; dispatches to `RunAveragedTiltMeasurement` or `RunAveragedCurvatureMeasurement`; stores result into the appropriate reading field; sets `measurementDoneForCurrentStep = true` on success
- `CancelCommand` — real implementation cancelling the in-flight `measureCts`
- `StatusText` — updated during and after measurement ("Run N/M... (Xs remaining)", "Measurement complete.", "Measurement cancelled.")
- `InspectorVM Inspector { get; }` — expose the injected inspector as a public read-only property for XAML binding
- `ObservableCollection<TiltMeasurementSummaryRow> StepMeasurementSummary` — populated at the end of each tilt measurement step; cleared when the wizard restarts or a new step begins
- `bool HasMeasurementConsistencyWarning` / `string MeasurementConsistencyWarningText` — set when max deviation across runs exceeds `MeasurementConsistencyWarningThreshold`
- Private constant `MeasurementConsistencyWarningThreshold` — see spec below; start at `0.02` and tune against real data

### XAML additions to Panel B (wizard steps)

After the measurement progress area, insert the following in order (each controlled by visibility triggers):

1. **Live inspector view** — `ContentControl` bound to `Inspector`, using `ContentTemplate="{StaticResource HF_InspectorLiveView}"`. Visible whenever `IsMeasuring` is true **or** `StepMeasurementSummary.Count > 0` (so the graph stays up after the measurement completes, until the step advances).

2. **Consistency warning** — red `TextBlock` or `Border` bound to `MeasurementConsistencyWarningText`, visible when `HasMeasurementConsistencyWarning`. Use `NotificationErrorBrush` for the foreground/border color, consistent with other warning displays in the plugin.

3. **Measurement summary table** — `DataGrid` or `ItemsControl` bound to `StepMeasurementSummary`, visible when `StepMeasurementSummary.Count > 0`. Columns: Run (header "Run", blank for average row), rotation direction (header "Direction", value `{0:F1}°`), tilt magnitude (header "Magnitude"). When `IsAverage` is true the row is rendered in bold; all other rows use the default font weight. If `MeasurementAverageCount == 1` the single row renders without the "Avg" label.

### `HF_InspectorLiveView` DataTemplate (in `AutoFocus/DataTemplates.xaml`)

A new `DataTemplate x:Key="HF_InspectorLiveView"` whose `DataType` is `InspectorVM`. It contains only the aberration contour graph and the tilt visualization portion of the existing `InspectorVM_Dockable` DataTemplate — extract those sub-trees into this reusable template. The wizard's `ContentControl` then references this key.

### Phase 3 verification

1. Build succeeds
2. No dev buttons visible in the panel
3. "Run Measurement" triggers a real aberration inspector run; the graph in the wizard panel updates live during the run
4. After the run completes the graph and summary table remain visible below the step instructions
5. `MeasurementAverageCount > 1` causes multiple runs; progress reports "Run N/M..."; summary table shows one row per run plus a bold average row
6. When runs are inconsistent (max tilt-vector deviation > threshold) a red warning appears beneath the graph
7. "Cancel" during a measurement stops it and the wizard stays on the current step; "Run Measurement" re-enables
8. Failed measurement (no stars detected) leaves the step incomplete; Run Measurement re-enables
9. Completing all steps with real measurements produces plausible screw angles
10. Angles and diagram persist across NINA restarts

---

## Reference Spec

### `Interfaces/ITiltAdapterOptions.cs`

```csharp
namespace NINA.Joko.Plugins.HocusFocus.Interfaces {
    public interface ITiltAdapterOptions : INotifyPropertyChanged {
        int ScrewCount { get; set; }             // 3 or 4; 0 = not yet set
        bool IsCalibrated { get; set; }
        double Screw1AngleDegrees { get; set; }  // clockwise from top in image space
        double Screw2AngleDegrees { get; set; }
        double Screw3AngleDegrees { get; set; }
        double Screw4AngleDegrees { get; set; }  // double.NaN when 3-screw setup
        int CalibratedScrewCount { get; set; }   // ScrewCount at time of calibration; 0 = never calibrated
        int MeasurementAverageCount { get; set; } // default 1
        int ScrewInwardCurvatureSign { get; set; } // +1 if inward raises curvature; -1 if lowers; 0 = not yet calibrated
    }
}
```

### `TiltAdapterWizard/TiltAdapterOptions.cs`

Exact pattern as `InspectorOptions.cs`:
- Extends `BaseINPC`, implements `ITiltAdapterOptions`
- `PluginOptionsAccessor` with `PluginOptionsAccessor.GetAssemblyGuid(typeof(AutoFocusOptions))`
- `profileService.ProfileChanged` → `InitializeOptions(); RaiseAllPropertiesChanged()`
- Defaults: `ScrewCount=3`, `IsCalibrated=false`, `Screw1-4AngleDegrees=double.NaN`, `MeasurementAverageCount=1`, `ScrewInwardCurvatureSign=0`

### `TiltAdapterWizard/TiltScrewDiagramItem.cs`

```csharp
public class TiltScrewDiagramItem {
    public double X { get; set; }         // Canvas.Left
    public double Y { get; set; }         // Canvas.Top
    public int Number { get; set; }       // 1..4
    public double AngleDegrees { get; set; }
}
```

### `HocusFocusPlugin.cs` modifications

Add using: `using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;`

In constructor body (after InspectorOptions block):
```csharp
if (TiltAdapterOptions == null) {
    TiltAdapterOptions = new TiltAdapterOptions(profileService);
}
```

Add static property (near other static option properties):
```csharp
public static TiltAdapterOptions TiltAdapterOptions { get; private set; }
```

### `TiltAdapterWizard/TiltAdapterWizardVM.cs` — full spec

**Class declaration**
```csharp
[PartCreationPolicy(CreationPolicy.Shared)]
[Export(typeof(IDockableVM))]
[Export]
public class TiltAdapterWizardVM : DockableVM {
```

**Constructors**
```csharp
[ImportingConstructor]
public TiltAdapterWizardVM(
    IProfileService profileService,
    IApplicationStatusMediator applicationStatusMediator,
    InspectorVM inspector)
    : this(profileService, applicationStatusMediator, inspector,
           HocusFocusPlugin.TiltAdapterOptions) { }

public TiltAdapterWizardVM(
    IProfileService profileService,
    IApplicationStatusMediator applicationStatusMediator,
    InspectorVM inspector,
    ITiltAdapterOptions tiltAdapterOptions)
    : base(profileService) { ... }
```

`InspectorVM` is `[Export]` + `CreationPolicy.Shared` — injected directly.

**Key properties**
- `bool IsWizardRunning` — toggles between the settings/results panel and the wizard steps panel
- `WizardStep CurrentStep` — raises `StepInstructions`, `IsOnMeasurementStep`, `IsOnAdjustmentStep`, `IsComplete`, `CanAdvance`
- `bool IsMeasuring` — blocks Next/Run commands during async work
- `bool CanAdvance` — true when on a non-measurement step OR `measurementDoneForCurrentStep`
- `string StepInstructions` — per-step text, screw-count-aware
- `string StatusText` — live measurement feedback
- `bool HasWarning` / `string WarningText` — shown on Complete step if angle separation check fails
- `ITiltAdapterOptions TiltAdapterOptions` — exposed for XAML binding
- `ObservableCollection<TiltScrewDiagramItem> ScrewDiagramItems`

**Internal state**
```csharp
private (double A, double B) baselineReading;
private (double A, double B) screw1Reading;
private (double A, double B) screw2Reading;
private double baselineCurvatureReading;  // mean focuser position at baseline
private double allScrewsCurvatureReading; // mean focuser position after all-screws-inward
private bool measurementDoneForCurrentStep;
private CancellationTokenSource measureCts;
```

`IsOnMeasurementStep` is true for `BaselineMeasurement`, `AllScrewsMeasurement`, `Screw1Measurement`, `Screw2Measurement`.

**Commands**
| Command | Type | CanExecute |
|---------|------|------------|
| `StartCommand` | `AsyncRelayCommand` | `!IsWizardRunning` |
| `RunMeasurementCommand` | `AsyncRelayCommand` | `IsOnMeasurementStep && !IsMeasuring` |
| `NextStepCommand` | `RelayCommand` | `CanAdvance` |
| `PreviousStepCommand` | `RelayCommand` | `CurrentStep > BaselineMeasurement && !IsMeasuring` |
| `CancelCommand` | `RelayCommand` | `IsMeasuring` |
| `RestartCommand` | `RelayCommand` | always |

`StartCommand`: set `measurementDoneForCurrentStep = false`, `CurrentStep = BaselineMeasurement`, `IsWizardRunning = true`.

`NextStep` logic:
- When advancing from `AllScrewsMeasurement` → `Screw1Adjustment`: call `CalculateAndSaveCurvatureSign()`.
- When advancing to `Complete`: call `CalculateAndSaveAngles()` then `RebuildDiagram()` before setting `CurrentStep = Complete`.

`RestartCommand`: cancel any in-flight measurement, set `IsWizardRunning = false`, reset `CurrentStep = BaselineMeasurement`, clear all internal readings (tilt and curvature). Does **not** clear `IsCalibrated`, saved angles, or `ScrewInwardCurvatureSign`.

**Curvature sign helper**
```csharp
private void CalculateAndSaveCurvatureSign() {
    double delta = allScrewsCurvatureReading - baselineCurvatureReading;
    tiltAdapterOptions.ScrewInwardCurvatureSign = delta >= 0 ? 1 : -1;
}
```

The curvature reading is the mean focuser position reported by the sensor model (a proxy for how far the whole sensor plane has moved from the focal plane). When all screws are turned inward equally the tilt (A, B) is unchanged but the mean focuser position shifts; the sign of that shift is the calibration result.

**Averaging helpers** (Phase 3)

Two variants — one for tilt steps, one for the curvature calibration step.

`RunMeasurementCommand` dispatches based on `CurrentStep`: `AllScrewsMeasurement` calls `RunAveragedCurvatureMeasurement` and stores the result in `baselineCurvatureReading` or `allScrewsCurvatureReading`; all other measurement steps call `RunAveragedTiltMeasurement`.

```csharp
// Tunable: max tilt-vector deviation (in A/B gradient units) across runs before warning.
private const double MeasurementConsistencyWarningThreshold = 0.02;

private async Task<(double A, double B)?> RunAveragedTiltMeasurement(CancellationToken token) {
    StepMeasurementSummary.Clear();
    HasMeasurementConsistencyWarning = false;

    int count = Math.Max(1, tiltAdapterOptions.MeasurementAverageCount);
    var readings = new List<(double A, double B)>(count);

    for (int i = 0; i < count; i++) {
        token.ThrowIfCancellationRequested();
        StatusText = $"Run {i + 1}/{count}...";
        bool ok = await inspector.AnalyzeAutoFocus(token, captureCameraBlock: true);
        if (!ok) return null;
        var m = inspector.TiltModel?.TiltPlaneModel;
        if (m == null) return null;
        readings.Add((m.A, m.B));
    }

    double avgA = readings.Average(r => r.A);
    double avgB = readings.Average(r => r.B);

    // Populate summary table
    for (int i = 0; i < readings.Count; i++) {
        var (a, b) = readings[i];
        StepMeasurementSummary.Add(new TiltMeasurementSummaryRow {
            RunNumber   = i + 1,
            Direction   = NormalizeAngle(Math.Atan2(a, -b) * 180.0 / Math.PI),
            Magnitude   = Math.Sqrt(a * a + b * b),
            IsAverage   = false
        });
    }
    if (count > 1) {
        StepMeasurementSummary.Add(new TiltMeasurementSummaryRow {
            RunNumber   = 0,
            Direction   = NormalizeAngle(Math.Atan2(avgA, -avgB) * 180.0 / Math.PI),
            Magnitude   = Math.Sqrt(avgA * avgA + avgB * avgB),
            IsAverage   = true
        });

        // Consistency check: max Euclidean distance from mean in (A, B) space
        double maxDev = readings.Max(r =>
            Math.Sqrt(Math.Pow(r.A - avgA, 2) + Math.Pow(r.B - avgB, 2)));
        if (maxDev > MeasurementConsistencyWarningThreshold) {
            HasMeasurementConsistencyWarning = true;
            MeasurementConsistencyWarningText =
                $"Measurements inconsistent: max deviation {maxDev:F4} exceeds {MeasurementConsistencyWarningThreshold:F4}. Consider re-running.";
        }
    }

    return (avgA, avgB);
}

private async Task<double?> RunAveragedCurvatureMeasurement(CancellationToken token) {
    int count = Math.Max(1, tiltAdapterOptions.MeasurementAverageCount);
    double sum = 0;
    for (int i = 0; i < count; i++) {
        token.ThrowIfCancellationRequested();
        StatusText = $"Run {i + 1}/{count}...";
        bool ok = await inspector.AnalyzeAutoFocus(token, captureCameraBlock: true);
        if (!ok) return null;
        var pos = inspector.SensorModel?.MeanFocuserPosition;
        if (pos == null) return null;
        sum += pos.Value;
    }
    return sum / count;
}
```

**`TiltMeasurementSummaryRow`** (new file `TiltAdapterWizard/TiltMeasurementSummaryRow.cs`):

```csharp
public class TiltMeasurementSummaryRow {
    public int    RunNumber  { get; set; }  // 1-based; 0 = average row
    public double Direction  { get; set; }  // clockwise-from-top angle in degrees: atan2(A, -B)
    public double Magnitude  { get; set; }  // sqrt(A²+B²) in tilt-plane gradient units
    public bool   IsAverage  { get; set; }
}
```

`Direction` is the same coordinate system as the stored screw angles (clockwise from top in image space). `Magnitude` is in raw gradient units (same as A and B); no unit conversion is applied here since the wizard uses relative comparisons, not absolute physical values.

**Angle calculation**

The two direct measurements (screw 1, screw 2) give noisy estimates of the true screw angles. Rather than storing the raw measurements, apply a constrained least-squares fit that enforces the equal-spacing invariant (120° for 3-screw, 90° for 4-screw) while minimising total angular error across both measurements.

The fit minimises `(θ₁ - angle1)² + (θ₁ + s - angle2)²` where `s` is the equal angular step. The closed-form solution shifts `angle1` by half the residual from the ideal gap, splitting error evenly:

```
θ₁ = angle1 + (rawDiff - expectedDiff) / 2
```

where `rawDiff = NormalizeAngle(angle2 - angle1)` and `expectedDiff` is 120° (CW 3-screw), 240° (CCW 3-screw), 90° (CW 4-screw), or 270° (CCW 4-screw). All remaining angles are derived from `θ₁` by the equal step, so stored angles always satisfy the constraint exactly.

```csharp
private void CalculateAndSaveAngles() {
    double d1A = screw1Reading.A - baselineReading.A;
    double d1B = screw1Reading.B - baselineReading.B;
    double d2A = screw2Reading.A - baselineReading.A;
    double d2B = screw2Reading.B - baselineReading.B;

    // Image coords: A=x-gradient (right=+), B=y-gradient (down=+)
    // clockwise-from-top angle: atan2(dA, -dB) → 0°=up, 90°=right, 180°=down
    double angle1 = NormalizeAngle(Math.Atan2(d1A, -d1B) * 180.0 / Math.PI);
    double angle2 = NormalizeAngle(Math.Atan2(d2A, -d2B) * 180.0 / Math.PI);

    double rawDiff = NormalizeAngle(angle2 - angle1);
    bool clockwise = rawDiff < 180.0;
    int n = tiltAdapterOptions.ScrewCount;

    if (n == 3) {
        double s = clockwise ? 120.0 : -120.0;
        double expectedDiff = clockwise ? 120.0 : 240.0;
        double theta1 = NormalizeAngle(angle1 + (rawDiff - expectedDiff) / 2.0);
        tiltAdapterOptions.Screw1AngleDegrees = theta1;
        tiltAdapterOptions.Screw2AngleDegrees = NormalizeAngle(theta1 + s);
        tiltAdapterOptions.Screw3AngleDegrees = NormalizeAngle(theta1 + 2 * s);
        tiltAdapterOptions.Screw4AngleDegrees = double.NaN;
    } else {
        double s = clockwise ? 90.0 : -90.0;
        double expectedDiff = clockwise ? 90.0 : 270.0;
        double theta1 = NormalizeAngle(angle1 + (rawDiff - expectedDiff) / 2.0);
        double theta2 = NormalizeAngle(theta1 + s);
        tiltAdapterOptions.Screw1AngleDegrees = theta1;
        tiltAdapterOptions.Screw2AngleDegrees = theta2;
        // Opposite screws are always 180° apart regardless of mirroring.
        tiltAdapterOptions.Screw3AngleDegrees = NormalizeAngle(theta1 + 180.0);
        tiltAdapterOptions.Screw4AngleDegrees = NormalizeAngle(theta2 + 180.0);
    }

    tiltAdapterOptions.CalibratedScrewCount = tiltAdapterOptions.ScrewCount;
    tiltAdapterOptions.IsCalibrated = true;
    ValidateAngleSeparation(rawDiff);
}

private static double NormalizeAngle(double deg) => ((deg % 360) + 360) % 360;
```

**Why `atan2(dA, -dB)`:**
Turning a screw inward at the image-top (low Y) moves focus higher at negative-norm_y → B decreases → dB < 0.
`atan2(0, -(-)) = atan2(0, +) = 0°` ✓
For right-side screw: dA > 0 → `atan2(+, 0) = 90°` ✓

**4-screw pairing math:**
Turning screw 1 inward (at angle θ) contributes delta toward θ.
Turning screw 3 outward (at θ+180°) contributes delta toward θ+180°+180° = θ.
Both contributions add → combined delta points at θ, amplitude doubled. Same formula works.

**Equal spacing requirements:**
- 3-screw: 120° between each screw (equilateral triangle)
- 4-screw: 90° between adjacent screws (square); opposite screws are always 180° apart

**Angle separation validation**

Because stored angles always satisfy the equal-spacing constraint exactly after the constrained fit, validation must operate on the raw measured `rawDiff` (before fitting) to detect poor-quality calibrations. The diff is folded to [0°, 180°] so CW and CCW measurements compare against the same expected value.

```csharp
private void ValidateAngleSeparation(double rawDiff) {
    int n = tiltAdapterOptions.ScrewCount;
    double expected = n == 3 ? 120.0 : 90.0;
    double foldedDiff = rawDiff <= 180.0 ? rawDiff : 360.0 - rawDiff;
    double deviation = Math.Abs(foldedDiff - expected);
    HasWarning = deviation > 30.0;
    WarningText = HasWarning
        ? $"Screw 1→2 measured angle gap is {foldedDiff:F1}° (expected ~{expected}°). Consider recalibrating."
        : string.Empty;
}
```

**Diagram builder**
Canvas is 200×200, center (100, 100), radius 75. Screw circle half-size = 12.
```csharp
private void RebuildDiagram() {
    ScrewDiagramItems.Clear();
    int n = tiltAdapterOptions.ScrewCount;
    if (n < 3 || !tiltAdapterOptions.IsCalibrated) return;
    var angles = new[] {
        tiltAdapterOptions.Screw1AngleDegrees,
        tiltAdapterOptions.Screw2AngleDegrees,
        tiltAdapterOptions.Screw3AngleDegrees,
        n == 4 ? tiltAdapterOptions.Screw4AngleDegrees : double.NaN
    };
    for (int i = 0; i < n; i++) {
        double theta = angles[i] * Math.PI / 180.0;
        double cx = 100 + 75 * Math.Sin(theta);
        double cy = 100 - 75 * Math.Cos(theta);
        ScrewDiagramItems.Add(new TiltScrewDiagramItem {
            X = cx - 12, Y = cy - 12, Number = i + 1, AngleDegrees = angles[i]
        });
    }
}
```

### `TiltAdapterWizard/DataTemplates.xaml` — full XAML structure

ResourceDictionary with `x:Class="NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard.DataTemplates"`.

**Required DataTemplate key:** `NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard.TiltAdapterWizardVM_Dockable`

```
ScrollViewer
└── StackPanel (root)
    │
    ├── Panel A: Settings + launch (visible when !IsWizardRunning)
    │   ├── "Settings" header TextBlock
    │   ├── ComboBox  (ScrewCount: 3 or 4, bound to TiltAdapterOptions.ScrewCount)
    │   ├── IntBox    (MeasurementAverageCount, bound to TiltAdapterOptions.MeasurementAverageCount)
    │   ├── Button    "Start Calibration" (StartCommand)
    │   ├── [Phase 1 only] Button "Set mock 3-screw" (DevSetMock3ScrewCommand)
    │   ├── [Phase 1 only] Button "Set mock 4-screw" (DevSetMock4ScrewCommand)
    │   ├── [Phase 1 only] Button "Clear calibration" (DevClearCalibrationCommand)
    │   │
    │   ├── TextBlock "No calibration saved." (visible when !TiltAdapterOptions.IsCalibrated)
    │   │
    │   └── Sub-panel: Saved calibration diagram (visible when TiltAdapterOptions.IsCalibrated)
    │       ├── Diagram: Canvas 200×200
    │       │       ├── Rectangle (image plane outline, inset 10px)
    │       │       ├── Crosshair lines at center
    │       │       └── ItemsControl (ScrewDiagramItems)
    │       │           └── ItemTemplate: Grid(Ellipse + Number TextBlock)
    │       │               Canvas.Left={Binding X}, Canvas.Top={Binding Y}
    │       └── Results: Screw 1-4 angle TextBlocks beneath the diagram
    │           (Screw 4 row hidden via DataTrigger when ScrewCount == 3)
    │
    ├── Panel B: Active wizard steps (visible when IsWizardRunning && !IsComplete)
    │   ├── StepInstructions TextBlock
    │   ├── "Run Measurement" button (visible only on measurement steps; stub in Phase 2)
    │   ├── [Phase 2 only] "Simulate Measurement" button (DevSimulateMeasurementCommand, visible only on measurement steps)
    │   ├── "Cancel" button (visible only when IsMeasuring; Phase 3 only)
    │   ├── StatusText TextBlock
    │   ├── Back / Next buttons
    │   └── "Abort Wizard" button (RestartCommand) — returns to Panel A without saving
    │
    └── Panel C: Complete step (visible when IsComplete)
        ├── "Calibration Complete!" header
        ├── StepInstructions TextBlock  ("Restore all screws to original position")
        ├── Warning border (visible when HasWarning)
        ├── Results: Screw 1-4 angle TextBlocks
        │   (Screw 4 row hidden via DataTrigger when ScrewCount == 3)
        ├── Diagram: Canvas 200×200 (same structure as Panel A sub-panel)
        └── Button "Done" (RestartCommand) — sets IsWizardRunning = false, returns to Panel A
```

ComboBox for ScrewCount uses `x:Array` of `System.Int32`:
```xml
<ComboBox SelectedItem="{Binding TiltAdapterOptions.ScrewCount}">
    <ComboBox.ItemsSource>
        <x:Array Type="{x:Type sys:Int32}">
            <sys:Int32>3</sys:Int32>
            <sys:Int32>4</sys:Int32>
        </x:Array>
    </ComboBox.ItemsSource>
</ComboBox>
```

Panel A and Panel B/C visibility use a `DataTrigger` on `IsWizardRunning`.
Screw 4 angle TextBlock uses a `DataTrigger` on `TiltAdapterOptions.ScrewCount` to hide when value is 3.

---

## Phase 4 — Tilt Guidance in Aberration Inspector

**Goal**: Add a live tilt correction guidance section to the Aberration Inspector panel. When a calibration model is present the section shows a per-screw adjustment table (arrow indicators) and a curvature direction note, updated after each measurement run. When no calibration is present it shows a prompt to run the Tilt Adapter Wizard instead.

**Stop and review before proceeding.**

### Changes from Phase 3

| File | Change |
|------|--------|
| `AutoFocus/TiltScrewGuidanceRow.cs` | New: per-screw row model for guidance table |
| `AutoFocus/InspectorVM.cs` | Add guidance computation; react to calibration and tilt result changes |
| `AutoFocus/DataTemplates.xaml` | Add guidance section to inspector panel |

### Guidance math

Each screw i at calibrated angle θᵢ, turned inward by tᵢ units, shifts the tilt vector by ΔA += sin(θᵢ)·tᵢ, ΔB += −cos(θᵢ)·tᵢ. To bring the current tilt (A, B) to zero, the minimum-norm solution that also keeps Σtᵢ = 0 (no net focus shift) is:

```
tᵢ = (2/n) · (−A · sin(θᵢ) + B · cos(θᵢ))
```

This formula works identically for both 3-screw and 4-screw adapters. For 4-screw it automatically enforces the mechanical coupling (t₃ = −t₁, t₄ = −t₂) because θ₃ = θ₁+180° and θ₄ = θ₂+180°, so no special-casing is needed.

### Arrow encoding

Normalize: ratioᵢ = tᵢ / max(|tⱼ|). Display as a glyph:

| ratio range | glyph | meaning |
|---|---|---|
| ≥ 0.5 | ⬆ | large inward turn |
| [0.1, 0.5) | ↑ | small inward turn |
| (−0.1, 0.1) | — | no adjustment |
| (−0.5, −0.1] | ↓ | small outward turn |
| ≤ −0.5 | ⬇ | large outward turn |

The thresholds 0.1 and 0.5 are private constants `GuidanceMinArrowThreshold` and `GuidanceLargeArrowThreshold` — tune against real data. When max(|tⱼ|) falls below a noise floor constant `GuidanceNoiseThreshold` (start at 0.005), all arrows show "—" and a "Tilt correction not needed" message replaces the table.

At most 2 screws will point inward and at most 2 will point outward for any given tilt vector (a consequence of the equal-spacing geometry). The user only needs to know relative magnitude (short vs long) to determine which screw to adjust more.

### Curvature guidance row

Below the per-screw table, show a single static row derived from `ScrewInwardCurvatureSign`:

- **+1** → "Curvature: ↑ all inward to raise · ↓ all outward to lower"
- **−1** → "Curvature: ↓ all inward to lower · ↑ all outward to raise"
- **0** → row is hidden (curvature direction not yet calibrated)

This row does not depend on the live measurement. It tells the user how to use the all-screw adjustment independent of the per-screw tilt correction.

### InspectorVM additions

- Accept `ITiltAdapterOptions tiltAdapterOptions` in the secondary (testability) constructor; in the MEF constructor pass `HocusFocusPlugin.TiltAdapterOptions`
- Subscribe to `tiltAdapterOptions.PropertyChanged`; on changes to any angle, `IsCalibrated`, `CalibratedScrewCount`, or `ScrewInwardCurvatureSign` call `RebuildTiltGuidance()`
- Call `RebuildTiltGuidance()` after each tilt measurement result is stored (same point where `TiltModel` is updated)
- `bool HasTiltAdapterCalibration` — `tiltAdapterOptions.IsCalibrated && tiltAdapterOptions.ScrewCount == tiltAdapterOptions.CalibratedScrewCount`
- `ObservableCollection<TiltScrewGuidanceRow> TiltGuidanceRows` — one row per screw, populated by `RebuildTiltGuidance()`; cleared to empty when no tilt result is available
- `string CurvatureGuidanceText` — the static direction string above; empty string when sign = 0
- `bool HasCurvatureGuidance` — `CurvatureGuidanceText.Length > 0`

### `AutoFocus/TiltScrewGuidanceRow.cs`

```csharp
public class TiltScrewGuidanceRow {
    public int    ScrewNumber { get; set; }
    public string Arrow       { get; set; }  // "⬆", "↑", "—", "↓", or "⬇"
}
```

Store the final glyph in the model (computed in `RebuildTiltGuidance`) so the DataTemplate binding is a plain `TextBlock.Text` with no converter.

### XAML additions (Aberration Inspector panel, `AutoFocus/DataTemplates.xaml`)

Add a new `Expander` section immediately after the tilt measurement section. Header: "Tilt Adapter Guidance".

**No calibration branch** (visible when `!HasTiltAdapterCalibration`):
- TextBlock: "No tilt adapter calibration found. Open the Tilt Adapter Wizard panel and run a calibration to enable guidance."

**Guidance branch** (visible when `HasTiltAdapterCalibration`):
- TextBlock "Run a measurement to see guidance." — visible when `TiltGuidanceRows.Count == 0`
- `ItemsControl` bound to `TiltGuidanceRows` (visible when `TiltGuidanceRows.Count > 0`):
  - Two columns: "Screw" (ScrewNumber) and "Adjustment" (Arrow glyph). Use a larger font for the arrow column so ⬆/↑/—/↓/⬇ render clearly.
- TextBlock bound to `CurvatureGuidanceText` — visible when `HasCurvatureGuidance`

### Phase 4 verification

1. Build succeeds
2. Aberration Inspector panel shows a "Tilt Adapter Guidance" expander
3. Without calibration: expander shows the prompt to run the wizard
4. After running the Tilt Adapter Wizard: expander switches to the guidance layout
5. Without a measurement: "Run a measurement to see guidance." is shown
6. After a measurement: table shows one row per screw with correct arrows
7. When tilt is near zero: all arrows show "—"
8. For a known tilt direction aligned directly with one screw: that screw shows ⬆/⬇ (large) and the opposite screw(s) show the complementary direction
9. Changing `ScrewCount` without recalibrating reverts the expander to the "no calibration" prompt
10. `ScrewInwardCurvatureSign` non-zero: curvature guidance row appears; zero: row is hidden

---

## Notes for Future Use

`HocusFocusPlugin.TiltAdapterOptions` exposes the full `ITiltAdapterOptions` interface (or the concrete `TiltAdapterOptions`). Future correction guidance VMs inject it via MEF or access it statically to read `ScrewCount`, `IsCalibrated`, and `Screw1-4AngleDegrees`. The angles are in image-space degrees clockwise from top, consistent with the coordinate system used by `TiltPlaneModel.A`/`B`.
