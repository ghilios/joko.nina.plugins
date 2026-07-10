# Show star-detection annotations during Auto Focus

## Context

During an auto-focus run HocusFocus displays every captured frame on NINA's imaging view, but never with the star-detection overlay — so the user can't see which stars the detector actually found, where the ROI landed, or which candidates were rejected, at the moment it matters most (while the sweep is running and focus is visibly changing).

The suppression is structural, not a flag. `AutoFocusEngine.PrepareExposure` calls `imagingMediator.PrepareImage(..., detectStars: false)`, so NINA's display pipeline runs neither detection nor `IStarAnnotator.GetAnnotatedImage`. Meanwhile the engine runs its *own* `IStarDetection.Detect` for the HFR measurement and throws the result at the AF curve — it never reaches the annotator. (Per-frame annotated TIFFs were removed in `ee9c5eb`; the Review-Frames UI re-renders overlays after the fact.)

The outcome we want: a new, off-by-default star-annotator option — **"Annotate During Auto Focus"**, placed immediately after "Show Annotations" — that overlays the AF run's *actual* detection result onto the displayed frame, live, as each frame is measured. We reuse the detection AF already computed rather than running a second one, so the overlay shows exactly the stars that produced the HFR point.

**Scope (decided):** live AF only, and only plain full-frame runs. Replay (`Rerun` over saved frames) is suppressed — its bounded prefetch prepares frames ahead of analysis, so the stale-frame guard would reject most of them and the overlay would appear erratically; Review Frames already covers that case. Multi-region runs (Aberration Inspector, Tilt Adapter Wizard) are suppressed too, so nine regions never fight over one display.

## Approach

After AF's own detection completes in `EvaluateExposure`, hand the result to the selected `IStarAnnotator` and push the annotated bitmap to the display via **`imagingMediator.SetImage(BitmapSource)`** — an existing NINA API (`ImagingVM.SetImage` → `ImageControl.Image = img`) that the engine can reach through the `imagingMediator` it already holds. No `IImageControlVM` import is needed.

Two NINA facts this rests on (verified against the NINA source at `/mnt/c/Users/ghili/src/nina`; those files are Windows-1252, read with `iconv -f WINDOWS-1252 -t UTF-8`):

- `RenderedImage.DetectStars(annotateImage: true, …)` annotates by calling `starAnnotator.GetAnnotatedImage(p, result, this.OriginalImage, maxStars, token)` and assigning the result to `Image`. So `OriginalImage` — the stretched, un-annotated bitmap — is the correct thing to pass, and `SetImage` is the correct place to put the answer.
- `PluggableBehaviorSelector`'s constructor takes only `(profileService, ninaDefault)`; plugin behaviors are appended later by the loader. So importing `IPluggableBehaviorSelector<IStarAnnotator>` into the plugin manifest cannot eagerly construct `HocusFocusStarAnnotator` (which reads the static `HocusFocusPlugin.StarAnnotatorOptions` in its importing constructor). The manifest already imports the `IStarDetection` selector the same way.

### Stale-frame guard

`AutoFocusState.ExposureSemaphore` permits `MaxConcurrent` in-flight analyses, so frame N's detection can finish *after* frame N+1 has already been displayed. Without a guard, a late `SetImage` would paint a stale overlay. Track the most recently prepared `IRenderedImage` on `AutoFocusState` and `ReferenceEquals`-check it before painting.

This also aligns `HocusFocusStarAnnotator`'s own live re-annotation guard (`previousAnnotatedImageRef` vs `imageControlVM.RenderedImage.OriginalImage`): `SetImage` writes only `Image`, never `RenderedImage`, so toggling an annotator option mid-run correctly re-renders the current AF frame.

### Fire-and-forget, with a drain

`GenerateAnnotatedImage` does a full-frame 16→8bpp conversion plus draw (tens to hundreds of ms). Awaiting it inline would sit on the HFR measurement path and hold the `ExposureSemaphore` slot longer, changing AF timing. Instead spawn it on a background task, track the task on `AutoFocusState`, and drain the tracked tasks in `RunImpl`'s `finally` so a late `SetImage` can never clobber the next non-AF image.

