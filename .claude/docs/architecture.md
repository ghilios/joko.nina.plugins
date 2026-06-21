# Architecture & Project Map

Read this when you need to locate a file, choose a namespace, check a dependency version, or understand the build output layout.

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

## Build Notes

- `AllowUnsafeBlocks: true` — required for performance-critical image processing
- `GenerateAssemblyInfo: false` — version lives in `Properties/AssemblyInfo.cs`
- Post-build copies output to `%localappdata%\NINA\Plugins\3.0.0\Hocus Focus\`
- Native OpenCV libs are in `dll\x86\` and `dll\x64\` subdirectories of the plugin folder
