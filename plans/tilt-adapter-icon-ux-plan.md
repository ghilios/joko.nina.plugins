# Tilt Adapter UI: vector rotation icons + diagram clarity

> On execution, copy this file into the repo as `plans/tilt-adapter-icon-ux-plan.md` (project convention: implementation plans live in `plans/`). This copy lives in the plan-mode scratch path only because that is the sole file editable during planning.

## Context

User-facing feedback on the HocusFocus **Tilt Adapter** UI:

1. The clockwise/counter-clockwise turn indicators are Unicode glyphs `⟳` (U+27F3) / `⟲` (U+27F2). They are small, low-resolution, and hard to read the direction of. Replace them with crisp vector (WPF `Path`/`Geometry`) icons.
2. The tilt-adapter screw diagram is confusing. Make it clear that (a) the rectangle depicts **image** orientation — its top = the top of the image as displayed in NINA, not the physical adapter; (b) **0° points straight up**; (c) **angles increase clockwise**.
3. On the diagram's calibration-info block, replace the literal text "CW" in "Screws CW" with the clockwise icon.
4. On the wizard Measurement section, shorten the over-long direction row: label → "Screw \<CW icon\> moves adapter", and shorten the combo-box values to "Toward the objective" / "Toward the camera" (drop the "— inward"/"— outward" suffixes).

**Decisions confirmed with the user:**
- **Scope of glyph → icon replacement: comprehensive.** Beyond the two wizard labels (items 3 & 4) and the diagram, also convert the **Aberration Inspector "Tilt Adapter Guidance" panel** (direction legend + the 12 per-screw Tilt/Backfocus/Total amount cells) and the **wizard step-summary rows** ("Screw 1 ⟳, Screw 3 ⟲").
- **Diagram clarification style: on-canvas markers + caption** (Style A).

Outcome: users get legible, unambiguous rotation icons everywhere a screw turn is shown, and the diagram self-explains its image-space orientation convention.

## Key design choice — how icons get into text

The `⟳`/`⟲` glyphs are **baked into bound strings** produced by the VMs (`FormatAmount`, `BuildDirectionLegend`, `StepDescription`), several of them mid-sentence and unit-tested for exact string content. Rather than restructure every VM property and rewrite the tests, introduce **one small attached property** that renders a string's `⟳`/`⟲` characters as inline vector icons. The VM strings keep the glyphs **as rendering sentinels**; the user only ever sees the icons; tests stay green.

- Two motion arrows `⬆⬇↑↓` (adapter-plate motion, a different concept) are **left as text** — the attached property only converts `⟳`/`⟲`.
- Do **not** touch `CurvatureSignDescription` (uses ↑/↓ motion-adjacent glyphs, intentional).

Fixed short labels that need a screw-vs-stepper wording switch (items 3 & 4) use a plain `StackPanel`(text + `Path`) with a `DataTrigger`, not the attached property — cleaner for a two-word label with a hardware-conditional variant.

---

## Implementation

### 1. New icon geometries — `Resources/OptionsDataTemplates.xaml` (CORE)

Add two filled circular-arrow `GeometryGroup` resources near the top of the shared dictionary (every area dictionary merges this file, so they resolve app-wide). Follow the house `…SVG` convention with a documenting comment (mirror `StarDetectionResultsSVG`). Coordinate box 0..100, center (50,50), ~300° annular arc (outer r≈34, inner r≈22) + triangular arrowhead; default `FillRule` (Nonzero).

```xml
<!-- Clockwise rotation icon: ~300° filled annular arc opening at top, capped by an
     arrowhead whose tip points clockwise. Pairs with HF_RotationCcwSVG. Stretch="Uniform". -->
<GeometryGroup x:Key="HF_RotationCwSVG">
    <PathGeometry Figures="M 67,20.56 A 34,34 0 1 1 33,20.56 L 30,15.36 L 44.18,22.61 L 42,36.14 L 39,30.95 A 22,22 0 1 0 61,30.95 Z" />
</GeometryGroup>

<!-- Counter-clockwise rotation icon: horizontal mirror (x→100−x) of HF_RotationCwSVG;
     arrowhead points counter-clockwise. Arc sweep flags inverted vs the CW variant. -->
<GeometryGroup x:Key="HF_RotationCcwSVG">
    <PathGeometry Figures="M 33,20.56 A 34,34 0 1 0 67,20.56 L 70,15.36 L 55.82,22.61 L 58,36.14 L 61,30.95 A 22,22 0 1 1 39,30.95 Z" />
</GeometryGroup>
```

The path figures above are a computed starting point — **open the file in a XAML previewer (or run NINA) and nudge the arrowhead wing offsets** if the head looks thin. Consume as (house pattern from `Options.xaml`):

