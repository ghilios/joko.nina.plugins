# HocusFocus Plugin — Claude Code Instructions

## Project Overview

NINA astrophotography plugin (C# WPF) providing advanced auto-focus, star detection, aberration inspection, and related tools. Uses MEF composition for dependency injection, CommunityToolkit.Mvvm for MVVM, and PluginOptionsAccessor for persistent settings.

**Solution**: `Joko.NINA.Plugins/Joko.NINA.Plugins.sln`
**Primary project**: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/`
**Tests**: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/`
**Assembly/RootNamespace**: `NINA.Joko.Plugins.HocusFocus`
**Target**: `.NET 8.0-windows7.0`, x64, Class Library + WPF + Windows Forms

## Domain Concepts: Tilt and Tilt Adapters

**Sensor tilt** occurs when a camera sensor is not perfectly orthogonal to the optical axis of the attached telescope. This causes one side of the image to be in better focus than another — stars in one region are sharp while stars in the opposite region are bloated/defocused. The aberration inspector detects and quantifies this tilt.

**Tilt adapters** sit between the telescope and camera and allow the sensor plane to be physically adjusted. Turning a screw inward pushes that corner of the sensor away from the telescope (increases the distance on that side), tilting the sensor plane toward the opposite side.

### Adapter configurations

- **3-screw adapter**: Screws are arranged in a triangle. Each screw can be turned fully independently.
- **4-screw adapter**: Screws are arranged in a square. Opposite screws are mechanically coupled, so adjustments must be made in pairs (or all four at once).

### Screw orientation and coordinate system

Each screw has a physical orientation relative to the sensor that determines how turning it affects the tilt vector:

- **Origin**: center of the sensor image.
- **Angle convention**: clockwise from straight up (12 o'clock) = 0°. So 0° is straight up, 90° is to the right, 180° is straight down, 270° is to the left.
- **Effect**: a screw at angle θ pushes the sensor away from the telescope in the direction opposite to θ — i.e., turning the screw inward tilts the sensor plane such that the side at angle θ moves away, which brings the opposite side (θ + 180°) closer.

Example: a screw at 0° (straight up from center) — turning it inward tilts the sensor plane along the vertical axis, pushing the top of the sensor away from the telescope.

### Image mirroring

Camera images may be mirrored horizontally and/or vertically depending on the optical train (e.g., a star diagonal introduces a mirror). **Do not assume that screws numbered clockwise around the physical adapter will appear clockwise around the sensor image.** The screw orientations must be determined from the actual image coordinates after accounting for any mirroring. Plans and features that involve tilt correction must track orientation in image-space, not physical-space.

## Plans Workflow

- **Every new plan MUST be written to the `plans/` folder** — never leave a plan only in chat, in another directory, or in a scratch file. This applies to all plans regardless of size or how they were produced (planning mode, brainstorming, ad-hoc requests, etc.).
- Use a meaningful filename based on the plan goal (e.g., `plans/tilt-adapter-screw-calibration-wizard.md`).
- **Clear your Claude Code context (`/clear`) before executing a plan** to avoid stale context from the planning session affecting implementation.
- The user will explicitly say when a plan is ready to execute.

## Git Workflow

- **Never push to `develop` directly.** Create a feature branch (`ghilios/<topic>`), push it, and open a PR — `develop` is only updated via PR merges.
- **Author + committer email** must be `322725+ghilios@users.noreply.github.com`. GitHub email-privacy blocks pushes from `ghilios@gmail.com`. When committing, set both:
  ```
  GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
    git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "..."
  ```
  If a commit slips through with `ghilios@gmail.com`, amend it with `--author=` and the env vars above before pushing.

---

## Directory Structure

```
Joko.NINA.Plugins.HocusFocus/
├── AutoFocus/         # Auto-focus engine, ViewModels, options
├── Controls/          # Custom WPF controls (ScottPlot wrappers, charts)
├── Converters/        # 40+ IValueConverter implementations
├── Inspection/        # Sensor aberration modeling
├── Interfaces/        # Service contracts
├── Properties/        # AssemblyInfo.cs (version source of truth), Settings
├── Resources/         # Shared XAML ResourceDictionaries
├── Scottplot/         # ScottPlot customizations
├── SequenceItems/     # NINA sequence instruction implementations
├── StarDetection/     # Star detection, PSF fitting, options
├── Utility/           # Algorithms, helpers, async utilities
├── ValidationRules/   # WPF ValidationRule subclasses
├── Options.xaml       # Plugin options UI root
└── HocusFocusPlugin.cs  # Plugin manifest/bootstrap (static property store)
```

## Namespace Conventions

| Purpose | Namespace |
|---|---|
| Plugin root and main services | `NINA.Joko.Plugins.HocusFocus` |
| Auto-focus engine and VMs | `NINA.Joko.Plugins.HocusFocus.AutoFocus` |
| Service interfaces | `NINA.Joko.Plugins.HocusFocus.Interfaces` |
| Algorithms and helpers | `NINA.Joko.Plugins.HocusFocus.Utility` |
| WPF value converters | `NINA.Joko.Plugins.HocusFocus.Converters` |
| Custom controls | `NINA.Joko.Plugins.HocusFocus.Controls` |
| Sensor analysis | `NINA.Joko.Plugins.HocusFocus.Inspection` |
| NINA sequence items | `NINA.Joko.Plugins.HocusFocus.SequenceItems` |
| Star detection / PSF | `NINA.Joko.Plugins.HocusFocus.StarDetection` |
| WPF validation rules | `NINA.Joko.Plugins.HocusFocus.ValidationRules` |
| Shared XAML resources | `NINA.Joko.Plugins.HocusFocus.Resources` |
| ScottPlot customizations | `NINA.Joko.Plugins.HocusFocus.Scottplot` |
| Sequence item exports | `NINA.Sequencer.SequenceItem.Autofocus` |

---

## MEF Composition

### Plugin Manifest

```csharp
[Export(typeof(IPluginManifest))]
public class HocusFocusPlugin : PluginBase {
    [ImportingConstructor]
    public HocusFocusPlugin(
        IProfileService profileService,
        ICameraMediator cameraMediator,
        IFocuserMediator focuserMediator,
        IFilterWheelMediator filterWheelMediator,
        IGuiderMediator guiderMediator,
        IImagingMediator imagingMediator,
        IImageDataFactory imageDataFactory,
        IImageSaveMediator imageSaveMediator,
        IOptionsVM options,
        IPluggableBehaviorSelector<IStarDetection> starDetectionSelector,
        IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector)
```

### DockableVM Export

```csharp
[PartCreationPolicy(CreationPolicy.Shared)]
[Export(typeof(IDockableVM))]
[Export]
public class InspectorVM : DockableVM, ICameraConsumer, IFocuserConsumer, ITelescopeConsumer
```

DataTemplate key must match: `{FullNamespace}.{ClassName}_Dockable`
```xaml
<DataTemplate x:Key="NINA.Joko.Plugins.HocusFocus.AutoFocus.HocusFocusVM_Dockable">
```

### Sequence Item Export

```csharp
[ExportMetadata("Name", "Run Aberration Inspector")]
[ExportMetadata("Description", "...")]
[ExportMetadata("Icon", "InspectorSVG")]
[ExportMetadata("Category", "Lbl_SequenceCategory_Focuser")]
[Export(typeof(ISequenceItem))]
[JsonObject(MemberSerialization.OptIn)]
public class RunAberrationInspector : SequenceItem, IValidatable
```

### Pluggable Behavior Export

```csharp
[Export(typeof(IPluggableBehavior))]
public class HocusFocusVMFactory : IAutoFocusVMFactory {
    public string Name => "Hocus Focus";
    public string ContentId => this.GetType().FullName;
    public IAutoFocusVM Create() { ... }
}
```

---

## MVVM Patterns

### Base Classes

| Base | Use When |
|---|---|
| `BaseINPC` | Options objects, plain observable models |
| `DockableVM` | Dockable panel ViewModels |
| `SequenceItem` | NINA sequence instructions |

### Commands

```csharp
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;
// AsyncRelayCommand also from CommunityToolkit.Mvvm.Input

// Async command
LoadSavedAutoFocusRunCommand = new AsyncRelayCommand(() => Task.Run(() => LoadData()));

// Sync command with canExecute
ClearAnalysesCommand = new RelayCommand(ClearAnalyses, canExecute: (o) => !AnalysisRunning());

// Cancel button for an async command
CancelCommand = new RelayCommand(Cancel);
```

Use `AsyncCommand<T>` from `NINA.Core.Utility` only when the NINA-native type is required. Default to `AsyncRelayCommand` from CommunityToolkit.Mvvm.

### Observable Properties

```csharp
private int stepCount;
public int StepCount {
    get => stepCount;
    set {
        if (stepCount != value) {
            stepCount = value;
            optionsAccessor.SetValueInt32(nameof(StepCount), stepCount);
            RaisePropertyChanged();
        }
    }
}

// Computed/dependent property — no backing field, no persistence
public string StepCountHint => $"(auto: {StepCount})";
// Notify it from the property it depends on:
// RaisePropertyChanged(nameof(StepCountHint));
```

### Mediator Consumer Registration

```csharp
public class MyVM : DockableVM, ICameraConsumer, IFocuserConsumer {
    public MyVM(ICameraMediator cameraMediator, IFocuserMediator focuserMediator, ...) {
        cameraMediator.RegisterConsumer(this);
        focuserMediator.RegisterConsumer(this);
    }
}
```

---

## Options / Settings System

### Full Pattern (follow `AutoFocus/InspectorOptions.cs`)

```csharp
public class InspectorOptions : BaseINPC, IInspectorOptions {
    private readonly PluginOptionsAccessor optionsAccessor;

    public InspectorOptions(IProfileService profileService) {
        var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(AutoFocusOptions));
        if (guid == null) throw new Exception("Guid not found in assembly metadata");
        optionsAccessor = new PluginOptionsAccessor(profileService, guid.Value);
        profileService.ProfileChanged += ProfileService_ProfileChanged;
        InitializeOptions();
    }

    private void ProfileService_ProfileChanged(object sender, EventArgs e) {
        InitializeOptions();
        RaiseAllPropertiesChanged();
    }

    private void InitializeOptions() {
        stepCount = optionsAccessor.GetValueInt32(nameof(StepCount), -1);
        // ... load all backing fields
    }

    private int stepCount;
    public int StepCount {
        get => stepCount;
        set {
            if (stepCount != value) {
                stepCount = value;
                optionsAccessor.SetValueInt32(nameof(StepCount), stepCount);
                RaisePropertyChanged();
            }
        }
    }

    public void ResetDefaults() {
        StepCount = -1;
        // ... reset all to defaults
    }
}
```

**Use `typeof(AutoFocusOptions)` as the GUID source** — all option classes share the same assembly GUID.

### Supported Accessor Types

| Method | C# type |
|---|---|
| `GetValueInt32` / `SetValueInt32` | `int` |
| `GetValueDouble` / `SetValueDouble` | `double` |
| `GetValueBoolean` / `SetValueBoolean` | `bool` |
| `GetValueString` / `SetValueString` | `string` |
| `GetValueEnum<T>` / `SetValueEnum<T>` | any `enum` |

### Options UI Requirement

Every new option added to `StarDetectionOptions` (or any other options class) **must** also have a corresponding UI control in `Resources/OptionsDataTemplates.xaml`. Options that are not exposed in the UI are invisible to users and cannot be tuned.

- Boolean options → `CheckBox` bound to `StarDetectionOptions.<PropertyName>`
- Double/numeric options → `ninactrl:UnitTextBox` with a `DoubleRangeRule` validation
- Enum options → `ComboBox` with `util:EnumBindingSource` and `HF_EnumStaticDescriptionValueConverter`
- Add a tooltip `TextBlock` resource (key: `<PropertyName>_Tooltip`) near the other tooltips at the top of `OptionsDataTemplates.xaml`

---

## Plugin Bootstrap (`HocusFocusPlugin.cs`)

Static properties store plugin-wide singletons; all other components access these rather than receiving them via MEF:

```csharp
public static StarDetectionOptions StarDetectionOptions { get; private set; }
public static StarAnnotatorOptions StarAnnotatorOptions { get; private set; }
public static AutoFocusOptions AutoFocusOptions { get; private set; }
public static InspectorOptions InspectorOptions { get; private set; }
public static AutoFocusEngineFactory AutoFocusEngineFactory { get; private set; }
public static ApplicationDispatcher ApplicationDispatcher { get; private set; }
public static IAlglibAPI AlglibAPI { get; private set; }
```

Constructor responsibilities (in order):
1. Upgrade persisted settings if needed (`Settings.Default.UpdateSettings`)
2. Lazily create all option objects
3. Register options with `IOptionsVM` for the options UI
4. Register image file name patterns (`$$FWHM$$`, `$$ECCENTRICITY$$`)
5. Subscribe to `IImageSaveMediator` to augment saved image metadata
6. Add native OpenCV DLL paths for the correct architecture
7. Create `RelayCommand` properties for settings UI buttons (reset defaults, path pickers)

### OpenCV Native Path Setup

```csharp
var archFolder = Environment.Is64BitProcess ? "x64" : "x86";
var dllPath = Path.Combine(thisAssemblyDir, "dll", archFolder);
OpenCvSharp.Internal.WindowsLibraryLoader.Instance.AdditionalPaths.Add(dllPath);
```

### Dual-Constructor Pattern (MEF + Testability)

```csharp
[ImportingConstructor]
public InspectorVM(IProfileService profileService, ...)
    : this(profileService, ...,
           HocusFocusPlugin.StarDetectionOptions,  // inject static singletons
           HocusFocusPlugin.InspectorOptions,
           ...)
{ }

public InspectorVM(IProfileService profileService, ...,
    IStarDetectionOptions starDetectionOptions,
    IInspectorOptions inspectorOptions, ...)
    : base(profileService)
{
    // actual construction using interface-typed parameters
}
```

---

## Sequence Instructions

Full implementation of a `SequenceItem`:

```csharp
[ExportMetadata("Name", "My Action")]
[ExportMetadata("Description", "...")]
[ExportMetadata("Icon", "MySVG")]
[ExportMetadata("Category", "Lbl_SequenceCategory_Focuser")]
[Export(typeof(ISequenceItem))]
[JsonObject(MemberSerialization.OptIn)]
public class MyAction : SequenceItem, IValidatable {

    [ImportingConstructor]
    public MyAction(ICameraMediator cameraMediator, IFocuserMediator focuserMediator, ...)
    { ... }

    // Clone constructor (required)
    private MyAction(MyAction cloneMe) : this(...) { CopyMetaData(cloneMe); }
    public override object Clone() => new MyAction(this);

    // Validation
    private IList<string> issues = new List<string>();
    public IList<string> Issues {
        get => issues;
        set { issues = value; RaisePropertyChanged(); }
    }

    public bool Validate() {
        var i = new List<string>();
        if (!cameraMediator.GetInfo().Connected)
            i.Add(Loc.Instance["LblCameraNotConnected"]);
        Issues = i;
        return i.Count == 0;
    }

    // Execution
    public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        var result = await DoWork(token);
        if (!result) throw new SequenceEntityFailedException("My action failed");
    }

    public override TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(estimatedSeconds);

    public override string ToString() =>
        $"Category: {Category}, Item: {nameof(MyAction)}";
}
```

---

## WPF / XAML Conventions

### ResourceDictionary with Code-Behind

```xaml
<ResourceDictionary
    x:Class="NINA.Joko.Plugins.HocusFocus.AutoFocus.DataTemplates"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <ResourceDictionary.MergedDictionaries>
        <ResourceDictionary Source="../Resources/OptionsDataTemplates.xaml" />
    </ResourceDictionary.MergedDictionaries>
```

### Key File Roles

| File | Purpose |
|---|---|
| `AutoFocus/DataTemplates.xaml` | VM data templates for dockable panels |
| `Options.xaml` | Root options page |
| `Resources/OptionsDataTemplates.xaml` | Shared option templates, converter registrations |

### DataTemplate Naming

- **Dockable panel**: `{FullNamespace}.{ClassName}_Dockable`
- **Options root**: `"Hocus Focus_Options"`
- **Nested options**: `"HocusFocus_{AreaName}_Options"`

### Converter Registration and Naming

Converter keys use `HF_` prefix:
```xaml
<hfconverters:DoubleNegativeToVisibilityConverter x:Key="HF_DoubleNegativeToVisibilityConverter" />
```

Converter class naming: `{SourceType}{Condition}To{TargetType}Converter`
Examples: `DoubleNegativeToVisibilityConverter`, `EmptyCollectionToVisibilityConverter`, `MultiBooleanToVisibilityCollapsedConverter`

### Tooltip Storage Pattern

Store tooltips as `TextBlock` resources, reference by key:
```xaml
<TextBlock x:Key="StepCount_Tooltip"
    Text="The minimum number of data points needed on each side of the AutoFocus curve minimum..." />
<!-- Usage -->
<Control ToolTip="{StaticResource StepCount_Tooltip}" />
```

### Binding Patterns

```xaml
<!-- With converter -->
<TextBlock Text="{Binding StepCount, Converter={StaticResource HF_ZeroToDoubleDashConverter}}" />

<!-- DataTrigger for conditional visibility -->
<Style.Triggers>
    <DataTrigger Binding="{Binding AutoFocusOptions.Save}" Value="False">
        <Setter Property="Height" Value="0" />
    </DataTrigger>
</Style.Triggers>

<!-- MultiBinding -->
<TextBlock.Text>
    <MultiBinding Converter="{StaticResource HF_MultiBooleanToVisibilityCollapsedConverter}">
        <Binding Path="Property1" />
        <Binding Path="Property2" />
    </MultiBinding>
</TextBlock.Text>
```

---

## Value Converters

Implement `IValueConverter`. `ConvertBack` almost always throws `NotImplementedException`.

```csharp
public class DoubleNegativeToVisibilityConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        if (value is double d)
            return d < 0.0 ? Visibility.Collapsed : Visibility.Visible;
        throw new ArgumentException("Invalid type for converter");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
```

---

## Validation Rules

```csharp
public class PositiveOddIntegerRule : ValidationRule {
    public override ValidationResult Validate(object value, CultureInfo cultureInfo) {
        var s = value?.ToString();
        if (int.TryParse(s, NumberStyles.Number, cultureInfo, out var parsed)
            && parsed > 0 && parsed % 2 == 1)
            return new ValidationResult(true, null);
        return new ValidationResult(false, "Value must be a positive odd integer");
    }
}
```

Convention: `-1` means "auto/infinite" — validated by `PositiveIntegerOrInfiniteRule`.

---

## Async Patterns

```csharp
// Threadpool work from a command
LoadCommand = new AsyncRelayCommand(() => Task.Run(() => LoadData(path)));

// Sequence Execute always takes progress + token
public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
    token.ThrowIfCancellationRequested();
    await DoWork(token);
}

// Progress reporting
private readonly IProgress<ApplicationStatus> progress;
// Created via: ProgressFactory.Create(applicationStatusMediator, "Aberration Inspector")
```

---

## Error Handling

Custom domain exceptions extend `Exception` directly:
```csharp
public class TooManyFailedMeasurementsException : Exception {
    public int NumFailures { get; }
    public TooManyFailedMeasurementsException(int numFailures)
        : base("Too many failed measurements") { NumFailures = numFailures; }
}
```

Sequence failures: throw `SequenceEntityFailedException`.
Validation errors: populate `Issues` list and return `false` from `Validate()`.

---

## Logging

```csharp
using Logger = NINA.Core.Utility.Logger;

Logger.Trace($"Frame {frame}: measured {hfr:F3}");
Logger.Debug($"State transition: {oldState} -> {newState}");
Logger.Info("Auto-focus run completed");
Logger.Warning("Retry attempt due to measurement failure");
Logger.Error("Focus failed");
Logger.Error(ex, "Unhandled exception in focus engine");
```

| Level | When |
|---|---|
| Trace | Frame-by-frame measurements, inner loops |
| Debug | State transitions, intermediate results |
| Info | Major milestones (run start/complete) |
| Warning | Retries, fallbacks, non-fatal conditions |
| Error | Failures and exceptions |

---

## Testing

**Run the unit test suite after every code change** before reporting work complete. From the solution root:

```
dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo
```

If any test fails, fix the underlying cause — do not skip, ignore, or mark tests as expected-to-fail to make the suite pass.

**Framework**: NUnit 4.4.0 + NUnit3TestAdapter

```csharp
namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility;

[TestFixture]
public class MathUtilityTests {
    [Test]
    public void MedianMAD_ScalesOddLengthMedianAbsoluteDeviation() {
        var (median, mad) = new[] { 1.0, 2.0, 3.0 }.MedianMAD();
        Assert.Multiple(() => {
            Assert.That(median, Is.EqualTo(2.0));
            Assert.That(mad, Is.EqualTo(1.483).Within(1e-12));
        });
    }
}
```

The test project links shared source files directly (e.g., `MathUtility.cs`) rather than referencing the plugin assembly.

### Star Detection Metrics UI Requirement

Every new field added to `StarDetectorMetrics` (rejection counts, flags, etc.) **must** also be displayed in the star detection metrics panel in `AutoFocus/DataTemplates.xaml`. The metrics panel uses a `UniformGrid Columns="2"` with `StackPanel` pairs. Add new entries using the `HF_ZeroToDoubleDashConverter` pattern:

```xaml
<StackPanel Orientation="Horizontal">
    <TextBlock Width="120" VerticalAlignment="Center" Text="My Metric" />
    <TextBlock Width="70" HorizontalAlignment="Center" VerticalAlignment="Center"
        Text="{Binding Metrics.MyMetricField, Converter={StaticResource HF_ZeroToDoubleDashConverter}}" />
</StackPanel>
```

---

## Gradient-Robust Contamination Test + Local Background Plane

The star detector's contamination test is **gradient-robust** (`StarDetector.ComputeGradientContamination`):
for each star it fits a robust plane `b0 + b1·dx + b2·dy` to the background-annulus pixels via IRLS (Huber)
to model a smooth one-sided background (galaxy/nebula gradient), subtracts it, then flags a star **only** when
a single octant shows a one-sided **positive** residual excess above `ContaminationSensitivity` sigma — a
contaminant adds light, so this ignores both smooth gradients (removed by the fit) and edge-clip deficits
(negative). The fitted plane (`LocalBackgroundPlane`, carried on `Star.BackgroundPlane`) doubles as the
**local background** used per-pixel for centroid, flux, HFR, and PSF, so a gradient no longer biases any
measurement; for flat fields the plane equals the annulus median (no change). The PSF fit has the gradient
*tilt* removed before fitting (zero at the star center, so the amplitude and fitted background `B` are
unaffected) for cleaner sigma/FWHM/eccentricity.

- **Quality gate**: `StarDetectorParams.RejectContaminatedStars` (option `StarDetectionOptions.
  RejectContaminatedStars`, **default ON**) rejects contaminated stars (metric `ContaminationRejected`);
  when off they are kept and only flagged (`Star.StarContaminationSuspected`, metric `ContaminationSuspected`).
- The legacy opposite-sector-median test was removed (it tripped on smooth gradients). Validated on M31 +
  Pleiades: the gradient-robust test drops smooth-gradient/edge false positives and recovers real faint
  companions that an opposing gradient had masked.

### Headless diagnostic (`TestApp`)

`TestApp` doubles as a **self-contained, headless diagnostic** for the contamination test. Use it to
root-cause / re-tune **without launching NINA** — it loads the user's real NINA profile and builds params via
`HocusFocusStarDetection.BuildStarDetectorParams` (the single options→params source of truth), then runs
detection with per-star diagnostics enabled and `RejectContaminatedStars=false` (so contaminated stars are
retained for analysis).

**Run it** (WSL interop runs the Windows `.exe` directly, so paths with spaces quote cleanly):

```bash
./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe \
  contamination --image "C:\path\to\image.xisf" --out "C:\temp\hf-diag"
```

- Build first: `cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"`.
- Args: `--image <path>` (req; `.xisf`/`.fits`/`.tif`), `--profile-id <guid>` (default: active profile),
  `--out <dir>` (default `%LOCALAPPDATA%\NINA\Logs\hf-diag\<timestamp>`), `--sensitivity <double>` (override),
  `--sensitivity-sweep <a,b,step>` (per-value CSVs → `sweep.csv`).
- No `--image`/`contamination` arg ⇒ TestApp launches its normal WPF GUI instead.

**Outputs** (in `--out`): `contamination_stars.csv` (one row per accepted star — center, HFR, background
(plane value at center), σ used, `ContaminationSuspected`, gradient-robust fields `GradientSlope`/
`LocalSigmaResidual`/`MaxSectorResidualOverSE`/`ResidualTrippingSector`, per-octant `resid*`/`residCount*`,
and per-star shape/proximity `Eccentricity`/`FWHMx`/`FWHMy`/`FWHMPixels`/`ThetaDeg`/`NearestNeighborDist`/
`NearestNeighborOverHfr`/`HasCloseNeighbor`/`PsfFitOk`); `contamination_summary.txt` (settings + flag rate +
a flagged-vs-clean profile + `MaxSectorResidualOverSE` distribution); `contamination_annotated.png` (green =
clean, magenta = flagged-with-close-neighbor, cyan = flagged-isolated); `gr_sweep.csv` (flag rate + flagged
set's median gradient slope/eccentricity vs sensitivity, from a single run); plus verbose TRACE in
`%LOCALAPPDATA%\NINA\Logs`.

**How the diagnostics hook works (off by default, zero overhead):** set
`StarDetectorParams.CollectContaminationDiagnostics = true` and read
`HocusFocusStarDetectorResult.ContaminationDiagnostics` (a `List<ContaminationDiagnosticRecord>`). When the
flag is false the detector fills no per-sector residual arrays and skips the diagnostic record (the plane fit
+ decision always run, since they are the production background/contamination path).

---

## Key File Locations

| Component | Path (relative to solution root) |
|---|---|
| Plugin bootstrap | `Joko.NINA.Plugins.HocusFocus/HocusFocusPlugin.cs` |
| Options pattern reference | `Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorOptions.cs` |
| AutoFocus options | `Joko.NINA.Plugins.HocusFocus/AutoFocus/AutoFocusOptions.cs` |
| StarDetection options | `Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionOptions.cs` |
| Inspector ViewModel | `Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorVM.cs` |
| AutoFocus ViewModel | `Joko.NINA.Plugins.HocusFocus/AutoFocus/HocusFocusVM.cs` |
| AutoFocus engine | `Joko.NINA.Plugins.HocusFocus/AutoFocus/AutoFocusEngine.cs` |
| Sequence item example | `Joko.NINA.Plugins.HocusFocus/SequenceItems/RunAberrationInspector.cs` |
| VM DataTemplates | `Joko.NINA.Plugins.HocusFocus/AutoFocus/DataTemplates.xaml` |
| Options templates | `Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml` |
| Version (AssemblyInfo) | `Joko.NINA.Plugins.HocusFocus/Properties/AssemblyInfo.cs` |
| Contamination diagnostic runner | `TestApp/ContaminationDiagnosticRunner.cs` |
| Options→params source of truth | `Joko.NINA.Plugins.HocusFocus/StarDetection/HocusFocusStarDetection.cs` (`BuildStarDetectorParams`) |
| Contamination decision + background plane | `Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetector.cs` (`ComputeGradientContamination`) |

---

## Key External Dependencies

| Package | Version | Purpose |
|---|---|---|
| `NINA.Plugin` | 3.2.0.2001-beta | NINA plugin framework (DockableVM, mediators, etc.) |
| `alglib.net` | 3.19.0 | Numerical optimization (Alglib API) |
| `MathNet.Numerics` | 5.0.0 | Mathematics library |
| `OpenCvSharp4` | 4.6.0 | Computer vision / image processing |
| `ScottPlot.WPF` | 4.1.59 | Scientific plotting in WPF |
| `KdTree` | 1.4.1 | K-D tree spatial index |
| `Dirkster.AvalonDock` | 4.70.3 | Dockable panel framework |
| `CommunityToolkit.Mvvm` | (transitive) | RelayCommand, AsyncRelayCommand, BaseINPC |

---

## Build Notes

- `AllowUnsafeBlocks: true` — required for performance-critical image processing
- `GenerateAssemblyInfo: false` — version lives in `Properties/AssemblyInfo.cs`
- Post-build copies output to `%localappdata%\NINA\Plugins\3.0.0\Hocus Focus\`
- Native OpenCV libs are in `dll\x86\` and `dll\x64\` subdirectories of the plugin folder