## Changes

### 1. `Interfaces/IStarAnnotatorOptions.cs`

Add `bool ShowAnnotationsDuringAutoFocus { get; set; }` immediately after `ShowAnnotations` (line 79).

### 2. `StarDetection/StarAnnotatorOptions.cs`

Four touch points, mirroring `ShowAnnotations` exactly (`GetValueBoolean`/`SetValueBoolean`):

- `InitializeOptions()` after line 50: `showAnnotationsDuringAutoFocus = optionsAccessor.GetValueBoolean("ShowAnnotationsDuringAutoFocus", false);`
- `ResetDefaults()` after line 83: `ShowAnnotationsDuringAutoFocus = false;`
- Backing field + property after the `ShowAnnotations` block (line 126), with the `if (x != value)` guard, `SetValueBoolean`, `RaisePropertyChanged()`.

Do **not** add it to `AutoFocusFrameReviewVM.OverlayAffectingProperties` — that set governs the separate Review-Frames re-render.

### 3. `AutoFocus/AutoFocusEngine.cs`

**Constructor (lines 63–84).** Add two dependencies after `starDetectionSelector`:
`IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector` and `IStarAnnotatorOptions starAnnotatorOptions`, with matching readonly fields. All needed usings are already present (`NINA.Core.Interfaces`, `NINA.Image.ImageAnalysis`, `…HocusFocus.Interfaces`, `System.Windows.Media.Imaging`).

**`AutoFocusState`** (near the other fields, ~line 560). Add a `volatile IRenderedImage` holding the most recently prepared frame, exposed as `LastDisplayedRenderedImage`; and a `List<Task>` of spawned annotation renders guarded by the existing `StatesLock`, with `TrackDisplayAnnotationTask(Task)` / `SnapshotDisplayAnnotationTasks()` (mirroring how `AnalysisTasks` is handled).

**`PrepareExposure(state, IImageData, token)` (lines 1012–1021).** Capture the return of `imagingMediator.PrepareImage(...)` into a local, assign it to `state.LastDisplayedRenderedImage`, then return it. This inner overload is the single choke point for both live and replay paths, and its return value is exactly what NINA assigns to `ImageControlVM.RenderedImage`.

**Pure gate**, placed beside the other `internal static` decision helpers (~line 464–524, next to `ShouldRetryFromCalculatedPoint`) so it is unit-testable in isolation:

```csharp
internal static bool ShouldAnnotateAutoFocusDisplay(
    bool annotateDuringAutoFocus, bool showAnnotations,
    bool isFullFrameRegion, bool isLiveRun, bool frameIsCurrentlyDisplayed)
```

returning the conjunction. `showAnnotations` is included because `GenerateAnnotatedImage` no-ops when the master switch is off — checking it up front skips the render entirely.

**Side-effecting helper** `MaybeDisplayAutoFocusAnnotation(state, regionState, image, annotationParams, analysisResult, isLiveRun, token)`, placed after `EvaluateExposure` (~line 765). It:

- returns if `image?.OriginalImage` is null, or the gate says no;
- resolves `starAnnotatorSelector.GetBehavior()` (null-check it);
- computes `maxStars` as `profileService.ActiveProfile.ImageSettings.AnnotateUnlimitedStars ? -1 : 200`, mirroring `RenderedImage.DetectStars`. The HocusFocus annotator ignores this in favour of its own `MaxStars` option, but NINA's built-in annotator honours it;
- spawns `Task.Run` (do **not** pass the token to `Task.Run` — let the delegate own cancellation so its `catch` always runs) that awaits `GetAnnotatedImage(annotationParams, analysisResult, image.OriginalImage, maxStars, token)`, **re-checks** `ReferenceEquals(state.LastDisplayedRenderedImage, image)` after the async render, then calls `imagingMediator.SetImage(annotated)`;
- swallows `OperationCanceledException` and logs any other exception via `Logger.Error` — annotation must never fail an AF measurement;
- registers the task with `state.TrackDisplayAnnotationTask(...)`.

**Call site.** In `EvaluateExposure`'s `STARHFR` branch, after the `PreserveExposures` block (line 743) and before the closing `Logger.Debug`/`return`:

