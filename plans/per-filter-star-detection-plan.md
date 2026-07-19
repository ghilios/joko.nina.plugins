# Per-Filter Star Detection Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Opt-in per-filter star detection settings: each filter in the active profile gets its own complete detection settings set, resolved at detection time from the image's capture-time filter name, with a filter selector + copy-from-filter on the options page and a target-filter picker in the Optimization Wizard.

**Architecture:** One JSON blob key in the profile (`PerFilterStarDetectionJson`) stores a `StarDetectionSettingsSnapshot` per filter name plus a `GlobalSeed`; the existing `StarDetectionOptions` singleton becomes the edit buffer for the selected filter (`PerFilterEditBinder` mirrors edits into the store; a write-suppressible accessor decorator freezes the legacy keys while the feature is on). Detection resolves the capture-time filter from image metadata and feeds that filter's snapshot through the existing `optionsOverride` seam; indeterminate filter soft-fails with a zero-star result plus a warning notification, and HF-owned entry points gate up front.

**Tech Stack:** C# / .NET 8 WPF NINA plugin (MEF, CommunityToolkit.Mvvm/BaseINPC, Newtonsoft.Json), NUnit 4.4 + NSubstitute.

**Spec:** `docs/per-filter-star-detection-design.md` (read it before starting; it is the authority on behavior).

---

## Ground rules for every task

- **Test command** (this machine is WSL; dotnet only exists on Windows — always run through `dotnet.exe`, allow up to 10 minutes / 600000 ms):

  ```bash
  dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~<FixtureName>"
  ```

  Full suite: same command without `--filter`. Builds also validate XAML. The plugin csproj PostBuild xcopies into the local NINA plugin folder on every build — harmless.
- **Commit convention** (GitHub email privacy; every commit step in this plan uses this shape):

  ```bash
  git add <files> && GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "<type>(per-filter): <message>"
  ```
- **No csproj edits are ever needed**: the test project references the plugin via `ProjectReference` (only TestApp helpers are source-linked), the plugin declares `InternalsVisibleTo("Joko.NINA.Plugins.HocusFocus.Tests")` (`Properties/AssemblyInfo.cs:18`), and both csprojs are SDK-style with default compile globbing.
- **Task order matters.** Tasks are sequenced so every referenced type already exists: accessor decorator + buffered options (1–2) → snapshot helpers + store (3–4) → binder + plugin bootstrap (5–6) → detection resolution (7–8) → gates (9–11) → options UI (12–13) → engine + wizard (14–17) → export provenance + docs + final full suite (18–19).
- Work on the existing feature branch `ghilios/per-filter-star-detection`.

## File structure

New plugin sources (all compiled automatically by SDK globbing):

- `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Utility/SuppressiblePluginOptionsAccessor.cs` — internal write-suppressible `IPluginOptionsAccessor` decorator with an always-write key allowlist.
- `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/IPerFilterStarDetectionStore.cs` — store interface + `PerFilterSnapshotChangedEventArgs`.
- `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/PerFilter/PerFilterStarDetectionData.cs` — persisted blob DTOs (`PerFilterStarDetectionData`, `PerFilterStarDetectionEntry`).
- `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/PerFilter/PerFilterStarDetectionStore.cs` — the store (Enabled flag, blob persistence, seeding, thread-safe snapshot access).
- `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/PerFilter/PerFilterEditBinder.cs` — edit-buffer binder (`EditedFilterName`, mirroring, enable/disable orchestration).
- `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/PerFilter/PerFilterSettingsUnavailableException.cs` — thrown by params-building when the feature is on and the filter is indeterminate.

Modified plugin sources: `StarDetection/StarDetectionOptions.cs` (accessor wrap, `PersistToProfile`, `ReloadFromProfile`), `AutoFocus/Replay/StarDetectionSettingsSnapshot.cs` (`Clone`, `CopyMachineLocalFrom`), `StarDetection/HocusFocusStarDetection.cs` (per-filter resolution + soft-fail), `HocusFocusPlugin.cs` (store/binder statics, copy command, binding wrappers), `AutoFocus/HocusFocusVM.cs` + `AutoFocus/InspectorVM.cs` + `SequenceItems/RunAberrationInspector.cs` (gates), `StarDetection/StarDetectionOptionsVM.cs` + `Resources/OptionsDataTemplates.xaml` (options UI), `Interfaces/IAutoFocusEngine.cs` + `AutoFocus/AutoFocusEngine.cs` (`UseExactImagingFilter`), `StarDetection/Optimization/StarDetectionOptimizerWizardVM.cs` + `StarDetection/Optimization/RunEvaluationLoader.cs` + `StarDetection/Optimization/DataTemplates.xaml` (wizard), `StarDetection/StarDetectionSettingsExport.cs` + `StarDetection/StarDetectionSettingsIO.cs` (provenance), `documentation/docs/settings/index.md` + `documentation/docs/optimization/index.md` + `.claude/docs/options-system.md` (docs).

Tests: new fixtures `Utility/SuppressiblePluginOptionsAccessorTests.cs`, `StarDetection/StarDetectionOptionsBufferedModeTests.cs`, `StarDetection/PerFilter/*Tests.cs`, plus additions to the existing detection/VM/wizard/engine/export fixtures.

---
## Task Group: Suppressible accessor + StarDetectionOptions buffered persistence

### Task 1: `SuppressiblePluginOptionsAccessor` — write-suppressible `IPluginOptionsAccessor` wrapper

**Files:**
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Utility/SuppressiblePluginOptionsAccessor.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Utility/SuppressiblePluginOptionsAccessorTests.cs`

The test project references the plugin via ProjectReference and the plugin declares `InternalsVisibleTo("Joko.NINA.Plugins.HocusFocus.Tests")` (`Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Properties/AssemblyInfo.cs:18`), so the `internal` class is directly testable and no csproj edits are needed (the plugin csproj is SDK-style with default compile globbing).

- [ ] **Step 1: Write the failing test fixture** — create `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Utility/SuppressiblePluginOptionsAccessorTests.cs` (block-scoped namespace to match sibling Utility fixtures; reuses `InMemoryPluginOptionsAccessor` from `TestDoubles`, whose `WriteCount`/`Snapshot` members give exact write accounting):

```csharp
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility {

    [TestFixture]
    public class SuppressiblePluginOptionsAccessorTests {

        private static (SuppressiblePluginOptionsAccessor accessor, InMemoryPluginOptionsAccessor inner) Build(params string[] alwaysWriteKeys) {
            var inner = new InMemoryPluginOptionsAccessor();
            var accessor = new SuppressiblePluginOptionsAccessor(inner, new HashSet<string>(alwaysWriteKeys, StringComparer.Ordinal));
            return (accessor, inner);
        }

        [Test]
        public void Constructor_NullArguments_Throw() {
            Assert.Throws<ArgumentNullException>(() => new SuppressiblePluginOptionsAccessor(null, new HashSet<string>()));
            Assert.Throws<ArgumentNullException>(() => new SuppressiblePluginOptionsAccessor(new InMemoryPluginOptionsAccessor(), null));
        }

        [Test]
        public void Writes_ForwardByDefault() {
            var (accessor, inner) = Build();
            accessor.SetValueBoolean("b", true);
            accessor.SetValueInt32("i", 42);
            accessor.SetValueDouble("d", 1.5);
            accessor.SetValueString("s", "hello");
            accessor.SetValueEnum("e", DayOfWeek.Friday);
            Assert.Multiple(() => {
                Assert.That(accessor.SuppressWrites, Is.False);
                Assert.That(inner.GetValueBoolean("b", false), Is.True);
                Assert.That(inner.GetValueInt32("i", -1), Is.EqualTo(42));
                Assert.That(inner.GetValueDouble("d", -1.0), Is.EqualTo(1.5));
                Assert.That(inner.GetValueString("s", null), Is.EqualTo("hello"));
                Assert.That(inner.GetValueEnum("e", DayOfWeek.Monday), Is.EqualTo(DayOfWeek.Friday));
            });
        }

        [Test]
        public void SuppressWrites_DropsNonAlwaysKeyWrites() {
            var (accessor, inner) = Build();
            accessor.SetValueInt32("i", 42);

            accessor.SuppressWrites = true;
            accessor.SetValueInt32("i", 99);
            accessor.SetValueBoolean("b", true);

            Assert.Multiple(() => {
                Assert.That(inner.GetValueInt32("i", -1), Is.EqualTo(42));
                Assert.That(inner.Snapshot.ContainsKey("b"), Is.False);
            });
        }

        [Test]
        public void SuppressWrites_AlwaysWriteKeysStillForward() {
            var (accessor, inner) = Build("DetectionDebugMode", "PSFParallelPartitionSize");
            accessor.SuppressWrites = true;

            accessor.SetValueBoolean("DetectionDebugMode", true);
            accessor.SetValueInt32("PSFParallelPartitionSize", 250);
            accessor.SetValueInt32("NoiseReductionRadius", 9);

            Assert.Multiple(() => {
                Assert.That(inner.GetValueBoolean("DetectionDebugMode", false), Is.True);
                Assert.That(inner.GetValueInt32("PSFParallelPartitionSize", -1), Is.EqualTo(250));
                Assert.That(inner.Snapshot.ContainsKey("NoiseReductionRadius"), Is.False);
            });
        }

        [Test]
        public void SuppressWrites_ReadsStillForward() {
            var (accessor, inner) = Build();
            inner.SetValueInt32("i", 42);
            inner.SetValueString("s", "x");

            accessor.SuppressWrites = true;

            Assert.Multiple(() => {
                Assert.That(accessor.GetValueInt32("i", -1), Is.EqualTo(42));
                Assert.That(accessor.GetValueString("s", null), Is.EqualTo("x"));
                Assert.That(accessor.GetValueBoolean("missing", true), Is.True);
            });
        }

        [Test]
        public void SuppressWrites_ToggledBackOff_ResumesWrites() {
            var (accessor, inner) = Build();
            accessor.SuppressWrites = true;
            accessor.SetValueInt32("i", 99);
            accessor.SuppressWrites = false;
            accessor.SetValueInt32("i", 7);
            Assert.That(inner.GetValueInt32("i", -1), Is.EqualTo(7));
        }

        // Exercises every typed setter of the IPluginOptionsAccessor surface exactly once (18 total).
        private static void InvokeEveryTypedSetter(SuppressiblePluginOptionsAccessor accessor) {
            accessor.SetValueColor("k", Colors.Red);
            accessor.SetValueEnum("k", DayOfWeek.Monday);
            accessor.SetValueBoolean("k", true);
            accessor.SetValueByte("k", (byte)1);
            accessor.SetValueSByte("k", (sbyte)-1);
            accessor.SetValueChar("k", 'x');
            accessor.SetValueDecimal("k", 1m);
            accessor.SetValueDouble("k", 1.0);
            accessor.SetValueSingle("k", 1f);
            accessor.SetValueInt32("k", 1);
            accessor.SetValueUInt32("k", 1u);
            accessor.SetValueInt64("k", 1L);
            accessor.SetValueUInt64("k", 1ul);
            accessor.SetValueInt16("k", (short)1);
            accessor.SetValueUInt16("k", (ushort)1);
            accessor.SetValueString("k", "v");
            accessor.SetValueDateTime("k", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            accessor.SetValueGuid("k", Guid.NewGuid());
        }

        [Test]
        public void EveryTypedSetter_SuppressedWhenSuppressWritesOn() {
            var (accessor, inner) = Build();
            accessor.SuppressWrites = true;
            InvokeEveryTypedSetter(accessor);
            Assert.That(inner.WriteCount, Is.EqualTo(0));
        }

        [Test]
        public void EveryTypedSetter_ForwardsWhenSuppressWritesOff() {
            var (accessor, inner) = Build();
            InvokeEveryTypedSetter(accessor);
            Assert.That(inner.WriteCount, Is.EqualTo(18));
        }
    }
}
```

- [ ] **Step 2: Run the fixture and confirm it FAILS** — from repo root:

```
dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~SuppressiblePluginOptionsAccessorTests"
```

Expected failure: the test project fails to **build** with `error CS0246: The type or namespace name 'SuppressiblePluginOptionsAccessor' could not be found` (the class does not exist yet).

- [ ] **Step 3: Implement the wrapper** — create `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Utility/SuppressiblePluginOptionsAccessor.cs`. Reads always forward; each setter forwards only when writes are unsuppressed or the key is machine-local. Full `IPluginOptionsAccessor` surface (`NINA.Profile.Interfaces`, `Color` = `System.Windows.Media.Color`):

```csharp
#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    /// <summary>
    /// Wraps an <see cref="IPluginOptionsAccessor"/> so profile writes can be suspended (per-filter buffered
    /// edit mode) while designated machine-local keys keep writing through. Reads always forward to the inner
    /// accessor.
    /// </summary>
    internal sealed class SuppressiblePluginOptionsAccessor : IPluginOptionsAccessor {
        private readonly IPluginOptionsAccessor inner;
        private readonly ISet<string> alwaysWriteKeys;

        public SuppressiblePluginOptionsAccessor(IPluginOptionsAccessor inner, ISet<string> alwaysWriteKeys) {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.alwaysWriteKeys = alwaysWriteKeys ?? throw new ArgumentNullException(nameof(alwaysWriteKeys));
        }

        public bool SuppressWrites { get; set; }

        private bool ShouldWrite(string name) => !SuppressWrites || alwaysWriteKeys.Contains(name);

        public Color GetValueColor(string name, Color defaultValue) => inner.GetValueColor(name, defaultValue);

        public void SetValueColor(string name, Color value) {
            if (ShouldWrite(name)) {
                inner.SetValueColor(name, value);
            }
        }

        public T GetValueEnum<T>(string name, T defaultValue) where T : struct, Enum => inner.GetValueEnum(name, defaultValue);

        public void SetValueEnum<T>(string name, T value) where T : struct, Enum {
            if (ShouldWrite(name)) {
                inner.SetValueEnum(name, value);
            }
        }

        public bool GetValueBoolean(string name, bool defaultValue) => inner.GetValueBoolean(name, defaultValue);

        public void SetValueBoolean(string name, bool value) {
            if (ShouldWrite(name)) {
                inner.SetValueBoolean(name, value);
            }
        }

        public byte GetValueByte(string name, byte defaultValue) => inner.GetValueByte(name, defaultValue);

        public void SetValueByte(string name, byte value) {
            if (ShouldWrite(name)) {
                inner.SetValueByte(name, value);
            }
        }

        public sbyte GetValueSByte(string name, sbyte defaultValue) => inner.GetValueSByte(name, defaultValue);

        public void SetValueSByte(string name, sbyte value) {
            if (ShouldWrite(name)) {
                inner.SetValueSByte(name, value);
            }
        }

        public char GetValueChar(string name, char defaultValue) => inner.GetValueChar(name, defaultValue);

        public void SetValueChar(string name, char value) {
            if (ShouldWrite(name)) {
                inner.SetValueChar(name, value);
            }
        }

        public decimal GetValueDecimal(string name, decimal defaultValue) => inner.GetValueDecimal(name, defaultValue);

        public void SetValueDecimal(string name, decimal value) {
            if (ShouldWrite(name)) {
                inner.SetValueDecimal(name, value);
            }
        }

        public double GetValueDouble(string name, double defaultValue) => inner.GetValueDouble(name, defaultValue);

        public void SetValueDouble(string name, double value) {
            if (ShouldWrite(name)) {
                inner.SetValueDouble(name, value);
            }
        }

        public float GetValueSingle(string name, float defaultValue) => inner.GetValueSingle(name, defaultValue);

        public void SetValueSingle(string name, float value) {
            if (ShouldWrite(name)) {
                inner.SetValueSingle(name, value);
            }
        }

        public int GetValueInt32(string name, int defaultValue) => inner.GetValueInt32(name, defaultValue);

        public void SetValueInt32(string name, int value) {
            if (ShouldWrite(name)) {
                inner.SetValueInt32(name, value);
            }
        }

        public uint GetValueUInt32(string name, uint defaultValue) => inner.GetValueUInt32(name, defaultValue);

        public void SetValueUInt32(string name, uint value) {
            if (ShouldWrite(name)) {
                inner.SetValueUInt32(name, value);
            }
        }

        public long GetValueInt64(string name, long defaultValue) => inner.GetValueInt64(name, defaultValue);

        public void SetValueInt64(string name, long value) {
            if (ShouldWrite(name)) {
                inner.SetValueInt64(name, value);
            }
        }

        public ulong GetValueUInt64(string name, ulong defaultValue) => inner.GetValueUInt64(name, defaultValue);

        public void SetValueUInt64(string name, ulong value) {
            if (ShouldWrite(name)) {
                inner.SetValueUInt64(name, value);
            }
        }

        public short GetValueInt16(string name, short defaultValue) => inner.GetValueInt16(name, defaultValue);

        public void SetValueInt16(string name, short value) {
            if (ShouldWrite(name)) {
                inner.SetValueInt16(name, value);
            }
        }

        public ushort GetValueUInt16(string name, ushort defaultValue) => inner.GetValueUInt16(name, defaultValue);

        public void SetValueUInt16(string name, ushort value) {
            if (ShouldWrite(name)) {
                inner.SetValueUInt16(name, value);
            }
        }

        public string GetValueString(string name, string defaultValue) => inner.GetValueString(name, defaultValue);

        public void SetValueString(string name, string value) {
            if (ShouldWrite(name)) {
                inner.SetValueString(name, value);
            }
        }

        public DateTime GetValueDateTime(string name, DateTime defaultValue) => inner.GetValueDateTime(name, defaultValue);

        public void SetValueDateTime(string name, DateTime value) {
            if (ShouldWrite(name)) {
                inner.SetValueDateTime(name, value);
            }
        }

        public Guid GetValueGuid(string name, Guid defaultValue) => inner.GetValueGuid(name, defaultValue);

        public void SetValueGuid(string name, Guid value) {
            if (ShouldWrite(name)) {
                inner.SetValueGuid(name, value);
            }
        }
    }
}
```

- [ ] **Step 4: Run the fixture and confirm PASS** —

```
dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~SuppressiblePluginOptionsAccessorTests"
```

Expected: 8 tests, all pass.

- [ ] **Step 5: Run the full suite, then commit** — full suite (project invariant; allow a long timeout, ~10 min):

```
dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo
```

Expected: all green. Then commit:

```
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Utility/SuppressiblePluginOptionsAccessor.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Utility/SuppressiblePluginOptionsAccessorTests.cs && GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(per-filter): add SuppressiblePluginOptionsAccessor for buffered profile writes"
```

---

### Task 2: `StarDetectionOptions` buffered-persistence mode (`PersistToProfile` / `ReloadFromProfile` / `MachineLocalKeys`)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionOptions.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectionOptionsBufferedModeTests.cs`

Key facts driving the tests (verified against `StarDetectionOptions.cs`): construction on an empty store writes exactly two keys — `nameof(IntermediateSavePath)` (empty-path fallback, `InitializeOptions` line ~244) and `"NoiseReductionRadius"`=4 (the `DerivePresetSettings` hotpixel-thresholding `+= 1` compensation, lines ~171-174) — so tests must read persisted-before values instead of assuming an empty store. `SaveIntermediateImages` is never persisted (its setter, line ~993, writes no accessor key), so it is deliberately absent from `MachineLocalKeys`. The machine-local persisted keys are `"DetectionDebugMode"` (note: differs from the `DebugMode` property name), `nameof(IntermediateSavePath)`, and `"PSFParallelPartitionSize"`.

