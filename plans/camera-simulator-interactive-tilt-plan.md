# Camera Simulator — Rig/Observing Split & Virtual Tilt Adapter — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user close the Aberration Inspector → turn-a-screw → re-inspect loop entirely in software, by adding a
virtual tilt adapter to the synthetic camera plus the options split and dockable that make it usable.

**Architecture:** Executes `docs/camera-simulator-interactive-tilt-design.md` (read it first; panel UX detail is in
`docs/virtual-tilt-adapter-panel-ux-design.md`). The load-bearing idea: the simulator's screw math is the *exact
inverse* of the inspector's guidance because both call `TiltAdapterWizard/TiltScrewGeometry.cs`. One
`UserControl` is hosted by two places (the camera setup dialog and an Imaging dockable). Screw clicks apply
deltas to the simulator's existing tilt/backfocus knobs — they are not a second source of truth.

**Tech Stack:** C# / .NET 8.0-windows7.0, WPF, MEF (`[Export]`/`[ImportingConstructor]`), CommunityToolkit.Mvvm
`RelayCommand`, NUnit 4.4.0 + NSubstitute, NINA plugin SDK 3.2.0.2001-beta.

**Phasing:** Phase A (Tasks 1-3) ships on its own — a camera setup dialog + tidier options. Phase B (Tasks 4-10)
adds the adapter and needs A's setup-dialog host. You can stop after A.

---

## AS-BUILT — read this before trusting any snippet below

**Status: delivered.** Tasks 1-10 are implemented, reviewed (spec + quality, each independently verified against
NINA's decompiled assemblies), and green. This section records what actually shipped, because **the plan below was
wrong in five places** and the corrections are the most useful thing in this document.

**Final: 2052 tests passing** (from a 1960 baseline). The per-task "expected: N passed" numbers inline below are
pre-execution estimates and drifted as reviewers added tests — trust the trend (always green), not the integers.

### Divergence log — where the plan was wrong and the shipped code is right

| # | Plan said | Shipped | Why the plan was wrong |
|---|---|---|---|
| 1 | `private readonly IWindowService windowService = new WindowService();` (Task 3) | `IWindowServiceFactory`, created **inside** `SetupDialog()` | `WindowService..ctor` is `Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher`. A field initializer runs at camera construction — during an equipment rescan and in every unit test, where `Application.Current` is null — pinning a stray dispatcher to the NUnit worker. |
| 2 | `AxialMicronsForUnits` multiplies by `CwMovesAdapterTowardObjectiveForSign(σ) ? 1.0 : -1.0` (Task 5) | **No rig factor at all** — `units * UnitMicrons` | That factor is exactly `−σ`: **backwards on default (σ=+1) rigs**, coincidentally right on σ=−1. The inspector's *tilt* guidance is sign-free (`InspectorVM.cs:2035`; `SignedTotalAdjustment` applies σ to backfocus only, with a regression pin saying so) because `PhysicalToStoredAngle` makes stored angles already carry rig direction (`p_stored = σ·p_phys`), so the two σ's cancel. **Flipping the ternary would also have been wrong.** |
| 3 | Capstone feeds axial µm straight into `ApplyMoves` (Task 6) | Computes turns the way `InspectorVM.FillNumericGuidance` does, then routes through `AxialMicronsForUnits` | The original **bypassed the sign path entirely**, making `unitMicrons`/`curvatureSign` provably inert (6 of 8 combinatorial cases were duplicates). It was empirically shown to **pass against bug #2** — a capstone that certifies the bug it exists to catch. |
| 4 | `ΔBackfocusErrorMicrons = −ΔZ0_phys·(…)` (Task 7 / design §3.2) | `+ΔZ0_phys·(…)` | Derived from the false premise "the inspector emits `backTurns = CurvatureAt(p)/pitch`". Shipped `ScrewCorrectionMicrons` returns **`−CurvatureAt`** (`TiltScrewGeometry.cs:102`), symmetric with its `−TiltAt`; `InspectorVM.cs:2004` says so in its own comment. The `−` form **doubles** the error (40 → 80) instead of nulling it, on both rigs. |
| 5 | Dockable template keyed `{x:Type …DockableVM}` (Task 9) | String key `"<FullTypeName>_Dockable"` | NINA's `PaneTemplateSelector.SelectTemplate` looks up `item.GetType().FullName + "_Dockable"`. An `{x:Type}` key compiles cleanly and silently renders the type name instead of the panel. All 7 pre-existing dockable templates use the string key. |

**Root cause of #2 and #4 — the lesson worth keeping.** Both signs were derived from *another design doc*
(`docs/precise-screw-adjustments-design.md`) rather than from the shipped code, and both were self-consistently
wrong. The structural fix is already in place: the capstone now consumes the **real** `TiltScrewGeometry` /
`ScrewCorrectionMicrons` instead of hand-rolled expectations, so a doc that drifts from the code can no longer
pass. **If you change the screw math, do not trust this plan or the design — read `TiltScrewGeometry` and
`InspectorVM.FillNumericGuidance`.**

### Smaller as-built divergences (all deliberate; repo was the truth)

- **Task 2:** the plan's XAML row numbers were from the *setup dialog's* layout, not `OptionsDataTemplates.xaml`. Real rows differ; the six rig controls are at `:3016-3050`.
- **Task 3:** the plan's inline tooltips were replaced by **moving** the six now-dead `CamSim_*_Tooltip` resources into the setup dictionary (keeping the prose, killing the dead keys, avoiding a cross-dictionary `StaticResource` parse-order race). The `FloatRangeRule` ranges the plan didn't supply were recovered from `5d6495a` (aperture 1–2000, focal 0–20000, fraction 0–0.9, throughput 0–1).
- **Task 4:** the options fixture helper is `Build()` (returns a tuple), not `BuildOptions()`. The plan's two tests **pass against the buggy naive NaN guard**, so a third test was added that actually pins it. `SimScrewCount` is also healed on load (the setter clamps, but `InitializeOptions` didn't).
- **Task 7:** `SimulatedTiltAdapterVM` has a **dual constructor** (1-arg public resolves `HocusFocusPlugin.TiltAdapterOptions`; `internal` 2-arg seam for tests) — touching `HocusFocusPlugin` runs its static ctor. `IsFlat` requires tilt **and** |backfocus| < 1 µm (per the UX doc, which is the named authority).
- **Task 8:** the VM is built **lazily on first bind**, not in the camera ctor — the provider makes a fresh camera per equipment rescan and the VM subscribes to process-lifetime singletons without unsubscribing, so eager construction retains one VM per rescan.
- **Task 9:** `IDockableVM` lives in `NINA.Equipment.Interfaces.ViewModel` (the plan's import didn't compile). The `DispatcherPriority.ApplicationIdle` gating the plan prescribed **does not guarantee ordering** — priority orders items queued *at the same time*, and NINA's layout restore is queued *later*; the shipped gate is order-independent instead (it re-closes whenever `IsVisible` goes true while disabled).
- **Cross-dictionary lookups** use `DynamicResource`, never `StaticResource`: plugin `ResourceDictionary` exports are merged in unspecified order, so a parse-time lookup is a load-order race.

---

## Background the engineer needs

**Build & test (Windows toolchain from WSL — bare `dotnet` is NOT on PATH):**
```bash
rtk dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo
rtk dotnet test  Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo
```
`rtk dotnet build` prints `fail` in its header even on a clean build — trust `errors=0` and exit code 0. Set the
Bash `timeout` to `600000`. Baseline: **1960 tests passing**. Never push; never touch `develop`.

**Commit identity (required by CLAUDE.md):**
```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "..."
```

**Project invariant:** every persisted option needs a UI control in `Resources/OptionsDataTemplates.xaml`.

