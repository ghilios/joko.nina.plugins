# Per-Filter Star Detection Settings — Design

## Summary

Add an opt-in mode where every filter in the active profile's filter wheel gets its own complete set of star
detection settings. When the feature is enabled, star detection resolves the capture-time filter of each image
and uses that filter's settings; without a determinable filter, detection soft-fails. The Star Detection options
page gains a filter selector and a copy-from-filter flow, and the Optimization Wizard gains a target-filter
picker on its start page so an optimization run captures on — and writes its result to — a specific filter.

Feature default: **off**. With the feature off, nothing changes (bit-identical detection behavior).

## Motivation

Different filters produce very different stellar profiles (narrowband vs broadband: fainter stars, different
halos, different background). A single global star detection tuning is a compromise; the Optimization Wizard can
already tune settings for one filter's data, but applying its result overwrites the only settings set. Per-filter
sets let each filter keep its own tuning — manual or optimizer-produced.

## Approved decisions (design Q&A)

1. **Scope**: the full detection settings set is per-filter — Simple/Advanced mode, the three `Simple_*`
   presets, all advanced knobs, and the optimizer snapshot (`UseOptimizedSettings` + optimized settings DTO).
   Machine-local/debug settings stay global: `DebugMode`, `IntermediateSavePath`, `PSFParallelPartitionSize`,
   `SaveIntermediateImages`. `StarAnnotatorOptions` (display) stays global.
2. **Filter identity**: keyed by **filter name** (`FilterInfo.Name`). Matches what NINA persists in sequences and
   what image metadata carries at detection time. A rename orphans the old entry (retained silently, re-attaches
   if the name returns).
3. **No filter wheel / indeterminate filter** (feature on): detection **soft-fails** — returns a valid zero-star
   result and pops a warning notification on every occurrence — and entry points we own (HF autofocus start, aberration
   inspector runs, `RunAberrationInspector` validation, Optimization Wizard start) **gate up front** with a clear
   message. Never throws into NINA's imaging pipeline.
4. **Seeding**: a filter with no stored set gets a copy of the current global (pre-feature) settings — eagerly
   for all profile filters when the feature is enabled, lazily for names first encountered later. Legacy global
   keys are frozen while the feature is on, so disabling returns exactly to pre-enable behavior.
5. **Optimization Wizard**: the start page gets a target-filter picker (both Live and Replay). Live moves the
   wheel to the target as part of Start (suppressing the auto-switch to the designated AF filter); Accept writes
   the optimized settings into the target filter's set.
6. **Options UX**: an "Editing filter" selector at the top of the Star Detection options with a "Copy from…"
   flow that reuses the existing import diff-preview dialog.
7. **Architecture**: one JSON blob in the profile for all per-filter sets; the existing `StarDetectionOptions`
   singleton becomes the **edit buffer** for the selected filter; detection reads per-filter snapshots through
   the existing per-call `optionsOverride` seam.

## Architecture overview

```
                 ┌─────────────────────────────────────────────┐
                 │ PerFilterStarDetectionStore (new, singleton) │
                 │  Enabled flag + per-filter snapshots         │
                 │  persisted: 1 JSON blob key via              │
                 │  PluginOptionsAccessor (per-profile)         │
                 └────────▲──────────────────────┬─────────────┘
        mirror on edit    │                      │ GetOrSeed(filterName) → clone
                 ┌────────┴─────────┐            │
                 │ PerFilterEdit-   │            ▼
                 │ Binder (new)     │   HocusFocusStarDetection.GetStarDetectorParams
                 │ EditedFilterName │     precedence: optionsOverride (replay)
                 └────────▲─────────┘       > per-filter snapshot (feature on)
        load snapshot /   │                 > StarDetectionOptions singleton (feature off)
        PropertyChanged   │
                 ┌────────┴─────────────────────┐
                 │ StarDetectionOptions          │  ← unchanged UI bindings, validation,
                 │ (existing singleton =         │    Simple-mode derivation, reset,
                 │  edit buffer when enabled)    │    import/export, wizard Accept
                 └───────────────────────────────┘
```

