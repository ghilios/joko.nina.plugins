# Frame-Number Picker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a dropdown frame picker between Prev and Next in all three review toolbars; its value tracks the current frame (showing `number — focuser position`) and selecting an entry jumps to that frame.

**Architecture:** A shared `FramePickerItem { Number, Label }` feeds a `ComboBox`. Controls #1 (AutoFocus) and #2 (Inspector) share `FrameReviewVMBase`, so the picker properties (`FramePickerItems`, two-way `SelectedFrameNumber`) live there once; each subclass fills `FramePickerItems` from its snapshot. Control #3 (`StarReviewVM`, a separate hierarchy) gets the same three additions directly. Selection jumps via the existing `LoadCurrent(fit:false)` path (preserving zoom/pan, identical to Prev/Next).

**Tech Stack:** C# / .NET 8.0-windows, WPF, NUnit 4.4.0, CommunityToolkit.Mvvm.

**Branch:** `ghilios/ui-feedback-prerelease` (the session's review-UI branch). **Design:** `docs/frame-number-picker-design.md`.

**Build/test (WSL → Windows):**
- Full test: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
- Filtered test: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~FrameReviewVMBaseTests"`
- Build: `rtk dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
- NOTE: `rtk dotnet build` prints `fail dotnet build:` in its header even on success — trust `errors=0` + exit 0. Set Bash `timeout` to `600000`.

**Commit command pattern (every commit):**
```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "<message>

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 1: `FramePickerItem` + `FrameReviewVMBase` picker (TDD)

**Files:**
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/FramePickerItem.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/FrameReviewVMBase.cs` (CurrentIndex setter ~54-63; insert after PositionLabel ~65)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/Optimization/Review/FrameReviewVMBaseTests.cs`

- [ ] **Step 1: Create the `FramePickerItem` type**

Create `FramePickerItem.cs`:
```csharp
#region "copyright"
/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/
#endregion "copyright"

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review {

    /// <summary>One entry in a review toolbar's frame-number dropdown: the 1-based frame <see cref="Number"/> (the
    /// ComboBox selection value, tracking the current frame) and a human <see cref="Label"/> like "3 — 2745"
    /// (number — focuser position).</summary>
    public sealed class FramePickerItem {
        public FramePickerItem(int number, string label) {
            Number = number;
            Label = label;
        }

        public int Number { get; }
        public string Label { get; }
    }
}
```

- [ ] **Step 2: Write the failing test**

Create `FrameReviewVMBaseTests.cs`:
```csharp
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.Review;
using NUnit.Framework;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.Optimization.Review {

    [TestFixture]
    public class FrameReviewVMBaseTests {

        // Minimal concrete subclass: records LoadFrame calls so jumps are observable without image/UI work.
        private sealed class TestReviewVM : FrameReviewVMBase<int> {
            public int LoadedIndex { get; private set; } = -1;
            public int LoadCount { get; private set; }
            public TestReviewVM(int frameCount) : base(frameCount) { }
            protected override void LoadFrame(int index) { LoadedIndex = index; LoadCount++; }
            protected override IReadOnlyList<StarReviewLegendEntry> BuildLegend() => new List<StarReviewLegendEntry>();
        }

        [Test]
        public void SelectedFrameNumber_Get_IsOneBasedCurrentIndex() {
            var vm = new TestReviewVM(5);
            Assert.That(vm.SelectedFrameNumber, Is.EqualTo(1));   // CurrentIndex defaults to 0
        }

        [Test]
        public void SelectedFrameNumber_Set_JumpsAndLoadsThatFrame() {
            var vm = new TestReviewVM(5);
            vm.SelectedFrameNumber = 3;                            // 1-based -> index 2
            Assert.Multiple(() => {
                Assert.That(vm.CurrentIndex, Is.EqualTo(2));
                Assert.That(vm.LoadedIndex, Is.EqualTo(2));
                Assert.That(vm.SelectedFrameNumber, Is.EqualTo(3));
            });
        }

        [TestCase(0)]    // index -1
        [TestCase(6)]    // index 5 == FrameCount (out of range)
        [TestCase(99)]
        public void SelectedFrameNumber_Set_OutOfRange_IsIgnored(int oneBasedValue) {
            var vm = new TestReviewVM(5);
            vm.SelectedFrameNumber = 3;                            // move to index 2 first
            var loadsBefore = vm.LoadCount;
            vm.SelectedFrameNumber = oneBasedValue;
            Assert.Multiple(() => {
                Assert.That(vm.CurrentIndex, Is.EqualTo(2));        // unchanged
                Assert.That(vm.LoadCount, Is.EqualTo(loadsBefore)); // no reload
            });
        }

        [Test]
        public void SelectedFrameNumber_Set_SameValue_DoesNotReload() {
            var vm = new TestReviewVM(5);
            vm.SelectedFrameNumber = 3;
            var loadsBefore = vm.LoadCount;
            vm.SelectedFrameNumber = 3;                            // no-op
            Assert.That(vm.LoadCount, Is.EqualTo(loadsBefore));
        }
    }
}
```

- [ ] **Step 3: Run the test to verify it fails (compile error — `SelectedFrameNumber` missing)**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~FrameReviewVMBaseTests"`
Expected: build failure — `FrameReviewVMBase` has no `SelectedFrameNumber`.

- [ ] **Step 4: Add the picker properties to `FrameReviewVMBase`**

In `FrameReviewVMBase.cs`, replace this block (the `CurrentIndex` property through the `PositionLabel` line, ~54-65):
```csharp
        private int currentIndex;
        public int CurrentIndex {
            get => currentIndex;
            protected set {
                if (currentIndex != value) {
                    currentIndex = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(PositionLabel));
                }
            }
        }

        public string PositionLabel => FrameCount > 0 ? $"{CurrentIndex + 1} / {FrameCount}" : "0 / 0";
```
with:
```csharp
        private int currentIndex;
        public int CurrentIndex {
            get => currentIndex;
            protected set {
                if (currentIndex != value) {
                    currentIndex = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(PositionLabel));
                    RaisePropertyChanged(nameof(SelectedFrameNumber));
                }
            }
        }

        public string PositionLabel => FrameCount > 0 ? $"{CurrentIndex + 1} / {FrameCount}" : "0 / 0";

        /// <summary>Toolbar frame-picker entries (1-based number + focuser-position label). Populated by the subclass,
        /// the only place with the per-frame focuser positions.</summary>
        public IReadOnlyList<FramePickerItem> FramePickerItems { get; protected set; }

        /// <summary>Two-way bound by the toolbar frame picker (1-based, matching <see cref="PositionLabel"/>). Selecting
        /// a frame jumps to it exactly as Prev/Next do — set the index, then LoadCurrent(fit:false) so zoom/pan is
        /// preserved. Out-of-range and no-op selections are ignored.</summary>
        public int SelectedFrameNumber {
            get => CurrentIndex + 1;
            set {
                var index = value - 1;
                if (index >= 0 && index < FrameCount && index != CurrentIndex) {
                    CurrentIndex = index;
                    LoadCurrent(fit: false);
                }
            }
        }
```
(`IReadOnlyList<>` is already in scope — `using System.Collections.Generic;` at line 13. `FramePickerItem` is in this same namespace.)

- [ ] **Step 5: Run the test to verify it passes**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~FrameReviewVMBaseTests"`
Expected: all `FrameReviewVMBaseTests` pass (6 cases).

- [ ] **Step 6: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/FramePickerItem.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/FrameReviewVMBase.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/Optimization/Review/FrameReviewVMBaseTests.cs
```
then commit with the standard command, message:
`feat(review-ui): add frame-picker model to FrameReviewVMBase (SelectedFrameNumber + items)`

---

## Task 2: Build the picker items in the AutoFocus + Inspector VMs

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/Review/AutoFocusFrameReviewVM.cs` (ctor ~101)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/FrameReviewVM.cs` (usings ~19; ctor ~118)

No new test — covered by Task 1's base test + the final manual smoke. These VMs build images and aren't headless-unit-friendly.

- [ ] **Step 1: AutoFocus VM — populate `FramePickerItems` from the snapshot**

In `AutoFocusFrameReviewVM.cs`, replace:
```csharp
            this.snapshot = snapshot;
            this.annotatorOptions = annotatorOptions ?? throw new ArgumentNullException(nameof(annotatorOptions));
```
with:
```csharp
            this.snapshot = snapshot;
            FramePickerItems = snapshot.Frames
                .Select((f, i) => new FramePickerItem(i + 1, $"{i + 1} — {f.FocuserPosition:0}"))
                .ToList();
            this.annotatorOptions = annotatorOptions ?? throw new ArgumentNullException(nameof(annotatorOptions));
```
(`System.Linq` is already imported — used by `.Any()/.Where()/.ToList()` below. `FramePickerItem` resolves via the base's namespace, already imported since the class derives from `FrameReviewVMBase<…>`.)

- [ ] **Step 2: Inspector VM — add `using System.Linq;`**

In `FrameReviewVM.cs`, replace:
```csharp
using System;
using System.Collections.Generic;
using System.Windows.Media;
```
with:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
```

- [ ] **Step 3: Inspector VM — populate `FramePickerItems` from the snapshot**

In `FrameReviewVM.cs`, replace:
```csharp
            this.snapshot = snapshot;
            RebuildLegend();
```
with:
```csharp
            this.snapshot = snapshot;
            FramePickerItems = snapshot.Frames
                .Select((f, i) => new FramePickerItem(i + 1, $"{i + 1} — {f.FocuserPosition:0}"))
                .ToList();
            RebuildLegend();
```

- [ ] **Step 4: Build**

Run: `rtk dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
Expected: `errors=0`, exit 0.

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/Review/AutoFocusFrameReviewVM.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/FrameReviewVM.cs
```
Message: `feat(review-ui): build frame-picker items in AutoFocus + Inspector review VMs`

---

## Task 3: `StarReviewVM` (standalone) picker properties

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/StarReviewVM.cs` (ctor ~208; CurrentIndex setter ~230-243)

- [ ] **Step 1: Populate `FramePickerItems` in the ctor**

In `StarReviewVM.cs`, replace:
```csharp
            this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
            this.labelsByRun = labelsByRun ?? throw new ArgumentNullException(nameof(labelsByRun));
```
with:
```csharp
            this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
            FramePickerItems = queue
                .Select((f, i) => new FramePickerItem(i + 1, $"{i + 1} — {f.FocuserPosition}"))
                .ToList();
            this.labelsByRun = labelsByRun ?? throw new ArgumentNullException(nameof(labelsByRun));
```
(`System.Linq` is already imported at line 18. `FrameReview.FocuserPosition` is an `int`, so no number format. `FramePickerItem` is in this same namespace.)

- [ ] **Step 2: Add the picker properties + sync the picker when CurrentIndex changes**

In `StarReviewVM.cs`, replace:
```csharp
        private int currentIndex;
        public int CurrentIndex {
            get => currentIndex;
            private set {
                if (currentIndex != value) {
                    currentIndex = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(PositionLabel));
                }
            }
        }

        public int QueueCount => queue.Count;

        public string PositionLabel => $"{CurrentIndex + 1} / {queue.Count}";
