# Plan: Import / Export Star Detection Settings (cross-machine transfer)

## Context

Star-detection optimization is CPU-heavy. The user wants to run the optimizer on a powerful
machine, then carry the resulting **star-detection parameters only** (not autofocus, inspector, or
tilt settings) over to a lower-power imaging machine. Today there is no way to move these settings
between machines — they live per-profile in NINA's plugin settings store. This adds **Export** and
**Import** buttons to the Star Detection Options pane: Export writes the current star-detection
parameters to a `.json` file; Import reads such a file, shows a **confirmation dialog with a table of
exactly which values will change**, and (on confirm) applies them to the active profile.

### Decisions locked with the user
1. **Import scope — keep machine-local knobs.** Import everything EXCEPT `PSFParallelPartitionSize`
   (CPU-parallelism throughput) and `DebugMode` (local diagnostics), so the imaging machine keeps its
   own. `IntermediateSavePath` / `SaveIntermediateImages` are also never transferred (already excluded
   by the existing snapshot-apply path, and they leak a local user path).
2. **Buttons shown in both views.** Place them in the shared `HocusFocus_StarDetection_Options`
   template next to "Optimize Star Detection", so they appear in **both** the Options-window tab and
   the dockable "Star Detection Options" panel — consistent with how Optimize already behaves. This
   requires wiring the commands on **both** host classes (`HocusFocusPlugin` and
   `StarDetectionOptionsVM`), which is the established pattern for the existing shared commands.
3. **Confirm with a diff table.** Import opens a modal showing rows of *Setting / Current / Imported*
   for every value that differs, with Apply / Cancel. Applying overwrites and persists the profile's
   star-detection settings.

## What we reuse (do not reinvent)

- **Export payload already exists:** `StarDetectionSettingsSnapshot.FromOptions(IStarDetectionOptions)`
  (`AutoFocus/Replay/StarDetectionSettingsSnapshot.cs:107`) — a flat, profile-detached
  `IStarDetectionOptions` that captures every knob + the optimizer-result layer and round-trips
  through Newtonsoft with no `TypeNameHandling`.
- **Import-apply ordering already exists:** `StarDetectionOptions.ApplyFullSnapshot` /`ApplyKnobs`
  (`StarDetection/StarDetectionOptions.cs:1167`/`:1199`) — restores optimized layer → Simple presets
  + mode flags → every knob, then `RaiseAllPropertiesChanged()`; each setter persists via
  `optionsAccessor`. We split it so import can skip the two machine-local perf/debug knobs.
- **Versioned-file envelope house style:** `AutoFocus/Replay/AutoFocusReplayMetadata.cs` —
  `CurrentSchemaVersion` const, static `JsonSettings` (CamelCase + Indented + NullValueHandling.Include),
  `Serialize()`/`Deserialize()`/`Validate()`/never-throwing `TryLoad`. Mirror exactly.
- **Modal-with-result pattern:** `AutoFocus/Replay/ReplaySettingsPrompt.cs` +
  `ReplaySettingsPromptVM.cs` + `ReplaySettingsPromptControl.xaml` — `IWindowServiceFactory`
  → `windowService.ShowDialog(vm, title, …)`, result via a VM `TaskCompletionSource` + `RequestClose`
  event. This is the exact template for the import-confirm dialog.
- **Diff-table XAML:** the optimizer's "Changed parameters" `DataGrid`
  (`StarDetection/Optimization/DataTemplates.xaml:568`) + its `ChangedParameterRow` row model.
- **File feedback:** `NINA.Core.Utility.Notification.ShowSuccess/ShowError` + `Logger.Info/Warning/Error`.
- **Test mirrors:** `AutoFocus/Replay/AutoFocusReplayMetadataTests.cs`,
  `AutoFocus/Replay/ApplyFullSnapshotTests.cs`, and the `TestDoubles/InMemoryPluginOptionsAccessor.cs`
  double already exist.