- [ ] **Step 1: Write the failing test fixture** — create `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectionOptionsBufferedModeTests.cs` (file-scoped namespace matching `StarDetectionOptionsTests.cs`; `MakeSnapshot` values copied from that fixture's proven-valid set):

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection;

[TestFixture]
public class StarDetectionOptionsBufferedModeTests {

    private static (StarDetectionOptions options, InMemoryPluginOptionsAccessor store) Build() {
        var profile = Substitute.For<IProfileService>();
        var store = new InMemoryPluginOptionsAccessor();
        var options = new StarDetectionOptions(profile, store);
        return (options, store);
    }

    private static OptimizedStarDetectionSettings MakeSnapshot() {
        // Distinctive curated values, all within each property's valid range.
        return new OptimizedStarDetectionSettings {
            BrightnessSensitivity = 3.3,
            StarClippingMultiplier = 2.7,
            NoiseClippingMultiplier = 5.5,
            StarPeakResponse = 0.66,
            MaxDistortion = 0.42,
            MinHFR = 1.1,
            StarCenterTolerance = 0.45,
            StructureLayers = 7,
            NoiseReductionRadius = 6,
            MinStarBoundingBoxSize = 8,
            HotpixelThresholdingEnabled = true,
            HotpixelThreshold = 0.02
        };
    }

    [Test]
    public void PersistToProfile_DefaultsTrue() {
        var (options, _) = Build();
        Assert.That(options.PersistToProfile, Is.True);
    }

    [Test]
    public void Suppressed_NormalSetter_UpdatesFieldRaisesButFreezesLegacyKey() {
        var (options, store) = Build();
        options.UseAdvanced = true;
        options.NoiseReductionRadius = 7; // persisted pre-suppression

        options.PersistToProfile = false;
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        options.NoiseReductionRadius = 9;

        Assert.Multiple(() => {
            Assert.That(options.NoiseReductionRadius, Is.EqualTo(9));
            Assert.That(raised, Does.Contain(nameof(StarDetectionOptions.NoiseReductionRadius)));
            Assert.That(store.GetValueInt32("NoiseReductionRadius", -1), Is.EqualTo(7));
        });
    }

    [Test]
    public void Suppressed_MachineLocalKeysStillWriteThrough() {
        var (options, store) = Build();
        options.UseAdvanced = true;
        options.PersistToProfile = false;

        var path = Path.Combine(Path.GetTempPath(), "hf-buffered-mode-test");
        options.DebugMode = true;
        options.PSFParallelPartitionSize = 250;
        options.IntermediateSavePath = path;

        Assert.Multiple(() => {
            Assert.That(store.GetValueBoolean("DetectionDebugMode", false), Is.True);
            Assert.That(store.GetValueInt32("PSFParallelPartitionSize", -1), Is.EqualTo(250));
            Assert.That(store.GetValueString(nameof(StarDetectionOptions.IntermediateSavePath), null), Is.EqualTo(path));
        });
    }

    [Test]
    public void Suppressed_ApplyAndClearOptimizedSettings_SkipLegacyKeys() {
        var (options, store) = Build();
        options.PersistToProfile = false;

        options.ApplyOptimizedSettings(MakeSnapshot());
        Assert.Multiple(() => {
            Assert.That(options.HasOptimizedSettings, Is.True);
            Assert.That(options.UseOptimizedSettings, Is.True);
            Assert.That(store.Snapshot.ContainsKey("OptimizedSettingsJson"), Is.False);
            Assert.That(store.Snapshot.ContainsKey(nameof(StarDetectionOptions.UseOptimizedSettings)), Is.False);
        });

        options.ClearOptimizedSettings();
        Assert.Multiple(() => {
            Assert.That(options.HasOptimizedSettings, Is.False);
            Assert.That(store.Snapshot.ContainsKey("OptimizedSettingsJson"), Is.False);
        });
    }

    [Test]
    public void Suppressed_ResetDefaults_SkipsLegacyKeys() {
        var (options, store) = Build();
        options.UseAdvanced = true;
        options.NoiseReductionRadius = 7;

        options.PersistToProfile = false;
        options.ResetDefaults();

        Assert.Multiple(() => {
            Assert.That(options.UseAdvanced, Is.False);
            Assert.That(options.NoiseReductionRadius, Is.EqualTo(3));
            Assert.That(store.GetValueBoolean("UseAdvanced", false), Is.True);
            Assert.That(store.GetValueInt32("NoiseReductionRadius", -1), Is.EqualTo(7));
            Assert.That(store.Snapshot.ContainsKey("OptimizedSettingsJson"), Is.False);
        });
    }

    [Test]
    public void ReenableAndReload_RestoresPreSuppressionPersistedValues() {
        var (options, store) = Build();
        options.UseAdvanced = true;
        options.NoiseReductionRadius = 7;
        options.MinHFR = 2.5;

        options.PersistToProfile = false;
        options.NoiseReductionRadius = 9;
        options.MinHFR = 0.8;
        options.ApplyOptimizedSettings(MakeSnapshot());

        options.PersistToProfile = true;
        options.ReloadFromProfile();

        Assert.Multiple(() => {
            Assert.That(options.UseAdvanced, Is.True);
            Assert.That(options.NoiseReductionRadius, Is.EqualTo(7));
            Assert.That(options.MinHFR, Is.EqualTo(2.5));
            Assert.That(options.HasOptimizedSettings, Is.False);
            Assert.That(options.UseOptimizedSettings, Is.False);
        });
    }

    [Test]
    public void Suppressed_SimpleModeDerivation_StillFires() {
        var (options, store) = Build();
        var persistedRadius = store.GetValueInt32("NoiseReductionRadius", -1);

        options.PersistToProfile = false;
        options.Simple_NoiseLevel = NoiseLevelEnum.High;

        Assert.Multiple(() => {
            Assert.That(options.StarMeasurementNoiseReductionEnabled, Is.True);
            Assert.That(options.NoiseReductionRadius, Is.GreaterThanOrEqualTo(5));
            Assert.That(store.GetValueInt32("NoiseReductionRadius", -1), Is.EqualTo(persistedRadius));
            Assert.That(store.Snapshot.ContainsKey("Simple_NoiseLevel"), Is.False);
            Assert.That(store.Snapshot.ContainsKey(nameof(StarDetectionOptions.StarMeasurementNoiseReductionEnabled)), Is.False);
        });
    }
}
```

- [ ] **Step 2: Run the fixture and confirm it FAILS** —

```
dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~StarDetectionOptionsBufferedModeTests"
```

Expected failure: test project build error `error CS1061: 'StarDetectionOptions' does not contain a definition for 'PersistToProfile'` (and the same for `ReloadFromProfile`).

- [ ] **Step 3: Wrap the accessor in `StarDetectionOptions`** — three edits to `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionOptions.cs`.

Edit 3a — add the `Utility` using (anchor: the using block at the top of the file, lines 13-14):

```csharp
// OLD
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;

// NEW
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.Utility;
```

Edit 3b — change the field type and wrap in the internal ctor (anchor: class declaration + ctors, lines 27-39; the public ctor stays as-is, the internal ctor signature is unchanged so TestApp call sites keep compiling):

```csharp
// OLD
    public class StarDetectionOptions : BaseINPC, IStarDetectionOptions {
        private readonly IPluginOptionsAccessor optionsAccessor;

        public StarDetectionOptions(IProfileService profileService)
            : this(profileService, CreateDefaultAccessor(profileService)) {
        }

        internal StarDetectionOptions(IProfileService profileService, IPluginOptionsAccessor optionsAccessor) {
            this.optionsAccessor = optionsAccessor ?? throw new ArgumentNullException(nameof(optionsAccessor));

// NEW
    public class StarDetectionOptions : BaseINPC, IStarDetectionOptions {
        private readonly SuppressiblePluginOptionsAccessor optionsAccessor;

        public StarDetectionOptions(IProfileService profileService)
            : this(profileService, CreateDefaultAccessor(profileService)) {
        }

        internal StarDetectionOptions(IProfileService profileService, IPluginOptionsAccessor optionsAccessor) {
            if (optionsAccessor == null) {
                throw new ArgumentNullException(nameof(optionsAccessor));
            }
            this.optionsAccessor = new SuppressiblePluginOptionsAccessor(optionsAccessor, MachineLocalKeys);
```

Edit 3c — add `MachineLocalKeys`, `PersistToProfile`, and `ReloadFromProfile` (anchor: immediately after the closing brace of `CreateDefaultAccessor`, lines 41-47). Every other member compiles unchanged because `SuppressiblePluginOptionsAccessor` implements `IPluginOptionsAccessor`:

```csharp
// OLD
        private static IPluginOptionsAccessor CreateDefaultAccessor(IProfileService profileService) {
            var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(StarDetectionOptions));
            if (guid == null) {
                throw new Exception($"Guid not found in assembly metadata");
            }
            return new PluginOptionsAccessor(profileService, guid.Value);
        }

// NEW
        private static IPluginOptionsAccessor CreateDefaultAccessor(IProfileService profileService) {
            var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(StarDetectionOptions));
            if (guid == null) {
                throw new Exception($"Guid not found in assembly metadata");
            }
            return new PluginOptionsAccessor(profileService, guid.Value);
        }

        // Machine-local persisted keys stay global in per-filter mode — they keep writing through even while
        // buffered edits suppress the legacy profile keys. (SaveIntermediateImages is never persisted.)
        internal static readonly ISet<string> MachineLocalKeys = new HashSet<string> {
            "DetectionDebugMode",
            nameof(IntermediateSavePath),
            "PSFParallelPartitionSize",
        };

        // Per-filter "buffered" edit mode: while false, the legacy profile keys are frozen (fields and
        // PropertyChanged behave normally; machine-local keys still write through).
        public bool PersistToProfile {
            get => !optionsAccessor.SuppressWrites;
            set => optionsAccessor.SuppressWrites = !value;
        }

        internal void ReloadFromProfile() {
            InitializeOptions();
            RaiseAllPropertiesChanged();
        }
```

- [ ] **Step 4: Run the new fixture and confirm PASS** —

```
dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~StarDetectionOptionsBufferedModeTests"
```

Expected: 7 tests, all pass.

- [ ] **Step 5: Run the existing options fixture as a regression check** — the wrap must be behavior-neutral while `PersistToProfile` is true (including `Constructor_ThrowsOnNullAccessor`, persistence round-trips, Simple-mode derivation):

```
dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~StarDetectionOptionsTests"
```

Expected: all pass, unchanged.

- [ ] **Step 6: Run the full suite, then commit** —

```
dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo
```

Expected: all green. Then commit:

```
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionOptions.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectionOptionsBufferedModeTests.cs && GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(per-filter): buffered persistence mode on StarDetectionOptions"
```

### Task 3: `StarDetectionSettingsSnapshot.Clone()` + `CopyMachineLocalFrom()`

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/Replay/StarDetectionSettingsSnapshot.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/Replay/StarDetectionSettingsSnapshotTests.cs`

- [ ] **Step 1: Write failing tests for `Clone()` and `CopyMachineLocalFrom()`**

  In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/Replay/StarDetectionSettingsSnapshotTests.cs`, add one using directive at the top (after `using NINA.Joko.Plugins.HocusFocus.StarDetection;`):

  ```csharp
  using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
  ```

  Then insert these three tests immediately after the existing `BuildStarDetectorParams_FromSnapshot_MatchesOriginalOptions` test (before the class's closing brace):

  ```csharp
          [Test]
          public void Clone_DeepCopiesIncludingOptimizedSettings() {
              var source = new StarDetectionSettingsSnapshot {
                  UseAdvanced = true,
                  BrightnessSensitivity = 7.5,
                  StructureLayers = 6,
                  IntermediateSavePath = @"C:\hf\debug",
                  OptimizedSettings = new OptimizedStarDetectionSettings { BrightnessSensitivity = 3.3, StructureLayers = 7 }
              };

              var clone = source.Clone();
              // Mutate the source after cloning; the clone must be fully detached (incl. the nested DTO).
              source.BrightnessSensitivity = 99.0;
              source.OptimizedSettings.StructureLayers = 42;

              Assert.Multiple(() => {
                  Assert.That(clone, Is.Not.SameAs(source));
                  Assert.That(clone.UseAdvanced, Is.True);
                  Assert.That(clone.BrightnessSensitivity, Is.EqualTo(7.5));
                  Assert.That(clone.StructureLayers, Is.EqualTo(6));
                  Assert.That(clone.IntermediateSavePath, Is.EqualTo(@"C:\hf\debug"));
                  Assert.That(clone.OptimizedSettings, Is.Not.SameAs(source.OptimizedSettings));
                  Assert.That(clone.OptimizedSettings.BrightnessSensitivity, Is.EqualTo(3.3));
                  Assert.That(clone.OptimizedSettings.StructureLayers, Is.EqualTo(7));
              });
          }

          [Test]
          public void Clone_NullOptimizedSettings_StaysNull() {
              var clone = new StarDetectionSettingsSnapshot().Clone();
              Assert.That(clone.OptimizedSettings, Is.Null);
          }

          [Test]
          public void CopyMachineLocalFrom_CopiesOnlyMachineLocalFields() {
              var target = new StarDetectionSettingsSnapshot {
                  BrightnessSensitivity = 7.5,
                  MinHFR = 1.05,
                  DebugMode = false,
                  IntermediateSavePath = "",
                  SaveIntermediateImages = false,
                  PSFParallelPartitionSize = 100
              };
              var source = new StarDetectionSettingsSnapshot {
                  BrightnessSensitivity = 99.0,
                  MinHFR = 9.9,
                  DebugMode = true,
                  IntermediateSavePath = @"C:\hf\debug",
                  SaveIntermediateImages = true,
                  PSFParallelPartitionSize = 250
              };

              target.CopyMachineLocalFrom(source);

              Assert.Multiple(() => {
                  Assert.That(target.DebugMode, Is.True);
                  Assert.That(target.IntermediateSavePath, Is.EqualTo(@"C:\hf\debug"));
                  Assert.That(target.SaveIntermediateImages, Is.True);
                  Assert.That(target.PSFParallelPartitionSize, Is.EqualTo(250));
                  // Detection knobs must be untouched.
                  Assert.That(target.BrightnessSensitivity, Is.EqualTo(7.5));
                  Assert.That(target.MinHFR, Is.EqualTo(1.05));
              });
          }
  ```

- [ ] **Step 2: Run the fixture and confirm it FAILS to build**

  From the repo root:

  ```
  dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~StarDetectionSettingsSnapshotTests"
  ```

  Expected: build failure of the test project with `error CS1061: 'StarDetectionSettingsSnapshot' does not contain a definition for 'Clone'` and the same for `'CopyMachineLocalFrom'`.

- [ ] **Step 3: Implement `Clone()` and `CopyMachineLocalFrom()`**

  In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/Replay/StarDetectionSettingsSnapshot.cs`, insert the two methods immediately after the `ApplyOptimizedSettings` expression-bodied method (line 103, `public void ApplyOptimizedSettings(OptimizedStarDetectionSettings settings) => OptimizedSettings = settings?.Clone();`) and before the `FromOptions` doc comment:

  ```csharp
          /// <summary>Deep-copies the snapshot (memberwise plus a defensive copy of the nested optimized-settings
          /// DTO) so callers can hand out or mutate copies without aliasing the stored instance.</summary>
          public StarDetectionSettingsSnapshot Clone() {
              var clone = (StarDetectionSettingsSnapshot)MemberwiseClone();
              clone.OptimizedSettings = OptimizedSettings?.Clone();
              return clone;
          }

          /// <summary>Overlays the machine-local settings (global by scope decision — debug, intermediate saves,
          /// and CPU parallelism) from a live options source onto this snapshot, so a per-filter snapshot about to
          /// drive detection uses this machine's local configuration.</summary>
          public void CopyMachineLocalFrom(IStarDetectionOptions source) {
              DebugMode = source.DebugMode;
              IntermediateSavePath = source.IntermediateSavePath;
              SaveIntermediateImages = source.SaveIntermediateImages;
              PSFParallelPartitionSize = source.PSFParallelPartitionSize;
          }
  ```

- [ ] **Step 4: Run the fixture and confirm PASS**

  ```
  dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~StarDetectionSettingsSnapshotTests"
  ```

  Expected: all tests in the fixture pass (2 pre-existing + 3 new).

- [ ] **Step 5: Run the full suite (project invariant)**

  ```
  dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo
  ```

  Expected: all tests pass (use a 600000 ms timeout for the command).

- [ ] **Step 6: Commit**

  ```
  git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/Replay/StarDetectionSettingsSnapshot.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/Replay/StarDetectionSettingsSnapshotTests.cs && GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(per-filter): add Clone and CopyMachineLocalFrom to StarDetectionSettingsSnapshot"
  ```

### Task 4: Per-filter blob DTOs, store interface, and `PerFilterStarDetectionStore`

**Files:**
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/IPerFilterStarDetectionStore.cs`
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/PerFilter/PerFilterStarDetectionData.cs`
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/PerFilter/PerFilterStarDetectionStore.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/PerFilter/PerFilterStarDetectionStoreTests.cs`

Note: both csproj files need no edits — the plugin project is SDK-style (implicit compile globbing picks up the new `StarDetection/PerFilter/` folder) and the test project references the plugin via `ProjectReference` (only TestApp sources are link-included). `InternalsVisibleTo("Joko.NINA.Plugins.HocusFocus.Tests")` already exists in `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Properties/AssemblyInfo.cs`, so the internal `Scrub` is directly testable.

- [ ] **Step 1: Write the failing store test fixture (complete file)**

  Create `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/PerFilter/PerFilterStarDetectionStoreTests.cs`:

  ```csharp
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using Newtonsoft.Json;
  using NINA.Core.Model.Equipment;
  using NINA.Core.Utility;
  using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
  using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
  using NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter;
  using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
  using NINA.Profile.Interfaces;
  using NSubstitute;
  using NUnit.Framework;

  namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.PerFilter;

  [TestFixture]
  public class PerFilterStarDetectionStoreTests {

      // Distinctive global settings, machine-local fields deliberately dirty so scrubbing is observable.
      private static StarDetectionSettingsSnapshot MakeGlobalSnapshot() {
          return new StarDetectionSettingsSnapshot {
              UseAdvanced = true,
              BrightnessSensitivity = 7.5,
              StructureLayers = 6,
              MinHFR = 1.05,
              DebugMode = true,
              IntermediateSavePath = @"C:\hf\debug",
              SaveIntermediateImages = true,
              PSFParallelPartitionSize = 250,
              OptimizedSettings = new OptimizedStarDetectionSettings { BrightnessSensitivity = 3.3, StructureLayers = 7 }
          };
      }

      private static (PerFilterStarDetectionStore store, InMemoryPluginOptionsAccessor accessor, IProfileService profile) Build(params string[] profileFilterNames) {
          var profile = Substitute.For<IProfileService>();
          // List ctor avoids the WPF SynchronizationContext path that per-item Add would take in AsyncObservableCollection.
          var filters = new ObserveAllCollection<FilterInfo>(profileFilterNames.Select((n, i) => new FilterInfo(n, 0, (short)i)));
          profile.ActiveProfile.FilterWheelSettings.FilterWheelFilters.Returns(filters);
          var accessor = new InMemoryPluginOptionsAccessor();
          var store = new PerFilterStarDetectionStore(profile, accessor, MakeGlobalSnapshot);
          return (store, accessor, profile);
      }

      [Test]
      public void Enable_SeedsEveryProfileFilterFromScrubbedGlobal() {
          var (store, accessor, _) = Build("Ha", "OIII");
          var enabledEvents = 0;
          store.EnabledChanged += (_, _) => enabledEvents++;

          store.Enabled = true;

          var ha = store.TryGetSnapshot("Ha");
          var oiii = store.TryGetSnapshot("OIII");
          Assert.Multiple(() => {
              Assert.That(enabledEvents, Is.EqualTo(1));
              Assert.That(accessor.Snapshot["PerFilterStarDetectionEnabled"], Is.True);
              Assert.That(store.GetKnownFilterNames(), Is.EquivalentTo(new[] { "Ha", "OIII" }));
              Assert.That(ha, Is.Not.Null);
              Assert.That(oiii, Is.Not.Null);
              // Detection settings copied from the captured global snapshot...
              Assert.That(ha.BrightnessSensitivity, Is.EqualTo(7.5));
              Assert.That(ha.StructureLayers, Is.EqualTo(6));
              Assert.That(ha.OptimizedSettings.BrightnessSensitivity, Is.EqualTo(3.3));
              // ...with the machine-local fields scrubbed.
              Assert.That(ha.DebugMode, Is.False);
              Assert.That(ha.IntermediateSavePath, Is.EqualTo(""));
              Assert.That(ha.SaveIntermediateImages, Is.False);
              Assert.That(ha.PSFParallelPartitionSize, Is.EqualTo(100));
          });
      }

      [Test]
      public void Enable_PersistsBlobReloadableByNewStoreInstance() {
          var (store, accessor, profile) = Build("Ha");
          store.Enabled = true;

          var reloaded = new PerFilterStarDetectionStore(profile, accessor, MakeGlobalSnapshot);

          Assert.Multiple(() => {
              Assert.That(reloaded.Enabled, Is.True);
              Assert.That(reloaded.GetKnownFilterNames(), Is.EquivalentTo(new[] { "Ha" }));
              Assert.That(reloaded.TryGetSnapshot("Ha").BrightnessSensitivity, Is.EqualTo(7.5));
          });
      }

      [Test]
      public void GlobalSeed_PersistsAcrossReload_AndSeedsNewNames() {
          var (store, accessor, profile) = Build("Ha");
          store.Enabled = true;

          // Reload with a capture delegate producing DIFFERENT values — the persisted GlobalSeed must win.
          var reloaded = new PerFilterStarDetectionStore(profile, accessor, () => new StarDetectionSettingsSnapshot { BrightnessSensitivity = 1.0 });
          var seeded = reloaded.GetOrSeedSnapshot("SII");

          Assert.That(seeded.BrightnessSensitivity, Is.EqualTo(7.5));
      }

      [Test]
      public void GetOrSeedSnapshot_UnknownName_SeedsFromGlobalSeedAndRaisesSnapshotChanged() {
          var (store, _, _) = Build("Ha");
          store.Enabled = true;
          var changed = new List<string>();
          store.SnapshotChanged += (_, e) => changed.Add(e.FilterName);

          var sii = store.GetOrSeedSnapshot("SII");

          Assert.Multiple(() => {
              Assert.That(sii.BrightnessSensitivity, Is.EqualTo(7.5));
              Assert.That(sii.PSFParallelPartitionSize, Is.EqualTo(100));
              Assert.That(changed, Is.EqualTo(new[] { "SII" }));
              Assert.That(store.GetKnownFilterNames(), Is.EquivalentTo(new[] { "Ha", "SII" }));
          });

          // Second fetch returns the stored entry without another seed event.
          store.GetOrSeedSnapshot("SII");
          Assert.That(changed, Has.Count.EqualTo(1));
      }

      [Test]
      public void GetOrSeedSnapshot_NoGlobalSeed_FallsBackToScrubbedCapture() {
          var (store, _, _) = Build("Ha"); // never enabled -> GlobalSeed is null

          var snap = store.GetOrSeedSnapshot("Ha");

          Assert.Multiple(() => {
              Assert.That(snap.BrightnessSensitivity, Is.EqualTo(7.5));
              Assert.That(snap.DebugMode, Is.False);
              Assert.That(snap.PSFParallelPartitionSize, Is.EqualTo(100));
          });
      }

      [Test]
      public void UpsertSnapshot_RoundTripsScrubbedAndRaisesSnapshotChanged() {
          var (store, accessor, profile) = Build("Ha");
          var changed = new List<string>();
          store.SnapshotChanged += (_, e) => changed.Add(e.FilterName);
          var custom = MakeGlobalSnapshot();
          custom.BrightnessSensitivity = 12.25;
          custom.MinHFR = 2.5;

          store.UpsertSnapshot("Ha", custom);

          var fetched = store.TryGetSnapshot("Ha");
          Assert.Multiple(() => {
              Assert.That(changed, Is.EqualTo(new[] { "Ha" }));
              Assert.That(fetched.BrightnessSensitivity, Is.EqualTo(12.25));
              Assert.That(fetched.MinHFR, Is.EqualTo(2.5));
              // Stored scrubbed: machine-local fields normalized even though the input had them set.
              Assert.That(fetched.DebugMode, Is.False);
              Assert.That(fetched.IntermediateSavePath, Is.EqualTo(""));
              Assert.That(fetched.SaveIntermediateImages, Is.False);
              Assert.That(fetched.PSFParallelPartitionSize, Is.EqualTo(100));
          });

          var reloaded = new PerFilterStarDetectionStore(profile, accessor, MakeGlobalSnapshot);
          Assert.That(reloaded.TryGetSnapshot("Ha").BrightnessSensitivity, Is.EqualTo(12.25));
      }

      [Test]
      public void Snapshots_AreClonesNotReferences() {
          var (store, _, _) = Build("Ha");
          var custom = MakeGlobalSnapshot();
          store.UpsertSnapshot("Ha", custom);

          // Mutating the input after the upsert must not change the stored copy.
          custom.BrightnessSensitivity = 999.0;
          custom.OptimizedSettings.StructureLayers = 42;
          var first = store.TryGetSnapshot("Ha");
          // Mutating a fetched snapshot must not change the stored copy either.
          first.BrightnessSensitivity = 555.0;
          first.OptimizedSettings.StructureLayers = 41;
          var second = store.TryGetSnapshot("Ha");

          Assert.Multiple(() => {
              Assert.That(second, Is.Not.SameAs(first));
              Assert.That(second.BrightnessSensitivity, Is.EqualTo(7.5));
              Assert.That(second.OptimizedSettings.StructureLayers, Is.EqualTo(7));
          });
      }

      [Test]
      public void Disable_RetainsStoredSnapshots_AndReEnableDoesNotOverwrite() {
          var (store, accessor, _) = Build("Ha", "OIII");
          store.Enabled = true;
          var custom = MakeGlobalSnapshot();
          custom.BrightnessSensitivity = 12.25;
          store.UpsertSnapshot("Ha", custom);

          store.Enabled = false;

          Assert.Multiple(() => {
              Assert.That(accessor.Snapshot["PerFilterStarDetectionEnabled"], Is.False);
              Assert.That(store.GetKnownFilterNames(), Is.EquivalentTo(new[] { "Ha", "OIII" }));
          });

          store.Enabled = true;

          Assert.That(store.TryGetSnapshot("Ha").BrightnessSensitivity, Is.EqualTo(12.25));
      }

      [Test]
      public void CorruptJson_IsDiscardedAndDoesNotThrow() {
          var profile = Substitute.For<IProfileService>();
          var accessor = new InMemoryPluginOptionsAccessor();
          accessor.SetValueBoolean("PerFilterStarDetectionEnabled", true);
          accessor.SetValueString("PerFilterStarDetectionJson", "{ this is not valid json");

          PerFilterStarDetectionStore store = null;
          Assert.DoesNotThrow(() => store = new PerFilterStarDetectionStore(profile, accessor, MakeGlobalSnapshot));
          Assert.Multiple(() => {
              // The flag survives; the blob is discarded so filters fall back to lazy re-seeding.
              Assert.That(store.Enabled, Is.True);
              Assert.That(store.GetKnownFilterNames(), Is.Empty);
          });
      }

      [Test]
      public void ProfileChanged_ReReadsBothKeysFromAccessor() {
          var (store, accessor, profile) = Build("Ha");
          store.Enabled = true;
          Assert.That(store.GetKnownFilterNames(), Is.Not.Empty);

          accessor.Clear();
          profile.ProfileChanged += Raise.Event<EventHandler>(profile, EventArgs.Empty);

          Assert.Multiple(() => {
              Assert.That(store.Enabled, Is.False);
              Assert.That(store.GetKnownFilterNames(), Is.Empty);
              Assert.That(store.TryGetSnapshot("Ha"), Is.Null);
          });
      }

      [Test]
      public void Scrub_NormalizesMachineLocalFieldsOnAClone() {
          var source = MakeGlobalSnapshot();

          var scrubbed = PerFilterStarDetectionStore.Scrub(source);

          Assert.Multiple(() => {
              Assert.That(scrubbed, Is.Not.SameAs(source));
              Assert.That(scrubbed.DebugMode, Is.False);
              Assert.That(scrubbed.IntermediateSavePath, Is.EqualTo(""));
              Assert.That(scrubbed.SaveIntermediateImages, Is.False);
              Assert.That(scrubbed.PSFParallelPartitionSize, Is.EqualTo(100));
              // Non-machine-local values intact on the clone; the source is untouched.
              Assert.That(scrubbed.BrightnessSensitivity, Is.EqualTo(7.5));
              Assert.That(source.DebugMode, Is.True);
              Assert.That(source.PSFParallelPartitionSize, Is.EqualTo(250));
          });
      }

      [Test]
      public void PerFilterStarDetectionData_JsonRoundTrip_PreservesEntries() {
          var haSettings = MakeGlobalSnapshot();
          haSettings.BrightnessSensitivity = 11.5;
          var data = new PerFilterStarDetectionData { GlobalSeed = MakeGlobalSnapshot() };
          data.Filters.Add(new PerFilterStarDetectionEntry { FilterName = "Ha", Settings = haSettings });
          data.Filters.Add(new PerFilterStarDetectionEntry { FilterName = "OIII", Settings = new StarDetectionSettingsSnapshot { BrightnessSensitivity = 4.5 } });

          var json = JsonConvert.SerializeObject(data);
          var restored = JsonConvert.DeserializeObject<PerFilterStarDetectionData>(json);

          Assert.Multiple(() => {
              Assert.That(restored.SchemaVersion, Is.EqualTo(1));
              Assert.That(restored.GlobalSeed.BrightnessSensitivity, Is.EqualTo(7.5));
              Assert.That(restored.Filters, Has.Count.EqualTo(2));
              Assert.That(restored.Filters[0].FilterName, Is.EqualTo("Ha"));
              Assert.That(restored.Filters[0].Settings.BrightnessSensitivity, Is.EqualTo(11.5));
              Assert.That(restored.Filters[0].Settings.OptimizedSettings.BrightnessSensitivity, Is.EqualTo(3.3));
              Assert.That(restored.Filters[0].Settings.OptimizedSettings.StructureLayers, Is.EqualTo(7));
              Assert.That(restored.Filters[1].FilterName, Is.EqualTo("OIII"));
              Assert.That(restored.Filters[1].Settings.BrightnessSensitivity, Is.EqualTo(4.5));
              Assert.That(restored.Filters[1].Settings.OptimizedSettings, Is.Null);
          });
      }
  }
  ```

- [ ] **Step 2: Run the fixture and confirm it FAILS to build**

  ```
  dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~PerFilterStarDetectionStoreTests"
  ```

  Expected: build failure with `error CS0234: The type or namespace name 'PerFilter' does not exist in the namespace 'NINA.Joko.Plugins.HocusFocus.StarDetection'` (and follow-on `CS0246` for `PerFilterStarDetectionStore` / `PerFilterStarDetectionData`).

- [ ] **Step 3: Create the store interface + event args**

  Create `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/IPerFilterStarDetectionStore.cs`:

  ```csharp
  #region "copyright"

  /*
      Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

      This Source Code Form is subject to the terms of the Mozilla Public
      License, v. 2.0. If a copy of the MPL was not distributed with this
      file, You can obtain one at http://mozilla.org/MPL/2.0/.
  */

  #endregion "copyright"

  using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
  using System;
  using System.Collections.Generic;

  namespace NINA.Joko.Plugins.HocusFocus.Interfaces {

      /// <summary>
      /// Owns the opt-in per-filter star detection settings: the enabled flag plus one
      /// <see cref="StarDetectionSettingsSnapshot"/> per filter name, persisted per-profile as a single JSON blob.
      /// Every snapshot handed out is a clone — mutate freely, write back via <see cref="UpsertSnapshot"/>.
      /// </summary>
      public interface IPerFilterStarDetectionStore {
          bool Enabled { get; set; }

          event EventHandler EnabledChanged;

          event EventHandler<PerFilterSnapshotChangedEventArgs> SnapshotChanged;

          StarDetectionSettingsSnapshot TryGetSnapshot(string filterName);

          StarDetectionSettingsSnapshot GetOrSeedSnapshot(string filterName);

          void UpsertSnapshot(string filterName, StarDetectionSettingsSnapshot snapshot);

          IReadOnlyList<string> GetKnownFilterNames();
      }

      public class PerFilterSnapshotChangedEventArgs : EventArgs {

          public PerFilterSnapshotChangedEventArgs(string filterName) {
              FilterName = filterName;
          }

          public string FilterName { get; }
      }
  }
  ```

- [ ] **Step 4: Create the persisted blob DTOs**

  Create `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/PerFilter/PerFilterStarDetectionData.cs`:

  ```csharp
  #region "copyright"

  /*
      Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

      This Source Code Form is subject to the terms of the Mozilla Public
      License, v. 2.0. If a copy of the MPL was not distributed with this
      file, You can obtain one at http://mozilla.org/MPL/2.0/.
  */

  #endregion "copyright"

  using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
  using System.Collections.Generic;

  namespace NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter {

      /// <summary>
      /// The persisted "PerFilterStarDetectionJson" blob: the scrubbed global settings captured when the feature
      /// was enabled (the seed for filter names first seen later) plus one self-keyed entry per filter name
      /// (NINA TrainedFlatExposureSetting precedent — a list of records, not a dictionary).
      /// </summary>
      public class PerFilterStarDetectionData {
          public int SchemaVersion = 1;
          public StarDetectionSettingsSnapshot GlobalSeed;
          public List<PerFilterStarDetectionEntry> Filters = new();
      }

      public class PerFilterStarDetectionEntry {
          public string FilterName;
          public StarDetectionSettingsSnapshot Settings;
      }
  }
  ```

- [ ] **Step 5: Create `PerFilterStarDetectionStore`**

  Create `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/PerFilter/PerFilterStarDetectionStore.cs`:

  ```csharp
  #region "copyright"

  /*
      Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

      This Source Code Form is subject to the terms of the Mozilla Public
      License, v. 2.0. If a copy of the MPL was not distributed with this
      file, You can obtain one at http://mozilla.org/MPL/2.0/.
  */

  #endregion "copyright"

  using Newtonsoft.Json;
  using NINA.Core.Utility;
  using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
  using NINA.Joko.Plugins.HocusFocus.Interfaces;
  using NINA.Profile;
  using NINA.Profile.Interfaces;
  using System;
  using System.Collections.Generic;
  using Logger = NINA.Core.Utility.Logger;

  namespace NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter {

      public class PerFilterStarDetectionStore : BaseINPC, IPerFilterStarDetectionStore {
          private const string EnabledKey = "PerFilterStarDetectionEnabled";
          private const string JsonKey = "PerFilterStarDetectionJson";

          private readonly IProfileService profileService;
          private readonly IPluginOptionsAccessor optionsAccessor;
          private readonly Func<StarDetectionSettingsSnapshot> captureGlobalSnapshot;
          // Detection resolves snapshots from background threads while the UI edits them; the lock guards the map
          // and the seed, and every snapshot crossing the boundary is a clone.
          private readonly object storeLock = new object();
          private readonly Dictionary<string, StarDetectionSettingsSnapshot> snapshotsByFilterName = new(StringComparer.Ordinal);

          private bool enabled;
          private StarDetectionSettingsSnapshot globalSeed;

          public PerFilterStarDetectionStore(IProfileService profileService, Func<StarDetectionSettingsSnapshot> captureGlobalSnapshot)
              : this(profileService, CreateDefaultAccessor(profileService), captureGlobalSnapshot) {
          }

          public PerFilterStarDetectionStore(IProfileService profileService, IPluginOptionsAccessor optionsAccessor, Func<StarDetectionSettingsSnapshot> captureGlobalSnapshot) {
              this.profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
              this.optionsAccessor = optionsAccessor ?? throw new ArgumentNullException(nameof(optionsAccessor));
              this.captureGlobalSnapshot = captureGlobalSnapshot ?? throw new ArgumentNullException(nameof(captureGlobalSnapshot));
              profileService.ProfileChanged += ProfileService_ProfileChanged;
              InitializeOptions();
          }

          private static IPluginOptionsAccessor CreateDefaultAccessor(IProfileService profileService) {
              var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(PerFilterStarDetectionStore));
              if (guid == null) {
                  throw new Exception($"Guid not found in assembly metadata");
              }
              return new PluginOptionsAccessor(profileService, guid.Value);
          }

          public event EventHandler EnabledChanged;

          public event EventHandler<PerFilterSnapshotChangedEventArgs> SnapshotChanged;

          public bool Enabled {
              get => enabled;
              set {
                  if (enabled != value) {
                      enabled = value;
                      optionsAccessor.SetValueBoolean(EnabledKey, enabled);
                      if (enabled) {
                          SeedAllFromGlobal();
                      }
                      RaisePropertyChanged();
                      EnabledChanged?.Invoke(this, EventArgs.Empty);
                  }
              }
          }

          public StarDetectionSettingsSnapshot TryGetSnapshot(string filterName) {
              lock (storeLock) {
                  return snapshotsByFilterName.TryGetValue(filterName, out var snapshot) ? snapshot.Clone() : null;
              }
          }

          public StarDetectionSettingsSnapshot GetOrSeedSnapshot(string filterName) {
              StarDetectionSettingsSnapshot result;
              bool seeded = false;
              lock (storeLock) {
                  if (!snapshotsByFilterName.TryGetValue(filterName, out var snapshot)) {
                      snapshot = globalSeed != null ? globalSeed.Clone() : Scrub(captureGlobalSnapshot());
                      snapshotsByFilterName[filterName] = snapshot;
                      PersistLocked();
                      seeded = true;
                  }
                  result = snapshot.Clone();
              }
              if (seeded) {
                  SnapshotChanged?.Invoke(this, new PerFilterSnapshotChangedEventArgs(filterName));
              }
              return result;
          }

          public void UpsertSnapshot(string filterName, StarDetectionSettingsSnapshot snapshot) {
              if (snapshot == null) {
                  throw new ArgumentNullException(nameof(snapshot));
              }
              lock (storeLock) {
                  snapshotsByFilterName[filterName] = Scrub(snapshot);
                  PersistLocked();
              }
              SnapshotChanged?.Invoke(this, new PerFilterSnapshotChangedEventArgs(filterName));
          }

          public IReadOnlyList<string> GetKnownFilterNames() {
              lock (storeLock) {
                  return new List<string>(snapshotsByFilterName.Keys);
              }
          }

          // Normalizes the machine-local fields on a clone (they stay global by scope decision): debug off, no
          // intermediate saves, and the PSFParallelPartitionSize factory default.
          internal static StarDetectionSettingsSnapshot Scrub(StarDetectionSettingsSnapshot s) {
              var scrubbed = s.Clone();
              scrubbed.DebugMode = false;
              scrubbed.IntermediateSavePath = "";
              scrubbed.SaveIntermediateImages = false;
              scrubbed.PSFParallelPartitionSize = 100;
              return scrubbed;
          }

          private void SeedAllFromGlobal() {
              lock (storeLock) {
                  globalSeed = Scrub(captureGlobalSnapshot());
                  var filters = profileService.ActiveProfile?.FilterWheelSettings?.FilterWheelFilters;
                  if (filters != null) {
                      foreach (var filter in filters) {
                          var name = filter?.Name;
                          if (string.IsNullOrEmpty(name) || snapshotsByFilterName.ContainsKey(name)) {
                              continue;
                          }
                          snapshotsByFilterName[name] = globalSeed.Clone();
                      }
                  }
                  PersistLocked();
              }
          }

          private void PersistLocked() {
              var data = new PerFilterStarDetectionData() { GlobalSeed = globalSeed };
              foreach (var kvp in snapshotsByFilterName) {
                  data.Filters.Add(new PerFilterStarDetectionEntry() { FilterName = kvp.Key, Settings = kvp.Value });
              }
              optionsAccessor.SetValueString(JsonKey, JsonConvert.SerializeObject(data));
          }

          private void ProfileService_ProfileChanged(object sender, EventArgs e) {
              InitializeOptions();
              RaiseAllPropertiesChanged();
          }

          private void InitializeOptions() {
              lock (storeLock) {
                  enabled = optionsAccessor.GetValueBoolean(EnabledKey, false);
                  globalSeed = null;
                  snapshotsByFilterName.Clear();
                  var json = optionsAccessor.GetValueString(JsonKey, "");
                  if (string.IsNullOrEmpty(json)) {
                      return;
                  }
                  try {
                      var data = JsonConvert.DeserializeObject<PerFilterStarDetectionData>(json);
                      globalSeed = data?.GlobalSeed;
                      if (data?.Filters == null) {
                          return;
                      }
                      foreach (var entry in data.Filters) {
                          if (!string.IsNullOrEmpty(entry?.FilterName) && entry.Settings != null) {
                              snapshotsByFilterName[entry.FilterName] = entry.Settings;
                          }
                      }
                  } catch (Exception ex) {
                      Logger.Warning($"Discarding corrupt PerFilterStarDetectionJson: {ex.Message}");
                      globalSeed = null;
                      snapshotsByFilterName.Clear();
                  }
              }
          }
      }
  }
  ```

- [ ] **Step 6: Run the fixture and confirm PASS**

  ```
  dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~PerFilterStarDetectionStoreTests"
  ```

  Expected: all 12 tests pass.

- [ ] **Step 7: Run the full suite (project invariant)**

  ```
  dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo
  ```

  Expected: all tests pass (use a 600000 ms timeout for the command).

- [ ] **Step 8: Commit**

  ```
  git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/IPerFilterStarDetectionStore.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/PerFilter/PerFilterStarDetectionData.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/PerFilter/PerFilterStarDetectionStore.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/PerFilter/PerFilterStarDetectionStoreTests.cs && GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(per-filter): add per-filter star detection store with seeded, scrubbed snapshots"
  ```

### Task 5: `PerFilterEditBinder` — buffered-edit binder between the store and the `StarDetectionOptions` singleton

Turns the `StarDetectionOptions` singleton into the edit buffer for one filter's snapshot while the feature is on. Depends on earlier task groups having landed: `Interfaces/IPerFilterStarDetectionStore.cs` (+ `PerFilterSnapshotChangedEventArgs`), `StarDetection/PerFilter/PerFilterStarDetectionStore.cs` (3-arg ctor `(IProfileService, IPluginOptionsAccessor, Func<StarDetectionSettingsSnapshot>)`, scrubbing `UpsertSnapshot`, `EnabledChanged` raised after seeding), and the `StarDetectionOptions` buffered-persistence change (`PersistToProfile`, internal `ReloadFromProfile`, `SuppressiblePluginOptionsAccessor` wrapping).

Read before coding: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionOptions.cs` — `ProfileService_ProfileChanged` (lines 49–52; `InitializeOptions` + `RaiseAllPropertiesChanged`), `StarDetectionOptions_PropertyChanged`/`ConfigureSimpleSettings` (61–72, 190–199), `ApplyImportedSnapshot`/`ApplySnapshotCore` (1191–1221). Key facts the design leans on: `ApplyImportedSnapshot` skips the machine-local `DebugMode`/`PSFParallelPartitionSize`/`IntermediateSavePath`/`SaveIntermediateImages` and ends with `RaiseAllPropertiesChanged()`; `InitializeOptions` writes **fields** then fires notifications, and the options singleton's `ProfileChanged` handler runs **before** the store's and the binder's (subscription order), so its re-read burst reaches the binder while the store still holds the old profile's map — the binder must not mirror during that window (guarded by comparing `profileService.ActiveProfile` to the profile the binder last observed).

**Files:**
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/PerFilter/PerFilterEditBinder.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/PerFilter/PerFilterEditBinderTests.cs`

No csproj edits needed: the test project references the plugin project (`<ProjectReference Include="..\Joko.NINA.Plugins.HocusFocus\Joko.NINA.Plugins.HocusFocus.csproj" />`), and `InternalsVisibleTo("Joko.NINA.Plugins.HocusFocus.Tests")` already exists in `Properties/AssemblyInfo.cs` (line 18), so the internal `StarDetectionOptions` ctor and `PersistToProfile` plumbing are reachable.

- [ ] **Step 1: Write the failing test file (two fixtures: substitute-store behavior tests + real-store integration tests)**

Create `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/PerFilter/PerFilterEditBinderTests.cs`:

```csharp
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection.PerFilter;

[TestFixture]
public class PerFilterEditBinderTests {

    private sealed class Harness {
        public IProfileService ProfileService;
        public IProfile Profile;
        public ObserveAllCollection<FilterInfo> Filters;
        public InMemoryPluginOptionsAccessor LegacyAccessor;
        public StarDetectionOptions Buffer;
        public IPerFilterStarDetectionStore Store;
        public PerFilterEditBinder Binder;
        public string CurrentFilterName;
    }

    private static ObserveAllCollection<FilterInfo> MakeFilters(params string[] names) {
        var filters = new ObserveAllCollection<FilterInfo>();
        foreach (var name in names) {
            filters.Add(new FilterInfo() { Name = name });
        }
        return filters;
    }

    private static IProfile MakeProfile(ObserveAllCollection<FilterInfo> filters) {
        var profile = Substitute.For<IProfile>();
        profile.FilterWheelSettings.FilterWheelFilters.Returns(filters);
        return profile;
    }

    // Snapshots built through a real options object so every knob holds validated values (several
    // StarDetectionOptions setters throw on out-of-range input; a default-constructed snapshot has zeros).
    private static StarDetectionSettingsSnapshot SnapshotWith(Action<StarDetectionOptions> mutate) {
        var scratch = new StarDetectionOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());
        mutate(scratch);
        return StarDetectionSettingsSnapshot.FromOptions(scratch);
    }

    private static Harness Build(bool enabled = false, string currentFilter = "Ha", string[] filterNames = null) {
        var filters = MakeFilters(filterNames ?? new[] { "L", "Ha", "Oiii" });
        var profile = MakeProfile(filters);
        var profileService = Substitute.For<IProfileService>();
        profileService.ActiveProfile.Returns(profile);

        var legacyAccessor = new InMemoryPluginOptionsAccessor();
        // Subscribes to ProfileChanged first, exactly like production construction order.
        var buffer = new StarDetectionOptions(profileService, legacyAccessor);

        var store = Substitute.For<IPerFilterStarDetectionStore>();
        store.Enabled.Returns(enabled);
        store.GetOrSeedSnapshot(Arg.Any<string>()).Returns(_ => StarDetectionSettingsSnapshot.FromOptions(buffer));

        var harness = new Harness() {
            ProfileService = profileService,
            Profile = profile,
            Filters = filters,
            LegacyAccessor = legacyAccessor,
            Buffer = buffer,
            Store = store,
            CurrentFilterName = currentFilter,
        };
        harness.Binder = new PerFilterEditBinder(store, buffer, profileService, () => harness.CurrentFilterName);
        return harness;
    }

    private static void SetEnabled(Harness h, bool enabled) {
        h.Store.Enabled.Returns(enabled);
        h.Store.EnabledChanged += Raise.Event<EventHandler>(h.Store, EventArgs.Empty);
    }

    [Test]
    public void Construction_FeatureAlreadyEnabled_EntersBufferedModeAndLoadsCurrentFilter() {
        var h = Build(enabled: true);
        Assert.Multiple(() => {
            Assert.That(h.Buffer.PersistToProfile, Is.False);
            Assert.That(h.Binder.EditedFilterName, Is.EqualTo("Ha"));
        });
        h.Store.Received().GetOrSeedSnapshot("Ha");
    }

    [Test]
    public void Enable_FlipsPersistToProfileOffAndLoadsCurrentWheelFilter() {
        var h = Build();
        Assert.That(h.Buffer.PersistToProfile, Is.True);

        SetEnabled(h, true);

        Assert.Multiple(() => {
            Assert.That(h.Buffer.PersistToProfile, Is.False);
            Assert.That(h.Binder.EditedFilterName, Is.EqualTo("Ha"));
        });
        h.Store.Received().GetOrSeedSnapshot("Ha");
    }

    [Test]
    public void Enable_NoCurrentWheelFilter_FallsBackToFirstProfileFilter() {
        var h = Build(currentFilter: null);

        SetEnabled(h, true);

        Assert.That(h.Binder.EditedFilterName, Is.EqualTo("L"));
        h.Store.Received().GetOrSeedSnapshot("L");
    }

    [Test]
    public void Disable_RestoresPersistToProfileAndReloadsPreEnableValues() {
        var h = Build();
        h.Buffer.UseAdvanced = true;
        h.Buffer.MaxDistortion = 0.45;
        SetEnabled(h, true);

        h.Buffer.MaxDistortion = 0.91; // buffered edit: mirrored to the store, not the legacy keys
        Assert.That((double)h.LegacyAccessor.Snapshot["MaxDistortion"], Is.EqualTo(0.45));

        SetEnabled(h, false);

        Assert.Multiple(() => {
            Assert.That(h.Buffer.PersistToProfile, Is.True);
            Assert.That(h.Buffer.MaxDistortion, Is.EqualTo(0.45));
        });
    }

    [Test]
    public void EditedFilterNameSwitch_LoadsThatFiltersSnapshotWithoutMirroring() {
        var h = Build(enabled: true);
        var oiii = SnapshotWith(o => {
            o.UseAdvanced = true;
            o.MaxDistortion = 0.31;
            o.MinHFR = 1.05;
        });
        h.Store.GetOrSeedSnapshot("Oiii").Returns(oiii);
        h.Store.ClearReceivedCalls();

        h.Binder.EditedFilterName = "Oiii";

        Assert.Multiple(() => {
            Assert.That(h.Buffer.UseAdvanced, Is.True);
            Assert.That(h.Buffer.MaxDistortion, Is.EqualTo(0.31));
            Assert.That(h.Buffer.MinHFR, Is.EqualTo(1.05));
        });
        h.Store.Received().GetOrSeedSnapshot("Oiii");
        h.Store.DidNotReceive().UpsertSnapshot(Arg.Any<string>(), Arg.Any<StarDetectionSettingsSnapshot>());
    }

    [Test]
    public void BufferEdit_MirrorsToTheEditedFiltersStoreEntry() {
        var h = Build(enabled: true);
        h.Store.ClearReceivedCalls();

        h.Buffer.UseAdvanced = true;
        h.Buffer.MaxDistortion = 0.42;

        h.Store.Received().UpsertSnapshot("Ha", Arg.Is<StarDetectionSettingsSnapshot>(s => s.MaxDistortion == 0.42 && s.UseAdvanced));
    }

    [Test]
    public void BufferEdit_FeatureDisabled_DoesNotMirror() {
        var h = Build();

        h.Buffer.UseAdvanced = true;
        h.Buffer.MaxDistortion = 0.42;

        h.Store.DidNotReceive().UpsertSnapshot(Arg.Any<string>(), Arg.Any<StarDetectionSettingsSnapshot>());
    }

    [Test]
    public void ExternalSnapshotChange_ForEditedFilter_ReloadsTheBuffer() {
        var h = Build(enabled: true);
        var updated = SnapshotWith(o => {
            o.UseAdvanced = true;
            o.MaxDistortion = 0.77;
        });
        h.Store.GetOrSeedSnapshot("Ha").Returns(updated);

        h.Store.SnapshotChanged += Raise.Event<EventHandler<PerFilterSnapshotChangedEventArgs>>(
            h.Store, new PerFilterSnapshotChangedEventArgs("Ha"));

        Assert.That(h.Buffer.MaxDistortion, Is.EqualTo(0.77));
    }

    [Test]
    public void ExternalSnapshotChange_ForOtherFilter_DoesNotTouchTheBuffer() {
        var h = Build(enabled: true);
        h.Store.ClearReceivedCalls();

        h.Store.SnapshotChanged += Raise.Event<EventHandler<PerFilterSnapshotChangedEventArgs>>(
            h.Store, new PerFilterSnapshotChangedEventArgs("Sii"));

        h.Store.DidNotReceive().GetOrSeedSnapshot(Arg.Any<string>());
    }

    [Test]
    public void SelfOriginatedUpsert_DoesNotReloadTheBuffer() {
        var h = Build(enabled: true);
        // Mimic the real store: UpsertSnapshot raises SnapshotChanged synchronously for the upserted filter.
        h.Store.When(s => s.UpsertSnapshot(Arg.Any<string>(), Arg.Any<StarDetectionSettingsSnapshot>()))
            .Do(ci => h.Store.SnapshotChanged += Raise.Event<EventHandler<PerFilterSnapshotChangedEventArgs>>(
                h.Store, new PerFilterSnapshotChangedEventArgs(ci.Arg<string>())));
        h.Store.ClearReceivedCalls();

        h.Buffer.UseAdvanced = true;
        h.Buffer.MaxDistortion = 0.42;

        Assert.That(h.Buffer.MaxDistortion, Is.EqualTo(0.42));
        h.Store.DidNotReceive().GetOrSeedSnapshot(Arg.Any<string>());
    }

    [Test]
    public void ProfileChanged_EditedFilterStillPresent_KeepsItAndReloads() {
        var h = Build(enabled: true);
        var newProfile = MakeProfile(MakeFilters("Ha", "Sii"));
        h.ProfileService.ActiveProfile.Returns(newProfile);
        h.Store.ClearReceivedCalls();

        h.ProfileService.ProfileChanged += Raise.Event<EventHandler>(h.ProfileService, EventArgs.Empty);

        Assert.Multiple(() => {
            Assert.That(h.Binder.EditedFilterName, Is.EqualTo("Ha"));
            Assert.That(h.Binder.AvailableFilterNames, Is.EqualTo(new[] { "Ha", "Sii" }));
        });
        h.Store.Received().GetOrSeedSnapshot("Ha");
    }

    [Test]
    public void ProfileChanged_EditedFilterMissing_ResolvesToDefault() {
        var h = Build(enabled: true);
        h.CurrentFilterName = null;
        var newProfile = MakeProfile(MakeFilters("Sii", "Oiii"));
        h.ProfileService.ActiveProfile.Returns(newProfile);
        h.Store.ClearReceivedCalls();

        h.ProfileService.ProfileChanged += Raise.Event<EventHandler>(h.ProfileService, EventArgs.Empty);

        Assert.That(h.Binder.EditedFilterName, Is.EqualTo("Sii"));
        h.Store.Received().GetOrSeedSnapshot("Sii");
    }

    [Test]
    public void ProfileChanged_OptionsReReadBurst_DoesNotMirrorStaleDataIntoTheStore() {
        var h = Build();
        h.Buffer.UseAdvanced = true;
        h.Buffer.MaxDistortion = 0.45;
        SetEnabled(h, true);
        h.Buffer.MaxDistortion = 0.91;

        var newProfile = MakeProfile(MakeFilters("L", "Ha", "Oiii"));
        h.ProfileService.ActiveProfile.Returns(newProfile);
        h.Store.ClearReceivedCalls();

        // StarDetectionOptions subscribed to ProfileChanged before the binder, so its re-read notification
        // burst fires while the binder still observes the old profile — it must not upsert during that window.
        h.ProfileService.ProfileChanged += Raise.Event<EventHandler>(h.ProfileService, EventArgs.Empty);
        h.Store.DidNotReceive().UpsertSnapshot(Arg.Any<string>(), Arg.Any<StarDetectionSettingsSnapshot>());

        // After the switch completes, edits mirror again.
        h.Buffer.MinHFR = 1.31;
        h.Store.Received().UpsertSnapshot("Ha", Arg.Is<StarDetectionSettingsSnapshot>(s => s.MinHFR == 1.31));
    }

    [Test]
    public void AvailableFilterNames_TracksProfileCollectionChanges() {
        var h = Build();
        var raised = new List<string>();
        h.Binder.PropertyChanged += (s, e) => raised.Add(e.PropertyName);

        h.Filters.Add(new FilterInfo() { Name = "Sii" });

        Assert.Multiple(() => {
            Assert.That(raised, Does.Contain(nameof(PerFilterEditBinder.AvailableFilterNames)));
            Assert.That(h.Binder.AvailableFilterNames, Is.EqualTo(new[] { "L", "Ha", "Oiii", "Sii" }));
        });
    }
}

// End-to-end binder <-> real store wiring: seeding on enable, scrubbing on upsert, synchronous
// SnapshotChanged reload, and data retention across disable — the production event chain, no substitutes.
[TestFixture]
public class PerFilterEditBinderStoreIntegrationTests {

    private sealed class Harness {
        public IProfileService ProfileService;
        public InMemoryPluginOptionsAccessor LegacyAccessor;
        public InMemoryPluginOptionsAccessor StoreAccessor;
        public StarDetectionOptions Buffer;
        public PerFilterStarDetectionStore Store;
        public PerFilterEditBinder Binder;
        public string CurrentFilterName = "Ha";
    }

    private static Harness Build() {
        var filters = new ObserveAllCollection<FilterInfo>();
        filters.Add(new FilterInfo() { Name = "L" });
        filters.Add(new FilterInfo() { Name = "Ha" });
        var profile = Substitute.For<IProfile>();
        profile.FilterWheelSettings.FilterWheelFilters.Returns(filters);
        var profileService = Substitute.For<IProfileService>();
        profileService.ActiveProfile.Returns(profile);

        var h = new Harness() {
            ProfileService = profileService,
            LegacyAccessor = new InMemoryPluginOptionsAccessor(),
            StoreAccessor = new InMemoryPluginOptionsAccessor(),
        };
        // Production construction order: options -> store -> binder (ProfileChanged handlers run in this order).
        h.Buffer = new StarDetectionOptions(profileService, h.LegacyAccessor);
        h.Store = new PerFilterStarDetectionStore(
            profileService, h.StoreAccessor, () => StarDetectionSettingsSnapshot.FromOptions(h.Buffer));
        h.Binder = new PerFilterEditBinder(h.Store, h.Buffer, profileService, () => h.CurrentFilterName);
        return h;
    }

    [Test]
    public void BufferEdit_RoundTripsThroughTheRealStore() {
        var h = Build();
        h.Store.Enabled = true;

        h.Buffer.UseAdvanced = true;
        h.Buffer.MaxDistortion = 0.37;

        var stored = h.Store.TryGetSnapshot("Ha");
        Assert.Multiple(() => {
            Assert.That(stored.UseAdvanced, Is.True);
            Assert.That(stored.MaxDistortion, Is.EqualTo(0.37));
        });
    }

    [Test]
    public void MachineLocalBufferEdits_DoNotClobberScrubbedStoreFields() {
        var h = Build();
        h.Store.Enabled = true;

        h.Buffer.DebugMode = true;
        h.Buffer.PSFParallelPartitionSize = 999;
        h.Buffer.SaveIntermediateImages = true;

        var stored = h.Store.TryGetSnapshot("Ha");
        Assert.Multiple(() => {
            Assert.That(stored.DebugMode, Is.False);
            Assert.That(stored.PSFParallelPartitionSize, Is.EqualTo(100));
            Assert.That(stored.SaveIntermediateImages, Is.False);
            Assert.That(stored.IntermediateSavePath, Is.EqualTo(""));
            // The buffer itself keeps the machine-local values (they stay global by scope decision).
            Assert.That(h.Buffer.DebugMode, Is.True);
            Assert.That(h.Buffer.PSFParallelPartitionSize, Is.EqualTo(999));
        });
    }

    [Test]
    public void ExternalUpsert_ReloadsTheBufferWithTheNewSnapshot() {
        var h = Build();
        h.Store.Enabled = true;

        var external = h.Store.TryGetSnapshot("Ha");
        external.UseAdvanced = true;
        external.MaxDistortion = 0.66;
        h.Store.UpsertSnapshot("Ha", external);

        Assert.Multiple(() => {
            Assert.That(h.Buffer.UseAdvanced, Is.True);
            Assert.That(h.Buffer.MaxDistortion, Is.EqualTo(0.66));
        });
    }

    [Test]
    public void DisableAfterEdits_RestoresPreEnableGlobalsAndRetainsStoreEntries() {
        var h = Build();
        h.Buffer.UseAdvanced = true;
        h.Buffer.MaxDistortion = 0.45;
        h.Store.Enabled = true;
        h.Buffer.MaxDistortion = 0.91;

        h.Store.Enabled = false;

        Assert.Multiple(() => {
            Assert.That(h.Buffer.MaxDistortion, Is.EqualTo(0.45));
            Assert.That(h.Store.TryGetSnapshot("Ha").MaxDistortion, Is.EqualTo(0.91));
        });
    }
}
```

- [ ] **Step 2: Run the fixture and confirm it FAILS to build**

From the repo root:

```
dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~PerFilterEditBinder"
```

Expected: build failure with `error CS0246: The type or namespace name 'PerFilterEditBinder' could not be found` (the store, event args, and `PersistToProfile` all resolve from the earlier tasks; only the binder is missing). If anything else fails to compile, an earlier task group's contract deliverable is missing — stop and fix that first.

- [ ] **Step 3: Implement `PerFilterEditBinder`**

Create `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/PerFilter/PerFilterEditBinder.cs`:

```csharp
#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter {

    /// <summary>
    /// Turns the <see cref="StarDetectionOptions"/> singleton into the edit buffer for one filter's per-filter
    /// snapshot while the feature is enabled: selecting <see cref="EditedFilterName"/> loads that filter's snapshot
    /// into the buffer (imported-snapshot semantics, so machine-local fields stay put), and buffer edits mirror back
    /// into the store. Legacy profile writes are suppressed for the duration (<c>PersistToProfile = false</c>) so
    /// disabling the feature returns exactly to the pre-enable global settings. UI-thread only, like the options
    /// singleton it wraps — detection threads read the store directly, never this binder.
    /// </summary>
    public class PerFilterEditBinder : BaseINPC {
        private readonly IPerFilterStarDetectionStore store;
        private readonly StarDetectionOptions buffer;
        private readonly IProfileService profileService;
        private readonly Func<string> getCurrentFilterName;

        // Profile whose filter collection is currently observed. Mirroring is gated on ActiveProfile still being
        // this instance: during a profile switch, StarDetectionOptions' ProfileChanged handler (subscribed before
        // this binder) re-reads the new profile's legacy keys and fires PropertyChanged BEFORE this binder's
        // handler runs — without the gate that burst would upsert stale data into the new profile's store blob.
        private IProfile observedProfile;
        private ObserveAllCollection<FilterInfo> observedFilters;
        private bool isLoading;
        private bool isMirroring;

        public PerFilterEditBinder(
            IPerFilterStarDetectionStore store,
            StarDetectionOptions buffer,
            IProfileService profileService,
            Func<string> getCurrentFilterName) {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            this.profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            this.getCurrentFilterName = getCurrentFilterName ?? throw new ArgumentNullException(nameof(getCurrentFilterName));

            ObserveProfileFilters();
            profileService.ProfileChanged += ProfileService_ProfileChanged;
            store.EnabledChanged += Store_EnabledChanged;
            store.SnapshotChanged += Store_SnapshotChanged;
            buffer.PropertyChanged += Buffer_PropertyChanged;
            if (store.Enabled) {
                // Feature already on at startup (persisted per-profile flag): enter buffered-edit mode immediately.
                OnFeatureEnabled();
            }
        }

        private string editedFilterName;

        public string EditedFilterName {
            get => editedFilterName;
            set {
                if (editedFilterName != value) {
                    editedFilterName = value;
                    RaisePropertyChanged();
                    if (!string.IsNullOrEmpty(editedFilterName)) {
                        LoadSnapshotIntoBuffer(editedFilterName);
                    }
                }
            }
        }

        public IReadOnlyList<string> AvailableFilterNames {
            get {
                var filters = profileService.ActiveProfile?.FilterWheelSettings?.FilterWheelFilters;
                if (filters == null) {
                    return Array.Empty<string>();
                }
                return filters.Select(f => f.Name).Where(n => !string.IsNullOrEmpty(n)).ToList();
            }
        }

        private void ObserveProfileFilters() {
            if (observedFilters != null) {
                observedFilters.CollectionChanged -= Filters_CollectionChanged;
            }
            observedProfile = profileService.ActiveProfile;
            observedFilters = observedProfile?.FilterWheelSettings?.FilterWheelFilters;
            if (observedFilters != null) {
                observedFilters.CollectionChanged += Filters_CollectionChanged;
            }
        }

        private void Filters_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) {
            RaisePropertyChanged(nameof(AvailableFilterNames));
        }

        private void ProfileService_ProfileChanged(object sender, EventArgs e) {
            // Subscription order guarantees StarDetectionOptions and the store re-read the new profile before this
            // runs, so re-resolving here loads the new profile's snapshots. Enabled is per-profile, so the buffer's
            // persistence mode must be re-synced too.
            ObserveProfileFilters();
            RaisePropertyChanged(nameof(AvailableFilterNames));
            if (store.Enabled) {
                buffer.PersistToProfile = false;
                var names = AvailableFilterNames;
                var name = !string.IsNullOrEmpty(editedFilterName) && names.Contains(editedFilterName)
                    ? editedFilterName
                    : ResolveDefaultFilterName(names);
                editedFilterName = name;
                RaisePropertyChanged(nameof(EditedFilterName));
                if (!string.IsNullOrEmpty(name)) {
                    LoadSnapshotIntoBuffer(name);
                }
            } else {
                buffer.PersistToProfile = true;
            }
        }

        private string ResolveDefaultFilterName(IReadOnlyList<string> names) {
            var current = getCurrentFilterName();
            if (!string.IsNullOrEmpty(current)) {
                return current;
            }
            return names.Count > 0 ? names[0] : null;
        }

        private void Store_EnabledChanged(object sender, EventArgs e) {
            if (store.Enabled) {
                OnFeatureEnabled();
            } else {
                buffer.PersistToProfile = true;
                buffer.ReloadFromProfile();
            }
        }

        private void OnFeatureEnabled() {
            // Suppress legacy writes BEFORE the load mutates the buffer, so the pre-enable keys stay frozen.
            buffer.PersistToProfile = false;
            // Set the field directly: the public setter's equality guard would skip the reload when re-enabling
            // with an unchanged name, but the buffer still needs the per-filter snapshot loaded.
            var name = ResolveDefaultFilterName(AvailableFilterNames);
            editedFilterName = name;
            RaisePropertyChanged(nameof(EditedFilterName));
            if (!string.IsNullOrEmpty(name)) {
                LoadSnapshotIntoBuffer(name);
            }
        }

        private void LoadSnapshotIntoBuffer(string filterName) {
            if (isLoading) {
                return; // GetOrSeedSnapshot raises SnapshotChanged when it seeds; never re-enter the load
            }
            isLoading = true;
            try {
                var snapshot = store.GetOrSeedSnapshot(filterName);
                buffer.ApplyImportedSnapshot(snapshot);
            } finally {
                isLoading = false;
            }
        }

        private void Buffer_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (!store.Enabled || isLoading || isMirroring || string.IsNullOrEmpty(editedFilterName)) {
                return;
            }
            if (!ReferenceEquals(profileService.ActiveProfile, observedProfile)) {
                return; // mid profile-switch: an earlier ProfileChanged handler is re-reading the buffer
            }
            isMirroring = true;
            try {
                store.UpsertSnapshot(editedFilterName, StarDetectionSettingsSnapshot.FromOptions(buffer));
            } finally {
                isMirroring = false;
            }
        }

        private void Store_SnapshotChanged(object sender, PerFilterSnapshotChangedEventArgs e) {
            if (isMirroring || isLoading || !store.Enabled) {
                return;
            }
            if (!string.Equals(e.FilterName, editedFilterName, StringComparison.Ordinal)) {
                return;
            }
            LoadSnapshotIntoBuffer(editedFilterName);
        }
    }
}
```

- [ ] **Step 4: Run the fixtures and confirm they PASS**

```
dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~PerFilterEditBinder"
```

Expected: 18 tests pass (14 in `PerFilterEditBinderTests`, 4 in `PerFilterEditBinderStoreIntegrationTests`), 0 failures.

- [ ] **Step 5: Run the full suite**

```
dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo
```

Expected: all tests pass. (The build's PostBuild xcopy into the local NINA plugin folder is harmless.)

- [ ] **Step 6: Commit**

```
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/PerFilter/PerFilterEditBinder.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/PerFilter/PerFilterEditBinderTests.cs && GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(per-filter): add PerFilterEditBinder that makes StarDetectionOptions the per-filter edit buffer"
```

### Task 6: `HocusFocusPlugin` bootstrap — construct the per-filter store and edit binder statics

Wire the two singletons into the plugin constructor, **after** the options singletons: `ProfileChanged` handlers run in subscription order, and both the store and the binder rely on `StarDetectionOptions` having re-read the legacy keys first (`HocusFocusPlugin.cs` constructs `StarDetectionOptions` at lines 103–105; insert after the `CameraSimulatorOptions` block that ends at line 123).

No new unit test: no existing test constructs `HocusFocusPlugin` (its ctor needs MEF-only collaborators like `IOptionsVM` and touches `Settings.Default`), so there is no plugin-level test pattern to extend. The wiring is verified by the build, the full suite, and the downstream task groups that consume `HocusFocusPlugin.PerFilterStarDetection` / `PerFilterStarDetectionEditBinder` (detection resolution, entry-point gates, wizard Accept delegate).

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/HocusFocusPlugin.cs`