```csharp
MaybeDisplayAutoFocusAnnotation(state, regionState, image, analysisParams, analysisResult,
    isLiveRun: cacheSource == null, token);
```

- `isFullFrameRegion` inside the gate is `regionState.Region == null`. `AutoFocusState`'s constructor (line 552) creates exactly one `AutoFocusRegionState(this, 0, null, …)` when `regions == null`, and `RunWithRegions` always supplies non-null regions — so `Region == null` is the exact discriminator for a plain single-region run, and it implies `RegionIndex == 0`.
- `cacheSource == null` is a reliable live/replay discriminator: `RerunImpl` constructs a `SavedDetectionCacheSource` unconditionally (line 1999) and the live path never passes one.
- Passing `analysisParams` is correct for this branch: with `Region == null`, the HocusFocus detector returns a `HocusFocusStarDetectionResult` (whose `DetectorParams.Region` drives ROI drawing, and `p` is ignored), while NINA's built-in detector returns a plain `StarDetectionResult`, for which the annotator reads `p.UseROI / InnerCropRatio / OuterCropRatio` — which `analysisParams` carries.

The `CONTRASTDETECTION` branch and the `Statistics` early return do no star detection and need nothing.

**Teardown drain.** In `RunImpl`'s outer `finally` (line 1744), before the `PerformPostAutoFocusActions` try, `await Task.WhenAll(autoFocusState.SnapshotDisplayAnnotationTasks())` inside a `try`/`catch`. `RunImpl` is already `async`. `RerunImpl` needs nothing — replay spawns no annotation tasks.

**Thread affinity is fine.** `ImagingVM.SetImage` just assigns `ImageControl.Image`; the setter assigns the field, calls `ResizeRectangleToImageSize` only when `ShowBahtinovAnalyzer` is true, and raises `PropertyChanged` (WPF marshals binding updates). `GenerateAnnotatedImage` returns a `Freeze()`d `BitmapSource`. Precedent: `HocusFocusStarAnnotator.cs:68` already assigns `imageControlVM.Image` from a `Task.Run`, and NINA's own `ProcessAndUpdateImage` sets `Image` off the UI thread.

### 4. `AutoFocus/AutoFocusEngineFactory.cs` and `HocusFocusPlugin.cs`

Thread the two new dependencies through. The factory mirrors the engine's constructor; `HocusFocusPlugin`'s `[ImportingConstructor]` (lines 73–83) gains `IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector`, and the `AutoFocusEngineFactory` construction (lines 115–125) passes it plus the existing static `StarAnnotatorOptions` (already assigned at lines 99–101, before the factory). **Keep the argument order identical across engine, factory, and this call site.**

### 5. `Resources/OptionsDataTemplates.xaml`

All edits confined to the tooltip block and the `HocusFocus_StarAnnotator_Options` template (lines 2334–2917). The template above it numbers rows up to 40 — do not touch it.

- After line 489, a `ShowAnnotationsDuringAutoFocus_Tooltip` `TextBlock` resource, alongside the other `*_Tooltip` keys.
- Add one `<RowDefinition />` to the block at lines 2341–2369 (29 → 30).
- Insert the label + `CheckBox` (`IsChecked="{Binding ShowAnnotationsDuringAutoFocus}"`, `Text="Annotate During Auto Focus"`) at `Grid.Row="1"`, between the Show Annotations block (ends 2385) and Show All Stars (starts 2386), copying the Show Annotations markup verbatim for margins/alignment.
- **Renumber every `Grid.Row` from 1..28 → 2..29** in lines 2386–2915. Work in **descending** order (28→29, then 27→28, …, 1→2) so no intermediate value collides. The two `Grid.Row="0"` at 2372/2378 stay. Highest existing value is 28 at lines 2887/2901.

### 6. `TestApp/Program.cs`

`StaticStarAnnotatorOptions` (line 291) implements the interface, so it needs `public bool ShowAnnotationsDuringAutoFocus { get; set; }` after line 292 — a required compile fix. `CreateDefault()` needs no entry (`false` is the default). TestApp constructs neither `AutoFocusEngine` nor `AutoFocusEngineFactory`, and `new HocusFocusStarAnnotator(...)` at line 248 is unchanged.