```
with:
```csharp
        private int currentIndex;
        public int CurrentIndex {
            get => currentIndex;
            private set {
                if (currentIndex != value) {
                    currentIndex = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(PositionLabel));
                    RaisePropertyChanged(nameof(SelectedFrameNumber));
                }
            }
        }

        public int QueueCount => queue.Count;

        public string PositionLabel => $"{CurrentIndex + 1} / {queue.Count}";

        /// <summary>Toolbar frame-picker entries (1-based number + focuser-position label).</summary>
        public IReadOnlyList<FramePickerItem> FramePickerItems { get; }

        /// <summary>Two-way bound by the toolbar frame picker (1-based, matching <see cref="PositionLabel"/>). Selecting
        /// a frame jumps to it the same way Prev/Next do (set index, then LoadCurrent(fitView:false) to preserve
        /// zoom/pan). Out-of-range and no-op selections are ignored.</summary>
        public int SelectedFrameNumber {
            get => CurrentIndex + 1;
            set {
                var index = value - 1;
                if (index >= 0 && index < queue.Count && index != CurrentIndex) {
                    CurrentIndex = index;
                    LoadCurrent(fitView: false);
                }
            }
        }
```

- [ ] **Step 3: Build**

Run: `rtk dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
Expected: `errors=0`, exit 0.

