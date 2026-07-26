# Software Binning for Star Detection — Implementation Plan

Design spec: [`docs/detection-binning-design.md`](../docs/detection-binning-design.md).

## Context

Hocus Focus has **no software binning today**. The only binning in the codebase is NINA's
hardware/driver binning: `AutoFocusEngine.TakeExposure` sets `CaptureSequence.Binning` from
`FilterInfo.AutoFocusBinning` (per-filter) or `FocuserSettings.AutoFocusBinning` (global), and
`HocusFocusStarDetection.ApplyDetectionImageContext` reads the resulting `MetaData.Camera.BinX` back
purely to scale `PixelScale` (arcsec/px). Nothing ever resamples the image.

That leaves long-focal-length rigs badly served. Every pixel-unit detector knob (`MinHFR`,
`MinimumStarBoundingBoxSize`, `NoiseReductionRadius`, `StructureLayers`, `BackgroundBoxExpansion`,
`StarCenterTolerance`, `DefocusDistortionSizeReference`, …) is tuned for in-focus HFR in the **2–4 px**
range. At 0.2″/px an in-focus star has HFR ≈ 7 px, so the defaults are wrong by 2–3×, detection is
slower (more pixels, bigger stars), and noisier. The current mitigation is the Simple-mode
`PixelScaleEnum.LongFocalLength` preset, which fudges four knobs — a workaround for the missing feature.

The fix: resample the frame **inside star detection only**, by an integer factor, so the detector always
works near its calibrated HFR range; then scale every pixel-space output back so nothing downstream
(auto-focus HFR, annotations, inspector, optimizer, saved runs) can tell the difference except that
detection got better. NINA's AF binning keeps its own job — matching the binning used for imaging —
and the two compose.

**Decisions already made (user):** Auto by default with a manual override; the Optimization Wizard
respects the setting and additionally recommends one from measured sweep HFR; the bundled camera
simulator gains real hardware binning so the stacked case is reproducible in NINA; the AF-binning
prompt offers to reset the global setting **and** any per-filter overrides.

---

## Design

### The unit contract

Detection runs in **binned pixel space**; every result crosses back to **capture pixel space** (the
resolution NINA delivered the frame at, i.e. after NINA AF binning) before leaving `StarDetector`.
So a user's tuned parameters stay in binned units — that is the whole point — while `AverageHFR`,
star centers, bounding boxes and the annotated image are unchanged in meaning.

| Quantity | Transform on the way out (factor `b`) |
|---|---|
| `Star.Center` | `c·b + (b−1)/2` (block-mean sample sits at the block center) |
| `Star.StarBoundingBox`, all `StarDetectorMetrics.*Bounds`, `DebugData.DetectionROI` | `X,Y,W,H → ·b` |
| `Star.HFR`, `PSF.Sigma`, `PSF.FWHMx/FWHMy` (pixels) | `·b` |
| `PSF.FWHMArcsecs` | **unchanged** — `PixelScale` was already scaled by `b`, so it is already physical |
| `Star.Background`, `MeanBrightness`, `PeakBrightness` | **unchanged** — mean binning preserves normalized level (and saturation semantics) |
| `Star.BackgroundPlane` | `OriginX/Y → ·b`; `B1/B2 → /b` (slope is per original pixel) |

Mean (not sum) binning is required: the detector works on `[0,1]`-normalized floats and
`SaturationThreshold = 0.99`; summing would clip. Mean binning keeps the level, divides read noise by
`b`, and is exactly what `Cv2.Resize(..., InterpolationFlags.Area)` computes on an exact integer
downscale.

### Auto resolution

Fixed, documented formula, one shared function so options hint / detection / TestApp / docs agree:

```
estimatedHfrPixels = AssumedFwhmArcsec / (2 · pixelScaleArcsecPerPixel)   // AssumedFwhmArcsec = 3.0
bin                = clamp(round(estimatedHfrPixels / TargetHfrPixels), 1, 4)   // TargetHfrPixels = 3.0
```

