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

## Alert colours — never use the NINA notification brushes as a Foreground

`NotificationErrorBrush` / `NotificationWarningBrush` are **fill** colours. NINA only ever uses them as a
`Background`, and 13 of its 18 built-in schemas set them to near-black (`#FF700000`, `#FF5E330B`). Used as a
`Foreground` they render at 1.06–1.9:1 against a dark page background. Their paired
`NotificationErrorTextBrush` is not a fix either — the "Dark" schema sets it to `#FF02010A`, i.e. near-black
text on near-black-red fill, 1.67:1.

Use `Resources/AlertBrushes.xaml` instead:

| Need | Brush |
|---|---|
| Text inside a filled alert badge | `HF_AlertErrorTextBrush` / `HF_AlertWarningTextBrush` |
| Alert text drawn on the page background | `HF_AlertErrorAccentBrush` / `HF_AlertWarningAccentBrush` |
| The filled badge `Border` itself | `HF_AlertErrorBadge` / `HF_AlertWarningBadge` (both `BasedOn`-able) |

Both text colours are computed by `Converters/ContrastMath.cs` and hold ≥ 4.5:1 on all 18 built-in schemas;
`ContrastMathTests.EveryBuiltInNinaSchema_ClearsWcagAaForBadgeTextAndAccent` is the regression.

Filled vs outlined follows the existing house rule (`AutoFocus/DataTemplates.xaml:120` and `:2967`): **filled**
for a short alert that carries an action or announces a lost capability, **outlined with accent text** for
advisory copy and for long banners that wrap paragraphs and buttons. When a `Border` does become filled, every
`TextBlock` inside it needs the badge text brush — children with no explicit `Foreground` otherwise inherit
`PrimaryBrush` onto the fill.

**Related trap: `Button`-content `TextBlock`s need their own foreground too, for the same reason.** A
`TextBlock` used as a `Button`'s content, with no explicit `Foreground` and no explicit `Style`, does not
inherit the button's foreground — NINA.WPF.Base ships a keyless implicit `TextBlock` style in
`Application.Resources` (`Foreground = PrimaryBrush`), and an implicit style beats inherited value. The label
therefore renders in `PrimaryBrush` on `ButtonBackgroundBrush`, and on the **Dark** and **Alternative Custom**
schemas those two are the identical colour `#FF550C18` — the label renders at 1.00:1, literally invisible.
Always set `Foreground="{StaticResource ButtonForegroundBrush}"` on a button-content `TextBlock`:

```xml
<TextBlock Margin="6,2" Foreground="{StaticResource ButtonForegroundBrush}" Text="Stay connected" />
```

Residual limitation, left as-is deliberately: this only restores the plugin's own pre-existing convention — it
takes those two schemas from 1.00:1 to 1.44:1, still short of WCAG AA. NINA's button palette itself
(`ButtonForegroundColor` on `ButtonBackgroundColor`) clears 4.5:1 on only 12 of its 18 built-in schemas.
Computing a contrast-safe button foreground would make every button in this plugin look different from every
other button in NINA — a product decision for the maintainer, not a bug fix, and deliberately not taken here.
Exception: `TiltAdapterDevices/Prompt/TiltDeviceAdjustmentPromptControl.xaml` carries its own self-contained,
theme-independent palette (`Fg`, `FgDim`, `WarnFg`, `DangerFg`); match that file's local foreground keys there
instead of `ButtonForegroundBrush`.

**A second form of the same trap: `Button Content="…"` (no `TextBlock` in the markup at all).** WPF's
`ContentPresenter` generates a `TextBlock` at runtime for string content, and that generated `TextBlock` hits
the identical keyless-implicit-style rule — so it also paints `PrimaryBrush` on `ButtonBackgroundBrush`. The
fix differs, and an explicit `Foreground` on the `Button` is **not** one: a style setter outranks an inherited
value, so the generated label ignores it. It takes a `Style` whose template puts an empty
`<Style TargetType="TextBlock" />` in `ContentPresenter.Resources` — that dictionary is nearest, so it wins the
implicit-style lookup, and carrying no `Foreground` of its own it lets the inherited value through again. Two
such styles exist; use one, or fall back to an explicit `<TextBlock Foreground="…">` child:

| Style | Where | Use for |
|---|---|---|
| `HF_TextButton` | `StarDetection/Optimization/DataTemplates.xaml` | Ordinary themed buttons. Byte-for-byte NINA's `StandardButton` look (same theme brushes, borders, hover/pressed/disabled), with `Foreground` pinned to `ButtonForegroundBrush` in **every** visual state — NINA's own triggers re-point `Foreground` at the (selected) background brush on hover/press, which would re-hide the label. |
| `HF_ReviewToolbarButton` | `.../Review/StarReviewControl.xaml`, `FrameReviewControl.xaml`, `AutoFocus/Review/AutoFocusFrameReviewControl.xaml` (one copy each) | The review panels' fixed dark chrome. Self-contained fixed light palette, deliberately theme-independent. |

The 13 previously-unfixed `Content="…"` sites in `StarDetection/Optimization/DataTemplates.xaml` (two `Browse…`,
`Optimize with feedback`, and the optimizer wizard's whole navigation footer) all carry `HF_TextButton` now; the
footer's `Close`, which needs its own visibility triggers, gets it through `BasedOn` on its inline
`<Button.Style>`.

`Resources/ButtonContentForegroundGuardTests.cs` is the automated net — the one thing that was missing when this
trap kept recurring. It walks every plugin `.xaml` and enforces both forms: a string `Content` must name a
style from a small allowlist, and a `TextBlock` used as button content must name its own `Foreground` (or
`Style`). Both rules are contrast rules, not resolution rules, which is exactly why
`XamlResourceResolutionTests` could never see them — every key involved resolves fine.

Residual limitation, left as-is deliberately: this only restores the plugin's own pre-existing convention — it
takes the Dark and Alternative Custom schemas from 1.00:1 to 1.44:1, still short of WCAG AA (see the note under
the explicit-`TextBlock` form above).

**Related trap: a local value outranks a style *trigger*, not just a style setter.** WPF dependency-property
precedence puts a local value (rank 3) above both a style setter (rank 8) and a style trigger (rank 6). So an
element must never carry a local `Visibility` attribute when its `Style` sets `Visibility` from a trigger — the
local value wins permanently and the trigger becomes dead code, with no error and no warning. This is why every
badge above keeps `Visibility` inside its `Style` and sets none on the element itself; it is safe to set
`Visibility` locally only on an element whose `Style` sets no `Visibility` at all.
