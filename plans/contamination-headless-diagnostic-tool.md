# Headless Contamination Diagnostic Tool

## Context

The recently added **sector-annulus asymmetry** contamination test
(`StarDetector.IsContaminatedBySectors`) still flags a substantial number of stars as
"contamination suspected" that are visually fine. We need to root-cause and re-tune it
**without the user launching NINA and eyeballing the overlay each time**.

This plan adds a **self-executable, headless diagnostic** that:
1. Takes a path to an image file on the command line (XISF / FITS / TIFF),
2. Runs HocusFocus star detection using the user's **real settings from the NINA profile DB**
   (so it reproduces exactly what NINA does), and
3. Emits machine-readable diagnostics so the contamination decision for **every accepted star**
   can be analyzed offline by the developer.

The current `TestApp` console project already loads an image into an OpenCV `Mat` and calls
`StarDetector.Detect`, but it (a) uses hard-coded params rather than the NINA DB, and (b) the
detector exposes **no per-star reasoning** for the contamination decision. Both gaps are closed here.

**Confirmed decisions (with the user):**
- Add an **opt-in, zero-overhead** diagnostics hook *inside the detector* (not a separate
  re-implementation), so the CSV exactly matches the production decision.
- The tool is a **self-contained executable** that accepts a file path argument and loads detection
  options from NINA automatically.
- Artifacts: **per-star CSV (including pixel coordinates)** + **annotated PNG** + a short summary
  text file, in addition to the NINA log file. Coordinates are required so we can pull raw pixel data
  from the original image during analysis.

> Per CLAUDE.md, this plan lives in `plans/`. Clear context (`/clear`) before executing.

## Key facts (verified against source)

Plugin repo: `/mnt/c/Users/ghili/src/joko.nina.plugins`. NINA source (for headless feasibility):
`/mnt/c/Users/ghili/src/nina`.

**Contamination code (`Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetector.cs`):**
- `IsContaminatedBySectors(double[] sectorMedians, int[] sectorCounts, double noiseSigma, double sensitivity, int minSectorPixels)` (~`:413-432`) — **pure**; for `s=0..3`, `opp=s+4`: skip if either
  `count < minSectorPixels`; `diff=|m[s]-m[opp]|`; `se = 1.2533*noiseSigma*sqrt(1/n_s+1/n_opp)`; trips
  when `diff > sensitivity*se`; returns false if `sensitivity<=0 || noiseSigma<=0`.
- `OctantOf` (~`:438`); consts `NumSectors=8`, `MinSectorPixels=8` (~`:824`).
- `ComputeStarParameters` (~`:788+`) bins annulus → `sectorMedians[8]`/`sectorCounts[8]` (~`:958-968`),
  calls `IsContaminatedBySectors(..., p.ContaminationSensitivity, MinSectorPixels)` (~`:969`), stores on
  private `StarCandidate.ContaminationSuspected`. Can `return null` (degenerate) at ~`:918`.
- `EvaluateStarCandidate` sets `star.StarContaminationSuspected` and increments
  `metrics.ContaminationSuspected` (~`:696-699`); has several earlier `return null` rejection paths.
- `noiseSigma` = `CvImageUtility.KappaSigmaNoiseEstimate(noiseReduced, p.NoiseClippingMultiplier).Sigma`
  (computed in `DetectImpl` ~`:214`, flows through `ScanStars`→`EvaluateStarCandidate`→`ComputeStarParameters`).
- Entry points: `Detect(Mat, StarDetectorParams, IProgress, CancellationToken)` (~`:88`, expects **CV_32F
  normalized [0,1]**); `Detect(IRenderedImage, ...)` (~`:99`). Ctor `StarDetector(IAlglibAPI)` (~`:39`).

**Types (`Interfaces/IStarDetector.cs`):** `StarDetectorParams` (~`:195`; `ContaminationSensitivity` ~`:219`;
`SaveIntermediateFilesPath` ~`:264` = the opt-in pattern to mirror; `ToString()` ~`:298`). `Star` (~`:302`;
`Center`, `StarBoundingBox`, `Background`, `HFR`, `StarContaminationSuspected` ~`:317`).
`StarDetectorMetrics.ContaminationSuspected` (~`:344`). `HocusFocusStarDetectorResult { DetectedStars;
Metrics; DebugData }` (~`:368`).