```xml
<Path Width="14" Height="14" Stretch="Uniform" VerticalAlignment="Center"
      Data="{StaticResource HF_RotationCwSVG}" Fill="{StaticResource PrimaryBrush}" />
```

Use `PrimaryBrush` on surfaces (the diagram already themes with it); `ButtonForegroundBrush` only for icons on buttons. Set `Fill` at the use-site, not in a shared style.

### 2. `IconizedText` attached property — `Controls/IconizedText.cs` (new; infra for the comprehensive sites)

Renders a bound string into a `TextBlock`'s `Inlines`, converting `⟳`/`⟲` to inline vector icons and passing everything else (incl. `⬆⬇↑↓` and the em-dash "—") through as text. Namespace `NINA.Joko.Plugins.HocusFocus.Controls` (already imported as `xmlns:controls` in `AutoFocus/DataTemplates.xaml` and `OptionsDataTemplates.xaml`).

```csharp
public static class IconizedText {
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(IconizedText),
            new PropertyMetadata(null, OnTextChanged));
    public static string GetText(DependencyObject o) => (string)o.GetValue(TextProperty);
    public static void SetText(DependencyObject o, string v) => o.SetValue(TextProperty, v);

    private static void OnTextChanged(DependencyObject o, DependencyPropertyChangedEventArgs e) {
        if (o is not TextBlock tb) return;
        tb.Inlines.Clear();
        var s = e.NewValue as string;
        if (string.IsNullOrEmpty(s)) return;
        var buf = new StringBuilder();
        foreach (var ch in s) {
            if (ch == '⟳' || ch == '⟲') {                 // ⟳ / ⟲ only
                if (buf.Length > 0) { tb.Inlines.Add(new Run(buf.ToString())); buf.Clear(); }
                tb.Inlines.Add(BuildIcon(tb, ch == '⟳' ? "HF_RotationCwSVG" : "HF_RotationCcwSVG"));
            } else {
                buf.Append(ch);
            }
        }
        if (buf.Length > 0) tb.Inlines.Add(new Run(buf.ToString()));
    }

    private static InlineUIContainer BuildIcon(TextBlock tb, string key) {
        double size = tb.FontSize > 0 ? tb.FontSize : 12;
        var path = new System.Windows.Shapes.Path {
            Data = tb.TryFindResource(key) as Geometry,
            Stretch = Stretch.Uniform, Width = size, Height = size,
            Fill = tb.Foreground, Margin = new Thickness(1, 0, 1, 0),
            SnapsToDevicePixels = true
        };
        return new InlineUIContainer(path) { BaselineAlignment = BaselineAlignment.Center };
    }
}
```

Notes / known limits: icon `Fill` inherits the TextBlock `Foreground`; size tracks `FontSize` at parse time (re-parses on `Text` change, not on a later `FontSize` change — fine for our fixed sizes); `TryFindResource` null → empty Path (safe); `TextWrapping="Wrap"` still works. Add a one-line comment at each producing VM method noting "⟳/⟲ are rendered as vector icons by IconizedText".

### 3. Diagram clarity — Style A in `HF_TiltScrewDiagram` (`TiltAdapterWizard/DataTemplates.xaml` ~L33-128) (CORE, XAML-only)

Wrap the existing `Canvas` in a `StackPanel` so **both** consumers (saved-calibration ~L559-569 and complete-step ~L1060-1069) update automatically. No VM change.

**Collision-safe zones** (screws sit on the radius-75 ring; canonical centers: 3-screw 0/120/240°, 4-screw 0/90/180/270°): the **rectangle interior** and the **central-top band** `x∈[93,115], y∈[43,56]` are empty for all rotations (to reach that low `y`, a screw must swing far to the side in `x`). Anchor markers only there; keep the possibly-colliding CW cue in the caption.

Add inside the `Canvas` (after the existing rectangle / crosshairs / screw items):

```xml
<!-- 0° = straight up: up-chevron + label in the always-empty central-top band -->
<Path Canvas.Left="93" Canvas.Top="46" Width="14" Height="10" Stretch="Uniform"
      Fill="{StaticResource PrimaryBrush}" Data="M0,10 L7,0 L14,10 Z" />
<TextBlock Canvas.Left="110" Canvas.Top="43" FontSize="10"
           Foreground="{StaticResource PrimaryBrush}" Text="0°" />
<!-- "top of image" hint inside the rectangle near its top edge (rect interior is screw-free) -->
<TextBlock Canvas.Left="70" Canvas.Top="74" FontSize="9" Opacity="0.75"
           Foreground="{StaticResource PrimaryBrush}" Text="top of image" />
```