`pixelScaleArcsecPerPixel` already includes NINA AF binning (it comes from
`ApplyDetectionImageContext`), so Auto automatically backs off when the camera is already binning.
NaN pixel scale (focal length / pixel size unset) ⇒ 1. Worked examples with a 3.76 µm sensor:
910 mm → 1×; 2800 mm → 2×; 3910 mm → 3×; 5600 mm → 4×.

---

## Work items

### 0. Repo bookkeeping (project convention)

Before touching code, land the **Design** section above as `docs/detection-binning-design.md` and this
plan as `plans/detection-binning-plan.md`, per the specs/plans workflow in `CLAUDE.md`. Work on a
`ghilios/detection-binning` branch; `develop` is PR-only.

### 1. Binning primitive — `Utility/CvImageUtility.cs`

`public static Mat BinMean(Mat src, int factor)`: crop to a multiple of `factor` (drop the ≤ `factor−1`
trailing rows/cols — document it), then `Cv2.Resize(cropped, dst, new Size(w/f, h/f), 0, 0,
InterpolationFlags.Area)`. Returns a new Mat the caller owns. `factor <= 1` ⇒ `src.Clone()`.

### 2. Auto resolver — new `Utility/DetectionBinningResolver.cs`

`DetectionBinningEnum` (new, in `Interfaces/IStarDetectionOptions.cs` next to the other option enums,
`[TypeConverter(typeof(EnumStaticDescriptionConverter))]` + `[Description]` per the existing pattern):
`Auto = 0, Bin1 = 1, Bin2 = 2, Bin3 = 3, Bin4 = 4`.

`static int Resolve(DetectionBinningEnum setting, double pixelScaleArcsecPerPixel)` implements the
formula above; `static string DescribeRecommendation(...)` produces the UI hint text. Constants
`AssumedFwhmArcsec`/`TargetHfrPixels` public so docs and tests cite one source.

### 3. Detector — `StarDetection/StarDetector.cs`, `Interfaces/IStarDetector.cs`

- `StarDetectorParams.DetectionBinning` (int, default 1) — always a **resolved** factor, never `Auto`.
  It lands in the full cache key automatically (`ToCanonicalCacheString` reflects over public
  properties) and must be **added to `EarlyCacheKeyProperties`** (it changes candidate formation), and
  to the `ToString()` line.
- In `BuildDetectionContextInternal`, before the ROI crop:
  - capture `fullImageSize` from the **pre-bin** dimensions (unchanged today, keep it that way);
  - when `DetectionBinning > 1` and the existing Step-1 hotpixel guard would fire, hoist
    `ApplyHotpixelFilter(srcImage, p)` to run at **native resolution** (hot pixels must die before they
    are averaged away into their block), record `metrics.HotpixelCount`, and set
    `hotpixelFilterAlreadyApplied = true` so Step 1 does not repeat it;
  - replace `srcImage` with `CvImageUtility.BinMean(srcImage, b)`, keeping the existing
    `liveOwnedImage` ownership discipline (the pre-bin Mat must be disposed on both the tracker and
    split paths);
  - carry `b` on `DetectionContext` so `GateAndMeasure` can scale a reused context.
- New `internal static void ScaleResultToSourcePixels(HocusFocusStarDetectorResult result, int b)`,
  applied at the **very end** of `GateAndMeasureInternal`, **after** the existing ROI-offset add (the
  ROI rect is in binned coordinates because we bin first, so one uniform scale at the end is correct).
  Implements the table above; mirror the `AllBoundsLists()` idiom with a `ScaleBounds(int)` on
  `StarDetectorMetrics` next to `AddROIOffset`, and add a `Star`-scaling extension beside the existing
  `CvImageUtility.AddOffset(this Star, int, int)`.
- Put `DetectionBinning` on `HocusFocusStarDetectorResult` for traceability, and `Binning` on
  `DebugData` (the structure map stays at binned size).

Doing this inside `StarDetector` means all ~10 TestApp runners that call
`detector.Detect(Mat, params, …)` get binning and correct coordinates for free.

### 4. Options — `StarDetection/StarDetectionOptions.cs`, `Interfaces/IStarDetectionOptions.cs`