- [ ] **Step 4: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/StarReviewVM.cs
```
Message: `feat(review-ui): add frame-picker model to StarReviewVM`

---

## Task 4: Add the ComboBox to the three toolbars

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/Review/AutoFocusFrameReviewControl.xaml` (toolbar ~127-128)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/FrameReviewControl.xaml` (toolbar ~131-132)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/StarReviewControl.xaml` (toolbar ~121-122)

All three bind `ItemsSource={Binding FramePickerItems}`, `SelectedValuePath="Number"`, `SelectedValue={Binding SelectedFrameNumber, Mode=TwoWay}`, with an explicit black-text `ItemTemplate` (the implicit white `TextBlock` style in these controls would otherwise render the items white-on-light).

- [ ] **Step 1: AutoFocus toolbar — ComboBox between Prev and Next (reuse `HF_LightCombo`)**

In `AutoFocusFrameReviewControl.xaml`, replace:
```xml
            <Button DockPanel.Dock="Left" Content="◀ Prev" Command="{Binding PrevCommand}" />
            <Button DockPanel.Dock="Left" Content="Next ▶" Command="{Binding NextCommand}" />
```
with:
```xml
            <Button DockPanel.Dock="Left" Content="◀ Prev" Command="{Binding PrevCommand}" />
            <ComboBox DockPanel.Dock="Left" Margin="4,0" VerticalAlignment="Center" MinWidth="96"
                      Style="{StaticResource HF_LightCombo}"
                      ItemsSource="{Binding FramePickerItems}"
                      SelectedValuePath="Number"
                      SelectedValue="{Binding SelectedFrameNumber, Mode=TwoWay}">
                <ComboBox.ItemTemplate>
                    <DataTemplate>
                        <TextBlock Foreground="Black" Text="{Binding Label}" />
                    </DataTemplate>
                </ComboBox.ItemTemplate>
            </ComboBox>
            <Button DockPanel.Dock="Left" Content="Next ▶" Command="{Binding NextCommand}" />
```

