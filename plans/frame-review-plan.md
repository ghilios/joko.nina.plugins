# Aberration Inspector — "Review Frames" Diagnostic Preview

## Context

The Aberration Inspector runs an autofocus sweep, detects stars across the full sensor in each
frame, registers those stars across frames, and fits a sensor model (tilt + field curvature). When
the model fit looks wrong, the user currently has no in-app way to see *why* — whether a frame had
too few stars, bad HFRs, or a registration that mis-aligned frames. Today the only visual diagnostic
is a set of TIFF files written to disk (`InspectorVM.SaveRegisteredImage`), and only when saving.

This feature adds a **"Review Frames"** button that opens a modal dialog showing, one frame at a
time: the frame image with its **accepted** detected stars overlaid, two text labels per star (a
**registration identifier** and the **HFR**, in different colors), the **number of detected stars**,
and — when RANSAC alignment was enabled — the **transformation details** (scale/rotation/translation)
plus **arrows from each star to its registered target location**. A right-side **legend** explains
the markers, mirroring the existing Star Review labeling UI. Goal: let the user visually diagnose
data-quality issues that impact the sensor-model fit.

### Confirmed product decisions
1. **Requires the Sensor Curve Model to be enabled** — Review reuses the full-sensor per-frame data
   and cross-frame registration that path already computes; no new detection/registration plumbing.
2. **New persisted toggle `FrameReviewEnabled`** (default OFF, in the profile). Its job is to force
   per-frame image retention so review works even on non-saving runs. The button is enabled only if
   the toggle was ON before the run **and** a sensor-model run completed with retained frames.
3. **Annotate all accepted detected stars** — every accepted (non-rejected) star shows its HFR; the
   registration-identifier shows the registered-star index where matched across frames, else **"—"**.
4. **Two labels per star in different colors** + a right-side legend (reuse `StarReviewLegendEntry`).
5. **Modal dialog window**, opened via NINA's window service (same mechanism as the optimizer wizard).

## Key findings driving the design (verified)
- Per-frame data lives in `InspectorVM.FullSensorDetectedStars` (`List<SensorDetectedStars>`,
  ~line 1492), populated for **region index 6 only** (`AutoFocusEngine_SubMeasurementPointCompleted`
  ~1211), and region 6 is added **only when the Sensor Curve Model is enabled** (`GetStarDetectionRegions`
  ~1187). `SensorModel.UpdateModel(...)` (called at ~497) runs only under `sensorCurveModelEnabled` (~485).
- `SensorDetectedStars` (`Inspection/SensorModel.cs` ~36-56): `FocuserPosition`,
  `StarDetectionResult` (`.StarList` = accepted stars; `.AverageHFR`), `Image` (`IRenderedImage`;
  `.Image` is a display-ready, frozen `BitmapSource`), `HasBeenAligned`, `AlignmentTransform`
  (`RANSACRegistration.Matrix3x2?`).
- **Image retention is the memory cost the toggle gates.** `AutoFocusEngine` keeps the per-frame image
  only when `options.PreserveExposures` (AutoFocusEngine.cs ~641); `GetAutoFocusEngineOptions`
  (InspectorVM.cs ~1006) currently sets that true only when `options.Save`.
- Registration results after a run: `SensorModel.SensorModelResult.RegisteredStars` (`RegisteredStar[]`),
  `SensorModel.ReferenceImage` (int). `RegisteredStar.MatchedStars` is `List<MatchedStar>`;
  `MatchedStar` has `ImageIndex` and `Star` (`HocusFocusDetectedStar`). **`MatchedStar.Star` is the
  same object reference as the `StarList` entry** (`SensorModel.MatchStarsUsingKdTree` ~1196-1210) —
  so a reference-identity dictionary maps each accepted star to its registered-star index. The index
  in `RegisteredStars` is the registration identifier and is stable for the same physical star across
  frames. Registration (and thus IDs) is computed **even when RANSAC is off**; RANSAC only controls
  whether frames are aligned (arrows/transform meaningful).
