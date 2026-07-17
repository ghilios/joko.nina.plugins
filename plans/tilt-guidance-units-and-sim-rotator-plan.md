# Tilt Guidance Turn-Units + Camera Sim Rotator Angle — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a persisted Turns/Degrees display toggle to the Aberration Inspector's Tilt Adapter
Guidance panel (screw adapters only), and drive the Camera Simulator's field rotation from a
connected rotator's mechanical angle (added to the manual Field Rotation as an offset).

**Architecture:** Feature 1 is a pure presentation change funneled through the single formatter
`TiltAdapterGuidanceVM.FormatAmount` plus a new persisted enum option on `ITiltAdapterOptions`;
the inspector already rebuilds guidance on any tilt-option change, so a ComboBox bound to a
passthrough property auto-refreshes the table. Feature 2 reuses the sim's existing
`RotationDegrees → RenderRequest → TanProjection` path — no pixel math — by injecting
`IRotatorMediator` and resolving the effective rotation in `BuildRenderRequest`.

**Tech Stack:** C# / .NET 8 (WPF), CommunityToolkit.Mvvm, MEF composition,
`PluginOptionsAccessor`, NUnit 4 + NSubstitute.

**Design doc:** `docs/tilt-guidance-units-and-sim-rotator-design.md`

**The two features are independent** (different subsystems, no shared code). Feature 1 = Tasks 1–5,
Feature 2 = Tasks 6–8. They may be committed and reviewed separately.

## Test command

No `dotnet` in WSL — run the Windows CLI over interop (timeout 600000 ms):

```bash
cd /home/ghilios/src/hocus-focus
dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo
```

Filter to one fixture while iterating, e.g.:
`dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo --filter "FullyQualifiedName~TiltAdapterGuidanceVMTests"`

## Commit convention (every commit)

```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "<message>"
```

Work on branch `ghilios/tilt-guidance-units-and-sim-rotator` (already created).

## File Structure

**Feature 1 — modify:**
- `Interfaces/ITiltAdapterOptions.cs` — new `TiltGuidanceAngleUnit` enum + `AngleDisplayUnit` interface member.
- `TiltAdapterWizard/TiltAdapterOptions.cs` — persist `AngleDisplayUnit` (field, property, load).
- `AutoFocus/TiltScrewGuidanceRow.cs` — degrees mode in `FormatAmount` / `BuildDirectionLegend`; new `ShowAngleUnitSelector` gate.
- `AutoFocus/InspectorVM.cs` — passthrough property + thread the unit into `FillNumericGuidance` and the legend.
- `AutoFocus/DataTemplates.xaml` — the ComboBox in the guidance panel.
- Tests: `Tests/AutoFocus/TiltAdapterGuidanceVMTests.cs`, `Tests/TiltAdapterWizard/TiltAdapterOptionsTests.cs`.

**Feature 2 — modify:**
- `CameraSimulator/HocusFocusSimulatorCameraProvider.cs` — import + forward `IRotatorMediator`.
- `CameraSimulator/HocusFocusSimulatorCamera.cs` — accept `IRotatorMediator`; resolve rotation in `BuildRenderRequest`.
- `Resources/OptionsDataTemplates.xaml` — Field Rotation tooltip wording.
- Tests: `Tests/CameraSimulator/HocusFocusSimulatorCameraProviderTests.cs`, `Tests/CameraSimulator/HocusFocusSimulatorCameraTests.cs`.

---

# Feature 1 — Tilt Adapter Guidance turn-units dropdown

