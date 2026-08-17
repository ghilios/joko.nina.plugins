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

## Per-Filter Star Detection (store + binder)

Star-detection settings can be per-filter (`StarDetection/PerFilter/`). Two persisted values are owned by
`PerFilterStarDetectionStore` — not by an options class, but written through the same `PluginOptionsAccessor`:

- `PerFilterStarDetectionEnabled` — bool, default `false`.
- `PerFilterStarDetectionJson` — one JSON blob (`PerFilterStarDetectionData`) holding the pre-enable global
  seed plus, per filter name, a `StarDetectionSettingsSnapshot` and an optional `PerFilterSweepGeometry`
  (the auto-focus step size / initial offset steps override; `-1` = inherit the profile). Corrupt JSON is
  discarded with a `Logger.Warning`.

While enabled, the `StarDetectionOptions` singleton is an edit buffer: `PerFilterEditBinder` loads the selected
filter's snapshot into it and mirrors every edit back to the store, and the singleton's legacy accessor writes
are suppressed (`PersistToProfile` backed by `SuppressiblePluginOptionsAccessor`) except for the machine-local
keys in `StarDetectionOptions.MachineLocalKeys`. Consequences when changing `StarDetectionOptions`:

- A new persisted star-detection option must also be added to `StarDetectionSettingsSnapshot` (and its
  `Clone`/`FromOptions`/apply paths), or it stays global and is silently dropped from per-filter sets.
- A machine-local (per-computer, not per-filter) option must be listed in `MachineLocalKeys` and handled in
  `PerFilterStarDetectionStore.Scrub` / `StarDetectionSettingsSnapshot.CopyMachineLocalFrom`.
- The UI invariant is unchanged: every new option still needs a control in `Resources/OptionsDataTemplates.xaml`.
- Add a tooltip `TextBlock` resource (key: `<PropertyName>_Tooltip`) near the other tooltips at the top of `OptionsDataTemplates.xaml`

### Transfer between machines / filters (Export, Import, Copy Settings From)

One filter's set travels as a `StarDetectionSettingsExport` JSON file (`StarDetectionSettingsIO`): a
`starDetection` snapshot node, plus a sibling `sweepGeometry` node so the filter's auto-focus sweep goes with
it. Geometry stays OUT of the snapshot on purpose (that type is also the AF replay payload and the engine's
detector override) and out of `ImportableSettings`/`ExcludedFromImport` (both are keyed on
`IStarDetectionOptions` properties); it gets its own diff rows via
`StarDetectionSettingsDiff.BuildSweepGeometryDiff`, shown in the same confirmation dialog. In the file, `null`
geometry means "says nothing, leave the target's override alone" and an unset instance means "the source
inherits the profile, clear the target's override" — do not collapse the two.

A new star-detection option therefore also needs a `(property, label)` entry in
`StarDetectionSettingsDiff.ImportableSettings`, or an `ExcludedFromImport` entry when it describes the computer
rather than the configuration. Three reflection guards fail if any hand-written copy step misses it:
`ImportableSettings_CoverExactlyTheImportableInterfaceProperties` (the list), and in
`StarDetectionSettingsTransferCoverageTests`, `FromOptions_CapturesEverySetting` (capture) plus
`EveryImportableSetting_SurvivesAFileRoundTripAndApply` (JSON + `ApplyImportedSnapshot`). The last two need a
second valid value per property; numeric knobs get theirs from the `NumericAlternates` table there, because
`StarDetectionOptions` setters throw rather than clamp on out-of-range input.