Then, below the `Canvas`, a caption that words all three facts and carries the clockwise **icon** inline (collision-proof, via `IconizedText` using the `⟳` sentinel):

```xml
<TextBlock MaxWidth="200" Margin="0,4,0,0" FontSize="10" Opacity="0.8" TextWrapping="Wrap"
           Foreground="{StaticResource PrimaryBrush}"
           controls:IconizedText.Text="Rectangle = image as shown in NINA (top = top of image). 0° points up; angles increase clockwise ⟳." />
```

Add `xmlns:controls="clr-namespace:NINA.Joko.Plugins.HocusFocus.Controls"` to the wizard dictionary root (it is not currently imported there). If the implementer prefers the CW arrow strictly on-canvas at the top, the only collision-safe spot is tight — verify against a rotated calibration first; the caption is the reliable fallback and is kept regardless.

### 4. Item 3 — "Screws CW" → "Screws \<CW icon\>" in `HF_TiltScrewCalibrationInfo` (~L181) (CORE)

DataContext is the wizard VM (`Content="{Binding}"`), so `IsStepperAdjustment` binds. Replace the single `<TextBlock Text="Screws CW" />` with a screw/stepper toggle (this file's idiom is `DataTrigger`, not converters):

```xml
<Grid HorizontalAlignment="Center" VerticalAlignment="Center">
  <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
    <StackPanel.Style>
      <Style TargetType="StackPanel">
        <Setter Property="Visibility" Value="Visible" />
        <Style.Triggers>
          <DataTrigger Binding="{Binding IsStepperAdjustment}" Value="True">
            <Setter Property="Visibility" Value="Collapsed" />
          </DataTrigger>
        </Style.Triggers>
      </Style>
    </StackPanel.Style>
    <TextBlock Text="Screws" VerticalAlignment="Center" />
    <Path Width="14" Height="14" Margin="2,0,0,0" Stretch="Uniform" VerticalAlignment="Center"
          Data="{StaticResource HF_RotationCwSVG}" Fill="{StaticResource PrimaryBrush}" />
  </StackPanel>
  <TextBlock Text="+ steps" VerticalAlignment="Center">
    <TextBlock.Style>
      <Style TargetType="TextBlock">
        <Setter Property="Visibility" Value="Collapsed" />
        <Style.Triggers>
          <DataTrigger Binding="{Binding IsStepperAdjustment}" Value="True">
            <Setter Property="Visibility" Value="Visible" />
          </DataTrigger>
        </Style.Triggers>
      </Style>
    </TextBlock.Style>
  </TextBlock>
</Grid>
```

### 5. Item 4 — direction-row label + ComboBox (`TiltAdapterWizard/DataTemplates.xaml` ~L386-420) (CORE)

Replace the `CwDirectionLabel`-bound TextBlock (L386-392) with the same screw/stepper toggle, keeping its `Grid.Row="2" Grid.Column="0"`, margin, and existing `ToolTip`:

```xml
<Grid Grid.Row="2" Grid.Column="0" Margin="0,2,5,2" VerticalAlignment="Center"
      ToolTip="Whether tightening moves the adapter toward the camera or the objective depends on its design (push vs pull screws). Sets the direction of backfocus/curvature guidance.">
  <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
    <StackPanel.Style>
      <Style TargetType="StackPanel">
        <Setter Property="Visibility" Value="Visible" />
        <Style.Triggers>
          <DataTrigger Binding="{Binding IsStepperAdjustment}" Value="True">
            <Setter Property="Visibility" Value="Collapsed" />
          </DataTrigger>
        </Style.Triggers>
      </Style>
    </StackPanel.Style>
    <TextBlock Text="Screw" VerticalAlignment="Center" />
    <Path Width="14" Height="14" Margin="2,0,2,0" Stretch="Uniform" VerticalAlignment="Center"
          Data="{StaticResource HF_RotationCwSVG}" Fill="{StaticResource PrimaryBrush}" />
    <TextBlock Text="moves adapter" VerticalAlignment="Center" />
  </StackPanel>
  <TextBlock Text="+ steps move adapter" VerticalAlignment="Center">
    <TextBlock.Style>
      <Style TargetType="TextBlock">
        <Setter Property="Visibility" Value="Collapsed" />
        <Style.Triggers>
          <DataTrigger Binding="{Binding IsStepperAdjustment}" Value="True">
            <Setter Property="Visibility" Value="Visible" />
          </DataTrigger>
        </Style.Triggers>
      </Style>
    </TextBlock.Style>
  </TextBlock>
</Grid>
```

Shorten the two ComboBox items (L410, L415) — keep the `Tag` bools and order:

```xml
<ComboBoxItem Content="Toward the camera"><ComboBoxItem.Tag><s:Boolean>False</s:Boolean></ComboBoxItem.Tag></ComboBoxItem>
<ComboBoxItem Content="Toward the objective"><ComboBoxItem.Tag><s:Boolean>True</s:Boolean></ComboBoxItem.Tag></ComboBoxItem>
```

**Constraint:** `CwDirectionLabel` (VM L783-785) is no longer bound but **must be retained** — `TiltAdapterWizardVMTests.cs:694` asserts it is raised on a profile swap; keep the property and its `RaisePropertyChanged` calls (L266, L312). Optionally shorten its return strings for consistency (no test asserts the value).

### 6. Inspector guidance panel — legend + 12 amount cells (`AutoFocus/DataTemplates.xaml`) (comprehensive)

Pure presentational swap `Text=` → `controls:IconizedText.Text=`; no VM/test change (`controls` is already imported here):
- Legend TextBlock (~L2836-2840): `controls:IconizedText.Text="{Binding TiltGuidance.DirectionLegend}"` (keeps `TextWrapping`; `⬆` passes through, `⟳` becomes an icon; stepper legend has no `⟳`).
- The 12 amount cells (~L2992-2996 Tilt, L2999-3003 Backfocus, L3006-3010 Total): change each `Text="{Binding TiltGuidance.ScrewNXxxAmount}"` to `controls:IconizedText.Text="{Binding …}"`, preserving each cell's `Grid.Row/Column`, `HorizontalAlignment`, `Visibility`, and the Total row's `FontWeight="SemiBold"` (the emitted `Run` inherits it). Em-dash and "+N steps" cells render as plain text.

Leave the separate motion-arrow row (`ScrewNTiltArrow`/`ScrewNBackfocusArrow`, `⬆⬇↑↓`) as plain `Text=` — unchanged.

### 7. Wizard step-summary rows (`TiltAdapterWizard/DataTemplates.xaml`) (comprehensive)

The two `ItemsControl ItemsSource="{Binding StepMeasurementSummary}"` render `StepDescription` at ~L889 and ~L935. Change both `<TextBlock Text="{Binding StepDescription}" />` → `controls:IconizedText.Text="{Binding StepDescription}"`. `StepDescription(...)` in the VM is unchanged (glyphs stay as sentinels), so `StepDescription_UsesRotationGlyphs` stays green. (Same `xmlns:controls` added in step 3.)

---

## Files to modify

| File | Change |
|---|---|
| `Resources/OptionsDataTemplates.xaml` | Add `HF_RotationCwSVG` / `HF_RotationCcwSVG` geometries |
| `Controls/IconizedText.cs` | **New** attached property |
| `TiltAdapterWizard/DataTemplates.xaml` | Add `xmlns:controls`; Style A diagram; item 3 label; item 4 label + combo; step-summary rows |
| `AutoFocus/DataTemplates.xaml` | Legend + 12 amount cells → `IconizedText.Text` |
| `TiltAdapterWizard/TiltAdapterWizardVM.cs` | Retain `CwDirectionLabel` (optionally shorten its strings); add sentinel comments |

**Do not change:** `TiltScrewGuidanceRow.cs` (`FormatAmount`/`BuildDirectionLegend` keep sentinels), `InspectorVM` guidance population, `CurvatureSignDescription`, the motion-arrow row, or the `RebuildDiagram` math. No test files change on this route.

## Testing & verification

1. **Build + unit tests** (project invariant — must pass, no skips):
   ```
   dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo
   ```
   Expect all green with no test edits (VM strings unchanged). If a glyph-asserting test fails, it means a VM string was altered unintentionally — fix the cause.
2. **Visual verification in NINA** (icons must be legible and directions unambiguous — the whole point). Per `.claude/docs/nina-mcp-screenshots.md`, capture via the Windows MCP:
   - Tilt Adapter Wizard panel: the diagram (0° up-chevron, "top of image" hint, caption with CW icon read correctly for a 3-screw and a 4-screw calibration), the "Screws \<CW\>" calibration row, and the shortened "Screw \<CW\> moves adapter" row + combo. Toggle a stepper adapter to confirm the "+ steps" variants show.
   - Aberration Inspector "Tilt Adapter Guidance": the legend CW icon, and the per-screw amount cells rendering `1.25 <CW icon>` / `0.50 <CCW icon>`; confirm the em-dash and stepper "+N steps" cells still render plainly and the Total row stays bold.
   - Confirm CW vs CCW are visually distinguishable at the rendered small size; tune the geometry arrowhead if not.
3. **Regression glance:** confirm motion arrows (`⬆⬇↑↓`) are unchanged and no icon appears where they render.