## Task 1: Add the `TiltGuidanceAngleUnit` enum + persisted `AngleDisplayUnit` option

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/ITiltAdapterOptions.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltAdapterOptions.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterWizard/TiltAdapterOptionsTests.cs`

- [ ] **Step 1: Write the failing tests (default + round-trip)**

Append these two tests inside the `TiltAdapterOptionsTests` class (before the closing `}`) in
`Tests/TiltAdapterWizard/TiltAdapterOptionsTests.cs`:

```csharp
    [Test]
    public void AngleDisplayUnit_DefaultsToTurns() {
        var (options, _, _) = Build();
        Assert.That(options.AngleDisplayUnit, Is.EqualTo(TiltGuidanceAngleUnit.Turns));
    }

    [Test]
    public void AngleDisplayUnit_PersistsAndRoundTrips() {
        var (options, store, _) = Build();
        options.AngleDisplayUnit = TiltGuidanceAngleUnit.Degrees;
        Assert.Multiple(() => {
            Assert.That(store.GetValueEnum(nameof(TiltAdapterOptions.AngleDisplayUnit), TiltGuidanceAngleUnit.Turns),
                Is.EqualTo(TiltGuidanceAngleUnit.Degrees));
            var reloaded = new TiltAdapterOptions(Substitute.For<IProfileService>(), store);
            Assert.That(reloaded.AngleDisplayUnit, Is.EqualTo(TiltGuidanceAngleUnit.Degrees));
        });
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo --filter "FullyQualifiedName~TiltAdapterOptionsTests"`
Expected: **build failure** — `TiltGuidanceAngleUnit` and `AngleDisplayUnit` do not exist yet.

- [ ] **Step 3: Add the enum + interface member**

In `Interfaces/ITiltAdapterOptions.cs`, add `using NINA.Joko.Plugins.HocusFocus.Converters;` to the
using block (keep the existing `using System.ComponentModel;`). Then add the enum above the
`ITiltAdapterOptions` interface, next to `TiltAdjustmentType`:

```csharp
    [TypeConverter(typeof(EnumStaticDescriptionConverter))]
    public enum TiltGuidanceAngleUnit {

        [Description("Turns")]
        Turns = 0,

        [Description("Degrees")]
        Degrees = 1
    }
```

Add this member to the `ITiltAdapterOptions` interface (e.g. just after `AdjustmentType`):

```csharp
        // Display unit for the Tilt Adapter Guidance numeric amounts on screw adapters:
        // Turns (default) or Degrees (1 turn = 360°). Ignored for stepper adapters (always whole steps).
        TiltGuidanceAngleUnit AngleDisplayUnit { get; set; }
```

- [ ] **Step 4: Persist it in `TiltAdapterOptions`**

In `TiltAdapterWizard/TiltAdapterOptions.cs`, add the load line at the end of `InitializeOptions()`
(after the `saveAFRunsPath = ...` line):

```csharp
            angleDisplayUnit = optionsAccessor.GetValueEnum(nameof(AngleDisplayUnit), TiltGuidanceAngleUnit.Turns);
```

Add the backing field + property (place it after the `AdjustmentType` property, mirroring it):

```csharp
        private TiltGuidanceAngleUnit angleDisplayUnit;

        public TiltGuidanceAngleUnit AngleDisplayUnit {
            get => angleDisplayUnit;
            set {
                if (angleDisplayUnit != value) {
                    angleDisplayUnit = value;
                    optionsAccessor.SetValueEnum(nameof(AngleDisplayUnit), angleDisplayUnit);
                    RaisePropertyChanged();
                }
            }
        }
```

(`TiltGuidanceAngleUnit` resolves via the existing `using NINA.Joko.Plugins.HocusFocus.Interfaces;`
at the top of the file. `TiltAdapterOptions` has no `ResetDefaults` method — nothing else to update.)

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo --filter "FullyQualifiedName~TiltAdapterOptionsTests"`
Expected: PASS (all `TiltAdapterOptionsTests`, including the two new ones).

- [ ] **Step 6: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/ITiltAdapterOptions.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltAdapterOptions.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterWizard/TiltAdapterOptionsTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(tilt): persist a Turns/Degrees display unit for tilt guidance"
```

---

## Task 2: Degrees mode in `FormatAmount` + `BuildDirectionLegend`

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/TiltScrewGuidanceRow.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorVM.cs` (call sites)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/TiltAdapterGuidanceVMTests.cs`

Both static methods gain a required `TiltGuidanceAngleUnit angleUnit` parameter. Steppers ignore it.
Screws in `Degrees` show whole degrees (`turns × 360`, rounded away-from-zero) with the `⟳/⟲` glyph,
keeping the existing `|turns| < 0.005` noise floor (so the smallest non-dash value is ~2°).

- [ ] **Step 1: Rewrite the test file to the new 3-arg signature + degrees cases**

Replace the three existing method-level tests in `Tests/AutoFocus/TiltAdapterGuidanceVMTests.cs`
(`FormatAmount_Screws_...`, `FormatAmount_Steppers_...`, `BuildDirectionLegend_...`) with the versions
below. Add `using NINA.Joko.Plugins.HocusFocus.Interfaces;` at the top of the file. Leave
`FreshGuidance_HasNoDirectionLegend` and `HasFourScrewBackfocus_...` unchanged.

```csharp
    [Test]
    public void FormatAmount_ScrewsTurns_ShowsMagnitudeWithRotationGlyph() {
        Assert.Multiple(() => {
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(1.25, steps: false, TiltGuidanceAngleUnit.Turns), Is.EqualTo("1.25 ⟳"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(-0.5, steps: false, TiltGuidanceAngleUnit.Turns), Is.EqualTo("0.50 ⟲"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.001, steps: false, TiltGuidanceAngleUnit.Turns), Is.EqualTo("—"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(-0.004, steps: false, TiltGuidanceAngleUnit.Turns), Is.EqualTo("—"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.005, steps: false, TiltGuidanceAngleUnit.Turns), Is.EqualTo("0.01 ⟳"));
        });
    }

    [Test]
    public void FormatAmount_ScrewsDegrees_RoundsToNearestDegreeWithGlyph() {
        Assert.Multiple(() => {
            // 1 turn = 360°, 0.5 turn = 180°, 0.125 turn = 45°.
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(1.0, steps: false, TiltGuidanceAngleUnit.Degrees), Is.EqualTo("360° ⟳"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(-0.5, steps: false, TiltGuidanceAngleUnit.Degrees), Is.EqualTo("180° ⟲"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.125, steps: false, TiltGuidanceAngleUnit.Degrees), Is.EqualTo("45° ⟳"));
            // Rounds to the nearest whole degree: 0.126 turn = 45.36° → 45°; 0.1264 turn = 45.504° → 46°.
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.126, steps: false, TiltGuidanceAngleUnit.Degrees), Is.EqualTo("45° ⟳"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.1264, steps: false, TiltGuidanceAngleUnit.Degrees), Is.EqualTo("46° ⟳"));
            // Same physical noise floor as turns: |turns| < 0.005 (= 1.8°) renders as the dash.
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.004, steps: false, TiltGuidanceAngleUnit.Degrees), Is.EqualTo("—"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(-0.004, steps: false, TiltGuidanceAngleUnit.Degrees), Is.EqualTo("—"));
            // At the floor, 0.005 turn = 1.8° rounds up to 2°.
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.005, steps: false, TiltGuidanceAngleUnit.Degrees), Is.EqualTo("2° ⟳"));
        });
    }

    [Test]
    public void FormatAmount_Steppers_IgnoreUnitAndShowSignedSteps() {
        // The unit selector never applies to steppers; assert both units render identical whole steps.
        foreach (var unit in new[] { TiltGuidanceAngleUnit.Turns, TiltGuidanceAngleUnit.Degrees }) {
            Assert.Multiple(() => {
                Assert.That(TiltAdapterGuidanceVM.FormatAmount(35.2, steps: true, unit), Is.EqualTo("+35 steps"));
                Assert.That(TiltAdapterGuidanceVM.FormatAmount(-35.2, steps: true, unit), Is.EqualTo("−35 steps"));
                Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.5, steps: true, unit), Is.EqualTo("+1 steps"));
                Assert.That(TiltAdapterGuidanceVM.FormatAmount(-0.5, steps: true, unit), Is.EqualTo("−1 steps"));
                Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.4, steps: true, unit), Is.EqualTo("—"));
                Assert.That(TiltAdapterGuidanceVM.FormatAmount(-0.2, steps: true, unit), Is.EqualTo("—"));
            });
        }
    }

    [Test]
    public void BuildDirectionLegend_UsesUnitWordAndProvenanceSuffix() {
        Assert.Multiple(() => {
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: false, signIsMeasured: true, TiltGuidanceAngleUnit.Turns),
                Is.EqualTo("⬆ = adapter moves toward the objective · ⟳ = clockwise (tighten) · amounts in turns"));
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: false, signIsMeasured: true, TiltGuidanceAngleUnit.Degrees),
                Is.EqualTo("⬆ = adapter moves toward the objective · ⟳ = clockwise (tighten) · amounts in degrees"));
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: false, signIsMeasured: false, TiltGuidanceAngleUnit.Degrees),
                Is.EqualTo("⬆ = adapter moves toward the objective · ⟳ = clockwise (tighten) · amounts in degrees (assumed — set or measure in the Tilt Adapter Wizard)"));
            // Steppers ignore the unit word entirely.
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: true, signIsMeasured: true, TiltGuidanceAngleUnit.Degrees),
                Is.EqualTo("⬆ = adapter moves toward the objective · steps are signed as in the wizard prompts"));
        });
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo --filter "FullyQualifiedName~TiltAdapterGuidanceVMTests"`
Expected: **build failure** — `FormatAmount`/`BuildDirectionLegend` still take 2 args.

- [ ] **Step 3: Update `FormatAmount` + `BuildDirectionLegend` in `TiltScrewGuidanceRow.cs`**

Add `using NINA.Joko.Plugins.HocusFocus.Interfaces;` to the top of `AutoFocus/TiltScrewGuidanceRow.cs`.
Replace the two static methods (lines 76–98) with:

```csharp
        public static string BuildDirectionLegend(bool steps, bool signIsMeasured, TiltGuidanceAngleUnit angleUnit) {
            string unitWord = angleUnit == TiltGuidanceAngleUnit.Degrees ? "degrees" : "turns";
            string body = steps
                ? "⬆ = adapter moves toward the objective · steps are signed as in the wizard prompts"
                : $"⬆ = adapter moves toward the objective · ⟳ = clockwise (tighten) · amounts in {unitWord}";
            string assumed = signIsMeasured ? string.Empty : " (assumed — set or measure in the Tilt Adapter Wizard)";
            return body + assumed;
        }

        /// <summary>
        /// Format a signed per-screw adjustment. Positive = clockwise / the wizard-prompt "+" step
        /// direction. Screws render the magnitude with a rotation glyph — either turns ("1.25 ⟳",
        /// 2 decimals) or whole degrees ("45° ⟳", 1 turn = 360°) per <paramref name="angleUnit"/>;
        /// steppers render signed whole steps ("+35 steps") and ignore the unit. Values below the
        /// 0.005-turn noise floor render as an em dash with no direction mark (so the smallest shown
        /// degree value is ~2°).
        /// </summary>
        public static string FormatAmount(double signedAmount, bool steps, TiltGuidanceAngleUnit angleUnit) {
            if (steps) {
                long rounded = (long)Math.Round(Math.Abs(signedAmount), MidpointRounding.AwayFromZero);
                if (rounded == 0) return "—";
                return signedAmount >= 0 ? $"+{rounded} steps" : $"−{rounded} steps";
            }
            if (Math.Abs(signedAmount) < 0.005) return "—";
            string glyph = signedAmount >= 0 ? "⟳" : "⟲";
            if (angleUnit == TiltGuidanceAngleUnit.Degrees) {
                long degrees = (long)Math.Round(Math.Abs(signedAmount) * 360.0, MidpointRounding.AwayFromZero);
                return $"{degrees}° {glyph}";
            }
            return $"{Math.Abs(signedAmount):0.00} {glyph}";
        }