- [ ] **Step 1: Add the usings and construct the singletons in the plugin ctor**

In `HocusFocusPlugin.cs`, extend the using block (anchor: the existing `using NINA.Joko.Plugins.HocusFocus.StarDetection;` / `...StarDetection.Optimization;` pair at lines 20–21):

```csharp
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization;
using NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
```

Then in the constructor, insert immediately before the `SimTiltAdapterVM` assignment (anchor: the comment `// Must follow CameraSimulatorOptions: the VM reads it and subscribes to its PropertyChanged.` at line 124):

```csharp
            if (PerFilterStarDetection == null) {
                // Constructed after the options singletons: ProfileChanged handlers run in subscription order,
                // so the store (and the binder below) re-read a new profile only after StarDetectionOptions has
                // re-read the legacy keys.
                PerFilterStarDetection = new PerFilterStarDetectionStore(
                    profileService,
                    () => StarDetectionSettingsSnapshot.FromOptions(StarDetectionOptions));
            }
            if (PerFilterStarDetectionEditBinder == null) {
                PerFilterStarDetectionEditBinder = new PerFilterEditBinder(
                    PerFilterStarDetection,
                    StarDetectionOptions,
                    profileService,
                    () => filterWheelMediator.GetInfo()?.SelectedFilter?.Name);
            }
```