Filter identity flows from **image metadata** (`RawImageData.MetaData.FilterWheel.Filter`), which NINA stamps at
capture time from the wheel's selected filter — capture-time correct, no live wheel read, no race with wheel
moves. AF exposures carry the AF filter in their metadata, so during an AF run detection automatically uses the
settings of the filter physically in the light path.

## Components

### 1. `PerFilterStarDetectionStore` (new)

Plugin-lifetime singleton, constructed in `HocusFocusPlugin` after `StarDetectionOptions` and exposed as a static
alongside the other options singletons. Owns two persisted values (same `PluginOptionsAccessor`, per-profile):

- `PerFilterStarDetectionEnabled` — bool, default `false`. Not part of any snapshot.
- `PerFilterStarDetectionJson` — one JSON blob:

```json
{
  "SchemaVersion": 1,
  "GlobalSeed": { /* StarDetectionSettingsSnapshot captured at enable time */ },
  "Filters": [
    { "FilterName": "Ha", "Settings": { /* StarDetectionSettingsSnapshot */ } }
  ]
}
```

`GlobalSeed` is the scrubbed snapshot of the global settings captured when the feature was enabled; it is the
seed source for filters encountered later (the legacy keys are frozen but not re-read for seeding).

Records are self-keyed by `filterName` (NINA `TrainedFlatExposureSetting` precedent — a list of records, not a
dictionary). `settings` reuses the existing `StarDetectionSettingsSnapshot` type (flat, profile-detached
`IStarDetectionOptions` implementation, already JSON-round-trips, already accepted by `BuildStarDetectorParams`).
Stored snapshots always hold **resolved** knob values plus the mode flags (`UseAdvanced`, `Simple_*`,
`UseOptimizedSettings`) and the optimized-settings DTO. Machine-local fields present on the snapshot type are
zeroed on capture and ignored on apply (see binder).

API surface (approximate): `Enabled`, `TryGetSnapshot(name)`, `GetOrSeedSnapshot(name)` (seeds from the current
global settings via `StarDetectionSettingsSnapshot.FromOptions`), `UpsertSnapshot(name, snapshot)`,
`KnownFilterNames`, change notification for external updates (wizard Accept, copy), `SeedAllFromGlobal()` on
enable.

Behavior:

- **Load**: on construction and on `IProfileService.ProfileChanged` (same re-read pattern as every options
  class). Corrupt JSON → discard with `Logger.Warning` (existing `OptimizedSettingsJson` precedent). Unknown
  JSON fields tolerated; snapshots missing newer fields fall back to property-initializer defaults.
- **Save**: serialize and `SetValueString` on mutation. NINA debounces profile saves (~1 s), so bursts from
  Simple-mode derivation are cheap.
- **Enable transition** (`false → true`): seed a snapshot from the current global settings for every
  `FilterWheelSettings.FilterWheelFilters` name that lacks an entry.
- **Orphans**: entries whose name no longer matches a profile filter are retained silently.
- **Thread safety**: internal lock; all snapshots handed out are clones.

### 2. `PerFilterEditBinder` (new)

Small unit that turns the existing `StarDetectionOptions` singleton into the edit buffer:

- `EditedFilterName` (INPC; session-only, not persisted). Default: the currently-loaded wheel filter if
  determinable, else the first profile filter.
- On selection change: load the filter's snapshot (`GetOrSeedSnapshot`) into the buffer using
  **imported-snapshot semantics** (the existing `ApplyImportedSnapshot` path, which skips machine-local fields),
  with mirroring suppressed during the load.
- On buffer `PropertyChanged` (mirroring enabled): capture `FromOptions(buffer)` (machine-local fields zeroed)
  and `UpsertSnapshot(EditedFilterName, …)`. Simple-mode derivation writes through normal setters, so derived
  values land in the store already resolved, for free.
- On external store change for the edited filter (wizard Accept, copy-from): reload the buffer.
- On profile switch: re-resolve `EditedFilterName` against the new profile's filter list and reload the buffer
  from the new profile's store. Ordering requirement: this must run **after** `StarDetectionOptions` re-reads the
  legacy keys and the store re-reads its blob (guaranteed by constructing store and binder after the options
  singletons — `ProfileChanged` handlers run in subscription order, an already-documented ordering caveat in this
  codebase).

### 3. `StarDetectionOptions` — buffered persistence mode (contained change)