```

- [ ] **Step 4: Update the four call sites in `InspectorVM.cs`**

In `AutoFocus/InspectorVM.cs`, `FillNumericGuidance` (after the `bool steps = ...` line ~2045), add:

```csharp
            var angleUnit = tiltAdapterOptions.AngleDisplayUnit;
```

Update the three `FormatAmount` calls in the loop (lines ~2069, ~2070, ~2077) to pass `angleUnit`:

```csharp
                tiltText[i] = TiltAdapterGuidanceVM.FormatAmount(corr.TiltMicrons / unitMicrons, steps, angleUnit);
```
```csharp
                backText[i] = TiltAdapterGuidanceVM.FormatAmount(resolvedSign * corr.BackfocusMicrons / unitMicrons, steps, angleUnit);
```
```csharp
                totalText[i] = TiltAdapterGuidanceVM.FormatAmount(totalSigned, steps, angleUnit);
```

Update the `BuildDirectionLegend` call in `RebuildTiltGuidance` (lines ~2023–2025) to pass the unit:

```csharp
                guidance.DirectionLegend = TiltAdapterGuidanceVM.BuildDirectionLegend(
                    steps: tiltAdapterOptions.AdjustmentType == TiltAdjustmentType.StepperMotors,
                    signIsMeasured: tiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured,
                    angleUnit: tiltAdapterOptions.AngleDisplayUnit);
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo --filter "FullyQualifiedName~TiltAdapterGuidanceVMTests"`
Expected: PASS (all five `TiltAdapterGuidanceVMTests` tests).

- [ ] **Step 6: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/TiltScrewGuidanceRow.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorVM.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/TiltAdapterGuidanceVMTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(tilt): render tilt-guidance amounts in degrees when selected"
```