`DetectionBinning` (`DetectionBinningEnum`, default `Auto`), following
`.claude/docs/options-system.md`: backing field, `GetValueEnum`/`SetValueEnum` in `InitializeOptions`,
`ResetDefaults`, the interface, and — because it is a per-filter-relevant knob, not machine-local —
`ApplyKnobs`, `StarDetectionSettingsSnapshot` (property + `FromOptions`), `StarDetectionSettingsDiff`
(display name "Detection Binning"), `StarDetectionSettingsExport`. **Not** in `SimplePropertyNames`
and **not** set by `DerivePresetSettings`, so it survives Simple mode and is not clobbered by the
presets.

Plus a read-only, `[JsonIgnore]`, non-persisted `DetectionBinningHint` string for the UI, computed from
`CameraSettings.PixelSize`, `TelescopeSettings.FocalLength` and `FocuserSettings.AutoFocusBinning`,
refreshed on profile change and when `DetectionBinning` changes:
`"Resolved: 2× — 0.28″/px, est. in-focus HFR ~5.4 px → ~2.7 px binned"`.

### 5. Wiring Auto into detection — `StarDetection/HocusFocusStarDetection.cs`

In `ApplyDetectionImageContext` (which already computes `pixelScale` from the profile × hardware
`BinX`): resolve the setting to an int, store it on the params, and multiply `PixelScale` by it, so
`PSF.FWHMArcsecs` stays physical:

```csharp
var softwareBin = DetectionBinningResolver.Resolve(effectiveOptions.DetectionBinning, pixelScale);
detectorParams.DetectionBinning = softwareBin;
detectorParams.PixelScale = pixelScale * softwareBin;
```

`ApplyDetectionImageContext` is currently option-free (it takes only the params + image), so pass the
already-resolved `IStarDetectionOptions` through from both call sites — it is resolved once per
detection today, so per-filter scoping is preserved. `BuildStarDetectorParams(options)` (the
image-independent bundle) leaves `DetectionBinning = 1`; `BuildDefaultStarDetectorParams()` likewise
(keeps the wizard seed at its documented default and the lockstep drift-guard test honest).

### 6. AF-binning conflict prompt

Trigger: `DetectionBinning` is set to a resolved factor > 1 **from the UI** while
`FocuserSettings.AutoFocusBinning > 1` or any `FilterInfo.AutoFocusBinning > 1`.

- `StarDetectionOptions` gains an `internal Func<AutoFocusBinningConflict, Task<bool>>
  AutoFocusBinningPromptHandler` (null in tests), invoked from the `DetectionBinning` setter via
  `IApplicationDispatcher` **Post** (never blocking — see the UI-thread rules in
  `.claude/docs/mvvm-patterns.md`), and suppressed while `InitializeOptions`/`ApplySnapshotCore`/
  `ApplyKnobs` run or while `PersistToProfile == false` (per-filter buffered edits), so import,
  replay, per-filter copy and profile load never prompt. Hosting it on the options object (not
  `StarDetectionOptionsVM`) is required because the same DataTemplate is bound from both the plugin
  Options page and the dockable VM.
- `HocusFocusPlugin` wires the default handler: `MyMessageBox.Show(msg, "Auto Focus Binning",
  MessageBoxButton.YesNo, MessageBoxResult.No)` — the existing idiom
  (`TiltAdapterWizardVM.ShowIdleDisconnectPromptAsync`), defaulting to **No** so a dismissed dialog
  never mutates the profile.
- Message: NINA's Auto Focus Binning should match the binning you image at, so focus is found for the
  frames you actually shoot. Hocus Focus detection binning is a separate, detection-only resample used
  to bring star sizes into the detector's calibrated range. They stack, and 2× on top of 2× is rarely
  what you want. It lists the global value and every filter row above 1, then asks whether to set them
  all back to 1.
- The conflict enumeration + message text is a **pure function** (`AutoFocusBinningConflict.Detect(
  IProfileService)` + `.Describe()`) so it is unit-testable without a dialog.

### 7. UI — `Resources/OptionsDataTemplates.xaml`