**Domain (from `.claude/docs/tilt-domain.md`):** screw angles are degrees **clockwise from straight up (0° = 12
o'clock)** in **image space**. 3-screw = independent; 4-screw = opposite screws mechanically coupled. The glyph
contract is non-negotiable: **⟳/⟲ and +/− mean rotation; ⬆/⬇ mean adapter-plate motion. Never print ⬆/⬇ as a
rotation.**

**The geometry helper you must reuse** (`TiltAdapterWizard/TiltScrewGeometry.cs`):
```csharp
public static (double x, double y) ScrewPositionMicrons(double angleDegrees, double radiusMicrons);
// δ = -(gx·x + gy·y): the axial move at a screw that CANCELS best-focus gradient (gx,gy)
public static double TiltCorrectionMicrons(double gx, double gy, double angleDegrees, double radiusMicrons);
public static int  DefaultScrewInwardCurvatureSign { get; }
public static bool CwMovesAdapterTowardObjectiveForSign(int curvatureSign);
```

**The model, stated once (Task 5 implements it):** applying an axial displacement field to the sensor moves the
best-focus surface by exactly that field. So if a set of screw moves `{δ_i}` at points `{p_i}` fits the plane
`δ(x,y) = ΔGx·x + ΔGy·y + ΔZ0`, then:
- `Gx += ΔGx`, `Gy += ΔGy` (tilt)
- `OptimalFocuserPosition += ΔZ0 / FocuserStepSizeMicrons` (piston shifts best focus)
- `BackfocusErrorMicrons += −ΔZ0·(halfW² + halfH²) / R²` (piston violates backfocus spacing ⇒ curvature)

This is *exactly* the inverse of `TiltCorrectionMicrons`: to cancel `(Gx,Gy)` you need `ΔGx=−Gx, ΔGy=−Gy`, i.e.
`δ_i = −(Gx·x_i + Gy·y_i) = TiltCorrectionMicrons(Gx,Gy,θ_i,R)`. Task 6 pins that with a round-trip test.

**Existing sim tilt inversion** (from `CameraSimulator/Rendering/AberrationSurface.cs`, keep consistent):
`|G| = TiltAmount / (|cosφ|·halfW + |sinφ|·halfH)`, `Gx=|G|cosφ`, `Gy=|G|sinφ`, `φ = TiltAngleDegrees`;
inverse: `TiltAmountMicrons = |Gx|·halfW + |Gy|·halfH`, `TiltAngleDegrees = atan2(Gy,Gx)` in degrees.

---

## File structure

| File | Responsibility |
|---|---|
| `Interfaces/ICameraSimulatorOptions.cs` *(modify)* | + `Sim*` adapter fields, `ShowSimulatorTiltAdapterPanel` |
| `CameraSimulator/CameraSimulatorOptions.cs` *(modify)* | persistence for the above |
| `CameraSimulator/HocusFocusSimulatorCamera.cs` *(modify)* | `HasSetupDialog` / `SetupDialog()` |
| `CameraSimulator/SetupDialog/SetupDataTemplates.xaml` *(+ `.cs`)* | exported `ResourceDictionary`: camera-keyed `DataTemplate` + rig setup view |
| `CameraSimulator/TiltAdapter/SimulatedTiltAdapter.cs` | **pure** screw model (no WPF, no options) |
| `CameraSimulator/TiltAdapter/SimulatedTiltAdapterVM.cs` | panel VM: operate/config/feedback/Undo |
| `CameraSimulator/TiltAdapter/TiltAdapterDataTemplates.xaml` | the shared panel control |
| `CameraSimulator/TiltAdapter/SimulatorTiltAdapterDockableVM.cs` | `[Export(typeof(IDockableVM))]`, gated |
| `Resources/OptionsDataTemplates.xaml` *(modify)* | rig group read-only; aberration group `Visibility` |
| `Tests/CameraSimulator/SimulatedTiltAdapterTests.cs` | unit tests for the pure model |
| `Tests/CameraSimulator/SimulatedTiltAdapterCapstoneTests.cs` | the parameterized round-trip |

---

# Phase A — Options split & visibility

### Task 1: Aberration group collapses when disabled

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml`

- [ ] **Step 1: Find the aberration group**

Run: `grep -n 'EnableAberrations' Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml`
Expected: a container with `IsEnabled="{Binding CameraSimulatorOptions.EnableAberrations}"`.

- [ ] **Step 2: Swap IsEnabled for Visibility**

Replace that attribute on the aberration group container with:

```xml
Visibility="{Binding CameraSimulatorOptions.EnableAberrations, Converter={StaticResource VisibilityConverter}}"
```

Use whichever boolean→visibility converter this dictionary already registers — check with:
`grep -n 'VisibilityConverter\|BooleanToVisibility' Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml`
If none is registered here, add NINA's: `xmlns:ninaconv="clr-namespace:NINA.Core.Utility.Converters;assembly=NINA.Core"`
and `<ninaconv:BooleanToVisibilityCollapsedConverter x:Key="VisibilityConverter" />` next to the other converter
resources near the top of the file.

The `EnableAberrations` CheckBox itself must stay **outside** the collapsing container.

- [ ] **Step 3: Build**

Run: `rtk dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
Expected: `errors=0`. (XAML binding errors do not fail the build — Task 10's manual check covers rendering.)

- [ ] **Step 4: Run the suite**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
Expected: 1960 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(camera-sim): collapse the aberration options group when disabled"
```

---

### Task 2: Rig options become read-only in plugin Options

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml`

The **rig set** is: `SensorModel`, `ApertureMillimeters`, `FocalLengthMillimeters`, `CentralObstructionEnabled`,
`CentralObstructionFraction`, `OpticalThroughput`. Everything else stays editable.

- [ ] **Step 1: Add the shared tooltip resource**

Next to the other `*_Tooltip` `TextBlock` resources at the dictionary root, add:

```xml
<TextBlock x:Key="CamSim_RigReadOnly_Tooltip"
           Text="Rig settings are edited in the camera's setup dialog — click the gear icon next to the camera in the Equipment &gt; Camera pane. They are shown here for reference only." />
```

- [ ] **Step 2: Make each rig control read-only**

For each of the six rig controls in `HocusFocus_CameraSimulator_Options`, add `IsEnabled="False"` and point the
tooltip at the new resource. For the two enum/bool controls and four numerics, e.g.:

```xml
<ComboBox Grid.Row="0" Grid.Column="1"
          ItemsSource="{Binding Source={util:EnumBindingSource {x:Type interfaces:SonySensorModel}}}"
          SelectedItem="{Binding CameraSimulatorOptions.SensorModel}"
          IsEnabled="False"
          ToolTip="{StaticResource CamSim_RigReadOnly_Tooltip}">
    <ComboBox.ItemTemplate>
        <DataTemplate>
            <TextBlock Text="{Binding Converter={StaticResource HF_EnumStaticDescriptionValueConverter}}" />
        </DataTemplate>
    </ComboBox.ItemTemplate>
</ComboBox>
```

```xml
<ninactrl:UnitTextBox Grid.Row="1" Grid.Column="1" Unit="mm" IsEnabled="False"
                      Text="{Binding CameraSimulatorOptions.ApertureMillimeters, Mode=OneWay}"
                      ToolTip="{StaticResource CamSim_RigReadOnly_Tooltip}" />
```

Note `Mode=OneWay` on the numerics: with `IsEnabled="False"` the validation rules can no longer run, so drop the
`Binding.ValidationRules` blocks on these six and bind one-way. The setup dialog (Task 3) re-adds the rules where
the values are actually edited.

- [ ] **Step 3: Build + test**

Run: `rtk dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` → `errors=0`
Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` → 1960 passed.

- [ ] **Step 4: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(camera-sim): show rig options read-only in plugin options"
```

---

### Task 3: Camera setup dialog

NINA calls `SetupDialog()` on a **fresh STA thread and blocks on `Join()`**. Never construct WPF there —
`WindowService` captures the main dispatcher, so it is the only safe path. The gear button is **not** gated on
`Connected`, so we gate `SensorModel` ourselves.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/HocusFocusSimulatorCamera.cs`
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/SetupDialog/SetupDataTemplates.xaml`
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/SetupDialog/SetupDataTemplates.xaml.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/HocusFocusSimulatorCameraTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `HocusFocusSimulatorCameraTests.cs`:

```csharp
[Test]
public void HasSetupDialog_IsTrue_SoNinaShowsTheGearButton() {
    var camera = BuildCamera(BuildOptions());
    // NINA's connector binds the gear button's IsEnabled directly to HasSetupDialog
    // (view/equipment/connector.baml) — false hides our only rig-config entry point.
    Assert.That(camera.HasSetupDialog, Is.True);
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~HasSetupDialog_IsTrue"`
Expected: FAIL — `Expected: True  But was: False`.

- [ ] **Step 3: Implement**

In `HocusFocusSimulatorCamera.cs`, change `HasSetupDialog` to `true` and implement `SetupDialog()`:

```csharp
/// <summary>
/// Rig settings (sensor/optics) live here rather than the plugin Options tab so they sit with the device.
/// NINA invokes this on a dedicated STA thread and blocks on Join(), so we must NOT build WPF here —
/// WindowService captures the main dispatcher and marshals correctly. Show() is non-modal, so this
/// returns immediately and NINA's STA thread ends while the window stays up (mirrors NINA's own
/// SimulatorCamera).
/// </summary>
public bool HasSetupDialog => true;

public void SetupDialog() {
    windowService.Show(this, "Hocus Focus Simulator Setup", ResizeMode.NoResize, WindowStyle.ToolWindow);
}
```

Add the field + usings (`NINA.Core.Utility.WindowService`, `System.Windows`):

```csharp
// AS-BUILT (divergence #1): a factory, NOT a field-initialized WindowService. WindowService..ctor captures
// `Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher`, so constructing it at camera-construction
// time — every equipment rescan, and every unit test, where Application.Current is null — pins a stray
// Dispatcher to the calling (NUnit worker) thread. The factory's ctor is inert; Create() runs inside
// SetupDialog(), where a live Application is guaranteed. Matches 6 existing precedents in this plugin.
private readonly IWindowServiceFactory windowServiceFactory = new WindowServiceFactory();
```

and `SetupDialog()` becomes:

```csharp
public void SetupDialog() {
    windowServiceFactory.Create().Show(
        this, "Hocus Focus Simulator Setup", ResizeMode.NoResize, WindowStyle.ToolWindow);
}
```

- [ ] **Step 4: Run the test**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~HasSetupDialog_IsTrue"`
Expected: PASS.

- [ ] **Step 5: Create the setup view resource dictionary**

`CameraSimulator/SetupDialog/SetupDataTemplates.xaml` — an exported `ResourceDictionary` whose **implicit**
`DataTemplate` (keyed by `DataType`, no `x:Key`) is what `WindowService.Show(this, …)` resolves for the camera:

```xml
<ResourceDictionary x:Class="NINA.Joko.Plugins.HocusFocus.CameraSimulator.SetupDialog.SetupDataTemplates"
                    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:camsim="clr-namespace:NINA.Joko.Plugins.HocusFocus.CameraSimulator"
                    xmlns:interfaces="clr-namespace:NINA.Joko.Plugins.HocusFocus.Interfaces"
                    xmlns:util="clr-namespace:NINA.Core.Utility;assembly=NINA.Core"
                    xmlns:ninactrl="clr-namespace:NINA.CustomControlLibrary;assembly=NINA.CustomControlLibrary"
                    xmlns:ninaconv="clr-namespace:NINA.Core.Utility.Converters;assembly=NINA.Core"
                    xmlns:hfconverters="clr-namespace:NINA.Joko.Plugins.HocusFocus.Converters">
    <hfconverters:EnumStaticDescriptionValueConverter x:Key="HF_EnumStaticDescriptionValueConverter" />
    <ninaconv:InverseBooleanConverter x:Key="InverseBooleanConverter" />

    <DataTemplate DataType="{x:Type camsim:HocusFocusSimulatorCamera}">
        <StackPanel Margin="10" MinWidth="360">
            <TextBlock Text="Rig" FontWeight="Bold" Margin="0,0,0,6" />
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="*" />
                </Grid.ColumnDefinitions>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" /><RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" /><RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" /><RowDefinition Height="Auto" />
                </Grid.RowDefinitions>

                <TextBlock Grid.Row="0" Grid.Column="0" Text="Sensor" VerticalAlignment="Center" />
                <ComboBox Grid.Row="0" Grid.Column="1"
                          ItemsSource="{Binding Source={util:EnumBindingSource {x:Type interfaces:SonySensorModel}}}"
                          SelectedItem="{Binding Options.SensorModel}"
                          IsEnabled="{Binding Connected, Converter={StaticResource InverseBooleanConverter}}"
                          ToolTip="Disconnect to change the sensor — NINA latches resolution, pixel size and bit depth at connect.">
                    <ComboBox.ItemTemplate>
                        <DataTemplate>
                            <TextBlock Text="{Binding Converter={StaticResource HF_EnumStaticDescriptionValueConverter}}" />
                        </DataTemplate>
                    </ComboBox.ItemTemplate>
                </ComboBox>

                <TextBlock Grid.Row="1" Grid.Column="0" Text="Aperture" VerticalAlignment="Center" />
                <ninactrl:UnitTextBox Grid.Row="1" Grid.Column="1" Unit="mm"
                                      Text="{Binding Options.ApertureMillimeters}" />

                <TextBlock Grid.Row="2" Grid.Column="0" Text="Focal length" VerticalAlignment="Center" />
                <ninactrl:UnitTextBox Grid.Row="2" Grid.Column="1" Unit="mm"
                                      Text="{Binding Options.FocalLengthMillimeters}"
                                      ToolTip="0 = use the profile's telescope focal length." />

                <CheckBox Grid.Row="3" Grid.Column="1" Content="Central obstruction"
                          IsChecked="{Binding Options.CentralObstructionEnabled}" />

                <TextBlock Grid.Row="4" Grid.Column="0" Text="Obstruction fraction" VerticalAlignment="Center" />
                <ninactrl:UnitTextBox Grid.Row="4" Grid.Column="1" Unit="ε"
                                      Text="{Binding Options.CentralObstructionFraction}"
                                      IsEnabled="{Binding Options.CentralObstructionEnabled}" />

                <TextBlock Grid.Row="5" Grid.Column="0" Text="Optical throughput" VerticalAlignment="Center" />
                <ninactrl:UnitTextBox Grid.Row="5" Grid.Column="1" Unit="T"
                                      Text="{Binding Options.OpticalThroughput}" />
            </Grid>
        </StackPanel>
    </DataTemplate>
</ResourceDictionary>
```

`SetupDataTemplates.xaml.cs`:

```csharp
using System.ComponentModel.Composition;
using System.Windows;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.SetupDialog {

    [Export(typeof(ResourceDictionary))]
    public partial class SetupDataTemplates : ResourceDictionary {

        public SetupDataTemplates() {
            InitializeComponent();
        }
    }
}
```

- [ ] **Step 6: Expose `Options` on the camera for the template to bind**

The template binds `Options.*` and `Connected`. Add to `HocusFocusSimulatorCamera.cs`:

```csharp
/// <summary>The simulator options, exposed so the setup dialog's DataTemplate can bind the rig fields.</summary>
public ICameraSimulatorOptions Options => options;
```

- [ ] **Step 7: Build + full suite**

Run: `rtk dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` → `errors=0`
Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` → 1961 passed (1960 + 1 new).

- [ ] **Step 8: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/ \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(camera-sim): add a camera setup dialog for rig settings"
```

**Milestone (Phase A):** builds; the gear button opens a rig dialog; rig options are read-only in plugin Options;
the aberration group collapses when disabled.

---

# Phase B — Virtual tilt adapter

### Task 4: `Sim*` adapter options

These are **deliberately separate** from the user's real `TiltAdapterOptions` and must never be implicitly
written — see the design's §3.1 (silent divergence would make the loop never converge).

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/ICameraSimulatorOptions.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/CameraSimulatorOptions.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/CameraSimulatorOptionsTests.cs`

| Option | Type | Default |
|---|---|---|
| `SimScrewCount` | int | 3 |
| `SimScrew1AngleDegrees` | double | 0 |
| `SimScrew2AngleDegrees` | double | 120 |
| `SimScrew3AngleDegrees` | double | 240 |
| `SimScrew4AngleDegrees` | double | `double.NaN` |
| `SimScrewInwardCurvatureSign` | int | `TiltScrewGeometry.DefaultScrewInwardCurvatureSign` |
| `SimAdjustmentType` | `TiltAdjustmentType` | `Screws` |
| `SimThreadPitchMicrons` | double | 500 |
| `SimStepperStepSizeMicrons` | double | 1.0 |
| `SimScrewRadiusMillimeters` | double | 30 |
| `ShowSimulatorTiltAdapterPanel` | bool | false |

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public void SimTiltAdapterDefaults_MatchDesign() {
    var options = BuildOptions();
    Assert.Multiple(() => {
        Assert.That(options.SimScrewCount, Is.EqualTo(3));
        Assert.That(options.SimScrew1AngleDegrees, Is.EqualTo(0.0));
        Assert.That(options.SimScrew2AngleDegrees, Is.EqualTo(120.0));
        Assert.That(options.SimScrew3AngleDegrees, Is.EqualTo(240.0));
        Assert.That(double.IsNaN(options.SimScrew4AngleDegrees), Is.True);
        Assert.That(options.SimScrewInwardCurvatureSign,
            Is.EqualTo(TiltScrewGeometry.DefaultScrewInwardCurvatureSign));
        Assert.That(options.SimAdjustmentType, Is.EqualTo(TiltAdjustmentType.Screws));
        Assert.That(options.SimThreadPitchMicrons, Is.EqualTo(500.0));
        Assert.That(options.SimStepperStepSizeMicrons, Is.EqualTo(1.0));
        Assert.That(options.SimScrewRadiusMillimeters, Is.EqualTo(30.0));
        Assert.That(options.ShowSimulatorTiltAdapterPanel, Is.False);
    });
}

[Test]
public void SimTiltAdapterOptions_PersistAndReadBack() {
    var store = new InMemoryPluginOptionsAccessor();
    var a = new CameraSimulatorOptions(Substitute.For<IProfileService>(), store);
    a.SimScrewCount = 4;
    a.SimScrew4AngleDegrees = 270.0;
    a.SimThreadPitchMicrons = 350.0;
    a.ShowSimulatorTiltAdapterPanel = true;

    var b = new CameraSimulatorOptions(Substitute.For<IProfileService>(), store);
    Assert.Multiple(() => {
        Assert.That(b.SimScrewCount, Is.EqualTo(4));
        Assert.That(b.SimScrew4AngleDegrees, Is.EqualTo(270.0));
        Assert.That(b.SimThreadPitchMicrons, Is.EqualTo(350.0));
        Assert.That(b.ShowSimulatorTiltAdapterPanel, Is.True);
    });
}
```

- [ ] **Step 2: Run and watch it fail**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~SimTiltAdapter"`
Expected: FAIL — compile error, `SimScrewCount` not defined.

- [ ] **Step 3: Add to the interface**

In `Interfaces/ICameraSimulatorOptions.cs`, inside `ICameraSimulatorOptions`:

```csharp
// Simulated tilt adapter. Deliberately separate from the user's real ITiltAdapterOptions: the inspector
// guides from the REAL calibration, so the two must be comparable but must never implicitly overwrite
// each other. The panel surfaces a coherence badge + explicit copy commands instead.
int SimScrewCount { get; set; }                      // 3 or 4
double SimScrew1AngleDegrees { get; set; }           // clockwise from top, image space
double SimScrew2AngleDegrees { get; set; }
double SimScrew3AngleDegrees { get; set; }
double SimScrew4AngleDegrees { get; set; }           // double.NaN when 3-screw
int SimScrewInwardCurvatureSign { get; set; }        // +1 / -1
TiltAdjustmentType SimAdjustmentType { get; set; }
double SimThreadPitchMicrons { get; set; }           // axial µm per full turn
double SimStepperStepSizeMicrons { get; set; }       // axial µm per step
double SimScrewRadiusMillimeters { get; set; }       // screw distance from sensor center
bool ShowSimulatorTiltAdapterPanel { get; set; }
```

- [ ] **Step 4: Implement persistence**

In `CameraSimulator/CameraSimulatorOptions.cs`, add loads to `InitializeOptions()`:

```csharp
simScrewCount = optionsAccessor.GetValueInt32(nameof(SimScrewCount), 3);
simScrew1AngleDegrees = optionsAccessor.GetValueDouble(nameof(SimScrew1AngleDegrees), 0.0);
simScrew2AngleDegrees = optionsAccessor.GetValueDouble(nameof(SimScrew2AngleDegrees), 120.0);
simScrew3AngleDegrees = optionsAccessor.GetValueDouble(nameof(SimScrew3AngleDegrees), 240.0);
simScrew4AngleDegrees = optionsAccessor.GetValueDouble(nameof(SimScrew4AngleDegrees), double.NaN);
simScrewInwardCurvatureSign = optionsAccessor.GetValueInt32(nameof(SimScrewInwardCurvatureSign),
    TiltScrewGeometry.DefaultScrewInwardCurvatureSign);
simAdjustmentType = optionsAccessor.GetValueEnum(nameof(SimAdjustmentType), TiltAdjustmentType.Screws);
simThreadPitchMicrons = optionsAccessor.GetValueDouble(nameof(SimThreadPitchMicrons), 500.0);
simStepperStepSizeMicrons = optionsAccessor.GetValueDouble(nameof(SimStepperStepSizeMicrons), 1.0);
simScrewRadiusMillimeters = optionsAccessor.GetValueDouble(nameof(SimScrewRadiusMillimeters), 30.0);
showSimulatorTiltAdapterPanel = optionsAccessor.GetValueBoolean(nameof(ShowSimulatorTiltAdapterPanel), false);
```

Mirror each in `ResetDefaults()` (assigning through the **public setters**), and add a backing field + property
per the file's canonical setter shape, e.g.:

```csharp
private int simScrewCount;

public int SimScrewCount {
    get => simScrewCount;
    set {
        var clamped = value == 4 ? 4 : 3;
        if (simScrewCount != clamped) {
            simScrewCount = clamped;
            optionsAccessor.SetValueInt32(nameof(SimScrewCount), simScrewCount);
            RaisePropertyChanged();
        }
    }
}
```

`double.NaN` note: `GetValueDouble`/`SetValueDouble` round-trip NaN fine, but `simScrew4AngleDegrees != value` is
always true for NaN — guard that setter with
`if (!(double.IsNaN(simScrew4AngleDegrees) && double.IsNaN(value)) && simScrew4AngleDegrees != value)`.

**AS-BUILT:** the two tests above are **not sufficient** — they pass against the naive buggy guard too
(`InitializeOptions` assigns backing fields directly so no setter runs, and `NaN → 270` is the one case where the
naive guard is coincidentally right). A third test was added — `SimScrew4Angle_SettingNaNOverNaNDefault_
DoesNotRaiseOrPersist` — asserting no `PropertyChanged` **and** no store write. That one actually fails under the
naive guard. Also: `InitializeOptions` heals `SimScrewCount` on load (`== 4 ? 4 : 3`), mirroring the `ClampGain`
precedent — the setter clamps, but a hand-edited profile bypasses it and would reach Task 5's ctor guard.

- [ ] **Step 5: Run the tests**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~SimTiltAdapter"`
Expected: PASS.

- [ ] **Step 6: Add the UI control for `ShowSimulatorTiltAdapterPanel` (project invariant)**

**AS-BUILT:** the `Grid.Row="26"` in the snippet below is stale — `HocusFocus_CameraSimulator_Options` is a
`StackPanel` of small per-section grids (Focus & Optics, Sensor & Filter, Sky & Catalog, Field Aberrations), each
with its own ~5-7 rows; there is no row 26. A new **"Tilt Adapter"** section was added instead, using the file's
label-in-column-0 + bare-CheckBox-in-column-1 convention rather than the snippet's `Content="…"` form.

The `Sim*` adapter fields get their controls in the panel itself (Task 7), but `ShowSimulatorTiltAdapterPanel` is a
plugin-level option and needs a control in `HocusFocus_CameraSimulator_Options`:

```xml
<TextBlock x:Key="CamSim_ShowTiltAdapterPanel_Tooltip"
           Text="Show the simulated tilt adapter as a panel in the Imaging tab, so it can be operated next to the Aberration Inspector. Requires a NINA restart to appear the first time." />
```
```xml
<CheckBox Grid.Row="26" Grid.Column="1" Content="Show tilt adapter panel in Imaging"
          IsChecked="{Binding CameraSimulatorOptions.ShowSimulatorTiltAdapterPanel}"
          ToolTip="{StaticResource CamSim_ShowTiltAdapterPanel_Tooltip}" />
```

- [ ] **Step 7: Full suite + commit**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` → 1963 passed.

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/ Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(camera-sim): add simulated tilt-adapter options"
```

---

### Task 5: `SimulatedTiltAdapter` — the pure screw model

This is the heart. It is **pure**: no WPF, no options object, no NINA types — so it is trivially testable and the
Task 6 capstone can hammer it. Every geometry convention comes from `TiltScrewGeometry`.

**Files:**
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/TiltAdapter/SimulatedTiltAdapter.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/SimulatedTiltAdapterTests.cs`

- [ ] **Step 1: Write the failing test**

`Tests/CameraSimulator/SimulatedTiltAdapterTests.cs`:

```csharp
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.TiltAdapter;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NUnit.Framework;
using System;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

[TestFixture]
public class SimulatedTiltAdapterTests {

    // A 3-screw adapter at 0/120/240°, R = 30 mm, 500 µm per turn.
    private static SimulatedTiltAdapter ThreeScrew() =>
        new SimulatedTiltAdapter(
            screwAnglesDegrees: new[] { 0.0, 120.0, 240.0 },
            screwRadiusMicrons: 30_000.0,
            unitMicrons: 500.0,
            inwardCurvatureSign: TiltScrewGeometry.DefaultScrewInwardCurvatureSign);

    [Test]
    public void SingleScrewMove_ProducesTiltAndOneThirdPiston() {
        var adapter = ThreeScrew();
        // Turn screw 1 by exactly one unit's worth of axial travel.
        var delta = adapter.ApplyMoves(new[] { 500.0, 0.0, 0.0 });

        // A plane through (p1, 500), (p2, 0), (p3, 0): the mean of the three displacements is the piston.
        Assert.That(delta.PistonMicrons, Is.EqualTo(500.0 / 3.0).Within(1e-9));
        // ...and it must tilt: the gradient cannot be zero.
        Assert.That(Math.Sqrt(delta.Gx * delta.Gx + delta.Gy * delta.Gy), Is.GreaterThan(0.0));
    }

    [Test]
    public void EqualMovesOnAllScrews_ArePurePiston() {
        var adapter = ThreeScrew();
        var delta = adapter.ApplyMoves(new[] { 250.0, 250.0, 250.0 });
        Assert.Multiple(() => {
            Assert.That(delta.Gx, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(delta.Gy, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(delta.PistonMicrons, Is.EqualTo(250.0).Within(1e-9));
        });
    }

    [Test]
    public void MovesThatCancelAKnownGradient_ZeroIt() {
        var adapter = ThreeScrew();
        const double gx = 1.5e-4, gy = -0.8e-4;   // dimensionless focuser-µm per sensor-µm
        // TiltScrewGeometry says this is the axial move at each screw that cancels (gx,gy).
        var moves = new[] { 0.0, 120.0, 240.0 }
            .Select(a => TiltScrewGeometry.TiltCorrectionMicrons(gx, gy, a, 30_000.0)).ToArray();

        var delta = adapter.ApplyMoves(moves);

        // Applying the cancelling moves must produce exactly the negated gradient.
        Assert.Multiple(() => {
            Assert.That(delta.Gx, Is.EqualTo(-gx).Within(1e-12));
            Assert.That(delta.Gy, Is.EqualTo(-gy).Within(1e-12));
        });
    }
}
```

- [ ] **Step 2: Run and watch it fail**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~SimulatedTiltAdapterTests"`
Expected: FAIL — compile error, `SimulatedTiltAdapter` not defined.

- [ ] **Step 3: Implement the model**

`CameraSimulator/TiltAdapter/SimulatedTiltAdapter.cs`:

```csharp
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using System;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.TiltAdapter {

    /// <summary>The best-focus surface change produced by a set of screw moves. Units: Gx/Gy are dimensionless
    /// (focuser µm of travel per µm of sensor displacement); piston is µm of focuser travel.</summary>
    public readonly struct AberrationDelta {
        public AberrationDelta(double gx, double gy, double pistonMicrons) {
            Gx = gx; Gy = gy; PistonMicrons = pistonMicrons;
        }
        public double Gx { get; }
        public double Gy { get; }
        public double PistonMicrons { get; }
    }

    /// <summary>
    /// A virtual tilt adapter. Moving a screw axially by δ moves the sensor there by δ, which moves the
    /// best-focus surface by exactly the plane fitted through the screw displacements. That makes this the
    /// exact inverse of <see cref="TiltScrewGeometry.TiltCorrectionMicrons"/>, which the Aberration Inspector
    /// uses to tell the user how far to turn each screw — so guidance applied here converges to zero.
    /// Pure: no options, no WPF, no NINA types.
    /// </summary>
    public sealed class SimulatedTiltAdapter {
        private readonly double[] anglesDegrees;
        private readonly double radiusMicrons;

        public SimulatedTiltAdapter(double[] screwAnglesDegrees, double screwRadiusMicrons,
                double unitMicrons, int inwardCurvatureSign) {
            if (screwAnglesDegrees == null) throw new ArgumentNullException(nameof(screwAnglesDegrees));
            if (screwAnglesDegrees.Length != 3 && screwAnglesDegrees.Length != 4)
                throw new ArgumentOutOfRangeException(nameof(screwAnglesDegrees), "Only 3- or 4-screw adapters exist.");
            if (screwRadiusMicrons <= 0) throw new ArgumentOutOfRangeException(nameof(screwRadiusMicrons));
            if (unitMicrons <= 0) throw new ArgumentOutOfRangeException(nameof(unitMicrons));

            anglesDegrees = (double[])screwAnglesDegrees.Clone();
            radiusMicrons = screwRadiusMicrons;
            UnitMicrons = unitMicrons;
            InwardCurvatureSign = inwardCurvatureSign;
        }

        public int ScrewCount => anglesDegrees.Length;
        /// <summary>Axial µm per unit of user input — thread pitch (per turn) or stepper step size (per step).</summary>
        public double UnitMicrons { get; }
        public int InwardCurvatureSign { get; }

        /// <summary>Axial displacement (µm) for a signed user amount. Direction comes from the button alone —
        /// deliberately NO rig-direction factor; see the CORRECTION note below.</summary>
        public double AxialMicronsForUnits(double units) => units * UnitMicrons;

        /// <summary>
        /// Fit the plane through the per-screw axial displacements. Exactly determined for 3 screws,
        /// least-squares for 4 (opposite screws are mechanically coupled, so a 4-screw move set is
        /// generally consistent and the fit is exact there too).
        /// </summary>
        public AberrationDelta ApplyMoves(double[] axialMicronsPerScrew) {
            if (axialMicronsPerScrew == null) throw new ArgumentNullException(nameof(axialMicronsPerScrew));
            if (axialMicronsPerScrew.Length != ScrewCount)
                throw new ArgumentException($"Expected {ScrewCount} displacements.", nameof(axialMicronsPerScrew));

            // Normal equations for z = a·x + b·y + c.
            double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0, sz = 0, sxz = 0, syz = 0;
            var n = ScrewCount;
            for (var i = 0; i < n; i++) {
                var (x, y) = TiltScrewGeometry.ScrewPositionMicrons(anglesDegrees[i], radiusMicrons);
                var z = axialMicronsPerScrew[i];
                sxx += x * x; sxy += x * y; syy += y * y;
                sx += x; sy += y; sz += z; sxz += x * z; syz += y * z;
            }

            // Solve the 3x3 system by Cramer's rule.
            var m = new[,] { { sxx, sxy, sx }, { sxy, syy, sy }, { sx, sy, (double)n } };
            var rhs = new[] { sxz, syz, sz };
            var det = Det3(m);
            if (Math.Abs(det) < 1e-12)
                throw new InvalidOperationException("Degenerate screw geometry: the screw angles are collinear.");

            var a = Det3(Replace(m, 0, rhs)) / det;
            var b = Det3(Replace(m, 1, rhs)) / det;
            var c = Det3(Replace(m, 2, rhs)) / det;
            return new AberrationDelta(a, b, c);
        }

        private static double[,] Replace(double[,] m, int col, double[] v) {
            var r = (double[,])m.Clone();
            for (var i = 0; i < 3; i++) r[i, col] = v[i];
            return r;
        }

        private static double Det3(double[,] m) =>
            m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1])
          - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
          + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
    }
}
```

Add `using System.Linq;` to the test file for `.Select`.

- [ ] **Step 4: Run the tests**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~SimulatedTiltAdapterTests"`
Expected: PASS (3 tests). If `MovesThatCancelAKnownGradient_ZeroIt` fails on sign, do **not** flip a sign in the
model — the model's plane fit is definitionally correct. Re-read `TiltScrewGeometry.TiltCorrectionMicrons`; the
test encodes its documented contract (`δ = -(gx·x + gy·y)`).

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/TiltAdapter/ \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/SimulatedTiltAdapterTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(camera-sim): add the pure simulated tilt-adapter screw model"
```

---

### Task 6: The round-trip capstone

This is what proves "exact inverse" and therefore that the interactive loop converges. It is the automated form
of the whole feature.

> **CORRECTION (found during execution — this plan was wrong twice here; both are fixed above and in the design).**
> 1. **The sign factor was inverted.** An earlier draft of `AxialMicronsForUnits` multiplied by
>    `CwMovesAdapterTowardObjectiveForSign(σ) ? 1.0 : -1.0`, which is exactly `−σ` — i.e. **backwards on default
>    (σ=+1) rigs**, and only coincidentally right on σ=−1. The correct factor is identically **+1 on both rigs**, so
>    *no* rig-dependent factor belongs there: the inspector's tilt guidance is sign-free (`InspectorVM.cs:2035`;
>    `TiltScrewGeometry.SignedTotalAdjustment` applies σ to backfocus only, with a regression pin saying so),
>    because `PhysicalToStoredAngle` makes stored angles already carry rig direction (`p_stored = σ·p_phys`) and the
>    two σ's cancel (σ²=1). Merely flipping the ternary would ALSO have been wrong.
> 2. **The capstone below was vacuous.** As originally written it fed axial µm straight into `ApplyMoves`,
>    bypassing `AxialMicronsForUnits` — making `unitMicrons` and `curvatureSign` provably inert (6 of its 8
>    combinatorial cases were duplicates). It was empirically shown to **pass against the buggy sign**. A capstone
>    that certifies the bug it exists to catch is worse than none.
>
> **The capstone MUST compute turns the way `InspectorVM.FillNumericGuidance` does and route them through
> `AxialMicronsForUnits`** — otherwise the sign path is never exercised. The shipped version does this and fails 16
> cases (precisely the σ=+1 half) if the bad sign is restored.

**Files:**
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/SimulatedTiltAdapterCapstoneTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.TiltAdapter;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NUnit.Framework;
using System;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

/// <summary>
/// The automated form of the interactive loop: inject a tilt, ask the REAL guidance math what to turn,
/// turn exactly that on the simulated adapter, and assert the tilt is gone. A private reimplementation of
/// the geometry would pass the unit tests and fail here.
/// </summary>
[TestFixture]
public class SimulatedTiltAdapterCapstoneTests {

    private const double RadiusMicrons = 30_000.0;

    private static double[] Angles(int screwCount) =>
        screwCount == 3 ? new[] { 0.0, 120.0, 240.0 } : new[] { 0.0, 90.0, 180.0, 270.0 };

    [Test]
    [Combinatorial]
    public void InspectorGuidance_AppliedToSimulator_ZeroesTheTilt(
            [Values(3, 4)] int screwCount,
            [Values(250.0, 1.5)] double unitMicrons,     // a screw pitch, and a stepper step size
            [Values(-1, 1)] int curvatureSign) {

        var angles = Angles(screwCount);
        var adapter = new SimulatedTiltAdapter(angles, RadiusMicrons, unitMicrons, curvatureSign);

        // An injected tilt, in the paraboloid model's units (focuser µm per sensor µm).
        const double gx = 2.1e-4, gy = -1.3e-4;

        // What the inspector would tell the user to do, from the REAL guidance helper.
        var axialMoves = angles
            .Select(a => TiltScrewGeometry.TiltCorrectionMicrons(gx, gy, a, RadiusMicrons))
            .ToArray();

        var delta = adapter.ApplyMoves(axialMoves);

        // The injected tilt plus the guidance-driven change must cancel.
        Assert.Multiple(() => {
            Assert.That(gx + delta.Gx, Is.EqualTo(0.0).Within(1e-12), "Gx did not cancel");
            Assert.That(gy + delta.Gy, Is.EqualTo(0.0).Within(1e-12), "Gy did not cancel");
        });
    }

    [Test]
    public void PureBackfocusMove_LeavesTiltUntouched([Values(3, 4)] int screwCount) {
        var angles = Angles(screwCount);
        var adapter = new SimulatedTiltAdapter(angles, RadiusMicrons, 250.0, -1);

        var delta = adapter.ApplyMoves(Enumerable.Repeat(100.0, screwCount).ToArray());

        Assert.Multiple(() => {
            Assert.That(delta.Gx, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(delta.Gy, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(delta.PistonMicrons, Is.EqualTo(100.0).Within(1e-9), "all-equal moves are pure piston");
        });
    }

    [Test]
    public void FourScrewCornerMove_PistonsZero() {
        // A diagonal pair turned opposite ways tilts toward a corner and must not change backfocus.
        var adapter = new SimulatedTiltAdapter(Angles(4), RadiusMicrons, 250.0, -1);
        var delta = adapter.ApplyMoves(new[] { 100.0, 0.0, -100.0, 0.0 });

        Assert.That(delta.PistonMicrons, Is.EqualTo(0.0).Within(1e-9),
            "a corner move is antisymmetric, so it cannot change backfocus");
    }
}
```

- [ ] **Step 2: Run it**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~SimulatedTiltAdapterCapstoneTests"`
Expected: PASS (8 combinatorial + 2 + 1 = 11 cases). It should pass immediately against Task 5's model — that is
the point. If it fails, the model is not the guidance's inverse and Task 5 is wrong; fix Task 5, not this test.

- [ ] **Step 3: Full suite**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
Expected: 1977 passed (1963 + 3 unit + 11 capstone).

- [ ] **Step 4: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/SimulatedTiltAdapterCapstoneTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "test(camera-sim): pin the tilt-adapter round-trip against the real guidance math"
```

---

### Task 7: The panel VM and shared control

Follow `docs/virtual-tilt-adapter-panel-ux-design.md` for layout and copy — it is the authority on the panel's
shape. This task wires the VM that binds it.

> **CORRECTION (found during execution — the `ApplyDelta` snippet below was wrong twice; both are fixed in the
> shipped code and in design §3.2).**
> 1. **It fed the raw fitted piston in.** `delta.PistonMicrons` is in the adapter's *response* frame; the physical
>    piston is `PistonDirectionSign · delta.PistonMicrons`. The raw form is right only on σ=+1 rigs and silently
>    backwards on σ=−1 ones — for **both** the `OptimalFocuserPosition` shift and the `BackfocusErrorMicrons` fold.
> 2. **The backfocus fold's outer sign was inverted.** `-delta.PistonMicrons * (...)` doubles the backfocus error
>    instead of nulling it, on **both** rigs. The correct fold is `+ΔZ0_phys · (halfW²+halfH²)/R²`, because
>    `ScrewInwardCurvatureSign` is *defined* as the sign of the curvature-effect response to a CW turn (a pure
>    piston with `ΔZ0_phys = σ·δ`). Design §3.2's corrected step 3 carries the full derivation and the two
>    independent cross-checks.
>
> The two are independent: the σ=−1-vs-σ=+1 asymmetry survives an outer flip, so a test of that asymmetry alone
> does **not** catch (2). The shipped tests pin both and were verified to fail against each bug in isolation —
> (1) fails 5 cases, all σ=−1; (2) fails 6 cases across both rigs.

**Files:**
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/TiltAdapter/SimulatedTiltAdapterVM.cs`
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/TiltAdapter/TiltAdapterDataTemplates.xaml` (+ `.cs`)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/SimulatedTiltAdapterVMTests.cs`

**VM responsibilities (keep it to these):**
1. Build a `SimulatedTiltAdapter` from the `Sim*` options + the resolved sensor half-dimensions.
2. `TurnCommand(screwIndex, rotationSign)` → build the per-screw move array for the current movement mode,
   call `ApplyMoves`, then fold the delta into the options (tilt / piston / backfocus per the design's §3.2).
3. Expose `TiltAmountMicrons`, `TiltAngleDegrees`, `BackfocusErrorMicrons`, `IsFlat` (tilt < 1 µm) for the state
   strip; `LastActionText`; `UndoCommand` (single level); per-screw net position in **µm**.
4. `AdapterMatchesReal` badge: compare the `Sim*` fields against `HocusFocusPlugin.TiltAdapterOptions`.
5. `CopyFromAdapterCommand` / `CopyToAdapterCommand` (the latter confirmed before writing real calibration).

- [ ] **Step 1: Write the failing test**

```csharp
using NINA.Joko.Plugins.HocusFocus.CameraSimulator;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.TiltAdapter;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

[TestFixture]
public class SimulatedTiltAdapterVMTests {

    // Built locally: CameraSimulatorOptionsTests' helper is private to that fixture.
    private static CameraSimulatorOptions BuildOptions() =>
        new CameraSimulatorOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());

    [Test]
    public void Turn_FoldsDeltaIntoOptionsAndConverges() {
        var options = BuildOptions();
        options.EnableAberrations = true;
        options.SimScrewCount = 3;
        options.TiltAmountMicrons = 40.0;
        options.TiltAngleDegrees = 30.0;

        var vm = new SimulatedTiltAdapterVM(options);
        var before = options.TiltAmountMicrons;

        vm.AmountPerClick = 0.25;
        vm.TurnCommand.Execute(new ScrewTurn(screwIndex: 0, rotationSign: -1));

        Assert.That(options.TiltAmountMicrons, Is.Not.EqualTo(before), "a turn must change the injected tilt");
        Assert.That(vm.LastActionText, Does.Contain("Screw 1"));
    }

    [Test]
    public void Undo_RestoresThePreviousState() {
        var options = BuildOptions();
        options.TiltAmountMicrons = 40.0;
        var vm = new SimulatedTiltAdapterVM(options);
        vm.AmountPerClick = 0.5;
        vm.TurnCommand.Execute(new ScrewTurn(0, +1));
        vm.UndoCommand.Execute(null);
        Assert.That(options.TiltAmountMicrons, Is.EqualTo(40.0).Within(1e-9));
    }
}
```

- [ ] **Step 2: Run and watch it fail**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~SimulatedTiltAdapterVMTests"`
Expected: FAIL — `SimulatedTiltAdapterVM` not defined.

- [ ] **Step 3: Implement the VM**

First the command argument the tests use — a tiny value type so the XAML can pass both pieces via
`CommandParameter` (put it in `SimulatedTiltAdapterVM.cs`):

```csharp
/// <summary>One click on a turn button: which screw row, and which way it was turned.
/// rotationSign is +1 for ⟳ / "+" and -1 for ⟲ / "−" — direction comes from the button, never from a
/// signed amount, so the two can't contradict each other.</summary>
public readonly struct ScrewTurn {
    public ScrewTurn(int screwIndex, int rotationSign) {
        ScrewIndex = screwIndex;
        RotationSign = Math.Sign(rotationSign) == 0 ? 1 : Math.Sign(rotationSign);
    }
    public int ScrewIndex { get; }      // 0-based
    public int RotationSign { get; }    // +1 or -1
}
```

Key conversion (put it in one private method so it is stated once):

```csharp
/// <summary>Fold a screw-move delta into the simulator's injected aberration. See design §3.2.</summary>
private void ApplyDelta(AberrationDelta delta) {
    var (halfW, halfH) = SensorHalfDimensionsMicrons();

    // Current gradient from the options' (azimuth, amount) form — the same inversion AberrationSurface uses.
    var phi = options.TiltAngleDegrees * Math.PI / 180.0;
    var den = Math.Abs(Math.Cos(phi)) * halfW + Math.Abs(Math.Sin(phi)) * halfH;
    var g = den > 0 ? options.TiltAmountMicrons / den : 0.0;
    var gx = g * Math.Cos(phi) + delta.Gx;
    var gy = g * Math.Sin(phi) + delta.Gy;

    // Back to (azimuth, amount).
    options.TiltAmountMicrons = Math.Abs(gx) * halfW + Math.Abs(gy) * halfH;
    options.TiltAngleDegrees = NormalizeDegrees(Math.Atan2(gy, gx) * 180.0 / Math.PI);

    // The piston both shifts best focus and violates the optics' backfocus spacing. Both consequences take the
    // PHYSICAL piston — the fitted constant is in the response frame (see the CORRECTION above).
    var pistonMicrons = adapter.PistonDirectionSign * delta.PistonMicrons;
    if (pistonMicrons != 0.0) {
        options.OptimalFocuserPosition +=
            (int)Math.Round(pistonMicrons / options.FocuserStepSizeMicrons);
        var r = options.SimScrewRadiusMillimeters * 1000.0;
        options.BackfocusErrorMicrons +=
            pistonMicrons * (halfW * halfW + halfH * halfH) / (r * r);
    }
}
```

`SensorHalfDimensionsMicrons()` resolves `SensorRegistry.Get(options.SensorModel)` →
`(Width * PixelSizeMicrons / 2, Height * PixelSizeMicrons / 2)`.

`TurnCommand` builds the move array per movement mode:
- **3-screw:** one entry non-zero.
- **4-screw Corner:** the named screw `+δ`, its opposite `−δ`, the other pair `0`.
- **4-screw Side:** the named screw and its neighbour `+δ`, the opposing pair `−δ`.
- **4-screw Backfocus:** all four `+δ`.
where `δ = adapter.AxialMicronsForUnits(AmountPerClick * rotationSign)`.

Snapshot `(TiltAmountMicrons, TiltAngleDegrees, BackfocusErrorMicrons, OptimalFocuserPosition)` before each turn
into a single-level undo field.

- [ ] **Step 4: Run the tests → PASS**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~SimulatedTiltAdapterVMTests"`
Expected: PASS.

- [ ] **Step 5: Build the control**

`TiltAdapterDataTemplates.xaml` — a `ResourceDictionary` exporting `<DataTemplate x:Key="HocusFocus_SimTiltAdapter_Panel">`
per the UX doc: the state strip, the Operate rows (one shared `AmountPerClick` `UnitTextBox` + per-row ⟲/⟳
buttons), the 4-screw `Corner | Side | Backfocus` selector, the last-action line + Undo, and two collapsed
`Expander`s (Injected aberration / Adapter configuration) with the coherence badge and copy buttons.
Register it in the plugin the same way `StarDetection/DataTemplates.xaml` is (`[Export(typeof(ResourceDictionary))]`
partial class in the `.xaml.cs`).

- [ ] **Step 6: Build + full suite + commit**

Run: `rtk dotnet build … --nologo` → `errors=0`; `rtk dotnet test … --nologo` → 1979 passed.

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/TiltAdapter/ \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(camera-sim): add the simulated tilt-adapter panel"
```

---

### Task 8: Host the panel in the setup dialog

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/SetupDialog/SetupDataTemplates.xaml`

- [ ] **Step 1: Add the panel below the Rig group**

Inside the camera `DataTemplate`'s `StackPanel`, after the Rig grid:

```xml
<Separator Margin="0,10" />
<ContentControl Content="{Binding TiltAdapterVM}"
                ContentTemplate="{StaticResource HocusFocus_SimTiltAdapter_Panel}" />
```

- [ ] **Step 2: Expose the VM on the camera**

In `HocusFocusSimulatorCamera.cs`:

```csharp
/// <summary>Backs the simulated tilt-adapter panel hosted by the setup dialog and the Imaging dockable.</summary>
public SimulatedTiltAdapterVM TiltAdapterVM { get; }
```
**AS-BUILT:** construct it **lazily on first bind** (`Lazy<SimulatedTiltAdapterVM>`), not in the camera's
constructor. `HocusFocusSimulatorCameraProvider.GetEquipment()` builds a fresh camera on every equipment rescan,
and the VM subscribes to process-lifetime options singletons without ever unsubscribing — eager construction
would retain one VM per rescan. It also keeps the plugin's static bootstrap out of the camera unit tests.

- [ ] **Step 3: Build + suite + commit**

Run: `rtk dotnet build … --nologo` → `errors=0`; `rtk dotnet test … --nologo` → 1979 passed.

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(camera-sim): host the tilt-adapter panel in the camera setup dialog"
```

---

### Task 9: The Imaging dockable

**NINA facts that constrain this** (verified — do not "fix" them): the dockable set is built **once at startup**
into a plain `List<IDockableVM>` with private setters on an `internal` `DockManagerVM`, and `IDockManagerVM` is
not composed into the plugin container — so the export must be unconditional and the sidebar button **cannot** be
removed. `InitializeAvalonDockLayout` sets every anchorable `IsVisible=false` then restores from the saved
`<profileId>.dock.config`, so a value set in the constructor is overwritten.

**On testing this task — read before you start.** This is the one task with no unit test, deliberately: the
`DockableVM` constructor resolves a WPF **pack URI** `ResourceDictionary` and the gating touches
`Application.Current.Dispatcher`, neither of which exists in the NUnit host — a test would be asserting against
WPF plumbing, not our logic. The gating *decision* is one predicate (`ShowSimulatorTiltAdapterPanel`), and it is
covered by the manual checklist (Task 10). **If you find yourself wanting coverage anyway, extract the predicate
rather than mocking WPF** — but do not let that grow into a test that only proves `Application.Current` is null.

**Files:**
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/TiltAdapter/SimulatorTiltAdapterDockableVM.cs`

- [ ] **Step 1: Implement the dockable**

Model it on `StarDetection/StarDetectionResultsVM.cs`:

```csharp
[Export(typeof(IDockableVM))]
public class SimulatorTiltAdapterDockableVM : DockableVM {

    private readonly ICameraSimulatorOptions options;

    [ImportingConstructor]
    public SimulatorTiltAdapterDockableVM(IProfileService profileService) : base(profileService) {
        Title = "Simulator Tilt Adapter";
        options = HocusFocusPlugin.CameraSimulatorOptions;

        var dict = new ResourceDictionary {
            Source = new Uri("NINA.Joko.Plugins.HocusFocus;component/TiltAdapterWizard/DataTemplates.xaml",
                             UriKind.RelativeOrAbsolute)
        };
        ImageGeometry = (GeometryGroup)dict["TiltAdapterWizardSVG"];
        ImageGeometry.Freeze();

        TiltAdapterVM = new SimulatedTiltAdapterVM(options);
        options.PropertyChanged += OptionsChanged;
    }

    public SimulatedTiltAdapterVM TiltAdapterVM { get; }

    private bool Enabled => options.ShowSimulatorTiltAdapterPanel;

    private void OptionsChanged(object sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(ICameraSimulatorOptions.ShowSimulatorTiltAdapterPanel) && !Enabled) {
            IsVisible = false;
        }
    }

    /// <summary>
    /// NINA's sidebar button calls this to toggle the panel. When the option is off we swallow it, so the
    /// panel cannot be reopened. The button itself cannot be hidden — NINA builds the dockable list once at
    /// startup and exposes no way to remove an entry.
    /// </summary>
    public override void Hide(object o) {
        if (!Enabled) {
            IsVisible = false;
            return;
        }
        base.Hide(o);
    }
}
```

- [ ] **Step 2: Gate visibility after layout init**

Because `InitializeAvalonDockLayout` overwrites ctor values, force the closed state on the first dispatcher idle
rather than in the constructor:

```csharp
Application.Current?.Dispatcher.BeginInvoke(
    System.Windows.Threading.DispatcherPriority.ApplicationIdle,
    new Action(() => { if (!Enabled) { IsVisible = false; } }));
```

- [ ] **Step 3: Add the panel content**

**AS-BUILT (divergence #5): the key below is WRONG — do not use `{x:Type …}`.** NINA's
`PaneTemplateSelector.SelectTemplate` resolves a dockable's visual by the **string** key
`item.GetType().FullName + "_Dockable"`, e.g.
`x:Key="NINA.Joko.Plugins.HocusFocus.CameraSimulator.TiltAdapter.SimulatorTiltAdapterDockableVM_Dockable"`.
An `{x:Type}` key compiles cleanly and silently renders the type's name instead of the panel. All 7 pre-existing
dockable templates in this plugin use the string key — follow them.

Add the dockable's `DataTemplate` (keyed `{x:Type ...SimulatorTiltAdapterDockableVM}`) to
`TiltAdapterDataTemplates.xaml`, hosting `HocusFocus_SimTiltAdapter_Panel` bound to `TiltAdapterVM`.

- [ ] **Step 4: Build + suite + commit**

Run: `rtk dotnet build … --nologo` → `errors=0`; `rtk dotnet test … --nologo` → 1979 passed.

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/TiltAdapter/
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(camera-sim): add the simulator tilt-adapter Imaging dockable"
```

---

### Task 10: Extend the manual smoke test

**Files:**
- Modify: `docs/synthetic-camera-manual-smoke-test.md`

- [ ] **Step 1: Add the checks**

Append a section covering: the gear button opens the rig dialog; rig options render read-only in plugin Options
with the pointer tooltip; Sensor Model is disabled while connected and editable when disconnected; the aberration
group collapses when `Enable Aberrations` is off; the tilt-adapter panel appears in the setup dialog; the Imaging
dockable stays closed when the option is off (**noting the sidebar button remains — a NINA limitation**); and the
headline loop:

> Inject tilt (azimuth 30°, amount 80 µm) → run the Aberration Inspector → note its per-screw guidance → apply
> exactly that on the panel → re-run → tilt should read ≈0 and the state strip should show `✓ ≈ flat`.

- [ ] **Step 2: Commit**

```bash
git add docs/synthetic-camera-manual-smoke-test.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "docs(camera-sim): extend the manual smoke test for the tilt adapter"
```

---

## Definition of done

- `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` → all green. **As-built: 2052
  passing** (from a 1960 baseline; the estimate here was ~1979 before reviewers added the tests that caught
  divergences #2 and #4).
- The round-trip capstone passes across screw count × adjustment type × sign — this is the feature's real proof.
- Every persisted option has a UI control (project invariant).
- The manual checklist's headline loop converges in a real NINA session.