---

## Task 3: `ShowAngleUnitSelector` gate on `TiltAdapterGuidanceVM`

The dropdown must appear only for screw adapters with numeric guidance. Expose one derived bool (the
`HasFourScrewBackfocus` precedent) so the XAML uses a single plain Visibility binding.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/TiltScrewGuidanceRow.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/TiltAdapterGuidanceVMTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `TiltAdapterGuidanceVMTests`:

```csharp
    [Test]
    public void ShowAngleUnitSelector_RequiresNumericGuidanceAndScrews() {
        Assert.Multiple(() => {
            Assert.That(new TiltAdapterGuidanceVM { HasNumericGuidance = true, UnitsAreSteps = false }.ShowAngleUnitSelector, Is.True);
            Assert.That(new TiltAdapterGuidanceVM { HasNumericGuidance = true, UnitsAreSteps = true }.ShowAngleUnitSelector, Is.False);
            Assert.That(new TiltAdapterGuidanceVM { HasNumericGuidance = false, UnitsAreSteps = false }.ShowAngleUnitSelector, Is.False);
        });
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo --filter "FullyQualifiedName~TiltAdapterGuidanceVMTests.ShowAngleUnitSelector_RequiresNumericGuidanceAndScrews"`
Expected: **build failure** — `ShowAngleUnitSelector` does not exist.

- [ ] **Step 3: Add the property**

In `AutoFocus/TiltScrewGuidanceRow.cs`, just after the `UnitsAreSteps` property (line 42), add:

```csharp
        // The Turns/Degrees dropdown is meaningful only for screw adapters with numeric guidance;
        // steppers always show whole steps. Single derived bool so the XAML uses one plain Visibility
        // binding (mirrors HasFourScrewBackfocus).
        public bool ShowAngleUnitSelector => HasNumericGuidance && !UnitsAreSteps;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo --filter "FullyQualifiedName~TiltAdapterGuidanceVMTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/TiltScrewGuidanceRow.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/TiltAdapterGuidanceVMTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(tilt): gate the tilt-guidance unit selector to screw adapters"
```

