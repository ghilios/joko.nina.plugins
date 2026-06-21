# Options / Settings System

Read this when adding, removing, or changing a persisted option on any `*Options` class.

**Invariant:** every new option **must** also get a UI control in `Resources/OptionsDataTemplates.xaml` (see "Options UI Requirement" below). Options not exposed in the UI are invisible to users and cannot be tuned.

## Full Pattern (follow `AutoFocus/InspectorOptions.cs`)

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

## Supported Accessor Types

| Method | C# type |
|---|---|
| `GetValueInt32` / `SetValueInt32` | `int` |
| `GetValueDouble` / `SetValueDouble` | `double` |
| `GetValueBoolean` / `SetValueBoolean` | `bool` |
| `GetValueString` / `SetValueString` | `string` |
| `GetValueEnum<T>` / `SetValueEnum<T>` | any `enum` |

## Options UI Requirement

Every new option added to `StarDetectionOptions` (or any other options class) **must** also have a corresponding UI control in `Resources/OptionsDataTemplates.xaml`. Options that are not exposed in the UI are invisible to users and cannot be tuned.

- Boolean options → `CheckBox` bound to `StarDetectionOptions.<PropertyName>`
- Double/numeric options → `ninactrl:UnitTextBox` with a `DoubleRangeRule` validation
- Enum options → `ComboBox` with `util:EnumBindingSource` and `HF_EnumStaticDescriptionValueConverter`
- Add a tooltip `TextBlock` resource (key: `<PropertyName>_Tooltip`) near the other tooltips at the top of `OptionsDataTemplates.xaml`