**Settings (`StarDetection/StarDetectionOptions.cs`):** `StarDetectionOptions(IProfileService)` (~`:28`)
reads real values via `PluginOptionsAccessor` from `profileService.ActiveProfile.PluginSettings`
(`InitializeOptions()` reads `"ContaminationSensitivity"` default 4.0 ~`:152`, `StarClippingMultiplier`
~`:151`, `NoiseClippingMultiplier` ~`:150`, etc.). Ctor subscribes to `profileService.ProfileChanged`
(needs a non-null `IProfileService`). Plugin GUID `0f1d10b6-d306-4168-b751-d454cbac9670`
(`Properties/AssemblyInfo.cs:21`).

**Options→params mapping:** `HocusFocusStarDetection.GetStarDetectorParams(IRenderedImage, StarDetectionRegion, bool)`
(~`:266-320`) is the source of truth and includes `ContaminationSensitivity = options.ContaminationSensitivity`.
It is **instance-bound and needs an `IRenderedImage`** (for Region/PixelScale), so the option-derived field
assignments must be factored out to be reusable headless (see Part 2).

**NINA infra (`/mnt/c/Users/ghili/src/nina`):**
- `NINA.Profile.ProfileService` — public ctor; `TryLoad(profileId)` enumerates
  `%LOCALAPPDATA%\NINA\Profiles\*.profile`, selects active. **Pitfall:** the `ActiveProfile` setter does
  `Application.Current.Resources["ActiveProfile"]=...` → **NRE if `Application.Current` is null**
  (`ProfileService.cs:~320`). `Profile.Load` rewrites `LastUsed` (harmless).
- `NINA.Core.Utility.Logger` (Serilog) → `%LOCALAPPDATA%\NINA\Logs`; `Logger.SetLogLevel(LogLevelEnum.TRACE)`.
- Image loading: `BaseImageData.FromFile(path, bitDepth, isBayered, IRawConverter, IImageDataFactory, ct)`
  dispatches by extension — `.xisf`→`XISF.Load`, `.fits`→`FITS.Load`, `.tif`→WPF decoder. `XISF.Load` is
  self-contained for pixel decode (signature→header→data block→`ushort[]`); it only uses
  `IImageDataFactory.CreateBaseImageData(ushort[], w, h, 16, isBayered, meta)` to *wrap* the result.
  `ImageDataFactory`'s ctor needs `IProfileService` + two `IPluggableBehaviorSelector<>` (stub-able).
  `CvImageUtility.ToOpenCVMat(IImageData)` (~`:99`) → CV_32F via `ToOpenCVMat(ushort[], bpp, w, h)` (~`:52`).

**TestApp (`TestApp/`):** `OutputType=Exe`, `net8.0-windows7.0`, `UseWPF=true`, `UseWindowsForms=true`,
`AllowUnsafeBlocks=true`; references HocusFocus + OpenCvSharp4 4.6.0 + alglib.net 3.19.0 + NINA.Plugin +
`Serilog.Sinks.Console`. `App.xaml` is the `ApplicationDefinition` (WPF auto-generates `Main` → shows a
window). `Program.cs` has a `MainAsync` (not currently the entry point) that already demonstrates:
OpenCV TIF load → `ConvertToFloat` (CV_32F), `new StarDetector(alglibAPI)`, `Detect(srcFloat, params, ...)`,
`HocusFocusStarAnnotator.GetAnnotatedImage(...)`, and a `StaticStarAnnotatorOptions` implementing
`IStarAnnotatorOptions` (incl. `ShowContaminated`/`ContaminatedColor`). Reuse these.

## Part 1 — Opt-in diagnostics in the detector (production; off by default)

Files: `Interfaces/IStarDetector.cs`, `StarDetection/StarDetector.cs`.