---

## Task 4: Passthrough property on `InspectorVM` for the dropdown

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorVM.cs`

No unit test: `InspectorVM` needs the full NINA composition to construct, so this is covered by the
existing `TiltAdapterOptionsTests` (persistence) + the manual/`/verify` check. The property is a thin
proxy; the rebuild it triggers is already exercised by the option's `PropertyChanged` wiring.

- [ ] **Step 1: Add the passthrough property**

In `AutoFocus/InspectorVM.cs`, immediately after the `TiltGuidance` property (line 1916), add:

```csharp
        // Two-way bound by the Tilt Adapter Guidance dropdown. Writing it flips the persisted option,
        // whose PropertyChanged is already subscribed to RebuildTiltGuidance() (see the constructor),
        // so the numeric strings + legend regenerate in the new unit automatically.
        public TiltGuidanceAngleUnit TiltGuidanceAngleUnit {
            get => tiltAdapterOptions?.AngleDisplayUnit ?? TiltGuidanceAngleUnit.Turns;
            set {
                if (tiltAdapterOptions != null && tiltAdapterOptions.AngleDisplayUnit != value) {
                    tiltAdapterOptions.AngleDisplayUnit = value;
                    RaisePropertyChanged();
                }
            }
        }
```

(`TiltGuidanceAngleUnit` resolves via the file's existing `using NINA.Joko.Plugins.HocusFocus.Interfaces;`.
`tiltAdapterOptions` is nullable in the dual-constructor test path — the null-guard mirrors the existing
guidance code, e.g. `HasTiltAdapterCalibration` at line 1920.)

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo --filter "FullyQualifiedName~TiltAdapterGuidanceVMTests"`
Expected: PASS (confirms the plugin project still compiles; no behavior change here).

- [ ] **Step 3: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorVM.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(tilt): expose a bindable tilt-guidance unit property on InspectorVM"
```

---

## Task 5: The ComboBox in the guidance panel

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/DataTemplates.xaml`

- [ ] **Step 1: Add the dropdown above the numeric grid**

In `AutoFocus/DataTemplates.xaml`, inside the calibrated-guidance `StackPanel`, insert this block
immediately **before** the numeric-amounts `Grid` (the `<Grid Margin="5,8,5,0" ...>` whose Visibility
binds `TiltGuidance.HasNumericGuidance`, at line ~2961):

```xml
                            <!--  Turns/Degrees display selector — screw adapters only (steppers always show whole steps)  -->
                            <StackPanel
                                Margin="5,8,5,0"
                                Orientation="Horizontal"
                                Visibility="{Binding TiltGuidance.ShowAngleUnitSelector, Converter={StaticResource BooleanToVisibilityCollapsedConverter}}">
                                <TextBlock Margin="0,0,6,0" VerticalAlignment="Center" Text="Display:" />
                                <ComboBox
                                    MinWidth="90"
                                    VerticalAlignment="Center"
                                    ItemsSource="{Binding Source={util:EnumBindingSource {x:Type hfenum:TiltGuidanceAngleUnit}}}"
                                    SelectedItem="{Binding TiltGuidanceAngleUnit, Mode=TwoWay}">
                                    <ComboBox.ItemTemplate>
                                        <DataTemplate>
                                            <TextBlock Text="{Binding Converter={StaticResource HF_EnumStaticDescriptionValueConverter}}" />
                                        </DataTemplate>
                                    </ComboBox.ItemTemplate>
                                </ComboBox>
                            </StackPanel>
```

(`util:EnumBindingSource`, `hfenum:` → `...HocusFocus.Interfaces`, and the
`HF_EnumStaticDescriptionValueConverter` StaticResource are all already declared/used in this file —
see the InterpolationAlgoEnum ComboBox at lines ~2055–2070. `TiltGuidanceAngleUnit` and
`SelectedItem`'s binding both resolve against the panel's InspectorVM DataContext.)

- [ ] **Step 2: Verify the XAML resources still resolve**

Run: `dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo --filter "FullyQualifiedName~XamlResourceResolutionTests"`
Expected: PASS (the XAML compiles and its StaticResources resolve).

- [ ] **Step 3: Run the FULL suite (end of Feature 1)**

Run: `dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo`
Expected: PASS (all tests). Fix any failure at its cause before continuing.

- [ ] **Step 4: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/DataTemplates.xaml
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(tilt): add Turns/Degrees dropdown to the Tilt Adapter Guidance panel"
```

- [ ] **Step 5: Manual/`/verify` check (recommended)**

Launch NINA (the build already xcopies the plugin into NINA's plugin folder). With a tilt-adapter
calibration present and a measurement run, open the Aberration Inspector → Tilt Adapter Guidance for a
**screw** adapter: a "Display: Turns/Degrees" dropdown appears above the numeric table; switching to
Degrees re-renders the amounts as whole degrees with `⟳/⟲` and the legend ends "amounts in degrees";
the choice survives a NINA restart. Confirm the dropdown is **absent** for a stepper adapter.

---

# Feature 2 — Camera Sim uses rotator mechanical angle

## Task 6: Inject `IRotatorMediator` into the sim (refactor, tests stay green)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/HocusFocusSimulatorCameraProvider.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/HocusFocusSimulatorCamera.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/HocusFocusSimulatorCameraProviderTests.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/HocusFocusSimulatorCameraTests.cs`