- Arrow endpoints: each star's `OriginalPosition` (raw detection) is the start; `Position` (overwritten
  to the transformed target by `ApplyAlignmentTransform` ~871-885) is the end. Reference frame keeps
  identity (`Position == OriginalPosition`) → no arrow. Transform text via
  `RANSACRegistration.Matrix3x2.ToFullString()` (~949). RANSAC flag: `IInspectorOptions.UseRANSAC`.
- **Reusable Star Review assets** (`StarDetection/Optimization/Review/`): `StarReviewViewport.cs` (pure,
  unit-tested zoom/pan + screen↔image math — reuse verbatim); `StarReviewLegendEntry` (legend row type);
  the Image+Canvas overlay structure + inverse-zoom bindings in `StarReviewControl.xaml(.cs)` (fork,
  stripped to read-only). `StarReviewImaging` is **not** needed (the image is already a `BitmapSource`).
- **Hosting**: `windowService.ShowDialog(vm, title, ResizeMode.CanResize, WindowStyle.SingleBorderWindow)`
  presents the VM in a `ContentPresenter` resolved by an **implicit `DataType` DataTemplate** in the
  exported `StarDetection/Optimization/DataTemplates.xaml` (see the wizard at lines 92-93). InspectorVM
  has `applicationDispatcher` (line 92) and uses CommunityToolkit `RelayCommand`; it must construct its
  own `new WindowServiceFactory()` (not injected; mirrors `HocusFocusPlugin`).

---

## Implementation

### 1. New persisted option `FrameReviewEnabled`
- `Interfaces/IInspectorOptions.cs`: add `bool FrameReviewEnabled { get; set; }` (after `SaveAlignmentImages`).
- `AutoFocus/InspectorOptions.cs`: clone the `SaveAlignmentImages` boolean pattern — backing field +
  property with `optionsAccessor.SetValueBoolean(nameof(FrameReviewEnabled), …)` in the setter,
  `frameReviewEnabled = optionsAccessor.GetValueBoolean(nameof(FrameReviewEnabled), false)` in
  `InitializeOptions()`, and `FrameReviewEnabled = false;` in `ResetDefaults()`.
- **Update every `IInspectorOptions` implementer in the Tests project** (grep `: IInspectorOptions`) with
  a `public bool FrameReviewEnabled { get; set; }` auto-prop, or the suite won't compile.
- **UI control (project invariant)** — `AutoFocus/DataTemplates.xaml` inspector options sub-panel
  (~1819-1844, the `SaveImagesOnReruns`/`SaveAlignmentImages` rows): add a "Keep frames for Review" label
  + checkbox bound to `InspectorOptions.FrameReviewEnabled`, `IsEnabled` bound to
  `InspectorOptions.SensorCurveModelEnabled` (the dependency). Add a tooltip resource explaining the
  memory cost + Sensor-Curve-Model requirement. Add one `<RowDefinition Height="Auto"/>` to the grid.

### 2. Snapshot DTOs + builder — new file `Inspection/FrameReviewSnapshot.cs`
Pure POCOs + a static builder in the `Inspection` namespace (no WPF dependency in the builder logic →
unit-testable; it may reference `System.Windows.Media.Imaging.BitmapSource` only as a carried value).
- `FrameReviewStar`: `CenterX/Y` (= `Position`), `BoxX/Y/Width/Height` (from `BoundingBox`), `Hfr`,
  `int? RegistrationId` (null ⇒ unmatched), `OriginalX/Y`, `bool HasArrow`.
- `FrameReviewFrame`: `ImageIndex`, `FocuserPosition`, `bool IsReference`, `string TransformText`
  (`ToFullString()` or "" for reference/non-RANSAC), `int DetectedStarCount`, `BitmapSource Image`
  (= `SensorDetectedStars.Image.Image`), `IReadOnlyList<FrameReviewStar> Stars`.