1. **New record** (`Interfaces/IStarDetector.cs`, near `Star`):
   ```csharp
   public sealed class ContaminationDiagnosticRecord {
       public double CenterX, CenterY;        // image/pixel coords (ROI offset applied like DetectedStars)
       public double NoiseSigma, Sensitivity; // values actually used
       public int    MinSectorPixels;         // = 8
       public double[] SectorMedians;         // length 8
       public int[]    SectorCounts;          // length 8
       public double[] PairDiff;              // length 4: |m[s]-m[s+4]|
       public double[] PairThreshold;         // length 4: sensitivity*1.2533*noiseSigma*sqrt(1/n_s+1/n_opp)
       public double[] PairRatio;             // length 4: PairDiff/PairThreshold (NaN if skipped)
       public bool[]   PairSkipped;           // length 4: either count < MinSectorPixels
       public int      TrippingPairIndex;     // first s that tripped, else -1
       public bool     ContaminationSuspected;
       public double   Hfr, Background;
   }
   ```
2. **Opt-in flag** on `StarDetectorParams`: `public bool CollectContaminationDiagnostics { get; set; } = false;`
   (exclude from `ToString()`).
3. **Result list** on `HocusFocusStarDetectorResult`:
   `public List<ContaminationDiagnosticRecord> ContaminationDiagnostics { get; set; } = null;`
4. **Keep `IsContaminatedBySectors` pure** (preserves the 6 existing unit tests). In
   `ComputeStarParameters`, immediately after the existing `IsContaminatedBySectors` call, **only when
   `p.CollectContaminationDiagnostics`**, recompute the 4 pair diff/threshold/ratio/skip values with the
   identical formula and stash them (plus `noiseSigma`, `sensitivity`, sector arrays) on the private
   `StarCandidate` (add nullable carry fields). No work when the flag is off.
5. In `EvaluateStarCandidate`, at the accepted-star site (~`:696`), when the flag is set, finalize a
   `ContaminationDiagnosticRecord` (fill `CenterX/Y/Hfr/Background` from the final `Star`) and add it to a
   `System.Collections.Concurrent.ConcurrentBag<ContaminationDiagnosticRecord>` field on the detector
   (allocated in `DetectImpl` only when enabled — scanning is parallel, so it must be thread-safe).
6. In `DetectImpl`, before returning, materialize the bag into an ordered `List<>`, apply the **same ROI
   offset** logic used for `DetectedStars`, and assign to `result.ContaminationDiagnostics`. Optionally
   `Logger.Trace` a one-line summary per flagged star (cheap; only when enabled).

Net effect: normal NINA runs are unchanged (flag false ⇒ no allocations, no extra math); rows correspond
1:1 with accepted `DetectedStars`/`metrics.ContaminationSuspected`.

## Part 2 — Reusable options→params mapping

Files: `StarDetection/HocusFocusStarDetection.cs`, `Properties/AssemblyInfo.cs`.

- Factor the **option-derived** field assignments of `GetStarDetectorParams` into
  `internal static StarDetectorParams BuildStarDetectorParams(IStarDetectionOptions options)` (includes
  `ContaminationSensitivity`, all clipping/structure/PSF/threshold fields). Have the existing instance
  method call it and then layer on the `IRenderedImage`-dependent bits (`Region`, `PixelScale`) — so
  production behavior is unchanged and there is a single source of truth.
- Add `[assembly: InternalsVisibleTo("TestApp")]` to the HocusFocus assembly so the harness calls the real
  mapping (no drift).
- *Fallback if InternalsVisibleTo is undesirable:* replicate the ~30 assignments in the harness with a
  "keep in sync with `BuildStarDetectorParams`" comment.

## Part 3 — Self-executable headless harness (in `TestApp`)

Files: `TestApp/TestApp.csproj`, `TestApp/Program.cs`, new `TestApp/ContaminationDiagnosticRunner.cs`,
optional `TestApp/StubBehaviorSelectors.cs`.

1. **Make it run headless deterministically:** WPF currently auto-generates `Main` from `App.xaml`.
   - In `TestApp.csproj` add `<StartupObject>TestApp.Program</StartupObject>` and change `App.xaml`'s build
     action from `ApplicationDefinition` to `Page` (no auto-`Main`, no auto-window).
   - Add `[STAThread] static async Task Main(string[] args)` to `Program.cs` (STA needed for the WPF TIFF
     decoder, the annotator's `BitmapSource`, and the `Application` shim below). If no args (or
     `--gui`), preserve the old behavior (run the existing window/`MainAsync`) so nothing is lost.