## Implementation (ordered, file-by-file)

> First execution step: per the project CLAUDE.md, copy this plan to
> `plans/star-detection-settings-import-export-plan.md` in the repo, and do the work on a feature
> branch `ghilios/star-detection-settings-import-export` (never push to `develop`). All commits use the
> privacy email per CLAUDE.md.

### 1. New: `StarDetection/StarDetectionSettingsExport.cs` (file envelope)
Wrapper around `StarDetectionSettingsSnapshot`, mirroring `AutoFocusReplayMetadata`. Keep the envelope
fields OUT of `StarDetectionSettingsSnapshot` (that type is reused as the AF replay payload and as the
engine's in-memory override — envelope fields there would be wrong).

- Fields: `public const int CurrentSchemaVersion = 1;`, `public const string ExpectedFileType =
  "HocusFocusStarDetectionSettings";`, static `JsonSettings` (copy from `AutoFocusReplayMetadata`),
  `FileType`, `SchemaVersion`, `CreatedAtUtc`, `PluginVersion`, `StarDetectionSettingsSnapshot StarDetection`.
- `FileType` discriminator is **required**: an AF `metadata.json` also has `schemaVersion` + a
  `starDetection` node of the same type and would otherwise deserialize cleanly — `Validate()` rejects
  any file whose `FileType != ExpectedFileType`.
- `FromOptions(IStarDetectionOptions options)`: build snapshot via `StarDetectionSettingsSnapshot.FromOptions`,
  then **blank the leaked local path**: `snapshot.IntermediateSavePath = ""`,
  `snapshot.SaveIntermediateImages = false` (these are never imported anyway). Stamp `CreatedAtUtc =
  DateTime.UtcNow`, `PluginVersion = typeof(...).Assembly.GetName().Version?.ToString()`.
- `Serialize()`, `static Deserialize(json)`, `Validate()` (FileType match, SchemaVersion > 0,
  StarDetection != null), and `static bool TryLoad(string filePath, out export, out error)` that takes
  a **file path directly** (unlike the AF folder-based `TryLoad`), never throws, and rejects
  `SchemaVersion > CurrentSchemaVersion`.

### 2. Edit: `StarDetection/StarDetectionOptions.cs` (add import-apply variant)
Split the existing apply into a parameterized core; keep `ApplyFullSnapshot` behavior identical (AF
replay option (c) still copies perf+debug), add a sibling for cross-machine import:

```csharp
public void ApplyFullSnapshot(IStarDetectionOptions source)
    => ApplySnapshotCore(source, includeMachineLocalPerfKnobs: true);

/// <summary>Cross-machine IMPORT: same faithful restore as ApplyFullSnapshot, but leaves the
/// machine-local PSFParallelPartitionSize and DebugMode untouched so the imaging machine keeps its own.</summary>
public void ApplyImportedSnapshot(IStarDetectionOptions source)
    => ApplySnapshotCore(source, includeMachineLocalPerfKnobs: false);

private void ApplySnapshotCore(IStarDetectionOptions source, bool includeMachineLocalPerfKnobs) {
    // ...exact body of current ApplyFullSnapshot (lines 1168-1194), but call ApplyKnobs(source, includeMachineLocalPerfKnobs)...
}
```
In `ApplyKnobs`, move ONLY the `DebugMode` (`:1234`) and `PSFParallelPartitionSize` (`:1235`)
assignments inside `if (includeMachineLocalPerfKnobs) { … }`; all other knob copies stay unconditional.

### 3. New: `StarDetection/StarDetectionSettingsDiff.cs` (diff model + labels)
Single source of truth for what is importable and its friendly label (there is **no** reusable
property→label map in the codebase; labels live only as hardcoded TextBlocks in
`OptionsDataTemplates.xaml`).

- `public sealed class DiffRow { public string Name; public string CurrentValue; public string NewValue; }`
- An ordered `IReadOnlyList<(string Property, string Label)> ImportableSettings` covering every
  `IStarDetectionOptions` scalar/enum/bool property EXCEPT the excluded set
  (`PSFParallelPartitionSize`, `DebugMode`, `IntermediateSavePath`, `SaveIntermediateImages`,
  `HasOptimizedSettings`). Labels copied verbatim from the pane (e.g. `NoiseClippingMultiplier` →
  "Noise Clipping Multiplier", `MinHFR` → "Min HFR", `UseAdvanced` → "Advanced Mode",
  `UseOptimizedSettings` → "Use Optimized Settings"; ~45 entries).
- `static IReadOnlyList<DiffRow> BuildDiff(IStarDetectionOptions current, IStarDetectionOptions imported)`:
  for each importable property read both values via `typeof(IStarDetectionOptions).GetProperty(name)`,
  format (enum → `[Description]` via the existing enum-description helper used by
  `EnumStaticDescriptionConverter`; double → `"0.###"`; bool → "On"/"Off"); emit a `DiffRow` only when
  they differ. Append one summary row when the stored optimizer result presence/identity differs
  ("Optimizer result" → e.g. "none → present" / "present → updated").
- Value formatting + reflection kept here so it is unit-testable independent of UI.

### 4. New: import-confirm modal (mirror `ReplaySettingsPrompt` trio)
- `StarDetection/ImportStarDetectionPreviewVM.cs` — `BaseINPC, IDisposable`; ctor takes
  `IReadOnlyList<DiffRow>`; exposes `Rows`, a header string ("N settings will change"),
  `OkCommand`/`CancelCommand` (CommunityToolkit `RelayCommand`), `event EventHandler RequestClose`, and
  `TaskCompletionSource<bool>` exposed as `Task<bool> Result` (constructed
  `RunContinuationsAsynchronously`; `Dispose()`/X-button ⇒ `TrySetResult(false)`).
- `StarDetection/ImportStarDetectionPreviewControl.xaml` (+ `.xaml.cs`) — a `DataGrid`
  (`AutoGenerateColumns=False`, `IsReadOnly`, `CanUserAddRows=False`, `MaxHeight` w/ scroll) with three
  columns: "Setting" → `Name`, "Current" → `CurrentValue`, "Imported" → `NewValue` (copy the optimizer
  table at `Optimization/DataTemplates.xaml:568`); header text + right-aligned Apply / Cancel buttons
  (copy `ReplaySettingsPromptControl.xaml:129`).
- `StarDetection/ImportStarDetectionPreview.cs` — `static Task<bool> ShowAsync(IWindowServiceFactory
  factory, ImportStarDetectionPreviewVM vm)` mirroring `ReplaySettingsPrompt.ShowAsync`
  (wire `RequestClose`→`windowService.Close()`, `OnClosed`→`vm.Dispose()`, `_ = ShowDialog(vm,
  "Import Star Detection Settings", ResizeMode.CanResize, WindowStyle.SingleBorderWindow); return await vm.Result;`).
- Register the implicit template in an **exported** `ResourceDictionary`:
  `<DataTemplate DataType="{x:Type sd:ImportStarDetectionPreviewVM}"><sd:ImportStarDetectionPreviewControl/></DataTemplate>`.
  Prefer `StarDetection/DataTemplates.xaml` **after confirming it has `[Export(typeof(ResourceDictionary))]`**;
  if not, use `StarDetection/Optimization/DataTemplates.xaml` (confirmed exported).

### 5. New: `StarDetection/StarDetectionSettingsIO.cs` (the two handlers)
- `static void Export(StarDetectionOptions options)`: `Microsoft.Win32.SaveFileDialog` (filter
  `"HocusFocus Star Detection Settings (*.json)|*.json"`, `DefaultExt=".json"`, `AddExtension`,
  `OverwritePrompt`, default name `HocusFocusStarDetection_yyyyMMdd_HHmmss.json`,
  `InitialDirectory = MyDocuments`); on OK → `File.WriteAllText(path,
  StarDetectionSettingsExport.FromOptions(options).Serialize())` → `Logger.Info` + `Notification.ShowSuccess`.
  Cancel = silent. Whole body in try/catch → `Logger.Error` + `Notification.ShowError` (never throws).
- `static async Task ImportAsync(StarDetectionOptions options, IWindowServiceFactory windowServiceFactory)`:
  show `Microsoft.Win32.OpenFileDialog` **before any await** (stays on UI thread); on OK →
  `StarDetectionSettingsExport.TryLoad(path, out export, out error)`; on failure →
  `Logger.Warning` + `Notification.ShowError(error)` and return (no mutation). Build
  `StarDetectionSettingsDiff.BuildDiff(options, export.StarDetection)`; if empty →
  `Notification.ShowInformation("Imported settings match the current settings; nothing to change.")`
  and return. Otherwise `var ok = await ImportStarDetectionPreview.ShowAsync(windowServiceFactory, new
  ImportStarDetectionPreviewVM(diff));` if `ok` → `options.ApplyImportedSnapshot(export.StarDetection)`
  → `Logger.Info` + `Notification.ShowSuccess`. Whole body try/catch.

### 6. Edit hosts: `HocusFocusPlugin.cs` and `StarDetection/StarDetectionOptionsVM.cs`
Both already expose `StarDetectionOptions` and the existing shared commands. Add to **each**:
```csharp
ExportStarDetectionSettingsCommand = new RelayCommand(() => StarDetectionSettingsIO.Export(StarDetectionOptions));
ImportStarDetectionSettingsCommand = new AsyncRelayCommand(() => StarDetectionSettingsIO.ImportAsync(StarDetectionOptions, windowServiceFactory));
// public ICommand ExportStarDetectionSettingsCommand { get; private set; }
// public ICommand ImportStarDetectionSettingsCommand { get; private set; }
```
- `HocusFocusPlugin` already has `windowServiceFactory` (`:59`). `StarDetectionOptionsVM` needs one —
  add `private readonly IWindowServiceFactory windowServiceFactory = new WindowServiceFactory();`
  (mirror the plugin). `using AsyncRelayCommand = CommunityToolkit.Mvvm.Input.AsyncRelayCommand;`.
- `StarDetectionOptions` is the concrete type on both hosts, so `ApplyImportedSnapshot` is reachable.

### 7. Edit: `Resources/OptionsDataTemplates.xaml` (the buttons)
Insert a horizontal button row in the outer `StackPanel` of `HocusFocus_StarDetection_Options`,
**after** the Optimize button `</Grid>` (line 935) and **before** the options `<Grid>` (line 936),
matching the plain-button style (`<TextBlock Foreground="{StaticResource ButtonForegroundBrush}"
TextWrapping="Wrap">`): two buttons `Import` (→ `ImportStarDetectionSettingsCommand`) and `Export`
(→ `ExportStarDetectionSettingsCommand`). Add two tooltip resources `ImportStarDetectionSettings_Tooltip`
/ `ExportStarDetectionSettings_Tooltip` next to `OptimizeStarDetection_Tooltip` (line ~498), noting that
only star-detection settings transfer and that intermediate-path/debug/PSF-parallel-size stay local.

### 8. Tests (new, under `Tests/StarDetection/`)
- `StarDetectionSettingsExportTests.cs` (mirror `AutoFocusReplayMetadataTests`): round-trips with no
  `$type`; `Validate` throws on wrong `FileType` / missing `StarDetection` / non-positive version;
  `TryLoad` true for a valid temp file; false+error for corrupt JSON, newer schema, missing file, and
  **a serialized `AutoFocusReplayMetadata`** (proves the `FileType` discriminator). Assert
  `FromOptions` blanks `IntermediateSavePath`/`SaveIntermediateImages`.
- `StarDetectionImportApplyTests.cs` (mirror `ApplyFullSnapshotTests`, using `new StarDetectionOptions(
  Substitute.For<IProfileService>(), new InMemoryPluginOptionsAccessor())`): import round-trip restores
  advanced knobs / Simple-optimized / Simple-preset modes; and a contrast test —
  `ApplyImportedSnapshot` leaves the target's own `PSFParallelPartitionSize`/`DebugMode` while a normal
  knob (e.g. `BrightnessSensitivity`) does change, whereas `ApplyFullSnapshot` on a fresh target DOES
  copy those two (guards the behavioral split).
- `StarDetectionSettingsDiffTests.cs`: `BuildDiff` emits a row only for changed values, formats
  enums/doubles/bools, and returns empty for identical inputs; a **drift guard** asserting the
  `ImportableSettings` label-map keys equal exactly the `IStarDetectionOptions` public instance
  properties minus the excluded set (catches a future added knob that isn't wired into import/diff).

## Edge cases / invariants
- **No new persisted option** is introduced (envelope fields live only in the file), so the CLAUDE.md
  "every persisted option needs a UI control" invariant is not triggered — we add buttons only.
- **Shared template ⇒ both hosts**: the commands MUST exist on both `HocusFocusPlugin` and
  `StarDetectionOptionsVM`, else the dockable panel's buttons silently disable.
- **Threading**: Export/Import start on the UI thread (RelayCommand/AsyncRelayCommand); the file dialog
  is shown before any await; `WindowService.ShowDialog` marshals internally; `ApplyImportedSnapshot`'s
  `RaiseAllPropertiesChanged()` runs on the UI thread. No `Task.Run`, no dispatcher needed.
- **Graceful failures**: cancel = no-op; corrupt/wrong-type/newer-schema/missing file →
  `Notification.ShowError`, no mutation; nothing throws to the UI.
- **Persistence**: `ApplyImportedSnapshot` drives the public setters, so an import is durable to the
  active profile (reflected in the tooltip wording).

## Verification
1. **Build + tests** (Windows dotnet from WSL, scoped to the Tests csproj per the repo convention):
   `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/...csproj -c Debug --nologo`.
   All existing + new tests pass.
2. **Manual in NINA** (both surfaces): Options → Star Detection tab AND the floating "Star Detection
   Options" panel — confirm Import/Export appear in both.
   - Export → choose a path → file written; open it and confirm it contains only star-detection
     settings, `fileType: "HocusFocusStarDetectionSettings"`, and a blank `intermediateSavePath`.
   - Change a couple of settings → Import that file → confirm the diff table lists exactly the changed
     rows with correct Current/Imported values → Apply → settings update and persist (reopen Options to
     confirm). Re-import the same file with no changes → "nothing to change" info, no dialog.
   - Cancel in the dialog → nothing changes. Import a corrupt file, an unrelated `.json`, and an AF
     `metadata.json` → each is rejected with an error toast, no mutation.
3. **Cross-machine smoke**: export on machine A, copy file to machine B, import → verify B's
   `PSFParallelPartitionSize`/`DebugMode`/intermediate path are unchanged while detection knobs match A.

## Files
- New: `StarDetection/StarDetectionSettingsExport.cs`, `StarDetection/StarDetectionSettingsDiff.cs`,
  `StarDetection/StarDetectionSettingsIO.cs`, `StarDetection/ImportStarDetectionPreviewVM.cs`,
  `StarDetection/ImportStarDetectionPreviewControl.xaml(.cs)`, `StarDetection/ImportStarDetectionPreview.cs`,
  3 test files under `Tests/StarDetection/`.
- Edit: `StarDetection/StarDetectionOptions.cs`, `HocusFocusPlugin.cs`,
  `StarDetection/StarDetectionOptionsVM.cs`, `Resources/OptionsDataTemplates.xaml`, and the exported
  `StarDetection/DataTemplates.xaml` (or `StarDetection/Optimization/DataTemplates.xaml`).