New always-visible row in the Star Detector grid, next to Noise Level / Pixel Scale / Focus Range (this
is a top-level usability knob, not an advanced one): label "Detection Binning", `ComboBox` with
`util:EnumBindingSource` + `HF_EnumStaticDescriptionValueConverter` (copy the `Simple_PixelScale` row),
plus a dimmed `TextBlock` bound to `StarDetectionOptions.DetectionBinningHint`. Append the
`RowDefinition` at the end of the hand-numbered list and add the row-number comment, per the existing
convention. Tooltip resource `DetectionBinning_Tooltip` near the others, explaining the *why*: star
detection is calibrated for in-focus HFR of roughly 2–4 px; at long focal lengths stars are far larger
than that, so binning the frame for detection only brings HFR back into range, improves SNR per pixel
and speeds detection up. It does not change the image you see or the HFR that is reported — those stay
at the binning NINA captured at. Leave it on Auto unless you have a reason not to.

### 8. Optimization Wizard — `StarDetection/Optimization/StarDetectionOptimizerWizardVM.cs` + `DataTemplates.xaml`

- **Respect**: nothing to do in the search itself — the wizard builds params through
  `GetStarDetectorParams`/`BuildStarDetectorParams`, and the early/late split is already keyed on
  `EarlyCacheKeyProperties` (item 3 adds `DetectionBinning` there, so contexts cannot be reused across
  factors).
- **Readout**: add `SweepDetectionBinning` next to the existing `SweepBinning` (`"2×2 (camera) + 2×
  (detection)"`), bound in the sweep confirmation panel around `DataTemplates.xaml:491`.
- **Recommend from data**: once the run's frames are evaluated at the baseline settings, take the
  median HFR of the frames nearest best focus, convert to native pixels (`× currentBin`), and run it
  through the same `TargetHfrPixels` rule. If it differs from the current setting, show an advisory on
  the summary page — measured HFR, recommended factor, and an **Apply** button that sets
  `StarDetectionOptions.DetectionBinning` and states plainly that the settings just produced were tuned
  at the old factor, so the wizard should be re-run. No silent re-tuning.

### 9. Camera simulator — `CameraSimulator/HocusFocusSimulatorCamera.cs`

`MaxBinX/MaxBinY => 4`; `BinningModes` = 1×1…4×4. Carry the bin factor on the `PendingExposure` record
(it must travel with the request/render/cts snapshot, same reason the record exists), and in
`DownloadExposure` mean-bin the rendered `ushort[]` by that factor, size the exposure data
`snapshotSensor.Width / b × Height / b`, and set `metaData.Camera.BinX/BinY` from the snapshot after
`FromCamera(this)`. `CameraXSize`/`CameraYSize` stay full-sensor (NINA convention). Update
`documentation/docs/overview/camera-simulator.md`, which currently says "1×1 binning only".

### 10. Docs — `documentation/docs/`

- New `settings/detection-binning.md` (nav entry after **Preprocessing & Noise** in `mkdocs.yml`):
  what it does, why the 2–4 px HFR target exists, the Auto formula with its two constants and worked
  examples, the relationship to NINA's Auto Focus Binning (and why they should not be stacked by
  accident), what is and is not rescaled, and the trailing-row/column crop.
- `settings/index.md`: pipeline overview + the EARLY-parameter list.
- `overview/autofocus.md`: a row/mention in **Key autofocus options** covering the AF-binning relationship.
- `optimization/index.md`: the new sweep readout and the summary recommendation.
- House style per `.claude/docs/documentation-style.md`; verify with `mkdocs build --strict`.

### Explicitly out of scope

`InspectorVM.TakeAndAnalyzeExposureImpl` builds its own `CaptureSequence` and never sets `.Binning`, so
the Inspector's own "take exposure" ignores `AutoFocusBinning` entirely — a pre-existing inconsistency,
unrelated to this feature, left alone.

---

## Verification

### Unit tests (`Joko.NINA.Plugins.HocusFocus.Tests/`)

- `Utility/CvImageUtilityBinningTests` — `BinMean` is an exact block mean; crop-to-multiple behavior;
  `factor <= 1` is a copy.
- `Utility/DetectionBinningResolverTests` — the worked-example table (910/2800/3910/5600 mm), NaN
  pixel scale ⇒ 1, clamping at 4, explicit factors pass through.