- [ ] **Step 2: Inspector toolbar — ComboBox between Prev and Next**

In `FrameReviewControl.xaml`, replace:
```xml
            <Button DockPanel.Dock="Left" Content="◀ Prev" Command="{Binding PrevCommand}" />
            <Button DockPanel.Dock="Left" Content="Next ▶" Command="{Binding NextCommand}" />
```
with:
```xml
            <Button DockPanel.Dock="Left" Content="◀ Prev" Command="{Binding PrevCommand}" />
            <ComboBox DockPanel.Dock="Left" Margin="4,0" VerticalAlignment="Center" MinWidth="96"
                      ItemsSource="{Binding FramePickerItems}"
                      SelectedValuePath="Number"
                      SelectedValue="{Binding SelectedFrameNumber, Mode=TwoWay}">
                <ComboBox.ItemTemplate>
                    <DataTemplate>
                        <TextBlock Foreground="Black" Text="{Binding Label}" />
                    </DataTemplate>
                </ComboBox.ItemTemplate>
            </ComboBox>
            <Button DockPanel.Dock="Left" Content="Next ▶" Command="{Binding NextCommand}" />
```
(If you discover `FrameReviewControl.xaml` already defines an `HF_LightCombo` style in its resources, add `Style="{StaticResource HF_LightCombo}"` to match Step 1; otherwise leave it plain.)