(`filterWheelMediator` is already a ctor field, line 57; `FilterWheelInfo.SelectedFilter` is null while no wheel is connected, so the delegate then yields null and the binder falls back to the first profile filter.)

- [ ] **Step 2: Add the static properties**

In the statics region, after `public static CameraSimulatorOptions CameraSimulatorOptions { get; private set; }` (line 295):

```csharp
        public static CameraSimulatorOptions CameraSimulatorOptions { get; private set; }

        public static PerFilterStarDetectionStore PerFilterStarDetection { get; private set; }

        public static PerFilterEditBinder PerFilterStarDetectionEditBinder { get; private set; }
```

- [ ] **Step 3: Run the full suite (build validates the wiring and the XAML)**

```
dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo
```

Expected: clean build, all tests pass.

- [ ] **Step 4: Commit**

```
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/HocusFocusPlugin.cs && GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(per-filter): construct the per-filter store and edit binder in the plugin bootstrap"
```

### Task 7: Per-filter options resolution in `HocusFocusStarDetection`

**Files:**
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/PerFilter/PerFilterSettingsUnavailableException.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/HocusFocusStarDetection.cs` (usings block lines 13–14; fields lines 226–230; ctors lines 238–254; `GetStarDetectorParams` lines 431–439)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/HocusFocusPlugin.cs` (`OptimizeStarDetection` fallback ctor, line ~197)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/HocusFocusStarDetectionTests.cs` (`Build()` helper, lines 19–29)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/PerFilterDetectionResolutionTests.cs` (new)

- [ ] **Step 1: Create `PerFilterSettingsUnavailableException`**

  Create `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/PerFilter/PerFilterSettingsUnavailableException.cs`:

  ```csharp
  #region "copyright"

  /*
      Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

      This Source Code Form is subject to the terms of the Mozilla Public
      License, v. 2.0. If a copy of the MPL was not distributed with this
      file, You can obtain one at http://mozilla.org/MPL/2.0/.
  */

  #endregion "copyright"

  using System;

  namespace NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter {

      /// <summary>
      /// Thrown when per-filter star detection is enabled but the capture-time filter of an image cannot be
      /// determined (no filter wheel metadata). The NINA-facing Detect entry point converts this into a
      /// soft-fail (empty result + warning notification); HocusFocus-owned entry points gate up front instead,
      /// and typed callers let it propagate to their existing failure handling.
      /// </summary>
      public class PerFilterSettingsUnavailableException : Exception {

          public PerFilterSettingsUnavailableException(string message) : base(message) {
          }
      }
  }
  ```

- [ ] **Step 2: Add the `IPerFilterStarDetectionStore` ctor parameter and update all call sites (no behavior change yet)**

  In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/HocusFocusStarDetection.cs`, add the using after line 14 (`using NINA.Joko.Plugins.HocusFocus.Utility;`):

  ```csharp
  using NINA.Joko.Plugins.HocusFocus.Interfaces;
  using NINA.Joko.Plugins.HocusFocus.Utility;
  using NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter;
  ```

  Extend the field block (lines 226–230):

  ```csharp
          private readonly IStarDetector starDetector;
          private readonly IStarDetectionOptions starDetectionOptions;
          private readonly IProfileService profileService;
          private readonly IFocuserMediator focuserMediator;
          private readonly IPerFilterStarDetectionStore perFilterStore;
          private bool pixelScaleWarningShown = false;
  ```

  Replace both ctors (lines 238–254):

  ```csharp
          [ImportingConstructor]
          public HocusFocusStarDetection(IImageStatisticsVM imageStatisticsVM, IProfileService profileService, IFocuserMediator focuserMediator) :
              this(imageStatisticsVM, profileService, focuserMediator, HocusFocusPlugin.StarDetectionOptions, HocusFocusPlugin.AlglibAPI, HocusFocusPlugin.PerFilterStarDetection) {
          }

          public HocusFocusStarDetection(
              IImageStatisticsVM imageStatisticsVM,
              IProfileService profileService,
              IFocuserMediator focuserMediator,
              IStarDetectionOptions starDetectionOptions,
              IAlglibAPI alglibAPI,
              IPerFilterStarDetectionStore perFilterStore) {
              this.starDetector = new StarDetector(alglibAPI);
              this.starDetectionOptions = starDetectionOptions;
              this.profileService = profileService;
              this.focuserMediator = focuserMediator;
              this.perFilterStore = perFilterStore;
              ImageStatisticsVM = imageStatisticsVM;
          }
  ```

  In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/HocusFocusPlugin.cs`, update the `OptimizeStarDetection` fallback (line ~197):

  ```csharp
              var detection = starDetectionSelector?.Behaviors?.OfType<IHocusFocusStarDetection>().FirstOrDefault()
                  ?? new HocusFocusStarDetection(
                      null,
                      profileService,
                      focuserMediator,
                      StarDetectionOptions,
                      AlglibAPI,
                      PerFilterStarDetection);
  ```

  In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/HocusFocusStarDetectionTests.cs`, update `Build()` (lines 19–29; `IPerFilterStarDetectionStore` is in `NINA.Joko.Plugins.HocusFocus.Interfaces`, already imported — a fresh substitute reports `Enabled == false`, so every existing test keeps its behavior):

  ```csharp
          private static HocusFocusStarDetection Build() {
              var profileService = Substitute.For<IProfileService>();
              var focuserMediator = Substitute.For<IFocuserMediator>();
              focuserMediator.GetInfo().Returns(new FocuserInfo());
              return new HocusFocusStarDetection(
                  imageStatisticsVM: Substitute.For<IImageStatisticsVM>(),
                  profileService: profileService,
                  focuserMediator: focuserMediator,
                  starDetectionOptions: Substitute.For<IStarDetectionOptions>(),
                  alglibAPI: new AlglibAPI(),
                  perFilterStore: Substitute.For<IPerFilterStarDetectionStore>());
          }
  ```

  Run the existing fixture — expect PASS (pure wiring, no behavior change):

  ```
  dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~HocusFocusStarDetectionTests"
  ```

- [ ] **Step 3: Write the resolution tests (two will fail)**

  Create `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/PerFilterDetectionResolutionTests.cs`:

  ```csharp
  using NINA.Core.Utility;
  using NINA.Equipment.Equipment.MyFocuser;
  using NINA.Equipment.Interfaces.Mediator;
  using NINA.Image.ImageAnalysis;
  using NINA.Image.ImageData;
  using NINA.Image.Interfaces;
  using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
  using NINA.Joko.Plugins.HocusFocus.Interfaces;
  using NINA.Joko.Plugins.HocusFocus.StarDetection;
  using NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter;
  using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
  using NINA.Joko.Plugins.HocusFocus.Utility;
  using NINA.Profile.Interfaces;
  using NINA.WPF.Base.Interfaces.ViewModel;
  using NSubstitute;
  using NUnit.Framework;
  using System.Windows.Media;

  namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

      [TestFixture]
      public class PerFilterDetectionResolutionTests {

          private static IRenderedImage MakeImage(string filterName) {
              var metaData = new ImageMetaData();
              metaData.FilterWheel.Filter = filterName;
              var imageData = Substitute.For<IImageData>();
              imageData.MetaData.Returns(metaData);
              var image = Substitute.For<IRenderedImage>();
              image.RawImageData.Returns(imageData);
              return image;
          }

          private static (HocusFocusStarDetection detection, IPerFilterStarDetectionStore store) Build(IStarDetectionOptions liveOptions) {
              var profileService = Substitute.For<IProfileService>();
              profileService.ActiveProfile.CameraSettings.PixelSize.Returns(3.76);
              profileService.ActiveProfile.TelescopeSettings.FocalLength.Returns(1000d);
              profileService.ActiveProfile.ApplicationSettings.SelectedPluggableBehaviors
                  .Returns(new AsyncObservableCollection<KeyValuePair<string, string>>());
              var focuserMediator = Substitute.For<IFocuserMediator>();
              focuserMediator.GetInfo().Returns(new FocuserInfo());
              var store = Substitute.For<IPerFilterStarDetectionStore>();
              var detection = new HocusFocusStarDetection(
                  imageStatisticsVM: Substitute.For<IImageStatisticsVM>(),
                  profileService: profileService,
                  focuserMediator: focuserMediator,
                  starDetectionOptions: liveOptions,
                  alglibAPI: new AlglibAPI(),
                  perFilterStore: store);
              return (detection, store);
          }

          [Test]
          public void FeatureOff_ParamsBitIdenticalToDirectBuild() {
              var profile = Substitute.For<IProfileService>();
              var accessor = new InMemoryPluginOptionsAccessor();
              var options = new StarDetectionOptions(profile, accessor);
              options.UseAdvanced = true;
              options.NoiseClippingMultiplier = 5.5;
              options.StructureLayers = 6;

              var (detection, store) = Build(options);
              store.Enabled.Returns(false);

              var expected = HocusFocusStarDetection.BuildStarDetectorParams(options);
              expected.PixelScale = MathUtility.ArcsecPerPixel(3.76, 1000);
              expected.Region = StarDetectionRegion.Full;

              var actual = detection.GetStarDetectorParams(MakeImage("Ha"), StarDetectionRegion.Full, isAutoFocus: false);

              Assert.Multiple(() => {
                  // The cache key canonicalizes every output-affecting param (region + pixel scale included),
                  // so key equality is the codebase's own bit-identity check for detector params.
                  Assert.That(StarDetector.ComputeCacheKey(actual), Is.EqualTo(StarDetector.ComputeCacheKey(expected)));
                  // The two denylisted (output-neutral) fields are outside the key; compare them directly.
                  Assert.That(actual.StoreStructureMap, Is.EqualTo(expected.StoreStructureMap));
                  Assert.That(actual.SaveIntermediateFilesPath, Is.EqualTo(expected.SaveIntermediateFilesPath));
                  store.DidNotReceive().GetOrSeedSnapshot(Arg.Any<string>());
              });
          }

          [Test]
          public void FeatureOn_UsesFilterSnapshot_MachineLocalFromLiveOptions() {
              var liveOptions = new StarDetectionSettingsSnapshot {
                  NoiseClippingMultiplier = 4.0,
                  DebugMode = true,
                  SaveIntermediateImages = true,
                  IntermediateSavePath = @"C:\hf-debug",
                  PSFParallelPartitionSize = 250
              };
              var snapshot = new StarDetectionSettingsSnapshot {
                  NoiseClippingMultiplier = 7.5, // distinctive per-filter knob
                  DebugMode = false,             // stored snapshots hold scrubbed machine-local values
                  SaveIntermediateImages = false,
                  IntermediateSavePath = "",
                  PSFParallelPartitionSize = 100,
                  PixelSampleSize = 1.0
              };
              var (detection, store) = Build(liveOptions);
              store.Enabled.Returns(true);
              store.GetOrSeedSnapshot("Ha").Returns(snapshot);

              var actual = detection.GetStarDetectorParams(MakeImage("Ha"), StarDetectionRegion.Full, isAutoFocus: false);

              Assert.Multiple(() => {
                  Assert.That(actual.NoiseClippingMultiplier, Is.EqualTo(7.5));             // filter snapshot knob
                  Assert.That(actual.StoreStructureMap, Is.True);                           // DebugMode from live options
                  Assert.That(actual.SaveIntermediateFilesPath, Is.EqualTo(@"C:\hf-debug"));// Save* from live options
                  Assert.That(actual.PSFParallelPartitionSize, Is.EqualTo(250));            // machine-local from live options
                  Assert.That(liveOptions.SaveIntermediateImages, Is.False);                // one-shot reset targets the live options
              });
          }

          [Test]
          public void FeatureOn_EmptyFilterName_GetStarDetectorParamsThrows() {
              var (detection, store) = Build(new StarDetectionSettingsSnapshot());
              store.Enabled.Returns(true);

              var ex = Assert.Throws<PerFilterSettingsUnavailableException>(
                  () => detection.GetStarDetectorParams(MakeImage(""), StarDetectionRegion.Full, isAutoFocus: false));

              Assert.Multiple(() => {
                  Assert.That(ex.Message, Is.EqualTo("Per-filter star detection is enabled but the active filter is unknown - connect a filter wheel."));
                  store.DidNotReceive().GetOrSeedSnapshot(Arg.Any<string>());
              });
          }

          [Test]
          public void FeatureOn_OptionsOverrideWins_NoStoreConsult() {
              var (detection, store) = Build(new StarDetectionSettingsSnapshot());
              store.Enabled.Returns(true);
              var overrideSnapshot = new StarDetectionSettingsSnapshot { NoiseClippingMultiplier = 9.25, PixelSampleSize = 1.0 };

              // Even with an indeterminate filter, an explicit override wins verbatim — replay never consults the store.
              var actual = detection.GetStarDetectorParams(MakeImage(""), StarDetectionRegion.Full, isAutoFocus: false, optionsOverride: overrideSnapshot);

              Assert.Multiple(() => {
                  Assert.That(actual.NoiseClippingMultiplier, Is.EqualTo(9.25));
                  store.DidNotReceive().GetOrSeedSnapshot(Arg.Any<string>());
              });
          }
      }
  }
  ```

- [ ] **Step 4: Run the new fixture and confirm the expected failures**

  ```
  dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~PerFilterDetectionResolutionTests"
  ```

  Expected: `FeatureOn_UsesFilterSnapshot_MachineLocalFromLiveOptions` FAILS (`actual.NoiseClippingMultiplier` is 4.0, the live options value, not 7.5) and `FeatureOn_EmptyFilterName_GetStarDetectorParamsThrows` FAILS (no exception thrown). `FeatureOff_ParamsBitIdenticalToDirectBuild` and `FeatureOn_OptionsOverrideWins_NoStoreConsult` PASS — they are regression guards for behavior that must not change.

- [ ] **Step 5: Implement `ResolveEffectiveOptions` and route `GetStarDetectorParams` through it**

  In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/HocusFocusStarDetection.cs`, replace the 3-arg `GetStarDetectorParams` (lines 431–439):

  ```csharp
          public StarDetectorParams GetStarDetectorParams(IRenderedImage image, StarDetectionRegion starDetectionRegion, bool isAutoFocus) {
              var detectorParams = BuildStarDetectorParams(ResolveEffectiveOptions(image));
              ApplyDetectionImageContext(detectorParams, image, starDetectionRegion, isAutoFocus);
              if (!isAutoFocus) {
                  // Only save intermediate images for 1 detection. Doing this again should require the user to pick it again.
                  // SaveIntermediateImages is machine-local by scope decision, so the one-shot reset always targets the
                  // live options — never a per-filter snapshot clone.
                  starDetectionOptions.SaveIntermediateImages = false;
              }
              return detectorParams;
          }

          /// <summary>
          /// Resolves the options source for a detection with strict precedence: the per-filter snapshot for the
          /// image's capture-time filter (feature enabled; machine-local fields copied from the live options so a
          /// snapshot never overrides debug/save/parallelism settings), else the injected singleton. Feature enabled
          /// with an indeterminate filter throws <see cref="PerFilterSettingsUnavailableException"/> — the NINA-facing
          /// Detect soft-fails on it; typed callers propagate it to their existing failure handling. The explicit
          /// optionsOverride overload of GetStarDetectorParams bypasses this entirely (replay wins).
          /// </summary>
          private IStarDetectionOptions ResolveEffectiveOptions(IRenderedImage image) {
              if (!perFilterStore.Enabled) {
                  return starDetectionOptions;
              }
              var filterName = image?.RawImageData?.MetaData?.FilterWheel?.Filter;
              if (string.IsNullOrEmpty(filterName)) {
                  throw new PerFilterSettingsUnavailableException("Per-filter star detection is enabled but the active filter is unknown - connect a filter wheel.");
              }
              var snapshot = perFilterStore.GetOrSeedSnapshot(filterName);
              snapshot.CopyMachineLocalFrom(starDetectionOptions);
              return snapshot;
          }
  ```

  The `optionsOverride` overload (lines 444–454) needs no change: a non-null override already builds from the override verbatim, and the null-override branch delegates to the 3-arg version above (so the per-filter path applies exactly when no replay override is in play).

- [ ] **Step 6: Run the fixture — expect all 4 PASS**

  ```
  dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~PerFilterDetectionResolutionTests"
  ```

- [ ] **Step 7: Run the full suite (project invariant; also validates XAML via the build)**

  ```
  dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo
  ```

  Expected: all tests pass.

- [ ] **Step 8: Commit**

  ```
  git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/PerFilter/PerFilterSettingsUnavailableException.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/HocusFocusStarDetection.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/HocusFocusPlugin.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/HocusFocusStarDetectionTests.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/PerFilterDetectionResolutionTests.cs && GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(per-filter): resolve per-filter detection options in GetStarDetectorParams"
  ```

### Task 8: Soft-fail the NINA-facing Detect on an indeterminate filter

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/HocusFocusStarDetection.cs` (6-arg `Detect`, lines 260–280)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/PerFilterDetectionResolutionTests.cs` (extend)

- [ ] **Step 1: Write the failing soft-fail test**

  Append to the `PerFilterDetectionResolutionTests` fixture (inside the class, after `FeatureOn_OptionsOverrideWins_NoStoreConsult`):

  ```csharp
          [Test]
          public async Task FeatureOn_EmptyFilterName_InterfaceDetectSoftFailsWithEmptyResult() {
              var (detection, store) = Build(new StarDetectionSettingsSnapshot());
              store.Enabled.Returns(true);
              var p = new StarDetectionParams { IsAutoFocus = false };

              // The NINA-facing entry point must NEVER throw into the imaging pipeline: it returns a valid
              // zero-star result. (Notification.ShowWarning is a headless no-op under test — NINA's manager is
              // null without Application.Current — so the observable contract here is the returned result.)
              var result = await detection.Detect(MakeImage(""), PixelFormats.Gray16, p, progress: null, token: CancellationToken.None);

              Assert.Multiple(() => {
                  Assert.That(result, Is.InstanceOf<HocusFocusStarDetectionResult>());
                  Assert.That(result.DetectedStars, Is.EqualTo(0));
                  Assert.That(result.StarList, Is.Empty);
                  Assert.That(result.Params, Is.SameAs(p));
              });
          }
  ```

- [ ] **Step 2: Run it and confirm it fails with the propagated exception**

  ```
  dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~PerFilterDetectionResolutionTests"
  ```

  Expected: `FeatureOn_EmptyFilterName_InterfaceDetectSoftFailsWithEmptyResult` FAILS — errored with unhandled `NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter.PerFilterSettingsUnavailableException : Per-filter star detection is enabled but the active filter is unknown - connect a filter wheel.` (Task 7 made `GetStarDetectorParams` throw; nothing catches it yet.) The other 5 tests PASS.

