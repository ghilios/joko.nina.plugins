# WPF / XAML, Value Converters & Validation Rules

Read this when editing XAML (data templates, options UI, bindings), or adding an `IValueConverter` or `ValidationRule`.

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