- [ ] **Step 3: Star Detection review toolbar — ComboBox between Prev and Next (WrapPanel, no Dock)**

In `StarReviewControl.xaml`, replace:
```xml
            <Button Content="◀ Prev" Command="{Binding PrevCommand}" />
            <Button Content="Next ▶" Command="{Binding NextCommand}" />
```
with:
```xml
            <Button Content="◀ Prev" Command="{Binding PrevCommand}" />
            <ComboBox Margin="4,0" VerticalAlignment="Center" MinWidth="96"
                      ItemsSource="{Binding FramePickerItems}"
                      SelectedValuePath="Number"
                      SelectedValue="{Binding SelectedFrameNumber, Mode=TwoWay}">
                <ComboBox.ItemTemplate>
                    <DataTemplate>
                        <TextBlock Foreground="Black" Text="{Binding Label}" />
                    </DataTemplate>
                </ComboBox.ItemTemplate>
            </ComboBox>
            <Button Content="Next ▶" Command="{Binding NextCommand}" />
```

- [ ] **Step 4: Build**

Run: `rtk dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
Expected: `errors=0`, exit 0 (XAML errors surface as build errors).

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/Review/AutoFocusFrameReviewControl.xaml \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/FrameReviewControl.xaml \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/StarReviewControl.xaml
```
Message: `feat(review-ui): frame-number dropdown between Prev and Next in all three review toolbars`

---

## Task 5: Final verification

- [ ] **Step 1: Full build + test**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
Expected: `errors=0`, all tests pass (the 1485 prior + the new `FrameReviewVMBaseTests`).

- [ ] **Step 2: Manual smoke (in NINA, recommended)**

In each review window (AutoFocus Review Frames, Inspector Review Frames, Star Detection review): a dropdown sits between ◀ Prev and Next ▶ showing `number — focuser` (e.g. `3 — 2745`); its value tracks the current frame as you use Prev/Next and the ◀/▶ arrow keys; selecting an entry jumps to that frame without re-fitting zoom/pan.

---

## Self-Review

**Spec coverage:**
- Dropdown between Prev and Next in all 3 review UIs → Task 4 (all 3 toolbars). ✓
- Value tracks current frame → `SelectedFrameNumber` get = `CurrentIndex+1`, and the `CurrentIndex` setter raises `SelectedFrameNumber` (Tasks 1 & 3) so it follows Prev/Next/arrow keys. ✓
- Changing it jumps to the selected frame → `SelectedFrameNumber` setter → `CurrentIndex = index; LoadCurrent(fit:false)` (Tasks 1 & 3). ✓
- `number — focuser position` label → Tasks 2 & 3 build `FramePickerItem(i+1, "$"{i+1} — {focuser}")`. ✓
- Project invariant "run the suite" → Tasks 1 & 5. ✓

**Placeholder scan:** every code step shows exact before/after; no TBD/TODO.

**Type consistency:** `FramePickerItem { Number, Label }`, `FramePickerItems` (IReadOnlyList), and `SelectedFrameNumber` (int, 1-based) are referenced identically across the base (Task 1), subclasses (Task 2), `StarReviewVM` (Task 3), and the XAML (`SelectedValuePath="Number"`, `DisplayMemberPath` via `ItemTemplate` on `Label`, `SelectedValue` → `SelectedFrameNumber`). `LoadCurrent` is `fit:` on the base and `fitView:` on `StarReviewVM` — matched to each class's real signature.
