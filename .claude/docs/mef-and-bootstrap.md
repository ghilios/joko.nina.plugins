# MEF Composition & Plugin Bootstrap

Read this when adding or wiring a MEF export (plugin manifest, DockableVM, sequence item, pluggable behavior), touching `HocusFocusPlugin.cs`, or using the dual-constructor pattern for testability.

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