- [ ] **Step 3: Catch the exception in the 6-arg `Detect`**

  In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/HocusFocusStarDetection.cs`, replace the body of the 6-arg `Detect` (lines 260–280). The 5-arg `IStarDetection.Detect` (line 256) delegates here, so this one catch covers both NINA-facing interface overloads:

  ```csharp
          public async Task<StarDetectionResult> Detect(IRenderedImage image, PixelFormat pf, StarDetectionParams p, IProgress<ApplicationStatus> progress, CancellationToken token, bool modelPSFForAutoFocus) {
              var selectedAutoFocusBehavior = profileService.ActiveProfile.ApplicationSettings.SelectedPluggableBehaviors.Where(k => k.Key == typeof(IAutoFocusVMFactory).FullName).ToList();
              var ninaStockAutoFocus = selectedAutoFocusBehavior.Count == 0 || selectedAutoFocusBehavior.First().Value == "NINA";
              var isNinaAutoFocus = ninaStockAutoFocus && p.IsAutoFocus;
              // UseAutoFocusCrop and ModelPSF are per-filter-scoped, so they must come from the SAME resolved
              // options object as every other knob in this detection (Task 7 review fix). Resolving here also
              // moves the indeterminate-filter throw inside the try below.
              IStarDetectionOptions effectiveOptions;
              StarDetectorParams detectorParams;
              try {
                  effectiveOptions = ResolveEffectiveOptions(image);
                  if (!effectiveOptions.UseAutoFocusCrop && !isNinaAutoFocus) {
                      p.UseROI = false;
                  }
                  var starDetectionRegion = StarDetectionRegion.FromStarDetectionParams(p);
                  detectorParams = BuildStarDetectorParams(effectiveOptions, image, starDetectionRegion, p.IsAutoFocus);
              } catch (PerFilterSettingsUnavailableException e) {
                  // Soft-fail: never throw into NINA's imaging pipeline. Warn on EVERY occurrence (no one-shot
                  // latch) so a misconfigured session cannot silently zero out all of its detections.
                  Logger.Warning(e.Message);
                  Notification.ShowWarning(e.Message);
                  return new HocusFocusStarDetectionResult() {
                      StarList = new List<DetectedStar>(),
                      DetectedStars = 0,
                      Params = p
                  };
              }
              // BuildStarDetectorParams forces ModelPSF off for auto-focus (speed); lift that for a Review-Frames run
              // so the per-star PSF properties are populated, honoring the effective PSF setting + fit type.
              if (modelPSFForAutoFocus) {
                  detectorParams.ModelPSF = effectiveOptions.ModelPSF;
              }
              var hocusFocusParams = ToHocusFocusParams(p);

              var detectionResult = await Detect(image, hocusFocusParams, detectorParams, progress, token);
              detectionResult.Params = p;
              return detectionResult;
          }
  ```

  No new usings needed: `Logger`/`Notification` (lines 17, 31), `List<>`/`DetectedStar` (lines 23, 18) are already imported. Typed callers (`GetStarDetectorParams` direct, `Detect(image, hocusFocusParams, detectorParams, …)`, `BuildDetectionContext`/`GateAndMeasure`) are deliberately untouched — the exception propagates to their existing failure handling, as `FeatureOn_EmptyFilterName_GetStarDetectorParamsThrows` pins.

- [ ] **Step 4: Run the fixture — expect all 6 PASS**

  ```
  dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~PerFilterDetectionResolutionTests"
  ```

- [ ] **Step 5: Run the full suite**

  ```
  dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo
  ```

  Expected: all tests pass.

- [ ] **Step 6: Commit**

  ```
  git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/HocusFocusStarDetection.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/PerFilterDetectionResolutionTests.cs && GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(per-filter): soft-fail NINA-facing Detect when the active filter is unknown"
  ```

### Task 9: Gate `HocusFocusVM.StartAutoFocus` when per-filter detection is on and no filter wheel is connected

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/HocusFocusVM.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/HocusFocusVMFactory.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TestDoubles/MediatorBundle.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/HocusFocusVMTests.cs`

This gate covers both the HF AutoFocus dockable and the sequencer's `RunAutofocus` (both go through the pluggable AF VM's `StartAutoFocus`). `Notification.ShowError` is a no-op in a headless test process (`Application.Current == null`), so tests assert the refusal via the return value and `AutoFocusEngineFactory.DidNotReceive().Create()`.

- [ ] **Step 1: Add the store plumbing to `HocusFocusVM` (no behavior change).**
  In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/HocusFocusVM.cs`:
  1. After the field `private readonly IApplicationDispatcher applicationDispatcher;` (line ~75), add:
  ```csharp
        private readonly IPerFilterStarDetectionStore perFilterStore;
  ```
  2. In the constructor signature, change:
  ```csharp
            IAlglibAPI alglibAPI,
            IApplicationDispatcher applicationDispatcher
        ) : base(profileService) {
  ```
  to:
  ```csharp
            IAlglibAPI alglibAPI,
            IApplicationDispatcher applicationDispatcher,
            IPerFilterStarDetectionStore perFilterStore = null
        ) : base(profileService) {
  ```
  3. After the assignment `this.applicationDispatcher = applicationDispatcher;` in the constructor body, add:
  ```csharp
            this.perFilterStore = perFilterStore;
  ```
  (`IPerFilterStarDetectionStore` resolves via the existing `using NINA.Joko.Plugins.HocusFocus.Interfaces;`. Optional-null matches `InspectorVM`'s null-tolerant optional-dependency idiom; null behaves as feature-off.)

- [ ] **Step 2: Wire the production path through `HocusFocusVMFactory`.**
  In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/HocusFocusVMFactory.cs`:
  1. After the field `private readonly IApplicationDispatcher applicationDispatcher;`, add:
  ```csharp
        private readonly IPerFilterStarDetectionStore perFilterStore;
  ```
  2. In the `[ImportingConstructor]` delegation, change the tail `HocusFocusPlugin.AlglibAPI, HocusFocusPlugin.ApplicationDispatcher) {` to:
  ```csharp
  HocusFocusPlugin.AlglibAPI, HocusFocusPlugin.ApplicationDispatcher, HocusFocusPlugin.PerFilterStarDetection) {
  ```
  3. In the testable constructor, change the last parameter `IApplicationDispatcher applicationDispatcher) {` to:
  ```csharp
            IApplicationDispatcher applicationDispatcher,
            IPerFilterStarDetectionStore perFilterStore = null) {
  ```
  and after `this.applicationDispatcher = applicationDispatcher;` add:
  ```csharp
            this.perFilterStore = perFilterStore;
  ```
  4. In `Create()` (line ~76), change the `new HocusFocusVM(...)` call to append the store:
  ```csharp
        public IAutoFocusVM Create() {
            return new HocusFocusVM(profileService, focuserMediator, autoFocusEngineFactory, autoFocusOptions, starDetectionOptions, filterWheelMediator, applicationStatusMediator, starDetectionSelector, alglibAPI, applicationDispatcher, perFilterStore);
        }
  ```

- [ ] **Step 3: Add the store to `MediatorBundle` and wire `BuildHocusFocusVM`.**
  In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TestDoubles/MediatorBundle.cs`:
  1. After the property `public ITiltAdapterOptions TiltAdapterOptions { get; } = Substitute.For<ITiltAdapterOptions>();`, add:
  ```csharp
    public IPerFilterStarDetectionStore PerFilterStarDetectionStore { get; } = Substitute.For<IPerFilterStarDetectionStore>();
  ```
  2. After the `WithGuiderConnected` helper, add:
  ```csharp
    public MediatorBundle WithPerFilterStarDetectionEnabled(bool enabled = true) {
        PerFilterStarDetectionStore.Enabled.Returns(enabled);
        return this;
    }
  ```
  3. In `BuildHocusFocusVM()`, change the last argument `applicationDispatcher: ApplicationDispatcher);` to:
  ```csharp
            applicationDispatcher: ApplicationDispatcher,
            perFilterStore: PerFilterStarDetectionStore);
  ```
  (Do NOT touch `BuildInspectorVM` yet — `InspectorVM` gains its parameter in Task 10.)

- [ ] **Step 4: Write the failing gate tests.**
  In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/HocusFocusVMTests.cs`, add the using (the file currently fully-qualifies `SynchronousApplicationDispatcher`; `Task`/`CancellationToken` come from implicit usings):
  ```csharp
  using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
  ```
  and add these tests inside the fixture, before `CountChanges`:
  ```csharp
        [Test]
        public async Task StartAutoFocus_PerFilterEnabledAndWheelDisconnected_RefusesWithoutStartingEngine() {
            var bundle = new MediatorBundle().WithFilterWheelConnected(false).WithPerFilterStarDetectionEnabled();
            var vm = bundle.BuildHocusFocusVM();

            var report = await vm.StartAutoFocus(imagingFilter: null, token: CancellationToken.None, progress: null);

            Assert.That(report, Is.Null);
            bundle.AutoFocusEngineFactory.DidNotReceive().Create();
            Assert.That(vm.AutoFocusInProgress, Is.False);
        }

        [Test]
        public async Task StartAutoFocus_PerFilterEnabledAndWheelConnected_ProceedsToEngine() {
            var bundle = new MediatorBundle().WithFilterWheelConnected(true).WithPerFilterStarDetectionEnabled();
            var vm = bundle.BuildHocusFocusVM();

            await vm.StartAutoFocus(imagingFilter: null, token: CancellationToken.None, progress: null);

            bundle.AutoFocusEngineFactory.Received(1).Create();
        }

        [Test]
        public async Task StartAutoFocus_PerFilterDisabled_WheelDisconnected_ProceedsToEngine() {
            var bundle = new MediatorBundle().WithFilterWheelConnected(false);
            var vm = bundle.BuildHocusFocusVM();

            await vm.StartAutoFocus(imagingFilter: null, token: CancellationToken.None, progress: null);

            bundle.AutoFocusEngineFactory.Received(1).Create();
        }
  ```
  (The two "proceeds" tests terminate cleanly on substitutes: `GetOptions()` returns null but `ApplyFrameReviewOptions` short-circuits on `IsInteractive == false`, and the substitute `Run(...)` returns a completed `Task<AutoFocusResult>` with a null result.)

- [ ] **Step 5: Run the fixture and expect exactly one failure.** From `/home/ghilios/src/hocus-focus`:
  ```
  dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~HocusFocusVMTests"
  ```
  Expected: `StartAutoFocus_PerFilterEnabledAndWheelDisconnected_RefusesWithoutStartingEngine` FAILS with `NSubstitute.Exceptions.ReceivedCallsException : Expected to receive no calls matching: Create()` (1 call actually received). The two "proceeds" tests and all pre-existing tests PASS.

- [ ] **Step 6: Implement the gate.**
  In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/HocusFocusVM.cs`, in `StartAutoFocus`, change:
  ```csharp
                if (AutoFocusInProgress) {
                    Notification.ShowError("Another AutoFocus is already in progress");
                    return null;
                }
  ```
  to:
  ```csharp
                if (AutoFocusInProgress) {
                    Notification.ShowError("Another AutoFocus is already in progress");
                    return null;
                }
                // Per-filter star detection keys settings off the capture-time filter name; without a
                // connected wheel every exposure would soft-fail, so refuse the run up front.
                if (perFilterStore?.Enabled == true && filterWheelMediator.GetInfo()?.Connected != true) {
                    Notification.ShowError("Per-filter star detection requires a connected filter wheel");
                    return null;
                }
  ```
  (This block is the one that `return null;`s — the similar block in `LoadSavedAutoFocusRun` returns `false` and must not be touched: replay needs no wheel.)

- [ ] **Step 7: Run the fixture and expect PASS.**
  ```
  dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~HocusFocusVMTests"
  ```
  Expected: all tests pass.

- [ ] **Step 8: Run the full suite.**
  ```
  dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo
  ```
  Expected: all tests pass (the build also validates XAML; the plugin csproj PostBuild xcopy into the local NINA plugin folder is harmless).

- [ ] **Step 9: Commit.**
  ```
  git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/HocusFocusVM.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/HocusFocusVMFactory.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TestDoubles/MediatorBundle.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/HocusFocusVMTests.cs && GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(per-filter): gate HocusFocus autofocus start on a connected filter wheel"
  ```

### Task 10: Gate `InspectorVM` full sensor runs and single-exposure analysis

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorVM.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TestDoubles/MediatorBundle.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/InspectorVMTests.cs`

Gating `AnalyzeAutoFocusImpl` covers both `RunAutoFocusAnalysisCommand` and the public `AnalyzeAutoFocus(...)` used by `RunAberrationInspector.Execute` and the tilt-wizard re-run delegate; gating `AnalyzeExposure` covers `RunExposureAnalysisCommand`. The saved-run replay paths (`RerunSavedAutoFocusAnalysisCommand` / `AnalyzeAutoFocusFromSavedPath`) are deliberately NOT gated — replay needs no wheel; capture-time metadata plus the detection soft-fail cover it (spec "Entry-point gates" section).

- [ ] **Step 1: Add the store plumbing to `InspectorVM` and wire `MediatorBundle.BuildInspectorVM` (no behavior change).**
  In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorVM.cs`:
  1. After the field `private readonly ITiltAdapterOptions tiltAdapterOptions;` (line ~105), add:
  ```csharp
        private readonly IPerFilterStarDetectionStore perFilterStarDetectionStore;
  ```
  2. In the `[ImportingConstructor]` delegation (line ~162), change the tail `HocusFocusPlugin.TiltAdapterOptions, HocusFocusPlugin.TiltDeviceConnectionService) {` to:
  ```csharp
  HocusFocusPlugin.TiltAdapterOptions, HocusFocusPlugin.TiltDeviceConnectionService, perFilterStarDetectionStore: HocusFocusPlugin.PerFilterStarDetection) {
  ```
  (Named argument — the intervening delegate parameters keep their `null` defaults.)
  3. In the testable constructor signature, change:
  ```csharp
            Func<CancellationToken, Task<bool>> reRunAnalysisAsync = null) : base(profileService) {
  ```
  to:
  ```csharp
            Func<CancellationToken, Task<bool>> reRunAnalysisAsync = null,
            IPerFilterStarDetectionStore perFilterStarDetectionStore = null) : base(profileService) {
  ```
  4. After `this.applicationDispatcher = applicationDispatcher;` (line ~203) in the ctor body, add:
  ```csharp
            this.perFilterStarDetectionStore = perFilterStarDetectionStore;
  ```
  5. In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TestDoubles/MediatorBundle.cs`, in `BuildInspectorVM`, change the last argument `reRunAnalysisAsync: reRunAnalysisAsync);` to:
  ```csharp
            reRunAnalysisAsync: reRunAnalysisAsync,
            perFilterStarDetectionStore: PerFilterStarDetectionStore);
  ```

- [ ] **Step 2: Write the failing gate tests.**
  In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/InspectorVMTests.cs`, add the using:
  ```csharp
  using CommunityToolkit.Mvvm.Input;
  ```
  and add these tests inside the fixture (before the closing brace; the fixture is already `[Apartment(ApartmentState.STA)]`):
  ```csharp
        [Test]
        public async Task AnalyzeAutoFocus_PerFilterEnabledAndWheelDisconnected_RefusesWithoutStartingEngine() {
            var bundle = new MediatorBundle().WithFilterWheelConnected(false).WithPerFilterStarDetectionEnabled();
            var vm = bundle.BuildInspectorVM();

            var result = await vm.AnalyzeAutoFocus(System.Threading.CancellationToken.None);

            Assert.That(result, Is.False);
            bundle.AutoFocusEngineFactory.DidNotReceive().Create();
        }

        [Test]
        public async Task AnalyzeAutoFocus_PerFilterEnabledAndWheelConnected_ProceedsToEngine() {
            var bundle = new MediatorBundle().WithFilterWheelConnected(true).WithPerFilterStarDetectionEnabled();
            var vm = bundle.BuildInspectorVM();

            await vm.AnalyzeAutoFocus(System.Threading.CancellationToken.None);

            bundle.AutoFocusEngineFactory.Received(1).Create();
        }

        [Test]
        public async Task RunExposureAnalysis_PerFilterEnabledAndWheelDisconnected_RefusesWithoutStartingEngine() {
            var bundle = new MediatorBundle().WithFilterWheelConnected(false).WithPerFilterStarDetectionEnabled();
            var vm = bundle.BuildInspectorVM();

            await ((IAsyncRelayCommand)vm.RunExposureAnalysisCommand).ExecuteAsync(null);

            bundle.AutoFocusEngineFactory.DidNotReceive().Create();
        }

        [Test]
        public async Task RunExposureAnalysis_PerFilterDisabled_WheelDisconnected_ProceedsToEngine() {
            var bundle = new MediatorBundle().WithFilterWheelConnected(false);
            var vm = bundle.BuildInspectorVM();

            await ((IAsyncRelayCommand)vm.RunExposureAnalysisCommand).ExecuteAsync(null);

            bundle.AutoFocusEngineFactory.Received(1).Create();
        }
  ```
  (The "proceeds" paths terminate deterministically on substitutes: the full-run body throws an NRE on the null `AutoFocusEngineOptions` inside the `Task.Run` and is caught by the existing `catch (Exception)` → returns false; the exposure loop exits after one iteration because `starDetectionSelector.GetBehavior() as IHocusFocusStarDetection` is null and `LoopingExposureAnalysis` defaults false. Both `Create()` assertions run only after the awaited task completes.)

- [ ] **Step 3: Run the fixture and expect exactly two failures.**
  ```
  dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~InspectorVMTests"
  ```
  Expected: `AnalyzeAutoFocus_PerFilterEnabledAndWheelDisconnected_RefusesWithoutStartingEngine` and `RunExposureAnalysis_PerFilterEnabledAndWheelDisconnected_RefusesWithoutStartingEngine` FAIL with `NSubstitute.Exceptions.ReceivedCallsException : Expected to receive no calls matching: Create()`. All other tests PASS.

- [ ] **Step 4: Implement both gates.**
  In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorVM.cs`:
  1. In `AnalyzeAutoFocusImpl` (line ~456), change:
  ```csharp
        private async Task<bool> AnalyzeAutoFocusImpl(bool captureCameraBlock, AutoFocusSaveOverride saveOverride = null) {
            var localAnalyzeTask = analyzeTask;
            if (localAnalyzeTask != null && !localAnalyzeTask.IsCompleted) {
                Notification.ShowError("Analysis still in progress");
                return false;
            }
  ```
  to:
  ```csharp
        private async Task<bool> AnalyzeAutoFocusImpl(bool captureCameraBlock, AutoFocusSaveOverride saveOverride = null) {
            var localAnalyzeTask = analyzeTask;
            if (localAnalyzeTask != null && !localAnalyzeTask.IsCompleted) {
                Notification.ShowError("Analysis still in progress");
                return false;
            }
            // Per-filter star detection keys settings off the capture-time filter name; without a
            // connected wheel every exposure would soft-fail, so refuse the run up front.
            if (perFilterStarDetectionStore?.Enabled == true && filterWheelMediator.GetInfo()?.Connected != true) {
                Notification.ShowError("Per-filter star detection requires a connected filter wheel");
                return false;
            }
  ```
  2. In `AnalyzeExposure` (line ~1130), change:
  ```csharp
        private async Task<bool> AnalyzeExposure() {
            var localAnalyzeTask = analyzeTask;
            if (analyzeTask != null && !analyzeTask.IsCompleted) {
                Notification.ShowError("Analysis still in progress");
                return false;
            }
  ```
  to:
  ```csharp
        private async Task<bool> AnalyzeExposure() {
            var localAnalyzeTask = analyzeTask;
            if (analyzeTask != null && !analyzeTask.IsCompleted) {
                Notification.ShowError("Analysis still in progress");
                return false;
            }
            if (perFilterStarDetectionStore?.Enabled == true && filterWheelMediator.GetInfo()?.Connected != true) {
                Notification.ShowError("Per-filter star detection requires a connected filter wheel");
                return false;
            }
  ```

- [ ] **Step 5: Run the fixture and expect PASS.**
  ```
  dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~InspectorVMTests"
  ```
  Expected: all tests pass.

- [ ] **Step 6: Run the full suite.**
  ```
  dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo
  ```
  Expected: all tests pass (the bundle's store substitute defaults `Enabled` to false, so every pre-existing `BuildInspectorVM` test runs feature-off).

- [ ] **Step 7: Commit.**
  ```
  git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorVM.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TestDoubles/MediatorBundle.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/InspectorVMTests.cs && GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(per-filter): gate aberration inspector run and exposure analysis on a connected filter wheel"
  ```

### Task 11: Add the filter-wheel validation issue to `RunAberrationInspector.Validate()`

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/SequenceItems/RunAberrationInspector.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/SequenceItems/RunAberrationInspectorTests.cs`

`RunAberrationInspector` has only its `[ImportingConstructor]` today; it gains a public 6-arg constructor as the testable path (mirroring `InspectorVM`'s MEF-delegates-to-testable pattern), with the MEF ctor passing `HocusFocusPlugin.PerFilterStarDetection`. Execution-time refusal already comes for free via Task 10 (`Execute` funnels through `InspectorVM.AnalyzeAutoFocus`); this task surfaces the pre-run sequencer issue.

- [ ] **Step 1: Add the store plumbing to `RunAberrationInspector` (no behavior change).**
  In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/SequenceItems/RunAberrationInspector.cs`:
  1. Add the using (the class lives in `NINA.Sequencer.SequenceItem.Autofocus`, so `HocusFocusPlugin` is not otherwise visible):
  ```csharp
  using NINA.Joko.Plugins.HocusFocus;
  ```
  2. After the field `private readonly IInspectorVMFactory inspectorVMFactory;`, add:
  ```csharp
        private readonly IPerFilterStarDetectionStore perFilterStarDetectionStore;
  ```
  3. Replace the constructors:
  ```csharp
        [ImportingConstructor]
        public RunAberrationInspector(
            IProfileService profileService, ICameraMediator cameraMediator, IFocuserMediator focuserMediator, IFilterWheelMediator filterWheelMediator, IInspectorVMFactory inspectorVMFactory) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.focuserMediator = focuserMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.inspectorVMFactory = inspectorVMFactory;
        }

        private RunAberrationInspector(RunAberrationInspector cloneMe) : this(cloneMe.profileService, cloneMe.cameraMediator, cloneMe.focuserMediator, cloneMe.filterWheelMediator, cloneMe.inspectorVMFactory) {
            CopyMetaData(cloneMe);
        }
  ```
  with:
  ```csharp
        [ImportingConstructor]
        public RunAberrationInspector(
            IProfileService profileService, ICameraMediator cameraMediator, IFocuserMediator focuserMediator, IFilterWheelMediator filterWheelMediator, IInspectorVMFactory inspectorVMFactory)
            : this(profileService, cameraMediator, focuserMediator, filterWheelMediator, inspectorVMFactory, HocusFocusPlugin.PerFilterStarDetection) {
        }

        public RunAberrationInspector(
            IProfileService profileService, ICameraMediator cameraMediator, IFocuserMediator focuserMediator, IFilterWheelMediator filterWheelMediator, IInspectorVMFactory inspectorVMFactory, IPerFilterStarDetectionStore perFilterStarDetectionStore) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.focuserMediator = focuserMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.inspectorVMFactory = inspectorVMFactory;
            this.perFilterStarDetectionStore = perFilterStarDetectionStore;
        }

        private RunAberrationInspector(RunAberrationInspector cloneMe) : this(cloneMe.profileService, cloneMe.cameraMediator, cloneMe.focuserMediator, cloneMe.filterWheelMediator, cloneMe.inspectorVMFactory, cloneMe.perFilterStarDetectionStore) {
            CopyMetaData(cloneMe);
        }
  ```
  (`HocusFocusPlugin.PerFilterStarDetection` may be null if MEF composes the sequence item before the plugin ctor runs — the null-conditional gate in Step 4 treats that as feature-off, same tolerance as `HocusFocusPlugin.LaunchStarDetectionOptimizer` documents.)

- [ ] **Step 2: Extend the test builder and write the failing tests.**
  In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/SequenceItems/RunAberrationInspectorTests.cs`:
  1. Add the using:
  ```csharp
  using NINA.Equipment.Equipment.MyFilterWheel;
  ```
  2. Replace the `Build` helper with (existing call sites use named args and are unaffected):
  ```csharp
        private static (RunAberrationInspector item, ICameraMediator camera, IFocuserMediator focuser, IFilterWheelMediator filterWheel, IInspectorVMFactory factory) Build(
            bool cameraConnected, bool focuserConnected, bool perFilterEnabled = false, bool filterWheelConnected = false) {
            var profileService = Substitute.For<IProfileService>();
            var cameraMediator = Substitute.For<ICameraMediator>();
            var focuserMediator = Substitute.For<IFocuserMediator>();
            var filterWheelMediator = Substitute.For<IFilterWheelMediator>();
            var inspectorVMFactory = Substitute.For<IInspectorVMFactory>();
            var perFilterStore = Substitute.For<IPerFilterStarDetectionStore>();

            cameraMediator.GetInfo().Returns(new CameraInfo { Connected = cameraConnected });
            focuserMediator.GetInfo().Returns(new FocuserInfo { Connected = focuserConnected });
            filterWheelMediator.GetInfo().Returns(new FilterWheelInfo { Connected = filterWheelConnected });
            perFilterStore.Enabled.Returns(perFilterEnabled);

            var item = new RunAberrationInspector(profileService, cameraMediator, focuserMediator, filterWheelMediator, inspectorVMFactory, perFilterStore);
            return (item, cameraMediator, focuserMediator, filterWheelMediator, inspectorVMFactory);
        }
  ```
  3. Add these tests inside the fixture:
  ```csharp
        [Test]
        public void Validate_PerFilterEnabledAndWheelDisconnected_AddsIssueAndReturnsFalse() {
            var (item, _, _, _, _) = Build(cameraConnected: true, focuserConnected: true, perFilterEnabled: true, filterWheelConnected: false);

            var result = item.Validate();

            Assert.Multiple(() => {
                Assert.That(result, Is.False);
                Assert.That(item.Issues, Has.Count.EqualTo(1));
                Assert.That(item.Issues[0], Is.EqualTo("Per-filter star detection requires a connected filter wheel"));
            });
        }

        [Test]
        public void Validate_PerFilterEnabledAndWheelConnected_NoIssuesAndReturnsTrue() {
            var (item, _, _, _, _) = Build(cameraConnected: true, focuserConnected: true, perFilterEnabled: true, filterWheelConnected: true);

            var result = item.Validate();

            Assert.Multiple(() => {
                Assert.That(result, Is.True);
                Assert.That(item.Issues, Is.Empty);
            });
        }

        [Test]
        public void Validate_PerFilterDisabled_WheelDisconnected_NoIssuesAndReturnsTrue() {
            var (item, _, _, _, _) = Build(cameraConnected: true, focuserConnected: true, perFilterEnabled: false, filterWheelConnected: false);

            var result = item.Validate();

            Assert.Multiple(() => {
                Assert.That(result, Is.True);
                Assert.That(item.Issues, Is.Empty);
            });
        }

        [Test]
        public void Validate_NullPerFilterStore_TreatedAsDisabled() {
            var cameraMediator = Substitute.For<ICameraMediator>();
            var focuserMediator = Substitute.For<IFocuserMediator>();
            cameraMediator.GetInfo().Returns(new CameraInfo { Connected = true });
            focuserMediator.GetInfo().Returns(new FocuserInfo { Connected = true });

            var item = new RunAberrationInspector(Substitute.For<IProfileService>(), cameraMediator, focuserMediator, Substitute.For<IFilterWheelMediator>(), Substitute.For<IInspectorVMFactory>(), perFilterStarDetectionStore: null);

            Assert.Multiple(() => {
                Assert.That(item.Validate(), Is.True);
                Assert.That(item.Issues, Is.Empty);
            });
        }
  ```

- [ ] **Step 3: Run the fixture and expect exactly one failure.**
  ```
  dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~RunAberrationInspectorTests"
  ```
  Expected: `Validate_PerFilterEnabledAndWheelDisconnected_AddsIssueAndReturnsFalse` FAILS (`Expected: False But was: True` / `Issues` count 0 instead of 1). All other tests, including the three other new ones, PASS.

- [ ] **Step 4: Implement the validation issue.**
  In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/SequenceItems/RunAberrationInspector.cs`, change `Validate()`:
  ```csharp
        public bool Validate() {
            var i = new List<string>();
            if (!cameraMediator.GetInfo().Connected) {
                i.Add(Loc.Instance["LblCameraNotConnected"]);
            }
            if (!focuserMediator.GetInfo().Connected) {
                i.Add(Loc.Instance["LblFocuserNotConnected"]);
            }

            Issues = i;
            return issues.Count == 0;
        }
  ```
  to:
  ```csharp
        public bool Validate() {
            var i = new List<string>();
            if (!cameraMediator.GetInfo().Connected) {
                i.Add(Loc.Instance["LblCameraNotConnected"]);
            }
            if (!focuserMediator.GetInfo().Connected) {
                i.Add(Loc.Instance["LblFocuserNotConnected"]);
            }
            if (perFilterStarDetectionStore?.Enabled == true && filterWheelMediator.GetInfo()?.Connected != true) {
                i.Add("Per-filter star detection requires a connected filter wheel");
            }

            Issues = i;
            return issues.Count == 0;
        }
  ```

- [ ] **Step 5: Run the fixture and expect PASS.**
  ```
  dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~RunAberrationInspectorTests"
  ```
  Expected: all tests pass.

- [ ] **Step 6: Run the full suite.**
  ```
  dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo
  ```
  Expected: all tests pass.

- [ ] **Step 7: Commit.**
  ```
  git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/SequenceItems/RunAberrationInspector.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/SequenceItems/RunAberrationInspectorTests.cs && GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(per-filter): surface filter-wheel requirement as RunAberrationInspector validation issue"
  ```

### Task 12: Copy-from-filter flow + dual-host per-filter binding surface

The Star Detection options page is one shared template (`HocusFocus_StarDetection_Options` in `Resources/OptionsDataTemplates.xaml`) rendered by two hosts with different DataContexts: the plugin options page (`Options.xaml` line 131, DataContext = the `HocusFocusPlugin` instance) and the Imaging dockable (`StarDetection/DataTemplates.xaml` line 48, DataContext = `StarDetectionOptionsVM`). Every binding path in the template must therefore resolve on BOTH hosts (the existing precedent: `StarDetectionOptions.*`, `OptimizeStarDetectionCommand`, `ImportStarDetectionSettingsCommand` exist on both). This task adds the copy-from-filter routine (delegate-injected seam for tests), wires the command on both hosts via the `LaunchStarDetectionOptimizer`-style static delegation (`HocusFocusPlugin.cs` lines 179-182, 324; `StarDetectionOptionsVM.cs` lines 51-55), and exposes identically-named `PerFilterStore` / `PerFilterEditBinder` instance properties on both hosts (WPF `Binding Path` cannot resolve static properties, so the plugin gets instance wrappers over its statics).

Prerequisites from earlier task groups: `Interfaces/IPerFilterStarDetectionStore.cs`, `StarDetection/PerFilter/PerFilterEditBinder.cs` (namespace `NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter`), and the `HocusFocusPlugin.PerFilterStarDetection` / `HocusFocusPlugin.PerFilterStarDetectionEditBinder` statics. The test project references the plugin via `<ProjectReference>` and the plugin has `[assembly: InternalsVisibleTo("Joko.NINA.Plugins.HocusFocus.Tests")]` (`Properties/AssemblyInfo.cs` line 18), so no test-csproj edits are needed and `internal` members are directly testable.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionSettingsIO.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/HocusFocusPlugin.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionOptionsVM.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectionCopyFromFilterTests.cs` (new)

- [ ] **Step 1: Write the failing tests for the copy routine.** Create `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectionCopyFromFilterTests.cs` (the test project has `ImplicitUsings` enabled, so `Task`, `IReadOnlyList<>` etc. need no using):

```csharp
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;
using NINA.Profile.Interfaces;
using NSubstitute;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    [TestFixture]
    public class StarDetectionCopyFromFilterTests {

        private static StarDetectionOptions NewOptions() =>
            new StarDetectionOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());

        private static StarDetectionSettingsSnapshot AdvancedSnapshot(double brightnessSensitivity) {
            var source = NewOptions();
            source.UseAdvanced = true;
            source.BrightnessSensitivity = brightnessSensitivity;
            source.DebugMode = true;
            source.PSFParallelPartitionSize = 999;
            return StarDetectionSettingsSnapshot.FromOptions(source);
        }

        [Test]
        public async Task CopyFromFilter_OnApply_LoadsSourceSnapshotIntoBuffer() {
            var store = Substitute.For<IPerFilterStarDetectionStore>();
            store.GetOrSeedSnapshot("Ha").Returns(AdvancedSnapshot(8.4));

            var buffer = NewOptions();
            buffer.UseAdvanced = true;
            buffer.BrightnessSensitivity = 2.0;
            buffer.DebugMode = false;
            buffer.PSFParallelPartitionSize = 50;

            IReadOnlyList<StarDetectionSettingDiffRow> shownRows = null;
            string shownSummary = null;
            await StarDetectionSettingsIO.CopyFromFilterAsync("Ha", store, buffer, (rows, summary) => {
                shownRows = rows;
                shownSummary = summary;
                return Task.FromResult(true);
            });

            Assert.Multiple(() => {
                Assert.That(shownRows, Is.Not.Empty);
                Assert.That(shownSummary, Does.Contain("Ha"));
                Assert.That(buffer.BrightnessSensitivity, Is.EqualTo(8.4));
                // Machine-local knobs never transfer between filters (ApplyImportedSnapshot semantics).
                Assert.That(buffer.DebugMode, Is.False);
                Assert.That(buffer.PSFParallelPartitionSize, Is.EqualTo(50));
            });
        }

        [Test]
        public async Task CopyFromFilter_OnCancel_LeavesBufferUntouched() {
            var store = Substitute.For<IPerFilterStarDetectionStore>();
            store.GetOrSeedSnapshot("Ha").Returns(AdvancedSnapshot(8.4));

            var buffer = NewOptions();
            buffer.UseAdvanced = true;
            buffer.BrightnessSensitivity = 2.0;

            await StarDetectionSettingsIO.CopyFromFilterAsync("Ha", store, buffer, (rows, summary) => Task.FromResult(false));

            Assert.That(buffer.BrightnessSensitivity, Is.EqualTo(2.0));
        }

        [Test]
        public async Task CopyFromFilter_IdenticalSettings_SkipsDialog() {
            var buffer = NewOptions();
            var store = Substitute.For<IPerFilterStarDetectionStore>();
            store.GetOrSeedSnapshot("Ha").Returns(StarDetectionSettingsSnapshot.FromOptions(buffer));

            var confirmCalled = false;
            await StarDetectionSettingsIO.CopyFromFilterAsync("Ha", store, buffer, (rows, summary) => {
                confirmCalled = true;
                return Task.FromResult(true);
            });

            Assert.That(confirmCalled, Is.False);
        }

        [Test]
        public async Task CopyFromFilter_EmptySourceName_IsANoOp() {
            var store = Substitute.For<IPerFilterStarDetectionStore>();
            var buffer = NewOptions();

            var confirmCalled = false;
            await StarDetectionSettingsIO.CopyFromFilterAsync("", store, buffer, (rows, summary) => {
                confirmCalled = true;
                return Task.FromResult(true);
            });

            Assert.That(confirmCalled, Is.False);
            store.DidNotReceive().GetOrSeedSnapshot(Arg.Any<string>());
        }
    }
}
```

- [ ] **Step 2: Run the fixture and confirm it FAILS to build.** From the repo root (allow a long timeout; builds are slow):

```
dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~StarDetectionCopyFromFilterTests"
```

Expected: build error `CS0117: 'StarDetectionSettingsIO' does not contain a definition for 'CopyFromFilterAsync'` (no tests run).

- [ ] **Step 3: Implement `CopyFromFilterAsync` in `StarDetectionSettingsIO`.** In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionSettingsIO.cs`, add two usings to the existing block (after `using NINA.Core.Utility.WindowService;` at line 14):