- `StarDetection/DetectionBinningTests` — with `Synthetic/SyntheticGaussianStarImage` +
  `Synthetic/HfrGroundTruth.Gaussian(sigma)`: a σ = 6 px star detected at `b = 1, 2, 3` returns the
  same center (within 0.5 px), the same HFR (within a few %), and HFR matching the closed form.
  Bounding boxes, `Metrics.*Bounds` and `DebugData.DetectionROI` land in source coordinates; an
  ROI-cropped run keeps the ROI offset correct.
- `StarDetection/StarDetectorEarlyLateSplitTests` — the early cache key changes with `DetectionBinning`
  and a context built at one factor is never reused at another.
- `StarDetection/StarDetectionOptionsTests` — persistence round-trip, `ResetDefaults`, `ApplyKnobs`,
  snapshot/diff/export completeness, and that `DerivePresetSettings` leaves it alone.
- `StarDetection/AutoFocusBinningConflictTests` — global-only, per-filter-only, both, and none; the
  reset touches exactly the rows it listed; the prompt never fires during import/replay/profile load.
- `CameraSimulator/HocusFocusSimulatorCameraTests` — `SetBinning(2)` ⇒ half-size frame, `BinX = 2` in
  metadata, mean level preserved; `MaxBinX == 4`.

### Headless capstone (`Tests/CameraSimulator/DetectionBinningCapstoneTests`, `[Category("SlowIntegration")]`)

Built on the existing `SyntheticCameraTestScene` + `StarFieldCompositor` + real `StarDetector` harness
(the same shape as `CameraSimulatorAfDegradationCapstoneTests`), with the scene's focal length
parameterized. Every case renders one field, detects at `b = 1` and at the case's factor, and asserts:
injected star positions recovered within ~1 capture pixel, `AverageHFR` agreeing within ~10% after
rescale, and star count not worse.

| Case | Focal length | NINA AF bin | HF setting | Expected |
|---|---|---|---|---|
| A | 910 mm | 1× | Auto | resolves 1×, results bit-identical to today |
| B | 2800 mm | 1× | Auto | resolves 2×, HFR ≈ 4.5 px → ≈ 2.3 px binned |
| C | 4500 mm | 1× | Auto | resolves 3× |
| D | 5600 mm | 2× (frame pre-binned, `BinX = 2` stamped) | Auto | resolves 2× on top ⇒ 4× total; HFR reported in capture (bin-2) pixels |
| E | 2800 mm | 2× | manual `Bin2` | the explicit stacked case; HFR reported in capture pixels |

Plus a faint-field case proving the benefit: at 4500 mm the same field detects **more** stars at the
Auto factor than at 1×.

### Full suite

`dotnet test` per `.claude/docs`/CLAUDE.md — from WSL: `dotnet.exe test "$(wslpath -w
Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo`, timeout 600 s. Note
`SendAsync_WritesOnABackgroundThread` is a known flake unrelated to this work.

### Manual verification inside NINA (to be handed to the user at the end)

With the bundled simulator and no hardware:

1. **Equipment** — Camera: *Hocus Focus Simulator*; Telescope: *NINA Simulator*; Focuser: *NINA Simulator*.
2. **Options → Equipment → Telescope** — Focal Length **2800 mm**. **Options → Equipment → Camera** —
   Pixel Size **3.76 µm**. (Sensor IMX533 in the Hocus Focus simulator options.)
3. **Options → Plugins → Hocus Focus → Star Detector** — Detection Binning **Auto**; the hint should
   read ≈ 0.28″/px, est. HFR ≈ 5.4 px, resolved **2×**.
4. **Options → Focuser → Auto Focus Binning** — leave at **1**. Run an auto-focus: HFR values and the
   annotated frame are at full capture resolution; the log records detection binning 2×.
5. **Stacked case** — set Auto Focus Binning to **2**, then set Detection Binning to **2** manually.
   The prompt appears explaining the difference and offering to reset AF binning; answer **No** to keep
   both. Re-run auto-focus: capture is 1504², detection runs at 752², HFR is reported in 1504² pixels.
6. **3× case** — set Focal Length to **4500 mm** with Detection Binning on Auto; the hint resolves 3×.

The final report will restate steps 1–6 with the values actually observed.