`Tests/TestDoubles/MediatorBundle.cs` already exposes `StarAnnotatorOptions` and `StarAnnotatorSelector` NSubstitute doubles and builds VMs (not the engine), so it needs no change.

### 7. Tests

`Tests/StarDetection/StarAnnotatorOptionsTests.cs` — add the new option to all four existing tests, matching their established shape: an `Is.False` assert in `Defaults_AreLoadedFromAccessor`; a set + `store.Snapshot[...]` assert in `Setters_PersistToAccessor`; a `[TestCase(nameof(StarAnnotatorOptions.ShowAnnotationsDuringAutoFocus), true)]` on `Setter_RaisesPropertyChanged`; and a set-then-reset assert in `ResetDefaults_RestoresDocumentedDefaults`.

`Tests/AutoFocus/AutoFocusEngineTests.cs` — update the `Build(...)` helper (lines 38–53) with the two new constructor arguments (`Substitute.For<...>()`; no new usings needed). Add table-driven tests for `ShouldAnnotateAutoFocusDisplay`: one all-true case, and one case per false branch (option off, `ShowAnnotations` off, multi-region, replay, stale frame). This matches how `ShouldRetryFromCalculatedPoint` / `EvaluateHfrImprovement` are already tested.

An end-to-end engine test asserting `SetImage` was called is feasible but heavy (fake `IStarDetection`, an `IRenderedImage` with `RawImageData`/`OriginalImage`, an `IStarAnnotator` double) and races on the fire-and-forget task. The pure gate carries the logic; skip it.

### 8. `documentation/docs/overview/star-annotation.md`

Add one row to the **Display options** table, directly after the "Show Annotations" row (line 31):

```markdown
| Annotate During Auto Focus | Off | on / off | Draws the overlay on the live image during a plain Auto Focus run, so you can watch star detection as the sweep progresses. Requires **Show Annotations**. Off by default. |
```

Follow `.claude/docs/documentation-style.md`. The page's opening paragraph already claims "The AutoFocus engine … pick[s] the active annotator through `IPluggableBehaviorSelector<IStarAnnotator>`" — after this change that becomes true for the first time, so no correction is needed there.

## Verification

1. **Unit tests.** Per `.claude/docs`, there is no `dotnet` in WSL — run on the Windows side against the WSL path, with a generous timeout:
   `dotnet test '\\wsl.localhost\Ubuntu-20.04\home\ghilios\src\hocus-focus\Joko.NINA.Plugins\Joko.NINA.Plugins.sln' -c Debug --nologo` (timeout 600).
   Everything must pass, including the new `StarAnnotatorOptions` and `ShouldAnnotateAutoFocusDisplay` cases. Never mark a test expected-to-fail to get green.
2. **XAML renumbering sanity.** After editing, confirm the annotator template has exactly 30 `RowDefinition`s and that `Grid.Row` values 0–29 each appear exactly twice within lines 2334–2917 (except the rows whose label/control pairing legitimately differs, e.g. the font-size row) — a stray duplicate silently stacks two controls on one row.
3. **Live behavior in NINA** (Windows MCP; see `.claude/docs/nina-mcp-screenshots.md`). Options → HocusFocus → Star Annotator: confirm "Annotate During Auto Focus" renders directly under "Show Annotations", unchecked. Run an auto focus with it **off** — frames display clean, as today. Turn it **on**, run auto focus again — each displayed frame shows star bounds, centers, labels, and the ROI box from that frame's own detection. While the sweep is running, toggle e.g. "Star Bounds Type": the current frame's overlay should re-render live (this exercises the annotator's `previousAnnotatedImageRef` guard lining up with `SetImage`).
4. **Non-regression on the suppressed paths.** With the option on, run the Aberration Inspector and a saved-run replay: neither should paint an overlay on the imaging view, and Review Frames must still render its own overlays normally.
5. **No late paint.** With the option on, finish an AF run and immediately take a normal snapshot exposure. The snapshot must not be overwritten by a trailing annotated AF frame — this is what the `RunImpl` drain protects.
