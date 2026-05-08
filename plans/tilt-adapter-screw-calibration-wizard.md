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

## Phase 2 — Wizard UI in mock mode

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

## Phase 3 — Full implementation

**Goal**: Replace all stubs and dev scaffolding with real behavior. The wizard calls the aberration inspector, reports progress, handles cancellation, and averages multiple runs.

### Changes from Phase 2

| File | Change |
|------|--------|
| `TiltAdapterWizard/TiltAdapterWizardVM.cs` | Replace measurement stub; wire real async flow |
| `TiltAdapterWizard/DataTemplates.xaml` | Remove dev button, show real Cancel button |

### VM changes for Phase 3

- Remove all `// DEV` commands and their XAML bindings
- Add `IProgress<ApplicationStatus>` (created via `ProgressFactory.Create(applicationStatusMediator, "Tilt Adapter Wizard")` in the constructor)
- `IsMeasuring` — real property, set true at start of measurement, false on completion or failure
- `RunMeasurementCommand` — real async implementation calling `RunAveragedMeasurement()`; stores result into the appropriate reading field; sets `measurementDoneForCurrentStep = true` on success; sets `StatusText` to describe the result
- `CancelCommand` — real implementation cancelling the in-flight `measureCts`
- `RunAveragedMeasurement()` — full implementation (see spec below)
- `StatusText` — updated during and after measurement

### Phase 3 verification

1. Build succeeds
2. No dev buttons visible in the panel
3. "Run Measurement" triggers a real aberration inspector run; the Aberration Inspector panel also updates
4. `MeasurementAverageCount > 1` causes multiple runs; progress reports "Run N/M..."
5. "Cancel" during a measurement stops it; "Run Measurement" re-enables
6. Failed measurement (no stars detected) leaves the step incomplete; "Next" stays disabled
7. Completing all steps with real measurements produces plausible screw angles
8. Angles and diagram persist across NINA restarts

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

**Averaging helper** (Phase 3)

Two variants — one for tilt steps, one for the curvature calibration step:

```csharp
private async Task<(double A, double B)?> RunAveragedTiltMeasurement(CancellationToken token) {
    int count = Math.Max(1, tiltAdapterOptions.MeasurementAverageCount);
    double sumA = 0, sumB = 0;
    for (int i = 0; i < count; i++) {
        progress.Report(new ApplicationStatus { Status = $"Run {i+1}/{count}..." });
        bool ok = await inspector.AnalyzeAutoFocus(token, captureCameraBlock: true);
        if (!ok) return null;
        var m = inspector.TiltModel?.TiltPlaneModel;
        if (m == null) return null;
        sumA += m.A;  sumB += m.B;
    }
    return (sumA / count, sumB / count);
}

private async Task<double?> RunAveragedCurvatureMeasurement(CancellationToken token) {
    int count = Math.Max(1, tiltAdapterOptions.MeasurementAverageCount);
    double sum = 0;
    for (int i = 0; i < count; i++) {
        progress.Report(new ApplicationStatus { Status = $"Run {i+1}/{count}..." });
        bool ok = await inspector.AnalyzeAutoFocus(token, captureCameraBlock: true);
        if (!ok) return null;
        var pos = inspector.SensorModel?.MeanFocuserPosition;
        if (pos == null) return null;
        sum += pos.Value;
    }
    return sum / count;
}
```

`RunMeasurementCommand` dispatches to the appropriate helper based on `CurrentStep`: `AllScrewsMeasurement` calls `RunAveragedCurvatureMeasurement` and stores the result into `baselineCurvatureReading` or `allScrewsCurvatureReading`; all other measurement steps call `RunAveragedTiltMeasurement`.

**Angle calculation**
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

    tiltAdapterOptions.Screw1AngleDegrees = angle1;
    tiltAdapterOptions.Screw2AngleDegrees = angle2;
    if (tiltAdapterOptions.ScrewCount == 3) {
        // Determine rotation direction in image space before inferring screw 3.
        // Screws numbered clockwise on the physical adapter may appear counterclockwise
        // in the sensor image due to optical mirroring. If angle2 is ~240° past angle1
        // (instead of ~120°), the screws run counterclockwise in image space and screw 3
        // must be extrapolated in the opposite direction.
        double diff = NormalizeAngle(angle2 - angle1);
        double step = diff < 180.0 ? 120.0 : -120.0;
        tiltAdapterOptions.Screw3AngleDegrees = NormalizeAngle(angle2 + step);
        tiltAdapterOptions.Screw4AngleDegrees = double.NaN;
    } else {
        tiltAdapterOptions.Screw3AngleDegrees = NormalizeAngle(angle1 + 180.0);
        tiltAdapterOptions.Screw4AngleDegrees = NormalizeAngle(angle2 + 180.0);
    }
    tiltAdapterOptions.IsCalibrated = true;
    ValidateAngleSeparation();
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

**Angle separation validation**
```csharp
private void ValidateAngleSeparation() {
    int n = tiltAdapterOptions.ScrewCount;
    double expected = n == 3 ? 120.0 : 90.0;
    double diff = NormalizeAngle(tiltAdapterOptions.Screw2AngleDegrees
                               - tiltAdapterOptions.Screw1AngleDegrees);
    // diff could be expected OR (360-expected) for the "other direction"
    double deviation = Math.Min(Math.Abs(diff - expected), Math.Abs(diff - (360 - expected)));
    HasWarning = deviation > 30.0;
    WarningText = HasWarning
        ? $"Screw 1→2 measured angle gap is {diff:F1}° (expected ~{expected}°). Consider recalibrating."
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

## Notes for Future Use

`HocusFocusPlugin.TiltAdapterOptions` exposes the full `ITiltAdapterOptions` interface (or the concrete `TiltAdapterOptions`). Future correction guidance VMs inject it via MEF or access it statically to read `ScrewCount`, `IsCalibrated`, and `Screw1-4AngleDegrees`. The angles are in image-space degrees clockwise from top, consistent with the coordinate system used by `TiltPlaneModel.A`/`B`.