While the feature is enabled, **all legacy accessor writes are skipped** — both the property setters'
`optionsAccessor.SetValueXxx` calls and the direct writes in `ApplyOptimizedSettings`/`ClearOptimizedSettings`
(the `OptimizedSettingsJson` key); fields still update and `PropertyChanged` still fires. The legacy global keys
are therefore frozen at their pre-enable values, and disabling the feature is exactly `InitializeOptions()` — a
byte-for-byte return to pre-enable behavior. Machine-local settings (`DebugMode`, `IntermediateSavePath`, `PSFParallelPartitionSize`)
keep writing through in both modes (they are global by scope decision). Store data is retained across
disable/re-enable.

### 4. Detection-time resolution (`HocusFocusStarDetection`)

`GetStarDetectorParams` (both overloads funnel here) resolves the options source with strict precedence:

1. Explicit `optionsOverride` (AF replay snapshots) — always wins, exactly as today.
2. Feature enabled → filter name from `image.RawImageData.MetaData.FilterWheel.Filter`; non-empty →
   `store.GetOrSeedSnapshot(name)`, with the live global machine-local settings (`DebugMode`,
   `IntermediateSavePath`, `SaveIntermediateImages`, `PSFParallelPartitionSize`) re-applied onto the resolved
   snapshot before params are built — stored snapshots are scrubbed of machine-local values, which stay global.
3. Feature disabled → the singleton (unchanged).