- [ ] **Step 1: Update the two test constructors first (they will fail to compile until wiring lands)**

In `Tests/CameraSimulator/HocusFocusSimulatorCameraProviderTests.cs`, add
`using NINA.Equipment.Interfaces.Mediator;` if not present (it is, line 2), and add the rotator
substitute to `BuildProvider`:

```csharp
    private static HocusFocusSimulatorCameraProvider BuildProvider() {
        return new HocusFocusSimulatorCameraProvider(
            Substitute.For<IProfileService>(),
            Substitute.For<IExposureDataFactory>(),
            Substitute.For<IImageDataFactory>(),
            Substitute.For<ITelescopeMediator>(),
            Substitute.For<IFocuserMediator>(),
            Substitute.For<IRotatorMediator>());
    }
```

In `Tests/CameraSimulator/HocusFocusSimulatorCameraTests.cs`, thread a rotator through both helper
factories (default to a disconnected substitute so existing tests are unaffected):

```csharp
    private static HocusFocusSimulatorCamera BuildCamera(
        ICameraSimulatorOptions options,
        IFocuserMediator focuser = null,
        ITelescopeMediator telescope = null,
        IRotatorMediator rotator = null) {
        return new HocusFocusSimulatorCamera(
            Substitute.For<IProfileService>(),
            Substitute.For<IExposureDataFactory>(),
            Substitute.For<IImageDataFactory>(),
            telescope ?? Substitute.For<ITelescopeMediator>(),
            focuser ?? Substitute.For<IFocuserMediator>(),
            rotator ?? Substitute.For<IRotatorMediator>(),
            options);
    }

    private static HocusFocusSimulatorCamera BuildCameraWithCompositor(
        ICameraSimulatorOptions options,
        IStarFieldCompositor compositor,
        IExposureDataFactory exposureDataFactory,
        IFocuserMediator focuser,
        ITelescopeMediator telescope,
        IRotatorMediator rotator = null) {
        return new HocusFocusSimulatorCamera(
            Substitute.For<IProfileService>(),
            exposureDataFactory,
            Substitute.For<IImageDataFactory>(),
            telescope,
            focuser,
            rotator ?? Substitute.For<IRotatorMediator>(),
            options,
            compositor);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo --filter "FullyQualifiedName~HocusFocusSimulatorCamera"`
Expected: **build failure** — the camera/provider constructors do not take `IRotatorMediator` yet.

- [ ] **Step 3: Add the import to the provider**

In `CameraSimulator/HocusFocusSimulatorCameraProvider.cs` (`using NINA.Equipment.Interfaces.Mediator;`
already present): add the field, constructor parameter + assignment, and forward it in
`GetEquipment()`:

```csharp
        private readonly IFocuserMediator focuserMediator;
        private readonly IRotatorMediator rotatorMediator;
```
```csharp
        [ImportingConstructor]
        public HocusFocusSimulatorCameraProvider(
            IProfileService profileService,
            IExposureDataFactory exposureDataFactory,
            IImageDataFactory imageDataFactory,
            ITelescopeMediator telescopeMediator,
            IFocuserMediator focuserMediator,
            IRotatorMediator rotatorMediator) {
            this.profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            this.exposureDataFactory = exposureDataFactory ?? throw new ArgumentNullException(nameof(exposureDataFactory));
            this.imageDataFactory = imageDataFactory ?? throw new ArgumentNullException(nameof(imageDataFactory));
            this.telescopeMediator = telescopeMediator ?? throw new ArgumentNullException(nameof(telescopeMediator));
            this.focuserMediator = focuserMediator ?? throw new ArgumentNullException(nameof(focuserMediator));
            this.rotatorMediator = rotatorMediator ?? throw new ArgumentNullException(nameof(rotatorMediator));
        }
```

In `GetEquipment()`, pass the rotator into the camera (between the focuser and `options` args):

```csharp
            var camera = new HocusFocusSimulatorCamera(
                profileService,
                exposureDataFactory,
                imageDataFactory,
                telescopeMediator,
                focuserMediator,
                rotatorMediator,
                options);
```

- [ ] **Step 4: Add the dependency to the camera (both constructors)**

In `CameraSimulator/HocusFocusSimulatorCamera.cs`, add the field beside the other mediators:

```csharp
        private readonly IFocuserMediator focuserMediator;
        private readonly IRotatorMediator rotatorMediator;
```