```csharp
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System.Collections.Generic;
```

Then append these methods after `ImportAsync` (i.e., after its closing brace at line 107, before `BuildSourceSummary`):

```csharp
        /// <summary>Copies another filter's star-detection settings onto the current edit buffer (per-filter mode).
        /// Reuses the import diff-preview dialog; a cancelled dialog or an identical source is a no-op. The edit
        /// binder's mirror persists the applied values to the edited filter's stored set.</summary>
        public static Task CopyFromFilterAsync(
                string sourceFilterName,
                IPerFilterStarDetectionStore store,
                StarDetectionOptions options,
                IWindowServiceFactory windowServiceFactory) {
            return CopyFromFilterAsync(sourceFilterName, store, options,
                (rows, summary) => ImportStarDetectionPreview.ShowAsync(windowServiceFactory, new ImportStarDetectionPreviewVM(rows, summary)));
        }

        /// <summary>Delegate-injected core of the copy-from-filter flow (unit-test seam — no WPF dialog).
        /// <paramref name="confirmDiff"/> receives the diff rows plus a provenance line and returns the decision.</summary>
        internal static async Task CopyFromFilterAsync(
                string sourceFilterName,
                IPerFilterStarDetectionStore store,
                StarDetectionOptions options,
                Func<IReadOnlyList<StarDetectionSettingDiffRow>, string, Task<bool>> confirmDiff) {
            if (string.IsNullOrEmpty(sourceFilterName) || store == null || options == null) {
                return;
            }
            try {
                var snapshot = store.GetOrSeedSnapshot(sourceFilterName);
                var diff = StarDetectionSettingsDiff.BuildDiff(options, snapshot);
                if (diff.Count == 0) {
                    Notification.ShowInformation($"'{sourceFilterName}' settings match the current settings; nothing to change.");
                    return;
                }

                var apply = await confirmDiff(diff, $"Copied from filter '{sourceFilterName}'");
                if (!apply) {
                    return;
                }

                options.ApplyImportedSnapshot(snapshot);
                Logger.Info($"Copied star detection settings from filter '{sourceFilterName}' ({diff.Count} setting(s) changed)");
                Notification.ShowInformation($"Copied star detection settings from '{sourceFilterName}'");
            } catch (Exception ex) {
                Logger.Error(ex, $"Failed to copy star detection settings from filter '{sourceFilterName}'");
                Notification.ShowError($"Failed to copy star detection settings: {ex.Message}");
            }
        }
```

(`Notification.*` is safe in headless unit tests: with no `Application.Current` its manager is null and every call is a no-op. `StarDetectionSettingsSnapshot` implements `IStarDetectionOptions`, so it feeds `BuildDiff` and `ApplyImportedSnapshot` directly.)

- [ ] **Step 4: Run the fixture and confirm PASS, then commit.**

```
dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~StarDetectionCopyFromFilterTests"
```

Expected: 4 passed. Then:

```
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionSettingsIO.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectionCopyFromFilterTests.cs && GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(per-filter): copy-from-filter settings flow with diff preview"
```

- [ ] **Step 5: Write the failing dual-host binding-surface test.** Append to the fixture in `StarDetectionCopyFromFilterTests.cs` (and add `using NINA.Joko.Plugins.HocusFocus;` to its usings block, alphabetically first among the `NINA.Joko` lines):

```csharp
        [Test]
        public void PerFilterBindingSurface_IdenticalOnBothHosts() {
            // The shared HocusFocus_StarDetection_Options template binds these DataContext-relative paths. Both
            // hosts — the plugin options page (DataContext = HocusFocusPlugin) and the Imaging dockable
            // (DataContext = StarDetectionOptionsVM) — must expose identically-named instance properties for the
            // same XAML to resolve on either.
            var bindingRoots = new[] { "PerFilterStore", "PerFilterEditBinder", "CopyStarDetectionFromFilterCommand" };
            Assert.Multiple(() => {
                foreach (var name in bindingRoots) {
                    Assert.That(typeof(HocusFocusPlugin).GetProperty(name), Is.Not.Null, $"HocusFocusPlugin.{name}");
                    Assert.That(typeof(StarDetectionOptionsVM).GetProperty(name), Is.Not.Null, $"StarDetectionOptionsVM.{name}");
                }
            });
        }
```

- [ ] **Step 6: Run the fixture and confirm the new test FAILS.** Same filtered command as Step 4. Expected: `PerFilterBindingSurface_IdenticalOnBothHosts` fails with `Expected: not null But was: null` for `HocusFocusPlugin.PerFilterStore` (and the other missing properties); the four Step-1 tests still pass.

- [ ] **Step 7: Wire the plugin host.** In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/HocusFocusPlugin.cs` (verify `using NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter;` is present — the earlier statics task group added it; add it if absent). Three edits:

(a) In the constructor, insert immediately after the `ImportStarDetectionSettingsCommand = new AsyncRelayCommand(...)` assignment (line 182; the file aliases `AsyncRelayCommand` to the non-generic type, so the generic form is fully qualified):

```csharp
            CopyStarDetectionFromFilterCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand<string>(CopyStarDetectionFromFilter);
            LaunchCopyStarDetectionFromFilter = CopyStarDetectionFromFilter;
```

(b) Insert this method after the closing brace of `OptimizeStarDetection()` (line 236):

```csharp
        private Task CopyStarDetectionFromFilter(string sourceFilterName) {
            return StarDetectionSettingsIO.CopyFromFilterAsync(sourceFilterName, PerFilterStarDetection, StarDetectionOptions, windowServiceFactory);
        }
```

(c) Insert after the `public static Action LaunchStarDetectionOptimizer { get; private set; }` property (line 324):

```csharp
        /// <summary>
        /// Shared launcher for the per-filter "Copy Settings From" flow, mirroring
        /// <see cref="LaunchStarDetectionOptimizer"/>: set by the plugin constructor so the Imaging-pane
        /// StarDetectionOptionsVM can run the same copy dialog without re-wiring its dependencies. May be null
        /// when no plugin instance has been constructed (e.g. unit tests).
        /// </summary>
        public static Func<string, Task> LaunchCopyStarDetectionFromFilter { get; private set; }

        // Instance wrappers over the per-filter statics: the shared HocusFocus_StarDetection_Options template
        // binds DataContext-relative paths (PerFilterStore.* / PerFilterEditBinder.*), and a WPF Binding Path
        // cannot resolve static properties, so both hosts expose the same instance property names.
        public IPerFilterStarDetectionStore PerFilterStore => PerFilterStarDetection;

        public PerFilterEditBinder PerFilterEditBinder => PerFilterStarDetectionEditBinder;
```

and insert after `public ICommand ImportStarDetectionSettingsCommand { get; private set; }` (line 344):

```csharp
        public ICommand CopyStarDetectionFromFilterCommand { get; private set; }
```

- [ ] **Step 8: Wire the dockable host.** Replace the contents of `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionOptionsVM.cs` with:

```csharp
#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Core.Utility.WindowService;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;
using AsyncRelayCommand = CommunityToolkit.Mvvm.Input.AsyncRelayCommand;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    [Export(typeof(IDockableVM))]
    public class StarDetectionOptionsVM : DockableVM {

        // WindowServiceFactory is not a MEF export (NINA exposes the concrete type with a default ctor only), so it is
        // instantiated directly here, mirroring HocusFocusPlugin. Used to show the import-confirmation dialog.
        private readonly IWindowServiceFactory windowServiceFactory = new WindowServiceFactory();

        [ImportingConstructor]
        public StarDetectionOptionsVM(IProfileService profileService)
            : base(profileService) {
            this.StarDetectionOptions = HocusFocusPlugin.StarDetectionOptions;
            this.PerFilterStore = HocusFocusPlugin.PerFilterStarDetection;
            this.PerFilterEditBinder = HocusFocusPlugin.PerFilterStarDetectionEditBinder;
            this.Title = "Star Detection Options";

            var dict = new ResourceDictionary();
            dict.Source = new Uri("NINA.Joko.Plugins.HocusFocus;component/StarDetection/DataTemplates.xaml", UriKind.RelativeOrAbsolute);
            ImageGeometry = (System.Windows.Media.GeometryGroup)dict["HocusFocusDetectStarsSVG"];
            ImageGeometry.Freeze();

            ChooseIntermediatePathDiagCommand = new RelayCommand(ChooseIntermediatePathDiag);
            OptimizeStarDetectionCommand = new RelayCommand(OptimizeStarDetection);
            ExportStarDetectionSettingsCommand = new RelayCommand(() => StarDetectionSettingsIO.Export(StarDetectionOptions));
            ImportStarDetectionSettingsCommand = new AsyncRelayCommand(() => StarDetectionSettingsIO.ImportAsync(StarDetectionOptions, windowServiceFactory));
            CopyStarDetectionFromFilterCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand<string>(CopyStarDetectionFromFilter);
        }

        private void OptimizeStarDetection() {
            // Delegates to the shared launcher set by HocusFocusPlugin. Null-safe so the command is a no-op
            // when no plugin instance has been constructed (e.g. a unit test constructing this VM directly).
            HocusFocusPlugin.LaunchStarDetectionOptimizer?.Invoke();
        }

        private Task CopyStarDetectionFromFilter(string sourceFilterName) {
            // Same static-delegation pattern as OptimizeStarDetection: the plugin owns the store/buffer/dialog wiring.
            return HocusFocusPlugin.LaunchCopyStarDetectionFromFilter?.Invoke(sourceFilterName) ?? Task.CompletedTask;
        }

        private void ChooseIntermediatePathDiag() {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog()) {
                dialog.SelectedPath = StarDetectionOptions.IntermediateSavePath;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                    StarDetectionOptions.IntermediateSavePath = dialog.SelectedPath;
                }
            }
        }

        public override bool IsTool { get; } = true;

        public StarDetectionOptions StarDetectionOptions { get; private set; }

        // Same property names as HocusFocusPlugin so the shared HocusFocus_StarDetection_Options template's
        // binding paths resolve on both hosts. Null when no plugin instance exists (unit tests) — bindings no-op.
        public IPerFilterStarDetectionStore PerFilterStore { get; private set; }

        public PerFilterEditBinder PerFilterEditBinder { get; private set; }

        public ICommand ChooseIntermediatePathDiagCommand { get; private set; }

        public ICommand OptimizeStarDetectionCommand { get; private set; }

        public ICommand ExportStarDetectionSettingsCommand { get; private set; }

        public ICommand ImportStarDetectionSettingsCommand { get; private set; }

        public ICommand CopyStarDetectionFromFilterCommand { get; private set; }
    }
}
```

- [ ] **Step 9: Run the fixture (expect 5 passed), then the full suite, then commit.**

```
dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~StarDetectionCopyFromFilterTests"
dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo
```

Expected: all pass. Then:

```
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/HocusFocusPlugin.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionOptionsVM.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectionCopyFromFilterTests.cs && GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(per-filter): expose per-filter surface and copy command on both options hosts"
```

### Task 13: Per-filter rows on the Star Detection options template

All XAML changes live in the shared `HocusFocus_StarDetection_Options` template so both hosts pick them up automatically. The new controls go in a separate small grid ABOVE the main settings grid (same pattern as the existing "Optimize Star Detection" button grid at `Resources/OptionsDataTemplates.xaml` lines 927-945), sharing the `HF_SDCol0`/`HF_SDCol1` size groups so labels/values align with the settings grid below. Enabled-only rows use the `RowDefinition`-style visibility pattern (`ShowOnUseAdvanced` precedent at lines 952-959). Binding expressions used (identical resolution on both hosts, guarded by `PerFilterBindingSurface_IdenticalOnBothHosts` from Task 12): `{Binding PerFilterStore.Enabled}`, `{Binding PerFilterEditBinder.AvailableFilterNames}`, `{Binding PerFilterEditBinder.EditedFilterName}`, `{Binding CopyStarDetectionFromFilterCommand}`. No host-side `CopySourceFilterName` property is needed: the Copy button passes the source ComboBox's `SelectedItem` via `CommandParameter` with an `ElementName` binding inside the template's namescope.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml`
- Test: build validation via the test command (XAML/BAML compiles during build) + full suite

- [ ] **Step 1: Add the tooltip resources.** In `Resources/OptionsDataTemplates.xaml`, immediately after the `ImportStarDetectionSettings_Tooltip` TextBlock (line 508), insert (per the `<Name>_Tooltip` convention in `.claude/docs/options-system.md` / `wpf-xaml.md`):

```xml
    <TextBlock x:Key="PerFilterStarDetectionEnabled_Tooltip" Text="Gives every filter in the active profile its own complete set of star detection settings. When enabled, detection uses the settings of the filter each image was captured with, and every control below edits the selected filter's set. Requires a filter wheel; images without a known filter soft-fail detection with a warning. When disabled, the single global settings apply exactly as before." />
    <TextBlock x:Key="EditedFilterName_Tooltip" Text="Which filter's star detection settings are being edited. Every control below reads and writes the selected filter's set. Only shown while per-filter star detection is enabled." />
    <TextBlock x:Key="CopyStarDetectionFromFilter_Tooltip" Text="Copies another filter's star detection settings onto the filter currently being edited. A confirmation dialog shows exactly which settings will change before anything is applied." />
```

- [ ] **Step 2: Add the per-filter rows to the shared template.** In the `HocusFocus_StarDetection_Options` DataTemplate, insert the following between the closing `</Grid>` of the "Optimize Star Detection" button grid (line 945) and the main settings `<Grid Margin="5" VerticalAlignment="Top" d:DataContext=...>` (line 946):

```xml
            <!--  Per-filter star detection: master toggle (always visible) + the editing-filter selector and
                  copy-from row (shown only while the feature is on, via the RowDefinition-style pattern). A
                  separate grid ABOVE the settings grid, sharing its label/value size groups so columns align.
                  Binding paths (PerFilterStore.*, PerFilterEditBinder.*, CopyStarDetectionFromFilterCommand)
                  resolve on BOTH hosts: HocusFocusPlugin (plugin options page) and StarDetectionOptionsVM
                  (Imaging dockable) expose identically-named instance properties.  -->
            <Grid Margin="5,0,5,5" HorizontalAlignment="Left">
                <Grid.Resources>
                    <Style x:Key="ShowOnPerFilterEnabled" TargetType="{x:Type RowDefinition}">
                        <Setter Property="Height" Value="0" />
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding PerFilterStore.Enabled}" Value="True">
                                <Setter Property="Height" Value="Auto" />
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </Grid.Resources>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto" SharedSizeGroup="HF_SDCol0" />
                    <ColumnDefinition Width="Auto" SharedSizeGroup="HF_SDCol1" />
                </Grid.ColumnDefinitions>
                <Grid.RowDefinitions>
                    <RowDefinition />
                    <RowDefinition Style="{StaticResource ShowOnPerFilterEnabled}" />
                    <RowDefinition Style="{StaticResource ShowOnPerFilterEnabled}" />
                </Grid.RowDefinitions>
                <TextBlock
                    Grid.Row="0"
                    Grid.Column="0"
                    VerticalAlignment="Center"
                    Text="Per-Filter Star Detection"
                    ToolTip="{StaticResource PerFilterStarDetectionEnabled_Tooltip}" />
                <CheckBox
                    Grid.Row="0"
                    Grid.Column="1"
                    MinWidth="80"
                    Margin="5,5,0,0"
                    HorizontalAlignment="Left"
                    VerticalAlignment="Center"
                    IsChecked="{Binding PerFilterStore.Enabled}"
                    ToolTip="{StaticResource PerFilterStarDetectionEnabled_Tooltip}" />
                <TextBlock
                    Grid.Row="1"
                    Grid.Column="0"
                    VerticalAlignment="Center"
                    Text="Editing Filter"
                    ToolTip="{StaticResource EditedFilterName_Tooltip}" />
                <ComboBox
                    Name="PART_EditedFilterList"
                    Grid.Row="1"
                    Grid.Column="1"
                    MinWidth="80"
                    Margin="5,5,0,0"
                    HorizontalAlignment="Left"
                    VerticalAlignment="Center"
                    ItemsSource="{Binding PerFilterEditBinder.AvailableFilterNames}"
                    SelectedItem="{Binding PerFilterEditBinder.EditedFilterName}"
                    ToolTip="{StaticResource EditedFilterName_Tooltip}" />
                <TextBlock
                    Grid.Row="2"
                    Grid.Column="0"
                    VerticalAlignment="Center"
                    Text="Copy Settings From"
                    ToolTip="{StaticResource CopyStarDetectionFromFilter_Tooltip}" />
                <StackPanel
                    Grid.Row="2"
                    Grid.Column="1"
                    Orientation="Horizontal">
                    <ComboBox
                        Name="PART_CopySourceFilterList"
                        MinWidth="80"
                        Margin="5,5,0,0"
                        VerticalAlignment="Center"
                        ItemsSource="{Binding PerFilterEditBinder.AvailableFilterNames}"
                        ToolTip="{StaticResource CopyStarDetectionFromFilter_Tooltip}" />
                    <Button
                        Margin="8,5,0,0"
                        VerticalAlignment="Center"
                        Command="{Binding CopyStarDetectionFromFilterCommand}"
                        CommandParameter="{Binding SelectedItem, ElementName=PART_CopySourceFilterList}"
                        ToolTip="{StaticResource CopyStarDetectionFromFilter_Tooltip}">
                        <TextBlock
                            Margin="10,5,10,5"
                            Foreground="{StaticResource ButtonForegroundBrush}"
                            Text="Copy"
                            TextWrapping="Wrap" />
                    </Button>
                </StackPanel>
            </Grid>
```

(A null `CommandParameter` — no source selected — is a safe no-op: `CopyFromFilterAsync` returns immediately on a null/empty name.)

- [ ] **Step 3: Build-validate the XAML.** The filtered test command builds the plugin (BAML compilation catches malformed XAML and unknown members; `StaticResource` keys inside a DataTemplate resolve at runtime, so double-check the three tooltip key spellings against Step 1 by eye):

```
dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~StarDetectionCopyFromFilterTests"
```

Expected: build succeeds, 5 passed.

- [ ] **Step 4: Run the full suite, then commit.**

```
dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo
```

Expected: all pass. Then:

```
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml && GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(per-filter): per-filter toggle, editing-filter selector, and copy-from row on the options page"
```