Feature on + empty/missing filter name → **soft-fail**: `Detect` returns a valid empty
`StarDetectionResult` (zero stars), logs a warning, and pops a warning notification on **every** soft-failed
detection ("Per-filter star detection is enabled but the active filter is unknown — connect a filter
wheel."). The
optimizer's split path (`BuildDetectionContext`/`GateAndMeasure`) is untouched — the wizard supplies params
explicitly. Detection result caching, `CacheKey`, and AF save/replay semantics are unchanged.

### 5. Entry-point gates

Feature on + filter wheel not connected → refuse to start, with a notification naming the feature:

- `HocusFocusVM.StartAutoFocus` (covers the HF AF dockable and the sequencer's `RunAutofocus` through the
  pluggable AF VM) — fail the AF run up front.
- `InspectorVM` — full sensor runs and single-exposure analysis commands.
- `RunAberrationInspector.Validate()` — surfaces as a sequencer validation issue.
- Optimization Wizard `StartAsync` validation (details below).

Stock NINA AF with the HF detector plugged in cannot be gated (core code path) — the soft-fail covers it.

### 6. Star Detection options page

All changes live in the shared `HocusFocus_StarDetection_Options` template
(`Resources/OptionsDataTemplates.xaml`), so both hosts — the plugin options page and the imaging-tab dockable —
get them automatically. Commands follow the existing dual-host pattern (`HocusFocusPlugin` + dockable VM
delegating to one implementation, like `OptimizeStarDetectionCommand`).

- New top row (always visible): **"Per-filter star detection"** CheckBox bound to `Enabled`, with a
  `_Tooltip` resource (options-system invariant).
- When enabled (existing row-visibility-style pattern):
  - **"Editing filter"** ComboBox over the profile filter names (`FilterWheelSettings.FilterWheelFilters` is
    observable, so adds/renames track live), bound to `EditedFilterName`. Every existing control below edits the
    selected filter's set with zero binding changes.
  - **"Copy from…"** source-filter picker + button: builds a `StarDetectionSettingsDiff` between the source
    filter's snapshot and the current buffer, shows the existing `ImportStarDetectionPreviewVM` diff-preview
    dialog, and on Apply loads the source snapshot into the buffer (mirror persists it to the edited filter).
- **Reset Defaults / Import / Export**: no rework — they already operate on the singleton, which is the edit
  buffer, so they naturally target the edited filter while the feature is on. Export gains the filter name in
  its provenance metadata.
- Star Annotator tab: untouched.

### 7. Optimization Wizard

Start page (SelectSource), when the feature is enabled:

- **"Target filter"** ComboBox over the profile filter list, shown for both Live and Replay, defaulting to the
  currently-loaded wheel filter (else first profile filter). The read-only `SweepFilterName`/`SweepGain`
  readouts reflect the target filter (name + its per-filter AF gain) instead of the AF-flagged filter. The VM
  receives the filter list / current filter through its existing delegate-injection constructor.
- **Start validation**: feature on → a target filter must be selected; Live additionally requires the wheel
  connected (on top of today's camera/focuser checks). Replay requires no equipment.
- **Live capture**: the wizard resolves the target name to the profile `FilterInfo` and passes it as the sweep's
  imaging filter together with a new engine option (`UseExactImagingFilter`) that suppresses
  `SetAutofocusFilter`'s auto-switch to the designated AF filter. The sweep runs on exactly the chosen filter;
  its per-filter AF exposure/binning/gain/offset apply via the existing `TakeExposure` logic. The wheel moves as
  part of Start and stays on the target filter afterward.
- **Baseline & seeding**: "current settings" (baseline J, "start from my current settings", Use-current mode)
  come from the target filter's snapshot via the existing `optionsOverride` overload, not the global singleton.
- **Accept**: set `EditedFilterName = target`, then apply through the buffer's existing
  `ApplyOptimizedSettings` — no duplication of the optimized-apply/derivation logic; the mirror persists the
  result to the store, and the options page ends up showing the filter that was just optimized. Identical flow
  in Replay mode with no wheel connected. `ApplyRecommendedStepSize` side-writes stay profile-level, unchanged.
- Feature off → wizard behaves exactly as today.

## Error handling

- **Indeterminate filter at detection** → zero-star result + a warning notification each time (never an
  exception into the imaging pipeline).
- **Wheel disconnected at an owned entry point** → refusal with a clear message before any capture starts.
- **Corrupt store blob** → discarded with a logged warning; per-filter sets fall back to lazy re-seeding from
  the (frozen) global settings.
- **Renamed filter** → old entry orphaned but retained; the new name lazily seeds from global settings.
- **Profile switch** → store reloads from the new profile's blob; binder re-resolves the edited filter; options
  singletons already re-read on `ProfileChanged`.
- **`ChangeFilter` failure during a wizard Start** (wheel jam etc.) → existing engine error surfaces; the wizard
  returns to SelectSource with the error banner, consistent with current failure handling.

## Testing

NUnit, existing patterns (fake `IPluginOptionsAccessor`, `MediatorBundle`, delegate-injected wizard ctor):

- **Store**: seed-on-enable for all profile filters; lazy `GetOrSeedSnapshot`; upsert round-trip; corrupt-JSON
  discard; profile-switch reload; orphan retention; unknown-field tolerance.
- **Binder**: no mirroring during snapshot load; mirroring on edit; edited-filter switch loads the right set;
  machine-local fields excluded both directions; external store change refreshes the buffer; disable →
  `InitializeOptions` restores pre-enable values.
- **Options buffered mode**: setters skip legacy keys while enabled (machine-local settings still write
  through); Simple-mode derivation mirrors resolved values into the store.
- **Detection resolution**: metadata name → that filter's snapshot feeds `BuildStarDetectorParams`; empty name →
  zero-star result with a warning notification raised on each occurrence; replay `optionsOverride` still wins;
  feature off → params bit-identical to today.
- **Gates**: `HocusFocusVM`, `InspectorVM`, `RunAberrationInspector.Validate`, wizard `StartAsync` validation
  matrix (Live/Replay × wheel connected × target selected).
- **Wizard**: target filter passed to the engine with `UseExactImagingFilter` (no AF-filter auto-switch);
  baseline/seed from the target snapshot; Accept routes through the binder into the store; readouts reflect the
  target filter.
- **Engine**: `UseExactImagingFilter` suppresses the AF-filter switch and preserves per-filter exposure
  parameters.

Run the full suite (`dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln`) after every change, per project
invariant. Update the MkDocs manual (star detection options + Optimization Wizard pages) as part of
implementation.

## Out of scope

- Per-filter `StarAnnotatorOptions` (display settings stay global).
- Per-filter settings for OSC/filterless rigs (feature is opt-in; without a wheel it should stay off).
- Gating the stock NINA AF path (core code; soft-fail covers it).
- Automatic migration of orphaned entries after a filter rename (retained, no UI).
- Multi-filter batch optimization in the wizard (one target filter per session).