- `FrameReviewSnapshot`: `bool RansacEnabled`, `int ReferenceImageIndex`, `IReadOnlyList<FrameReviewFrame> Frames`.
- `FrameReviewSnapshotBuilder.Build(allDetectedStars, registeredStars, referenceImageIndex, ransacEnabled)`:
  - Build `Dictionary<HocusFocusDetectedStar,int>` keyed by **`ReferenceEqualityComparer.Instance`** from
    `registeredStars[i].MatchedStars` → `i`.
  - Drop frames whose `Image`/`Image.Image` is null; order surviving frames by `FocuserPosition`.
  - Per accepted `HocusFocusDetectedStar` in `StarList`: copy primitives into a `FrameReviewStar`,
    `RegistrationId` from the map (else null), `HasArrow = ransacEnabled && !isReference && Position≠OriginalPosition`.
  - Copy only primitives + the frozen `BitmapSource` reference → snapshot is immutable against later
    star mutation. (Use plain `{ get; set; }` if `init`/`IsExternalInit` isn't already used in the project.)

### 3. Capture + command wiring in `AutoFocus/InspectorVM.cs`
- **Force retention** (~1006): `if (options.Save || (inspectorOptions.FrameReviewEnabled &&
  inspectorOptions.SensorCurveModelEnabled)) { options.PreserveExposures = true; }`.
- **Freeze the per-run decision**: set `frameReviewRequestedForRun = inspectorOptions.FrameReviewEnabled
  && inspectorOptions.SensorCurveModelEnabled;` immediately before the `RunWithRegions`/`RerunWithRegions`
  call so a mid-run toggle change can't desync retention from snapshot.
- **Build the snapshot** at the end of the `if (sensorCurveModelEnabled)` block in `AnalyzeAutoFocusResult`
  (just after `SaveRegisteredImages`, ~514), guarded by `frameReviewRequestedForRun`, from
  `FullSensorDetectedStars`, `SensorModel.SensorModelResult.RegisteredStars`, `SensorModel.ReferenceImage`,
  `inspectorOptions.UseRANSAC`. Store in a `private FrameReviewSnapshot reviewSnapshot` field. This point
  runs after `UpdateModel` applied transforms, so `Position`/`OriginalPosition`/`BoundingBox` are final.
- **Availability + command**: `public bool ReviewFramesAvailable => reviewSnapshot?.Frames.Count > 0;`
  and `public RelayCommand ReviewFramesCommand` (`canExecute: () => ReviewFramesAvailable`), declared near
  the other commands (~1483) and initialized in the ctor (~175-182).
- After building the snapshot, marshal via `applicationDispatcher` (background thread): raise
  `ReviewFramesAvailable` and call `ReviewFramesCommand.NotifyCanExecuteChanged()`.
- **Clear** `reviewSnapshot = null` (+ raise/notify) in `ClearAnalysis()` (run start, ~1421) and
  `ClearAnalyses()` (button, ~2102).
- **`ShowFrameReview()`** (private; model on `HocusFocusPlugin.OptimizeStarDetection` ~156-201): construct
  `new FrameReviewVM(reviewSnapshot)`, `new WindowServiceFactory().Create()`, wire `vm.RequestClose →
  windowService.Close()` and `windowService.OnClosed → vm.Dispose()`, then
  `windowService.ShowDialog(vm, "Review Frames", ResizeMode.CanResize, WindowStyle.SingleBorderWindow)`.

### 4. New VM — `StarDetection/Optimization/Review/FrameReviewVM.cs`
`BaseINPC`, `IDisposable`, exposes `event EventHandler RequestClose` and `event EventHandler FitRequested`.
Read-only fork of `StarReviewVM` (drops all labeling/undo). Reuses `StarReviewViewport` and `StarReviewLegendEntry`.
- ctor `(FrameReviewSnapshot snapshot)`: store snapshot, `Viewport = new StarReviewViewport()`, build
  `LegendEntries`, `PrevCommand`/`NextCommand`/`FitCommand`/`CloseCommand`, `CurrentIndex = 0`, `LoadCurrent(fit:true)`.
- Properties: `CurrentIndex`, `PositionLabel` ("3 / 12"), `BitmapSource FrameImage`, `ImageWidth/Height`,
  `FrameHeader`, `DetectedCountText`, `ReferenceNote`, `TransformText`, `bool ShowTransform`,
  `StarReviewViewport Viewport`, `ObservableCollection<FrameReviewMarker> Markers`,
  `IReadOnlyList<StarReviewLegendEntry> LegendEntries`.
- Inverse-zoom bindings copied from `StarReviewVM` (~275-321): `MarkerStrokeThickness`, `MarkerTextScale`,
  `HfrLabelOffset` (HFR label **above** box), plus a `RegistrationLabelOffset` (reg-id label **below** box);
  `NotifyViewportChanged()` re-raises them.
- `FrameReviewMarker` (image-pixel coords, all primitives): box geometry, `RegistrationIdText` ("12"/"—"),
  `HfrText` ("2.34"/"—"), `bool HasArrow`, `ArrowStartX/Y` (= OriginalPosition), `ArrowEndX/Y` (= Position),
  and a precomputed `PointCollection ArrowHead` (small triangle at the target end, so XAML needs no rotation math).
- Frozen brushes: reg-id = cyan `#00E5FF`, HFR = yellow `#FFD700`, accepted box = green `#00FF00`,
  arrow = orange `#FF8C00`. `LegendEntries`: Accepted star / Registration ID (— = unmatched) / HFR /
  Registration arrow (target).
- `LoadCurrent(bool fit)`: set header/count/reference/transform/image, rebuild `Markers` from the frame's
  `Stars`, raise `ShowTransform`, update `Prev/Next` CanExecute; `Next`/`Prev` use `fit:false` (preserve zoom).
- `Dispose()`: drop snapshot + collections so the retained `BitmapSource`s become collectible.

### 5. New View + host
- `StarDetection/Optimization/Review/FrameReviewControl.xaml(.cs)` — read-only fork of `StarReviewControl`:
  header + `PositionLabel`; toolbar (`Fit`, `◀ Prev`, `Next ▶`, right-aligned `Close`); the Image + a
  `ViewportCanvas` with the shared `TransformGroup` (`ScaleTransform` + `TranslateTransform`); a
  `<Image Source="{Binding FrameImage}" Stretch="None" BitmapScalingMode="NearestNeighbor"/>`. Two overlay
  `ItemsControl`s bound to `Markers`:
  1. star layer — green `Rectangle` (box) + HFR `TextBlock` (yellow, above) + reg-id `TextBlock` (cyan,
     below), all with inverse-zoom `ScaleTransform` and `IsHitTestVisible="False"`, faint `#B0000000` bg;
  2. arrow layer (container at canvas origin) — `Line` (`ArrowStart→ArrowEnd`) + `Polygon` (`ArrowHead`),
     orange, `Visibility` from `HasArrow` via `BooleanToVisibilityConverter`.
  A HUD `Border` **outside** the RenderTransform shows `DetectedCountText`, `ReferenceNote`, and (when
  `ShowTransform`) `TransformText`. Right-column legend `StackPanel` bound to `LegendEntries` (copied from
  `StarReviewControl`). Code-behind: copy only the read-only viewport plumbing (`OnDataContextChanged`,
  `OnFitRequested`/`ApplyViewport`, `UpdateScrollBars`, scroll handlers, `MouseWheel` zoom, right-drag pan,
  `SizeChanged` re-fit, Left/Right/F keys). No labeling/drag/undo handlers.
- Register the implicit template in `StarDetection/Optimization/DataTemplates.xaml` (xmlns:review exists,
  line 8) near the wizard template (~93):
  `<DataTemplate DataType="{x:Type review:FrameReviewVM}"><review:FrameReviewControl/></DataTemplate>`.
- Add the **"Review Frames" button** to the inspector button bar in `AutoFocus/DataTemplates.xaml`
  (~1157-1318), `Command="{Binding ReviewFramesCommand}"`, `IsEnabled="{Binding ReviewFramesAvailable}"`
  (clone an existing button; the Clear Analyses button ~1299-1318 shows the pattern).

### 6. Cross-cutting
- No new MEF exports. Declare `<BooleanToVisibilityConverter x:Key="BoolToVis"/>` locally in the new
  control (as `StarReviewControl.xaml:7` does); no new converter classes.
- Threading: snapshot is built on the analysis background task (only reads computed data + copies frozen
  bitmap refs); marshal the `RaisePropertyChanged`/`NotifyCanExecuteChanged` via `applicationDispatcher`.
  `ShowFrameReview` runs on the dispatcher (RelayCommand); `WindowService` handles window creation.

## Critical files
| File | Change |
|---|---|
| `Interfaces/IInspectorOptions.cs`, `AutoFocus/InspectorOptions.cs` | new `FrameReviewEnabled` option |
| `Inspection/FrameReviewSnapshot.cs` *(new)* | snapshot DTOs + `FrameReviewSnapshotBuilder` |
| `AutoFocus/InspectorVM.cs` | force `PreserveExposures`; capture/clear snapshot; `ReviewFramesCommand` + `ShowFrameReview` |
| `StarDetection/Optimization/Review/FrameReviewVM.cs` *(new)* | read-only review VM (reuses `StarReviewViewport`, `StarReviewLegendEntry`) |
| `StarDetection/Optimization/Review/FrameReviewControl.xaml(.cs)` *(new)* | read-only forked viewer + overlays + legend |
| `StarDetection/Optimization/DataTemplates.xaml` | implicit `DataTemplate` for `FrameReviewVM` |
| `AutoFocus/DataTemplates.xaml` | "Keep frames for Review" checkbox + "Review Frames" button |
| Tests `IInspectorOptions` fakes | add `FrameReviewEnabled` |

## Verification
- **Unit tests** (`Tests/Inspection/FrameReviewSnapshotBuilderTests.cs`): reference-identity ID mapping
  (same physical star → same ID across frames); unmatched → null/"—"; arrow gating (off when RANSAC
  disabled / reference frame; on with start=OriginalPosition, end=Position otherwise); reference-frame
  flag + transform text empty for reference; null-image frames dropped; frames ordered by focuser
  position; snapshot immutable after source mutation. Add a `FrameReviewEnabled` round-trip case to the
  existing options tests. Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`.
- **Manual in NINA**: enable Sensor Curve Model + "Keep frames for Review" (verify the checkbox is
  disabled when Sensor Curve Model is off). Run a non-saving Detailed Analysis → "Review Frames" enables.
  Open it: modal dialog, one frame at a time, accepted-star boxes, cyan reg-id + yellow HFR labels, star
  count + transform in the HUD, orange arrows on non-reference frames (RANSAC on), "Reference frame" note
  with no arrows/transform on the reference, legend present; Prev/Next/Fit/wheel-zoom/pan/Close work.
  Toggle RANSAC off and rerun → labels remain, no arrows/transform. Run with the toggle OFF → button stays
  disabled. Clear Analyses / start a new run → button disables and snapshot clears.

## Risks / edge cases
- **Reference frame**: identity transform → no arrows, empty transform text, "Reference frame" note.
- **RANSAC disabled**: IDs still computed (matching still runs); all `HasArrow=false`, `TransformText=""`;
  optionally hide the arrow legend row when `!RansacEnabled`.
- **Unmatched stars**: `RegistrationId=null` → "—"; HFR still shown.
- **Null images / all frames dropped**: `Frames.Count==0` → button stays disabled.
- **Memory**: snapshot holds references to the *same* frozen bitmaps the engine already kept (no copies);
  `Dispose()` + clearing on new run/ClearAnalyses release them; tooltip documents the cost.
- **Two-label overlap** at dense fields: labels are constant on-screen (inverse-zoom), one above/one below
  — acceptable for a diagnostic view (matches Star Review).