Public constructor — add the parameter and forward it to the internal ctor (rotator goes right after
`focuserMediator`, before `options`):

```csharp
        public HocusFocusSimulatorCamera(
            IProfileService profileService,
            IExposureDataFactory exposureDataFactory,
            IImageDataFactory imageDataFactory,
            ITelescopeMediator telescopeMediator,
            IFocuserMediator focuserMediator,
            IRotatorMediator rotatorMediator,
            ICameraSimulatorOptions options)
            : this(profileService, exposureDataFactory, imageDataFactory, telescopeMediator, focuserMediator, rotatorMediator, options,
                  new StarFieldCompositor(path => new AstapCatalogReader(path ?? CameraSimulatorOptions.DefaultAstapCatalogPath))) {
        }
```

Internal constructor — add the parameter, assign the field with a null-guard:

```csharp
        internal HocusFocusSimulatorCamera(
            IProfileService profileService,
            IExposureDataFactory exposureDataFactory,
            IImageDataFactory imageDataFactory,
            ITelescopeMediator telescopeMediator,
            IFocuserMediator focuserMediator,
            IRotatorMediator rotatorMediator,
            ICameraSimulatorOptions options,
            IStarFieldCompositor compositor) {
```

and inside its body, after the `focuserMediator` assignment (line ~108):

```csharp
            this.rotatorMediator = rotatorMediator ?? throw new ArgumentNullException(nameof(rotatorMediator));
```