2. **Runner** (`ContaminationDiagnosticRunner.Run(string[] args)`), invoked when the first arg is
   `contamination` (or `--image` is present):
   - **Args:** `--image <path>` (required); `--profile-id <guid>` (optional, default active);
     `--out <dir>` (default `%LOCALAPPDATA%\NINA\Logs\hf-diag\<timestamp>`); `--sensitivity <double>`
     (optional override); `--sensitivity-sweep <a,b,step>` (optional A/B sweep, re-`Detect` per value on
     the same Mat).
   - **Logger:** `Logger.SetLogLevel(LogLevelEnum.TRACE)`; add the Serilog console sink (package present).
   - **WPF shim:** `if (Application.Current == null) new Application();` (do **not** call `Run()` / show a
     window) — makes the real `ProfileService.ActiveProfile` setter safe.
   - **Profile:** `var ps = new ProfileService(); ps.TryLoad(profileId);` → `ps.ActiveProfile`; log id/name.
     *Fallbacks:* (A) `Profile.Load(path)` directly + hand-built `PluginOptionsAccessor`; (B) seed an
     `InMemoryPluginOptionsAccessor` from the `.profile` for the plugin GUID.
   - **Settings:** `var opts = new StarDetectionOptions(ps);` → log every resolved value (proves the real
     profile was read, e.g. `ContaminationSensitivity` ≠ the 4.0 default).
   - **Params:** `var p = HocusFocusStarDetection.BuildStarDetectorParams(opts);
     p.CollectContaminationDiagnostics = true;` apply `--sensitivity` override if given. Keep the profile's
     real `ModelPSF`/clipping values for fidelity (don't force-disable).
   - **Image → Mat (CV_32F, [0,1])** by extension:
     - `.tif/.tiff`: reuse `Program.ConvertToFloat(new Mat(path, ImreadModes.Unchanged))` (no NINA dep).
     - `.xisf/.fits`: `XISF.Load`/`FITS.Load` → `IImageData` → `CvImageUtility.ToOpenCVMat(imageData)`.
       Provide a minimal `IImageDataFactory` (real `ImageDataFactory` with stub
       `IPluggableBehaviorSelector<IStarDetection>` / `IPluggableBehaviorSelector<IStarAnnotator>` returning
       null), or construct `BaseImageData` directly from the decoded `ushort[]`. Sample file
       `...BitDepth16_Bayered0...xisf` is 16-bit mono ⇒ `isBayered=false`, use `Detect(Mat,...)`.
   - **Detect:** `var result = await new StarDetector(new AlglibAPI()).Detect(srcFloat, p, null, CancellationToken.None);`
3. **Outputs** (to `--out`):
   - **`contamination_stars.csv`** — one row per accepted star (1:1 with `result.ContaminationDiagnostics`):
     `CenterX,CenterY,Hfr,Background,NoiseSigma,Sensitivity,MinSectorPixels,m0..m7,c0..c7,
     diff0..diff3,thr0..thr3,ratio0..ratio3,skip0..skip3,TrippingPairIndex,ContaminationSuspected,
     MaxRatio`. (`CenterX/Y` let us reload raw pixels around any star from the original image.)
   - **`contamination_annotated.png`** — reuse `HocusFocusStarAnnotator.GetAnnotatedImage(...)` with a
     `StaticStarAnnotatorOptions` that sets `ShowContaminated=true` (+ distinct `ContaminatedColor`) so
     flagged stars are visually obvious; save the result to PNG (headless `BitmapSource`→PNG via
     `PngBitmapEncoder`). Fallback: draw with OpenCV `Cv2.Circle` (green=accepted, red=flagged) and
     `Cv2.ImWrite`.
   - **`contamination_summary.txt`** — image path + dims, resolved settings (`p.ToString()`), total
     detected, `Metrics.ContaminationSuspected`, % flagged, and the distribution of tripping-pair
     `MaxRatio` (min/median/p90/max + buckets 1.0–1.25 / 1.25–1.5 / 1.5–2 / >2).
   - For `--sensitivity-sweep`: `contamination_stars_<v>.csv` per value + `sweep.csv`
     (`sensitivity,suspected,total,percent`). Mirror the summary via `Logger.Info`.
4. **Stubs:** minimal `IPluggableBehaviorSelector<>` impls (members verified against NINA source) only to
   satisfy `ImageDataFactory`'s ctor for XISF/FITS loading.

## Files to create / modify

- `Joko.NINA.Plugins.HocusFocus/Interfaces/IStarDetector.cs` — `ContaminationDiagnosticRecord`,
  `StarDetectorParams.CollectContaminationDiagnostics`, `HocusFocusStarDetectorResult.ContaminationDiagnostics`.
- `Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetector.cs` — capture in `ComputeStarParameters`
  (opt-in), `StarCandidate` carry fields, emit in `EvaluateStarCandidate`, concurrent bag + populate result
  in `DetectImpl`. `IsContaminatedBySectors` stays pure.
- `Joko.NINA.Plugins.HocusFocus/StarDetection/HocusFocusStarDetection.cs` — extract
  `BuildStarDetectorParams(IStarDetectionOptions)`.
- `Joko.NINA.Plugins.HocusFocus/Properties/AssemblyInfo.cs` — `[assembly: InternalsVisibleTo("TestApp")]`.
- `TestApp/TestApp.csproj` — `<StartupObject>`, demote `App.xaml` to `Page`.
- `TestApp/Program.cs` — `[STAThread] Main` dispatch (headless vs existing GUI path).
- `TestApp/ContaminationDiagnosticRunner.cs` (new) + optional `TestApp/StubBehaviorSelectors.cs` (new).

## Verification

1. **Build (Windows toolchain required for the `-windows` TFM):**
   `cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"` → 0 errors.
2. **Regression:** `cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo"`
   → all pass (the 6 `IsContaminatedBySectors_*` tests unchanged + the rest).
3. **Run on the sample (headless, real settings):**
   `cmd.exe /c "Joko.NINA.Plugins\TestApp\bin\x64\Debug\TestApp.exe contamination --image ""C:\Workshop Data\Data\autofocus\uneven\final\11_Frame00_BitDepth16_Bayered0_Focuser56457.xisf"" --out ""C:\temp\hf-diag"""`
   (exe path: `bin\x64\Debug\` for Debug|x64, else `bin\Debug\`). Expect console to report the loaded
   profile name, the resolved `ContaminationSensitivity` (the user's value, not 4.0), image dims, and
   `Detected N, M contamination-suspected (M/N%)`; plus the three artifacts + a NINA log.
4. **Analyze:** read `contamination_stars.csv` / `contamination_annotated.png`. Root-cause levers:
   `MaxRatio` clustered just above 1.0 ⇒ threshold marginally too tight (raise default
   `ContaminationSensitivity` and/or revisit the `1.2533` SE model / `MinSectorPixels`); large ratios ⇒
   biased sector medians (annulus pulling in star flux, octant binning, or underestimated `noiseSigma`) ⇒
   drill into `ComputeStarParameters`. Use `--sensitivity-sweep` to pick a defensible default. This feeds
   the real follow-up fix to the contamination test.

## Risks & fallbacks

- **`ProfileService.ActiveProfile` NRE without `Application.Current`** → create a non-running WPF
  `Application` on the STA thread (primary); Fallback A `Profile.Load` + hand-built accessor; Fallback B
  `InMemoryPluginOptionsAccessor` from the `.profile`.
- **`ImageDataFactory` needs MEF selectors** → stub selectors, or construct `BaseImageData` directly from
  the XISF/FITS-decoded `ushort[]`. TIFF uses OpenCV directly.
- **WPF auto-`Main` hijack** → `<StartupObject>` + demote `App.xaml` to `Page` + explicit `[STAThread] Main`.
- **`GetStarDetectorParams` not reusable headless** → factored static `BuildStarDetectorParams` + `InternalsVisibleTo`
  (fallback: replicate the mapping in the harness).
- **Parallel scanning** → `ConcurrentBag` for diagnostics; materialize/order after detection.
- **noiseSigma fidelity** → mono sample uses `Detect(Mat,...)`; for bayered inputs prefer
  `Detect(IRenderedImage,...)` (debayer/hotpixel) and note which path produced the numbers.