### Task 14: Engine — `UseExactImagingFilter` suppresses the AF-filter substitution

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/IAutoFocusEngine.cs` (anchor: `AutoFocusEngineOptions`, after `ReuseSavedDetection`)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/AutoFocusEngine.cs` (anchors: `InitializeState` ~line 1433, `SetAutofocusFilter` ~line 1829)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/AutoFocusEngineTests.cs`

- [ ] **Step 1: Write the failing tests.** In `AutoFocusEngineTests.cs`, add the usings `using NINA.Core.Model;`, `using NINA.Core.Model.Equipment;`, and `using NINA.Core.Utility;` to the existing using block, and extend the `Build` helper with a filter-wheel parameter. Replace:

  ```csharp
        private static AutoFocusEngine Build(
            IProfileService profileService = null,
            IAutoFocusOptions autoFocusOptions = null,
            IPluggableBehaviorSelector<IStarDetection> starDetectionSelector = null,
            IFocuserMediator focuserMediator = null) {
            return new AutoFocusEngine(
                profileService: profileService ?? Substitute.For<IProfileService>(),
                cameraMediator: Substitute.For<ICameraMediator>(),
                filterWheelMediator: Substitute.For<IFilterWheelMediator>(),
  ```

  with:

  ```csharp
        private static AutoFocusEngine Build(
            IProfileService profileService = null,
            IAutoFocusOptions autoFocusOptions = null,
            IPluggableBehaviorSelector<IStarDetection> starDetectionSelector = null,
            IFocuserMediator focuserMediator = null,
            IFilterWheelMediator filterWheelMediator = null) {
            return new AutoFocusEngine(
                profileService: profileService ?? Substitute.For<IProfileService>(),
                cameraMediator: Substitute.For<ICameraMediator>(),
                filterWheelMediator: filterWheelMediator ?? Substitute.For<IFilterWheelMediator>(),
  ```

  Then add these tests before the `TempDir` nested class:

  ```csharp
        // --- UseExactImagingFilter (per-filter wizard target sweeps) ---
        // SetAutofocusFilter substitutes the designated AF filter when UseFilterWheelOffsets is on. The
        // options-aware overload must skip that substitution — and move the wheel to the requested filter —
        // when UseExactImagingFilter is set, so a wizard target-filter sweep exposes through EXACTLY the
        // chosen filter. Default options must preserve the substitution byte-for-byte.

        private static IProfileService ProfileWithAfFilter(out FilterInfo afFilter, out FilterInfo targetFilter) {
            var profileService = Substitute.For<IProfileService>();
            afFilter = new FilterInfo("Lum", 0, 0) { AutoFocusFilter = true };
            targetFilter = new FilterInfo("Ha", 0, 1);
            profileService.ActiveProfile.FocuserSettings.UseFilterWheelOffsets.Returns(true);
            profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters.Returns(
                new ObserveAllCollection<FilterInfo>(new[] { afFilter, targetFilter }));
            return profileService;
        }

        private static IFilterWheelMediator EchoingFilterWheel() {
            var filterWheelMediator = Substitute.For<IFilterWheelMediator>();
            filterWheelMediator.ChangeFilter(Arg.Any<FilterInfo>(), Arg.Any<CancellationToken>(), Arg.Any<IProgress<ApplicationStatus>>())
                .Returns(ci => Task.FromResult(ci.Arg<FilterInfo>()));
            return filterWheelMediator;
        }

        [Test]
        public async Task SetAutofocusFilter_UseExactImagingFilter_MovesToExactFilterAndSkipsAfSubstitution() {
            var profileService = ProfileWithAfFilter(out var afFilter, out var targetFilter);
            var filterWheelMediator = EchoingFilterWheel();
            var engine = Build(profileService, filterWheelMediator: filterWheelMediator);
            var options = new AutoFocusEngineOptions { UseExactImagingFilter = true };

            var result = await engine.SetAutofocusFilter(options, targetFilter, CancellationToken.None, null);

            Assert.That(result, Is.SameAs(targetFilter), "the exact imaging filter is used, not the designated AF filter");
            _ = filterWheelMediator.Received(1).ChangeFilter(targetFilter, Arg.Any<CancellationToken>(), Arg.Any<IProgress<ApplicationStatus>>());
        }

        [Test]
        public async Task SetAutofocusFilter_DefaultOptions_StillSubstitutesDesignatedAfFilter() {
            var profileService = ProfileWithAfFilter(out var afFilter, out var targetFilter);
            var filterWheelMediator = EchoingFilterWheel();
            var engine = Build(profileService, filterWheelMediator: filterWheelMediator);
            var options = new AutoFocusEngineOptions(); // UseExactImagingFilter defaults to false

            var result = await engine.SetAutofocusFilter(options, targetFilter, CancellationToken.None, null);

            Assert.That(result, Is.SameAs(afFilter), "default behavior unchanged: the AF filter substitutes the imaging filter");
            _ = filterWheelMediator.Received(1).ChangeFilter(afFilter, Arg.Any<CancellationToken>(), Arg.Any<IProgress<ApplicationStatus>>());
        }

        [Test]
        public async Task SetAutofocusFilter_UseExactImagingFilter_NullImagingFilter_FallsBackToAfSubstitution() {
            var profileService = ProfileWithAfFilter(out var afFilter, out _);
            var filterWheelMediator = EchoingFilterWheel();
            var engine = Build(profileService, filterWheelMediator: filterWheelMediator);
            var options = new AutoFocusEngineOptions { UseExactImagingFilter = true };

            var result = await engine.SetAutofocusFilter(options, null, CancellationToken.None, null);

            Assert.That(result, Is.SameAs(afFilter), "no exact filter to honor: fall back to the designated AF filter");
        }
  ```

- [ ] **Step 2: Run and expect FAIL.** From the repo root:
  `dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~AutoFocusEngineTests"`
  Expect a compile failure: CS0117 `'AutoFocusEngineOptions' does not contain a definition for 'UseExactImagingFilter'` and CS1501 no `SetAutofocusFilter` overload taking 4 arguments.

- [ ] **Step 3: Implement.** In `Interfaces/IAutoFocusEngine.cs`, inside `AutoFocusEngineOptions`, insert after the `public bool ReuseSavedDetection { get; set; } = false;` line:

  ```csharp
        // When true and an imaging filter is supplied, the run exposes through EXACTLY that filter: the engine
        // moves the wheel to it and skips the designated-AF-filter substitution that UseFilterWheelOffsets
        // normally applies in SetAutofocusFilter. Set by the Star Detection Optimizer Wizard's target-filter
        // sweep (per-filter star detection). Default false = existing behavior, byte-identical.
        public bool UseExactImagingFilter { get; set; } = false;
  ```

  In `AutoFocus/AutoFocusEngine.cs`, insert immediately above `public async Task<FilterInfo> SetAutofocusFilter(FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress) {` (~line 1829):

  ```csharp
        // Options-aware wrapper used by InitializeState: honors UseExactImagingFilter by moving the wheel to
        // exactly the requested imaging filter (no designated-AF-filter substitution). A ChangeFilter failure
        // propagates so the caller's existing error handling surfaces it — an explicit target filter must never
        // silently fall back to a different filter. Internal for direct unit testing (InternalsVisibleTo).
        internal async Task<FilterInfo> SetAutofocusFilter(AutoFocusEngineOptions options, FilterInfo imagingFilter, CancellationToken token, IProgress<ApplicationStatus> progress) {
            if (options.UseExactImagingFilter && imagingFilter != null) {
                await filterWheelMediator.ChangeFilter(imagingFilter, token, progress);
                return imagingFilter;
            }
            return await SetAutofocusFilter(imagingFilter, token, progress);
        }
  ```

  In `InitializeState` (~line 1433), replace:

  ```csharp
            var autofocusFilter = forRerun ? imagingFilter : await SetAutofocusFilter(imagingFilter, token, progress);
  ```

  with:

  ```csharp
            var autofocusFilter = forRerun ? imagingFilter : await SetAutofocusFilter(options, imagingFilter, token, progress);
  ```

- [ ] **Step 4: Run and expect PASS.**
  `dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~AutoFocusEngineTests"`
  All AutoFocusEngineTests pass, including the three new ones.

- [ ] **Step 5: Run the full suite** (use a 600000 ms timeout):
  `dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo`
  Expect all green.

- [ ] **Step 6: Commit.**
  `git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/IAutoFocusEngine.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/AutoFocusEngine.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/AutoFocusEngineTests.cs && GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(per-filter): honor an exact imaging filter in the AF engine via UseExactImagingFilter"`

### Task 15: Wizard VM — per-filter delegates, target-filter state, Start validation, sweep readouts

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/StarDetectionOptimizerWizardVM.cs` (anchors: delegate fields ~line 312, primary ctor ~line 406, `IsLive` ~line 616, `SweepGain`/`SweepFilterName` ~lines 668–682, `ValidateSourceBeforeStart` ~line 1524)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/Optimization/StarDetectionOptimizerWizardVMTests.cs`

- [ ] **Step 1: Write the failing tests.** In `StarDetectionOptimizerWizardVMTests.cs`, add `using NINA.Core.Model.Equipment;` to the usings. Extend the `NewVM` helper — replace its parameter list tail and constructor-call tail:

  ```csharp
        Func<bool> confirmRoughFocus = null,
        Func<string> currentFilterName = null,
        Func<int?> currentGain = null) {
  ```

  with:

  ```csharp
        Func<bool> confirmRoughFocus = null,
        Func<string> currentFilterName = null,
        Func<int?> currentGain = null,
        Func<bool> perFilterEnabled = null,
        Func<bool> isFilterWheelConnected = null,
        Func<IReadOnlyList<string>> getFilterNames = null,
        Func<string> getCurrentFilterName = null,
        Func<string, FilterInfo> resolveFilterByName = null,
        Func<string, IStarDetectionOptions> getFilterDetectionOptions = null,
        Action<string, OptimizedStarDetectionSettings> applyOptimizedToFilter = null) {
  ```

  and replace the ctor-call tail:

  ```csharp
            confirmRoughFocus: confirmRoughFocus,
            currentFilterName: currentFilterName,
            currentGain: currentGain);
  ```

  with:

  ```csharp
            confirmRoughFocus: confirmRoughFocus,
            currentFilterName: currentFilterName,
            currentGain: currentGain,
            perFilterEnabled: perFilterEnabled,
            isFilterWheelConnected: isFilterWheelConnected,
            getFilterNames: getFilterNames,
            getCurrentFilterName: getCurrentFilterName,
            resolveFilterByName: resolveFilterByName,
            getFilterDetectionOptions: getFilterDetectionOptions,
            applyOptimizedToFilter: applyOptimizedToFilter);
  ```

  Then add these tests (e.g. after `IsLive_TracksSourceMode`):

  ```csharp
    // ---- Per-filter star detection: target filter state + Start validation -----------------------------

    [Test]
    public void TargetFilterName_DefaultsToCurrentWheelFilter_WhenPerFilterEnabled() {
        var vm = NewVM(LoaderReturning(GoodRun()),
            perFilterEnabled: () => true,
            getFilterNames: () => new[] { "Lum", "Ha", "OIII" },
            getCurrentFilterName: () => "Ha");
        Assert.Multiple(() => {
            Assert.That(vm.IsPerFilterEnabled, Is.True);
            Assert.That(vm.TargetFilterName, Is.EqualTo("Ha"));
            Assert.That(vm.AvailableFilterNames, Is.EqualTo(new[] { "Lum", "Ha", "OIII" }));
        });
    }

    [Test]
    public void TargetFilterName_DefaultsToFirstProfileFilter_WhenWheelFilterUnknown() {
        var vm = NewVM(LoaderReturning(GoodRun()),
            perFilterEnabled: () => true,
            getFilterNames: () => new[] { "Lum", "Ha" },
            getCurrentFilterName: () => null);
        Assert.That(vm.TargetFilterName, Is.EqualTo("Lum"));
    }

    [Test]
    public void IsPerFilterEnabled_False_ByDefault() {
        var vm = NewVM(LoaderReturning(GoodRun()));
        Assert.Multiple(() => {
            Assert.That(vm.IsPerFilterEnabled, Is.False);
            Assert.That(vm.TargetFilterName, Is.Null, "feature off: no target filter is seeded");
        });
    }

    [Test]
    public async Task Start_PerFilterOn_NoTargetFilter_SetsErrorAndDoesNotLoad() {
        var loader = LoaderReturning(GoodRun());
        var vm = NewVM(loader, perFilterEnabled: () => true, getFilterNames: () => Array.Empty<string>());
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.ErrorMessage, Is.EqualTo("Select a target filter."));
            Assert.That(vm.CurrentStep, Is.Not.EqualTo(WizardStep.Summary));
            loader.DidNotReceiveWithAnyArgs().LoadSavedRunAsync(default, default, default);
        });
    }

    [Test]
    public async Task Start_PerFilterOn_Live_FilterWheelDisconnected_SetsError() {
        var vm = NewVM(LoaderReturning(GoodRun()),
            isCameraConnected: () => true, isFocuserConnected: () => true,
            perFilterEnabled: () => true,
            isFilterWheelConnected: () => false,
            getFilterNames: () => new[] { "Ha" });
        vm.SourceMode = SourceMode.Live;
        vm.SaveFolderPath = @"C:\live";

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(vm.ErrorMessage, Is.EqualTo("Connect a filter wheel before running a live optimization."));
            Assert.That(vm.CurrentStep, Is.Not.EqualTo(WizardStep.Summary));
        });
    }

    [Test]
    public async Task Start_PerFilterOn_Replay_DoesNotRequireFilterWheel() {
        // Replay needs no equipment: the wheel-connected gate applies to Live only.
        var vm = NewVM(LoaderReturning(GoodRun()),
            perFilterEnabled: () => true,
            isFilterWheelConnected: () => false,
            getFilterNames: () => new[] { "Ha" });
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);

        Assert.That(vm.CurrentStep, Is.EqualTo(WizardStep.Summary));
    }

    [Test]
    public void SweepReadouts_ReflectTargetFilter_WhenPerFilterEnabled() {
        var target = new FilterInfo("Ha", 0, 1) { AutoFocusGain = 200 };
        var vm = NewVM(LoaderReturning(GoodRun()),
            currentFilterName: () => "Lum", currentGain: () => 100,
            perFilterEnabled: () => true,
            getFilterNames: () => new[] { "Lum", "Ha" },
            getCurrentFilterName: () => "Ha",
            resolveFilterByName: name => name == "Ha" ? target : null);
        Assert.Multiple(() => {
            Assert.That(vm.SweepFilterName, Is.EqualTo("Ha"), "the target filter name, not the AF/current filter");
            Assert.That(vm.SweepGain, Is.EqualTo("200"), "the target filter's per-filter AF gain");
        });
    }

    [Test]
    public void SweepReadouts_FallBackToProviders_WhenPerFilterOff() {
        var vm = NewVM(LoaderReturning(GoodRun()), currentFilterName: () => "Lum", currentGain: () => 100);
        Assert.Multiple(() => {
            Assert.That(vm.SweepFilterName, Is.EqualTo("Lum"));
            Assert.That(vm.SweepGain, Is.EqualTo("100"));
        });
    }
  ```

- [ ] **Step 2: Run and expect FAIL.**
  `dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~StarDetectionOptimizerWizardVMTests"`
  Expect a compile failure: CS1739 `The best overload for 'StarDetectionOptimizerWizardVM' does not have a parameter named 'perFilterEnabled'`.

- [ ] **Step 3: Implement the VM changes.** In `StarDetectionOptimizerWizardVM.cs`:

  (a) Add `using NINA.Core.Model.Equipment;` to the usings (for `FilterInfo`).

  (b) After the existing delegate fields (`confirmRoughFocus` / `currentFilterName` / `currentGain`, ~line 314), add:

  ```csharp
        // Per-filter star detection collaborators (delegates, so the VM stays store/mediator-free and testable).
        // perFilterEnabled gates the target-filter picker and per-filter routing; the rest resolve profile
        // filters and the per-filter settings store. Defaults keep the feature off for existing callers/tests.
        private readonly Func<bool> perFilterEnabled;
        private readonly Func<bool> isFilterWheelConnected;
        private readonly Func<IReadOnlyList<string>> getFilterNames;
        private readonly Func<string> getCurrentFilterName;
        private readonly Func<string, FilterInfo> resolveFilterByName;
        private readonly Func<string, IStarDetectionOptions> getFilterDetectionOptions;
        private readonly Action<string, OptimizedStarDetectionSettings> applyOptimizedToFilter;
  ```

  (c) In the primary ctor, replace the parameter-list tail:

  ```csharp
            Func<bool> confirmRoughFocus = null,
            Func<string> currentFilterName = null,
            Func<int?> currentGain = null) {
  ```

  with:

  ```csharp
            Func<bool> confirmRoughFocus = null,
            Func<string> currentFilterName = null,
            Func<int?> currentGain = null,
            Func<bool> perFilterEnabled = null,
            Func<bool> isFilterWheelConnected = null,
            Func<IReadOnlyList<string>> getFilterNames = null,
            Func<string> getCurrentFilterName = null,
            Func<string, FilterInfo> resolveFilterByName = null,
            Func<string, IStarDetectionOptions> getFilterDetectionOptions = null,
            Action<string, OptimizedStarDetectionSettings> applyOptimizedToFilter = null) {
  ```

  (d) After the existing assignments `this.currentFilterName = currentFilterName; this.currentGain = currentGain;`, add:

  ```csharp
            this.perFilterEnabled = perFilterEnabled ?? (() => false);
            this.isFilterWheelConnected = isFilterWheelConnected ?? (() => true);
            this.getFilterNames = getFilterNames ?? (() => Array.Empty<string>());
            this.getCurrentFilterName = getCurrentFilterName;
            this.resolveFilterByName = resolveFilterByName;
            this.getFilterDetectionOptions = getFilterDetectionOptions;
            this.applyOptimizedToFilter = applyOptimizedToFilter;
            if (this.perFilterEnabled()) {
                // Default the target to the currently-loaded wheel filter, else the first profile filter.
                var current = this.getCurrentFilterName?.Invoke();
                targetFilterName = !string.IsNullOrEmpty(current) ? current : this.getFilterNames().FirstOrDefault();
            }
  ```

  (e) After the `public bool IsLive => SourceMode == SourceMode.Live;` property (~line 616), add:

  ```csharp
        private string targetFilterName;

        /// <summary>The ONE filter this run captures on, seeds its baseline from, and (on Accept) writes its
        /// result to. Only meaningful while per-filter star detection is enabled. Session-only, not persisted.</summary>
        public string TargetFilterName {
            get => targetFilterName;
            set {
                if (targetFilterName != value) {
                    targetFilterName = value;
                    RaisePropertyChanged();
                    // The sweep readouts (filter/gain) reflect the target filter while per-filter is on.
                    RaiseSweepReadoutsChanged();
                }
            }
        }

        /// <summary>True while per-filter star detection is enabled: shows the target-filter picker (both
        /// modes) and routes baseline/capture/Accept through the target filter's settings set.</summary>
        public bool IsPerFilterEnabled => perFilterEnabled();

        /// <summary>The active profile's filter names, for the target-filter picker.</summary>
        public IReadOnlyList<string> AvailableFilterNames => getFilterNames();
  ```

  (f) Replace the `SweepGain` getter body:

  ```csharp
        public string SweepGain {
            get {
                var gain = currentGain?.Invoke();
                return gain.HasValue ? gain.Value.ToString() : "Unavailable";
            }
        }
  ```

  with:

  ```csharp
        public string SweepGain {
            get {
                if (IsPerFilterEnabled && !string.IsNullOrEmpty(TargetFilterName)) {
                    var filter = resolveFilterByName?.Invoke(TargetFilterName);
                    if (filter != null && filter.AutoFocusGain > -1) {
                        return filter.AutoFocusGain.ToString();
                    }
                }
                var gain = currentGain?.Invoke();
                return gain.HasValue ? gain.Value.ToString() : "Unavailable";
            }
        }
  ```

  (g) Replace the `SweepFilterName` getter body:

  ```csharp
        public string SweepFilterName {
            get {
                var name = currentFilterName?.Invoke();
                return string.IsNullOrEmpty(name) ? "Unavailable" : name;
            }
        }
  ```

  with:

  ```csharp
        public string SweepFilterName {
            get {
                if (IsPerFilterEnabled && !string.IsNullOrEmpty(TargetFilterName)) {
                    return TargetFilterName;
                }
                var name = currentFilterName?.Invoke();
                return string.IsNullOrEmpty(name) ? "Unavailable" : name;
            }
        }
  ```

  (h) In `ValidateSourceBeforeStart()`, insert at the top of the method body:

  ```csharp
            if (IsPerFilterEnabled && string.IsNullOrWhiteSpace(TargetFilterName)) {
                ErrorMessage = "Select a target filter.";
                return false;
            }
  ```

  and inside the `SourceMode == SourceMode.Live` branch, after the `isFocuserConnected()` check block, add:

  ```csharp
                if (IsPerFilterEnabled && !isFilterWheelConnected()) {
                    ErrorMessage = "Connect a filter wheel before running a live optimization.";
                    return false;
                }
  ```

- [ ] **Step 4: Run and expect PASS.**
  `dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~StarDetectionOptimizerWizardVMTests"`
  All wizard VM tests pass, including the eight new ones.

- [ ] **Step 5: Run the full suite** (600000 ms timeout):
  `dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo`
  Expect all green.

- [ ] **Step 6: Commit.**
  `git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/StarDetectionOptimizerWizardVM.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/Optimization/StarDetectionOptimizerWizardVMTests.cs && GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(per-filter): add target-filter state and start validation to the optimizer wizard"`

### Task 16: Wizard VM — target-filter capture, baseline override, Accept routing, production wiring

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/RunEvaluationLoader.cs` (anchors: `IRunEvaluationLoader` interface ~line 60, progress overload ~line 129, baseline params ~line 167)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/StarDetectionOptimizerWizardVM.cs` (anchors: convenience ctor tail ~line 400, `AcquireAsync` ~line 1780, `RunLiveAttemptAsync` ~lines 1830/1839, `Apply` ~line 2191, re-load calls ~lines 2480/2608)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/Optimization/StarDetectionOptimizerWizardVMTests.cs`

- [ ] **Step 1: Add the baseline-override loader seam (pure addition, suite stays green).** In `RunEvaluationLoader.cs`, append to the `IRunEvaluationLoader` interface (after the progress overload):

  ```csharp
        /// <summary>
        /// As the progress overload, additionally building the run's <see cref="LoadedRun.Baseline"/> ("current
        /// settings") params from <paramref name="baselineOptionsOverride"/> instead of the detector's injected
        /// options — the per-filter wizard resolves the TARGET filter's settings set through this seam (the
        /// existing optionsOverride overload of GetStarDetectorParams). Null is identical to the progress
        /// overload: the detector's own options build the baseline, byte-identical to today.
        /// </summary>
        Task<LoadedRun> LoadSavedRunAsync(string attemptFolderPath, StarDetectionRegion region, IReadOnlyList<FrameLabels> labels, IProgress<RunLoadProgress> progress, IStarDetectionOptions baselineOptionsOverride, CancellationToken token);
  ```

  In the `RunEvaluationLoader` class, change the progress overload into a delegation and move its body into the new overload. Replace:

  ```csharp
        /// <inheritdoc />
        public async Task<LoadedRun> LoadSavedRunAsync(string attemptFolderPath, StarDetectionRegion region, IReadOnlyList<FrameLabels> labels, IProgress<RunLoadProgress> progress, CancellationToken token) {
  ```

  with:

  ```csharp
        /// <inheritdoc />
        public Task<LoadedRun> LoadSavedRunAsync(string attemptFolderPath, StarDetectionRegion region, IReadOnlyList<FrameLabels> labels, IProgress<RunLoadProgress> progress, CancellationToken token) =>
            LoadSavedRunAsync(attemptFolderPath, region, labels, progress, baselineOptionsOverride: null, token);

        /// <inheritdoc />
        public async Task<LoadedRun> LoadSavedRunAsync(string attemptFolderPath, StarDetectionRegion region, IReadOnlyList<FrameLabels> labels, IProgress<RunLoadProgress> progress, IStarDetectionOptions baselineOptionsOverride, CancellationToken token) {
  ```

  and inside the (moved) body replace the baseline line:

  ```csharp
            var baseline = detection.GetStarDetectorParams(firstImage, region, isAutoFocus: true);
  ```

  with:

  ```csharp
            // The optionsOverride overload delegates to the plain path on null, so a null override is byte-identical.
            var baseline = detection.GetStarDetectorParams(firstImage, region, isAutoFocus: true, baselineOptionsOverride);
  ```

  Run `dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~StarDetectionOptimizerWizardVMTests"` — expect PASS (nothing calls the new overload yet).

- [ ] **Step 2: Write the failing tests + update the test fakes for the new overload.** In `StarDetectionOptimizerWizardVMTests.cs`:

  (a) In `LoaderReturning`, after the existing progress-overload stub, add:

  ```csharp
        // The baseline-override overload is the one the VM calls once per-filter routing lands; same queue.
        loader.LoadSavedRunAsync(Arg.Any<string>(), Arg.Any<StarDetectionRegion>(), Arg.Any<IReadOnlyList<FrameLabels>>(), Arg.Any<IProgress<RunLoadProgress>>(), Arg.Any<IStarDetectionOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(queue.Count > 0 ? queue.Dequeue() : runs[runs.Length - 1]));
  ```

  (b) In `RecordingLoader`, add the new interface member (delegating so its counters keep their meaning):

  ```csharp
        public Task<LoadedRun> LoadSavedRunAsync(string attemptFolderPath, StarDetectionRegion region, IReadOnlyList<FrameLabels> labels, IProgress<RunLoadProgress> progress, IStarDetectionOptions baselineOptionsOverride, CancellationToken token) =>
            LoadSavedRunAsync(attemptFolderPath, region, labels, progress, token);
  ```

  (c) In `RunCountTwo_LoadsTwoRuns_SummaryReportsRunCountTwo`, replace the received-calls assertion:

  ```csharp
            loader.Received(2).LoadSavedRunAsync(Arg.Any<string>(), Arg.Any<StarDetectionRegion>(), Arg.Any<IReadOnlyList<FrameLabels>>(), Arg.Any<IProgress<RunLoadProgress>>(), Arg.Any<CancellationToken>());
  ```

  with:

  ```csharp
            loader.Received(2).LoadSavedRunAsync(Arg.Any<string>(), Arg.Any<StarDetectionRegion>(), Arg.Any<IReadOnlyList<FrameLabels>>(), Arg.Any<IProgress<RunLoadProgress>>(), Arg.Any<IStarDetectionOptions>(), Arg.Any<CancellationToken>());
  ```

  (d) Add the new tests (after the per-filter tests from Task 15):

  ```csharp
    // ---- Per-filter star detection: capture / baseline / Accept routing --------------------------------

    [Test]
    public async Task Start_PerFilterLive_PassesTargetFilterWithUseExactImagingFilter() {
        var target = new FilterInfo("Ha", 0, 1);
        FilterInfo capturedFilter = null;
        AutoFocusEngineOptions capturedOptions = null;
        var engine = LiveEngine(ci => {
            capturedOptions = ci.ArgAt<AutoFocusEngineOptions>(0);
            capturedFilter = ci.ArgAt<FilterInfo>(1);
            return new AutoFocusResult { Succeeded = true, SaveFolder = @"C:\live\attempt" };
        });
        var vm = NewVM(LoaderReturning(GoodRun()),
            isCameraConnected: () => true, isFocuserConnected: () => true, autoFocusEngine: engine,
            perFilterEnabled: () => true, isFilterWheelConnected: () => true,
            getFilterNames: () => new[] { "Lum", "Ha" }, getCurrentFilterName: () => "Ha",
            resolveFilterByName: name => name == "Ha" ? target : null);
        vm.SourceMode = SourceMode.Live;
        vm.OptimizeMode = WizardOptimizeMode.UseCurrentSettings;
        vm.SaveFolderPath = @"C:\live";

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(capturedFilter, Is.SameAs(target), "the sweep exposes through the resolved target filter");
            Assert.That(capturedOptions.UseExactImagingFilter, Is.True, "the AF-filter substitution is suppressed");
        });
    }

    [Test]
    public async Task Start_Live_PerFilterOff_KeepsNullFilterAndDefaultEngineOptions() {
        var capturedFilter = new FilterInfo("sentinel", 0, 0);
        AutoFocusEngineOptions capturedOptions = null;
        var engine = LiveEngine(ci => {
            capturedOptions = ci.ArgAt<AutoFocusEngineOptions>(0);
            capturedFilter = ci.ArgAt<FilterInfo>(1);
            return new AutoFocusResult { Succeeded = true, SaveFolder = @"C:\live\attempt" };
        });
        var vm = NewLiveVM(engine);

        await vm.StartAsync(CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(capturedFilter, Is.Null, "feature off: no imaging filter is passed (existing behavior)");
            Assert.That(capturedOptions.UseExactImagingFilter, Is.False);
        });
    }

    [Test]
    public async Task Start_PerFilterOn_ThreadsTargetFilterOptionsIntoLoaderBaseline() {
        var filterOptions = Substitute.For<IStarDetectionOptions>();
        var loader = LoaderReturning(GoodRun());
        var vm = NewVM(loader,
            perFilterEnabled: () => true,
            getFilterNames: () => new[] { "Ha" }, getCurrentFilterName: () => "Ha",
            getFilterDetectionOptions: name => name == "Ha" ? filterOptions : null);
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);

        await loader.Received(1).LoadSavedRunAsync(
            @"C:\run1", Arg.Any<StarDetectionRegion>(), Arg.Any<IReadOnlyList<FrameLabels>>(),
            Arg.Any<IProgress<RunLoadProgress>>(), filterOptions, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Start_PerFilterOff_PassesNullBaselineOverrideToLoader() {
        var loader = LoaderReturning(GoodRun());
        var vm = NewVM(loader);
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);

        await loader.Received(1).LoadSavedRunAsync(
            @"C:\run1", Arg.Any<StarDetectionRegion>(), Arg.Any<IReadOnlyList<FrameLabels>>(),
            Arg.Any<IProgress<RunLoadProgress>>(), (IStarDetectionOptions)null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Accept_PerFilterOn_RoutesThroughApplyOptimizedToFilter() {
        var options = Substitute.For<IStarDetectionOptions>();
        string appliedFilter = null;
        OptimizedStarDetectionSettings appliedDto = null;
        var vm = NewVM(LoaderReturning(GoodRun()), options,
            perFilterEnabled: () => true,
            getFilterNames: () => new[] { "Ha" }, getCurrentFilterName: () => "Ha",
            applyOptimizedToFilter: (name, dto) => { appliedFilter = name; appliedDto = dto; });
        vm.SourcePaths[0] = @"C:\run1";
        await vm.StartAsync(CancellationToken.None);

        vm.AcceptCommand.Execute(null);

        Assert.Multiple(() => {
            Assert.That(appliedFilter, Is.EqualTo("Ha"), "the DTO lands in the TARGET filter's settings set");
            Assert.That(appliedDto, Is.Not.Null);
            Assert.That(appliedDto.BrightnessSensitivity, Is.EqualTo(vm.Result.BestParams.Sensitivity).Within(1e-9));
        });
        options.DidNotReceiveWithAnyArgs().ApplyOptimizedSettings(default);
    }
  ```

- [ ] **Step 3: Run and expect FAIL.**
  `dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~StarDetectionOptimizerWizardVMTests"`
  Expected failures: `Start_PerFilterLive_PassesTargetFilterWithUseExactImagingFilter` (capturedFilter null / UseExactImagingFilter false), both baseline-override tests and `RunCountTwo_...` ("Expected to receive... actually received no matching calls" — the VM still calls the 5-arg overload), and `Accept_PerFilterOn_RoutesThroughApplyOptimizedToFilter` (appliedFilter null, ApplyOptimizedSettings received).

- [ ] **Step 4: Implement.** In `StarDetectionOptimizerWizardVM.cs`:

  (a) Add the resolver next to `AcquireAsync`:

  ```csharp
        /// <summary>Per-filter runs resolve "current settings" from the TARGET filter's stored set, threaded to
        /// the loader as the baseline optionsOverride (the existing GetStarDetectorParams override seam). Null
        /// when the feature is off — baseline params byte-identical to today.</summary>
        private IStarDetectionOptions ResolveBaselineOptionsOverride() {
            if (!IsPerFilterEnabled || getFilterDetectionOptions == null || string.IsNullOrEmpty(TargetFilterName)) {
                return null;
            }
            return getFilterDetectionOptions(TargetFilterName);
        }
  ```

  (b) In `AcquireAsync` (~line 1780), replace:

  ```csharp
                    var loaded = await loader.LoadSavedRunAsync(folder, region, null, loadProgress, token).ConfigureAwait(true);
  ```

  with:

  ```csharp
                    var loaded = await loader.LoadSavedRunAsync(folder, region, null, loadProgress, ResolveBaselineOptionsOverride(), token).ConfigureAwait(true);
  ```

  (c) In `ReOptimizeWithLabelsAsync` (~line 2480), replace:

  ```csharp
                    var loaded = await loader.LoadSavedRunAsync(folder, region, frameLabels, loadProgress, token).ConfigureAwait(true);
  ```

  with:

  ```csharp
                    var loaded = await loader.LoadSavedRunAsync(folder, region, frameLabels, loadProgress, ResolveBaselineOptionsOverride(), token).ConfigureAwait(true);
  ```

  (d) In `ContinueOptimizationAsync` (~line 2608), replace:

  ```csharp
                    // No labels for a plain continue (byte-identical to the no-label load).
                    var loaded = await loader.LoadSavedRunAsync(folder, region, null, loadProgress, token).ConfigureAwait(true);
  ```

  with:

  ```csharp
                    // No labels for a plain continue (byte-identical to the no-label load).
                    var loaded = await loader.LoadSavedRunAsync(folder, region, null, loadProgress, ResolveBaselineOptionsOverride(), token).ConfigureAwait(true);
  ```

  (e) In `RunLiveAttemptAsync`, after the lines:

  ```csharp
            capturedLiveExposureSeconds = LiveExposureSeconds;   // remember what we captured with, for the Summary write-back
            RaiseExposureRowChanged();
  ```

  insert:

  ```csharp
            // Per-filter runs sweep on EXACTLY the chosen target filter: pass it as the imaging filter and set
            // UseExactImagingFilter so the engine skips the designated-AF-filter substitution. The wheel moves
            // as part of the sweep and stays on the target afterward.
            FilterInfo sweepFilter = null;
            if (IsPerFilterEnabled) {
                sweepFilter = resolveFilterByName?.Invoke(TargetFilterName);
                options.UseExactImagingFilter = true;
            }
  ```

  and replace:

  ```csharp
                var afResult = await autoFocusEngine.CaptureFixedSweepAsync(options, null, token, captureProgress).ConfigureAwait(true);
  ```

  with:

  ```csharp
                var afResult = await autoFocusEngine.CaptureFixedSweepAsync(options, sweepFilter, token, captureProgress).ConfigureAwait(true);
  ```

  (f) In `Apply` (~line 2191), replace:

  ```csharp
                starDetectionOptions.ApplyOptimizedSettings(dto);
                Logger.Info($"Applied optimized star-detection settings (J {summary.SeedJ:F3} -> {summary.BestJ:F3}, {summary.RunCount} run(s))");
  ```

  with:

  ```csharp
                if (IsPerFilterEnabled && applyOptimizedToFilter != null) {
                    // Per-filter: write the result into the TARGET filter's settings set (production routes
                    // through the edit binder, so the options page lands on the filter just optimized).
                    applyOptimizedToFilter(TargetFilterName, dto);
                    Logger.Info($"Applied optimized star-detection settings to filter '{TargetFilterName}' (J {summary.SeedJ:F3} -> {summary.BestJ:F3}, {summary.RunCount} run(s))");
                } else {
                    starDetectionOptions.ApplyOptimizedSettings(dto);
                    Logger.Info($"Applied optimized star-detection settings (J {summary.SeedJ:F3} -> {summary.BestJ:F3}, {summary.RunCount} run(s))");
                }
  ```

  (g) Production wiring — in the MEF convenience ctor, replace the chained-call tail:

  ```csharp
                currentFilterName: () => ResolveSweepFilterName(profileService, filterWheelMediator),
                currentGain: () => ResolveSweepGain(profileService, filterWheelMediator, cameraMediator)) {
  ```

  with:

  ```csharp
                currentFilterName: () => ResolveSweepFilterName(profileService, filterWheelMediator),
                currentGain: () => ResolveSweepGain(profileService, filterWheelMediator, cameraMediator),
                // Per-filter star detection: resolve the store/binder statics lazily inside the delegates (they
                // are created by the plugin bootstrap after the options singletons).
                perFilterEnabled: () => HocusFocusPlugin.PerFilterStarDetection?.Enabled == true,
                isFilterWheelConnected: () => filterWheelMediator?.GetInfo()?.Connected == true,
                getFilterNames: () => profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters.Select(f => f.Name).ToList(),
                getCurrentFilterName: () => filterWheelMediator?.GetInfo()?.SelectedFilter?.Name,
                resolveFilterByName: name => profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters.FirstOrDefault(f => f.Name == name),
                getFilterDetectionOptions: name => HocusFocusPlugin.PerFilterStarDetection.GetOrSeedSnapshot(name),
                applyOptimizedToFilter: (name, dto) => {
                    // Point the options-page edit buffer at the target filter, then apply through it: the binder
                    // mirrors the result into the store and the page shows the filter that was just optimized.
                    HocusFocusPlugin.PerFilterStarDetectionEditBinder.EditedFilterName = name;
                    HocusFocusPlugin.StarDetectionOptions.ApplyOptimizedSettings(dto);
                }) {
  ```

- [ ] **Step 5: Run and expect PASS.**
  `dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~StarDetectionOptimizerWizardVMTests"`
  All wizard tests pass, including the five new ones and the updated `RunCountTwo_...`.

- [ ] **Step 6: Run the full suite** (600000 ms timeout):
  `dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo`
  Expect all green (`RunEvaluationLoaderTests` and `LabelRunIdMatchTests` exercise the loader/fakes and must stay green).

- [ ] **Step 7: Commit.**
  `git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/RunEvaluationLoader.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/StarDetectionOptimizerWizardVM.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/Optimization/StarDetectionOptimizerWizardVMTests.cs && GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(per-filter): route wizard capture, baseline, and Accept through the target filter"`

### Task 17: Wizard XAML — target-filter picker on the SelectSource panel

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/DataTemplates.xaml` (anchors: tooltip block ~line 29, SelectSource Source row ~lines 280–296)

- [ ] **Step 1: Add the tooltip resource.** After the line:

  ```xml
    <TextBlock x:Key="SDOpt_LiveSaveFolder_Tooltip" Text="The captured frames are saved here, in a timestamped AutoFocus_… subfolder, then loaded back like a saved run so the optimizer can search them. A folder is required before Start. Choosing one also saves it as your Hocus Focus auto-focus save path." />
  ```

  insert:

  ```xml
    <TextBlock x:Key="SDOpt_TargetFilter_Tooltip" Text="Per-filter star detection is enabled, so this run tunes ONE filter's settings set. The baseline and 'current settings' come from this filter's set; a live sweep moves the wheel to this filter and exposes through it; and Accept writes the optimized result into this filter's set." />
  ```

- [ ] **Step 2: Add the Target filter row.** In the SelectSource panel, after the Source row's closing tags:

  ```xml
                    </StackPanel>

                    <StackPanel Margin="0,4" Orientation="Horizontal">
                        <TextBlock
                            Width="90"
                            VerticalAlignment="Center"
                            Text="Mode" />
  ```

  insert the new row between the two StackPanels so the block reads:

  ```xml
                    </StackPanel>

                    <!--  Target filter (per-filter star detection only, both modes): the ONE filter this run
                          captures on, seeds its baseline from, and writes its accepted result to.  -->
                    <StackPanel
                        Margin="0,4"
                        Orientation="Horizontal"
                        Visibility="{Binding IsPerFilterEnabled, Converter={StaticResource BooleanToVisibilityCollapsedConverter}}">
                        <TextBlock
                            Width="90"
                            VerticalAlignment="Center"
                            Text="Target filter" />
                        <ComboBox
                            Width="160"
                            ItemsSource="{Binding AvailableFilterNames}"
                            SelectedItem="{Binding TargetFilterName}"
                            ToolTip="{StaticResource SDOpt_TargetFilter_Tooltip}" />
                    </StackPanel>

                    <StackPanel Margin="0,4" Orientation="Horizontal">
                        <TextBlock
                            Width="90"
                            VerticalAlignment="Center"
                            Text="Mode" />
  ```

- [ ] **Step 3: Run the full suite (the build validates the XAML)** (600000 ms timeout):
  `dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo`
  Expect a clean build (no XAML/BAML errors) and all tests green.

- [ ] **Step 4: Commit.**
  `git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/DataTemplates.xaml && GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(per-filter): add the target-filter picker to the wizard start page"`

### Task 18: Export provenance — `FilterName` on `StarDetectionSettingsExport`

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionSettingsExport.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionSettingsIO.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectionSettingsExportTests.cs`

The export envelope gains an optional `FilterName` provenance field. It is stamped by `StarDetectionSettingsIO.Export` with the edited filter name while per-filter mode is on, serialized only when set (so exports with the feature off, and every pre-existing export file, keep the legacy schema byte-for-byte), tolerated when absent, and never drives an import. `TryLoad`/`Validate` are unchanged.

- [ ] **Step 1: Write failing tests for the FilterName provenance field.** Read `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectionSettingsExportTests.cs`, then append these five tests inside the fixture, after `TryLoad_RejectsAutoFocusMetadataFile()` (they reuse the fixture's existing `BuildPopulated()` helper and its existing `using` set — no new usings needed):

```csharp
        [Test]
        public void RoundTrips_FilterNameProvenance() {
            var original = BuildPopulated();
            original.FilterName = "Ha";

            var json = original.Serialize();
            Assert.That(json, Does.Contain("\"filterName\": \"Ha\""));

            var restored = StarDetectionSettingsExport.Deserialize(json);
            restored.Validate();
            Assert.That(restored.FilterName, Is.EqualTo("Ha"));
        }

        [Test]
        public void Serialize_OmitsFilterName_WhenNotSet() {
            // Exports written with per-filter mode off must stay byte-compatible with the pre-FilterName schema.
            var json = BuildPopulated().Serialize();

            Assert.That(json, Does.Not.Contain("\"filterName\""));

            var restored = StarDetectionSettingsExport.Deserialize(json);
            restored.Validate();
            Assert.That(restored.FilterName, Is.Null);
        }

        [Test]
        public void TryLoad_LoadsLegacyFileWithoutFilterName() {
            var path = Path.Combine(Path.GetTempPath(), "HFExport_" + Guid.NewGuid().ToString("N") + ".json");
            try {
                // Written by a plugin version that predates the FilterName provenance field.
                File.WriteAllText(path, @"{
  ""fileType"": ""HocusFocusStarDetectionSettings"",
  ""schemaVersion"": 1,
  ""createdAtUtc"": ""2026-06-28T01:02:03Z"",
  ""pluginVersion"": ""3.0.0.99"",
  ""starDetection"": { ""brightnessSensitivity"": 7.25 }
}");

                var ok = StarDetectionSettingsExport.TryLoad(path, out var export, out var error);

                Assert.Multiple(() => {
                    Assert.That(ok, Is.True);
                    Assert.That(error, Is.Null);
                    Assert.That(export.FilterName, Is.Null);
                    Assert.That(export.StarDetection.BrightnessSensitivity, Is.EqualTo(7.25));
                });
            } finally {
                File.Delete(path);
            }
        }

        [Test]
        public void FromOptions_StampsFilterName_WhenProvided() {
            var options = new StarDetectionOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());
            var export = StarDetectionSettingsExport.FromOptions(options, "SII");
            Assert.That(export.FilterName, Is.EqualTo("SII"));
        }

        [Test]
        public void FromOptions_LeavesFilterNameNull_ByDefault() {
            var options = new StarDetectionOptions(Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor());
            var export = StarDetectionSettingsExport.FromOptions(options);
            Assert.That(export.FilterName, Is.Null);
        }
```

- [ ] **Step 2: Run the fixture and confirm it fails to compile.** From the repo root (Bash timeout 600000):

```
dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~StarDetectionSettingsExportTests"
```

Expected: build FAILURE of the test project — `CS1061: 'StarDetectionSettingsExport' does not contain a definition for 'FilterName'` (three occurrences) and `CS1501: No overload for method 'FromOptions' takes 2 arguments`. No tests run.

- [ ] **Step 3: Add the `FilterName` property and the `FromOptions` parameter.** In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionSettingsExport.cs` (Read it first), make two edits.

Edit 1 — after the `PluginVersion` property (line 53, under the `// Informational provenance` comment):

```csharp
        // Informational provenance — not used to drive an import.
        public string PluginVersion { get; set; }

        /// <summary>Name of the filter whose per-filter settings set was exported. Stamped only while per-filter
        /// star detection is enabled; omitted from the JSON when null so files written with the feature off (and
        /// every pre-existing export) keep the legacy schema byte-for-byte. Provenance only — an import applies to
        /// whichever filter is being edited, regardless of this value.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string FilterName { get; set; }
```

(The fixture's `JsonSettings` uses `NullValueHandling.Include` globally; the member-level attribute overrides it for this one property, which is what keeps the key absent when unset. `CamelCasePropertyNamesContractResolver` still camel-cases the name because the attribute sets no `PropertyName`. `Newtonsoft.Json` is already a `using` in this file.)

Edit 2 — `FromOptions` (line 62) gains an optional parameter and stamps it in the initializer:

```csharp
        /// <summary>Captures the live options into a portable export. The machine-local intermediate-image path and
        /// save-intermediate flag are blanked: they are never applied on import and the path would otherwise leak a
        /// local user directory into the shared file. <paramref name="filterName"/> is the edited filter when
        /// per-filter star detection is on; null/empty leaves the provenance field absent.</summary>
        public static StarDetectionSettingsExport FromOptions(IStarDetectionOptions options, string filterName = null) {
            if (options == null) {
                throw new ArgumentNullException(nameof(options));
            }

            var snapshot = StarDetectionSettingsSnapshot.FromOptions(options);
            snapshot.IntermediateSavePath = "";
            snapshot.SaveIntermediateImages = false;

            return new StarDetectionSettingsExport() {
                FileType = ExpectedFileType,
                SchemaVersion = CurrentSchemaVersion,
                CreatedAtUtc = DateTime.UtcNow,
                PluginVersion = typeof(StarDetectionSettingsExport).Assembly.GetName().Version?.ToString(),
                FilterName = string.IsNullOrEmpty(filterName) ? null : filterName,
                StarDetection = snapshot
            };
        }
```

`Validate()` and `TryLoad` are deliberately untouched: a null `FilterName` is valid, so old and foreign files load exactly as before.

- [ ] **Step 4: Run the fixture and confirm all tests pass.**

```
dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~StarDetectionSettingsExportTests"
```

Expected: PASS — all 15 tests in the fixture (10 pre-existing + 5 new) green.

- [ ] **Step 5: Commit.**

```
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionSettingsExport.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectionSettingsExportTests.cs && GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(per-filter): add FilterName provenance to the settings export"
```

- [ ] **Step 6: Stamp the export in `StarDetectionSettingsIO` and show the filter on import.** In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionSettingsIO.cs` (Read it first), make three edits. This wires the UI path only; the stamping logic itself is covered by the Step-1 `FromOptions` tests (the `SaveFileDialog`/`OpenFileDialog` flow is not unit-testable).

Edit 1 — in `Export(StarDetectionOptions options)` (line 51), pass the edited filter:

```csharp
                var export = StarDetectionSettingsExport.FromOptions(options, GetEditedFilterName());
```

Edit 2 — add the resolver after the `Export` method (before `ImportAsync`). `HocusFocusPlugin` lives in the parent namespace `NINA.Joko.Plugins.HocusFocus`, so no new `using` is needed; the null-conditionals make this a no-op under unit tests where the statics are never constructed:

```csharp
        /// <summary>The filter whose set the edit buffer currently holds, or null when per-filter star detection is
        /// off (or the plugin singletons are absent, as under unit tests). Provenance only; never drives an import.</summary>
        private static string GetEditedFilterName() {
            return HocusFocusPlugin.PerFilterStarDetection?.Enabled == true
                ? HocusFocusPlugin.PerFilterStarDetectionEditBinder?.EditedFilterName
                : null;
        }
```

Edit 3 — in `BuildSourceSummary` (line 109), surface the provenance in the import confirmation dialog:

```csharp
        private static string BuildSourceSummary(StarDetectionSettingsExport export) {
            var when = export.CreatedAtUtc == default(DateTime)
                ? "unknown time"
                : export.CreatedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            var version = string.IsNullOrEmpty(export.PluginVersion) ? "unknown" : export.PluginVersion;
            var summary = $"Exported {when}  ·  plugin {version}";
            if (!string.IsNullOrEmpty(export.FilterName)) {
                summary += $"  ·  filter {export.FilterName}";
            }
            return summary;
        }
```

- [ ] **Step 7: Build + rerun the fixture to validate the wiring compiles and nothing regressed.**

```
dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo --filter "FullyQualifiedName~StarDetectionSettingsExportTests"
```

Expected: PASS (15/15). The build step also compiles `StarDetectionSettingsIO.cs` against the `HocusFocusPlugin.PerFilterStarDetection` / `PerFilterStarDetectionEditBinder` statics added earlier in this plan.

- [ ] **Step 8: Commit.**

```
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionSettingsIO.cs && GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(per-filter): stamp exports with the edited filter and surface it on import"
```

### Task 19: Documentation + wrap-up

**Files:**
- Modify: `documentation/docs/settings/index.md`
- Modify: `documentation/docs/optimization/index.md`
- Modify: `.claude/docs/options-system.md`
- Test: full suite (no filter)

Update the user manual (per `.claude/docs/documentation-style.md`: exact in-app labels, plain prose, no em-dash leaning, sparse admonitions, verify with `mkdocs build --strict` — `mkdocs` is installed at `~/.local/bin/mkdocs` and the strict build passes today), keep the internal options doc truthful, then run the full suite and make the final commit. If an earlier UI task changed any label from the design's wording ("Per-filter star detection", "Editing filter", "Copy from…", "Target filter"), use the label actually in `Resources/OptionsDataTemplates.xaml` / the wizard XAML.

- [ ] **Step 1: Document the feature on the Star Detection Settings page.** Read `documentation/docs/settings/index.md`, then make two edits.

Edit 1 — in the `### Exporting and importing star-detection settings` subsection, append a new paragraph directly after the paragraph that ends `Those keep their local values on import.` (line 69):

```markdown
With [per-filter star detection](#per-filter-star-detection) enabled, **Export** writes the set of the filter you are currently editing and records that filter's name in the file, and the import confirmation shows the name so you can tell which filter a file was tuned for. **Import** applies to whichever filter you are editing at the time, regardless of the name in the file.
```

Edit 2 — insert a new section immediately before the `## Reading the results: the Star Detection Results panel` heading (line 73):

```markdown
## Per-filter star detection

A single set of detection settings serves every filter by default. That is a compromise: narrowband frames carry fainter stars and darker backgrounds than broadband frames, so settings tuned for luminance can miss half the stars through Ha. **Per-filter star detection** gives every filter in the active profile's filter wheel its own complete settings set, tuned by hand or by the [Optimization Wizard](../optimization/index.md).

Enable it with the **Per-filter star detection** checkbox at the top of the **Star Detector** tab (default **off**; with it off, nothing changes). On enable, every filter defined in the profile starts with a copy of your current settings, so behavior is identical until you change something for a specific filter. A filter added to the profile later starts from that same captured copy the first time it is used.

### Editing one filter's settings

With the feature on, an **Editing filter** dropdown appears above the settings. Everything below it, including the Simple-mode presets, **Advanced Mode**, **Use Optimized Settings**, **Reset Defaults**, **Export**, and **Import**, now edits the selected filter's set. Pick another filter and the controls reload with that filter's values; changes are still written to the profile as you make them. The machine-local values (**Debug Mode**, the intermediate-image path and **Save Intermediate** flag, and **PSF Parallel Size**) stay global, since they describe the computer rather than the filter.

**Copy from…** copies another filter's set onto the filter you are editing. Like Import, it shows the confirmation dialog listing every setting that would change and applies nothing until you click **Apply**. A common workflow: tune one narrowband filter with the wizard, then copy the result to the other narrowband filters.

At detection time each image uses the settings of the filter it was captured with, read from the image metadata. Autofocus frames carry the filter that is physically in the light path, so a focus run through Ha is measured with your Ha settings automatically.

### Filter wheel required

Per-filter sets are keyed by filter name, so detection must know which filter took each frame; that knowledge comes from the connected filter wheel. With the feature on and no wheel connected:

- Hocus Focus refuses to start its own operations up front: autofocus runs, aberration inspector runs and analyses, and the Optimization Wizard all stop with a message naming the feature, and the sequencer's **Run Aberration Inspector** instruction reports a validation issue.
- Any detection that still reaches a frame with no filter in its metadata (NINA's built-in autofocus using the Hocus Focus detector, for example) returns zero stars and shows a warning each time: *"Per-filter star detection is enabled but the active filter is unknown - connect a filter wheel."* It never fails the imaging pipeline with an error.

If you image without a filter wheel (an OSC rig, say), leave the feature off.

Renaming a filter starts the new name from the captured global copy; the old name's set is kept and reattaches if the name returns. Disabling the feature restores the single global set exactly as it was when you enabled per-filter mode, and the per-filter sets are kept for the next time you enable it.
```

- [ ] **Step 2: Document the target filter on the Optimization Wizard page.** Read `documentation/docs/optimization/index.md`, then insert a new section immediately before the `## How a candidate is scored` heading (line 114), after the paragraph that ends `so you focus with the exposure you optimized against.`:

```markdown
## Optimizing one filter

With [per-filter star detection](../settings/index.md#per-filter-star-detection) enabled, the start page adds a **Target filter** dropdown for both sources, defaulting to the filter currently in the wheel. The whole run is about that one filter: the baseline and the **"Start from my current settings"** seed come from its settings set, the filter and gain readouts on the start page show the target filter (not the profile's designated autofocus filter), and **Accept** writes the winning settings into the target filter's set. The Star Detector options switch to the filter you just optimized, so what you see there afterward is what the run produced.

A target filter must be selected before the run can start. A live run additionally requires the filter wheel connected: **Start** moves the wheel to the target filter and sweeps on exactly that filter with its per-filter autofocus exposure, binning, gain, and offset. The usual switch to the profile's designated autofocus filter is skipped, and the wheel stays on the target when the sweep finishes. A replay needs no equipment, so you can work through your saved runs and optimize each filter in turn without connecting anything.
```

- [ ] **Step 3: Keep `.claude/docs/options-system.md` truthful.** Read it, then append this section at the end of the file (after the "Options UI Requirement" section):

```markdown
## Per-Filter Star Detection (store + binder)

Star-detection settings can be per-filter (`StarDetection/PerFilter/`). Two persisted values are owned by
`PerFilterStarDetectionStore` — not by an options class, but written through the same `PluginOptionsAccessor`:

- `PerFilterStarDetectionEnabled` — bool, default `false`.
- `PerFilterStarDetectionJson` — one JSON blob (`PerFilterStarDetectionData`) holding the pre-enable global
  seed plus a `StarDetectionSettingsSnapshot` per filter name. Corrupt JSON is discarded with a
  `Logger.Warning`.

While enabled, the `StarDetectionOptions` singleton is an edit buffer: `PerFilterEditBinder` loads the selected
filter's snapshot into it and mirrors every edit back to the store, and the singleton's legacy accessor writes
are suppressed (`PersistToProfile` backed by `SuppressiblePluginOptionsAccessor`) except for the machine-local
keys in `StarDetectionOptions.MachineLocalKeys`. Consequences when changing `StarDetectionOptions`:

- A new persisted star-detection option must also be added to `StarDetectionSettingsSnapshot` (and its
  `Clone`/`FromOptions`/apply paths), or it stays global and is silently dropped from per-filter sets.
- A machine-local (per-computer, not per-filter) option must be listed in `MachineLocalKeys` and handled in
  `PerFilterStarDetectionStore.Scrub` / `StarDetectionSettingsSnapshot.CopyMachineLocalFrom`.
- The UI invariant is unchanged: every new option still needs a control in `Resources/OptionsDataTemplates.xaml`.
```

- [ ] **Step 4: Verify the manual builds strictly.** From the repo root:

```
mkdocs build --strict -q
```

Expected: exit 0, no warnings (broken links or bad nav fail the build). If the anchor link `#per-filter-star-detection` is reported broken, the heading text in Step 1 Edit 2 drifted; make them match.

- [ ] **Step 5: Adversarial review of the two changed manual pages.** Invoke the `adversarial-doc-review` skill with `args: pages=settings/index.md,optimization/index.md`. Apply any CONFIRMED findings (formatting or AI-tell fixes) to the two pages, then re-run `mkdocs build --strict -q` if anything changed. Expected: no confirmed findings remain.

- [ ] **Step 6: Run the FULL test suite.** From the repo root (Bash timeout 600000):

```
dotnet.exe test $(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln) -c Debug --nologo
```

Expected: PASS — every test in the solution green, no skips added anywhere in this plan. If anything fails, fix the cause before committing (project invariant).

- [ ] **Step 7: Final commit.**

```
git add documentation/docs/settings/index.md documentation/docs/optimization/index.md .claude/docs/options-system.md && GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "docs(per-filter): document per-filter star detection in the manual and options doc"
```