(`IRotatorMediator` resolves via the file's existing `using NINA.Equipment.Interfaces.Mediator;`, line 14.)

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo --filter "FullyQualifiedName~HocusFocusSimulatorCamera"`
Expected: PASS (all existing sim camera + provider tests; rotator is injected but not yet used).

- [ ] **Step 6: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/HocusFocusSimulatorCameraProvider.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/HocusFocusSimulatorCamera.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/HocusFocusSimulatorCameraProviderTests.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/HocusFocusSimulatorCameraTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "refactor(sim): inject IRotatorMediator into the simulator camera"
```

---

## Task 7: Drive `RotationDegrees` from the rotator in `BuildRenderRequest`

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/HocusFocusSimulatorCamera.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/HocusFocusSimulatorCameraTests.cs`

- [ ] **Step 1: Write the failing tests**

Add these two tests to `HocusFocusSimulatorCameraTests` (they mirror
`BuildRenderRequest_UsesTheEffectiveFocuserStepSize`, capturing the `RenderRequest` through a fake
compositor). Add `using NINA.Equipment.Equipment.MyRotator;` to the file's usings:

```csharp
    private static IRotatorMediator RotatorAt(float mechanical) {
        var rotator = Substitute.For<IRotatorMediator>();
        rotator.GetInfo().Returns(new RotatorInfo { Connected = true, MechanicalPosition = mechanical });
        return rotator;
    }

    [Test]
    public async Task BuildRenderRequest_RotatorConnected_AddsMechanicalToManualRotation() {
        var focuser = FocuserAt(5000);
        var telescope = ConnectedTelescope();
        var rotator = RotatorAt(30.0f);

        var options = BuildOptions();
        options.SensorModel = SonySensorModel.IMX533; // smallest sensor: keeps the fake render array small
        options.RotationDegrees = 5.0; // manual value acts as the offset

        RenderRequest captured = null;
        var compositor = Substitute.For<IStarFieldCompositor>();
        compositor.Render(Arg.Any<RenderRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => {
                captured = call.Arg<RenderRequest>();
                return new ushort[3008 * 3008];
            });

        var camera = BuildCameraWithCompositor(
            options, compositor, Substitute.For<IExposureDataFactory>(), focuser, telescope, rotator);
        camera.Connect(CancellationToken.None).GetAwaiter().GetResult();
        camera.StartExposure(new CaptureSequence { ExposureTime = 0.0 });
        await camera.DownloadExposure(CancellationToken.None);

        Assert.That(captured, Is.Not.Null, "the compositor must have been handed a render snapshot");
        Assert.That(captured.RotationDegrees, Is.EqualTo(35.0).Within(1e-6));
    }

    [Test]
    public async Task BuildRenderRequest_RotatorDisconnected_UsesManualRotationOnly() {
        var focuser = FocuserAt(5000);
        var telescope = ConnectedTelescope();
        var rotator = Substitute.For<IRotatorMediator>();
        rotator.GetInfo().Returns(new RotatorInfo { Connected = false, MechanicalPosition = 30.0f });

        var options = BuildOptions();
        options.SensorModel = SonySensorModel.IMX533;
        options.RotationDegrees = 5.0;

        RenderRequest captured = null;
        var compositor = Substitute.For<IStarFieldCompositor>();
        compositor.Render(Arg.Any<RenderRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => {
                captured = call.Arg<RenderRequest>();
                return new ushort[3008 * 3008];
            });

        var camera = BuildCameraWithCompositor(
            options, compositor, Substitute.For<IExposureDataFactory>(), focuser, telescope, rotator);
        camera.Connect(CancellationToken.None).GetAwaiter().GetResult();
        camera.StartExposure(new CaptureSequence { ExposureTime = 0.0 });
        await camera.DownloadExposure(CancellationToken.None);

        Assert.That(captured, Is.Not.Null, "the compositor must have been handed a render snapshot");
        Assert.That(captured.RotationDegrees, Is.EqualTo(5.0).Within(1e-6));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo --filter "FullyQualifiedName~HocusFocusSimulatorCameraTests.BuildRenderRequest_Rotator"`
Expected: FAIL — `BuildRenderRequest_RotatorConnected_...` asserts 35.0 but the code still returns
`options.RotationDegrees` (5.0). (`BuildRenderRequest_RotatorDisconnected_...` may already pass.)

- [ ] **Step 3: Resolve the effective rotation in `BuildRenderRequest`**

In `CameraSimulator/HocusFocusSimulatorCamera.cs`, `BuildRenderRequest` — after the telescope block
(after line ~650, before the `return new RenderRequest {`), add:

```csharp
            // A connected rotator drives the frame's field rotation from its mechanical angle; the manual
            // RotationDegrees option then acts as a calibration offset (zero-point nudge). With no rotator,
            // the manual value sets the rotation directly — the pre-rotator behavior. Only RotationDegrees
            // changes: the sensor-tilt azimuth (TiltAngleDegrees) is fixed to the sensor, which rotates with
            // the camera, so it stays put in image space. If the rendered field ever turns the wrong way as
            // MechanicalPosition increases, negate it here (TanProjection: +deg rotates E,N CCW into x,up).
            var rotatorInfo = rotatorMediator.GetInfo();
            var rotatorConnected = rotatorInfo?.Connected ?? false;
            double rotationDegrees = rotatorConnected
                ? rotatorInfo.MechanicalPosition + options.RotationDegrees
                : options.RotationDegrees;
```

Then change the request field (line ~678) from `RotationDegrees = options.RotationDegrees,` to:

```csharp
                RotationDegrees = rotationDegrees,
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo --filter "FullyQualifiedName~HocusFocusSimulatorCameraTests"`
Expected: PASS (both new tests + all pre-existing camera tests).

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/HocusFocusSimulatorCamera.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/HocusFocusSimulatorCameraTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(sim): render at a connected rotator's mechanical angle (offset by Field Rotation)"
```

---

## Task 8: Field Rotation tooltip + full-suite gate

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml`

- [ ] **Step 1: Update the tooltip wording**

In `Resources/OptionsDataTemplates.xaml`, replace the `CamSim_RotationDegrees_Tooltip` resource
(line 2937) with:

```xml
    <TextBlock x:Key="CamSim_RotationDegrees_Tooltip" Text="Field rotation (degrees) applied to the projected star field. When a rotator is connected, this value is added to the rotator's mechanical angle as an offset; with no rotator it sets the field rotation directly." />
```

- [ ] **Step 2: Verify XAML resources resolve**

Run: `dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo --filter "FullyQualifiedName~XamlResourceResolutionTests"`
Expected: PASS.

- [ ] **Step 3: Run the FULL suite (end of Feature 2)**

Run: `dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo`
Expected: PASS (all tests). Fix any failure at its cause.

- [ ] **Step 4: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "docs(sim): note rotator-offset behavior in the Field Rotation tooltip"
```

- [ ] **Step 5: Manual/`/verify` check (recommended)**

In NINA, connect the Hocus Focus Simulator camera with a mount + focuser + a rotator (NINA's own
simulator rotator works). Take an exposure, rotate the rotator (e.g. to 90° mechanical), and take
another: the star field visibly rotates with the mechanical angle. Confirm the field turns the
**correct way** as the mechanical angle increases; if reversed, negate `rotatorInfo.MechanicalPosition`
in `BuildRenderRequest` (Task 7, Step 3) and re-run the suite. With no rotator connected, the frame
matches the manual Field Rotation value exactly (pre-feature behavior).

---

# Final wrap-up

- [ ] Run the full suite one last time; confirm green.
- [ ] Push the branch and open a PR to `develop` (never push to `develop` directly):

```bash
git push -u origin ghilios/tilt-guidance-units-and-sim-rotator
gh pr create --base develop --head ghilios/tilt-guidance-units-and-sim-rotator \
  --title "Tilt-guidance turn-units dropdown + camera-sim rotator angle" \
  --body "Implements docs/tilt-guidance-units-and-sim-rotator-design.md. Two independent features: a persisted Turns/Degrees display toggle on the Tilt Adapter Guidance panel (screw adapters only), and driving the Camera Simulator's field rotation from a connected rotator's mechanical angle (offset by the manual Field Rotation).

🤖 Generated with [Claude Code](https://claude.com/claude-code)"
```
