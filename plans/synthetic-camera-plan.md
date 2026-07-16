# Synthetic Star-Field Camera — Implementation Plan

Executes `docs/synthetic-camera-design.md`. Read that first for the math, constants, sensor tables, and ASTAP format.
All new code under `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/`; interfaces/enums under
`Interfaces/`; tests under `Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/`. No new NuGet references
(OpenCvSharp4 `Cv2.Dft`, MathNet.Numerics distributions/erf, alglib already present).

## Reference files to model on

- Device shape: NINA `SimulatorCamera` (`BaseINPC, ICamera, ITelescopeConsumer`; DI `IProfileService,
  ITelescopeMediator, IExposureDataFactory, IImageDataFactory`; `DownloadExposure` → `ushort[w*h]` →
  `exposureDataFactory.CreateImageArrayExposureData(input, w, h, BitDepth, isBayered:false, metaData)` after
  `metaData.FromCamera(this)`; registers as `ITelescopeConsumer`).
- Options: `AutoFocus/InspectorOptions.cs` (`BaseINPC` + `IPluginOptionsAccessor`, dual ctor, `InitializeOptions`,
  `ResetDefaults`, `ProfileChanged`); enums with `[Description]` + `[TypeConverter(EnumStaticDescriptionConverter)]`
  in `Interfaces/`.
- Bootstrap: `HocusFocusPlugin.cs` (static option singletons ~L97-129 & L244-265; `RelayCommand` reset + folder
  picker `ChooseSavePathDiag` ~L217-225).
- Options UI: `Resources/OptionsDataTemplates.xaml` (ComboBox + `EnumBindingSource` + `HF_EnumStaticDescriptionValueConverter`;
  `ninactrl:UnitTextBox` + `FloatRangeRule`; `_Tooltip` string resources) hosted from `Options.xaml`.
- Aberration inverse target: `Inspection/SensorParaboloidModel.cs` (`z = Gx·(x−X0)+Gy·(y−Y0)+Kx·(x−X0)²+Ky·(y−Y0)²+Z0`,
  units µm; `Theta`, `TiltEffectMicrons`, `CurvatureEffectMicrons`, `R_mm=1/(2000|K|)`).
- Detector for the capstone test: `new StarDetector(new AlglibAPI()).Detect(Mat, new StarDetectorParams(), null, ct)`
  → `DetectedStars` (`Star.Center` `Point2d`, `Star.HFR` double). Example: `Tests/StarDetection/MeasureStarBiasTests.cs`.
- Render primitives to relocate: `Tests/Synthetic/SyntheticStarField.cs` (`AddStar`),
  `Tests/Synthetic/SyntheticDefocusedStarImage.cs` (annulus + `AddGaussianNoise` Box-Muller).

---

## Phase 0 — Skeleton & registration (get the camera to appear + fail cleanly)

1. `Interfaces/ICameraSimulatorOptions.cs` — interface + enums `SonySensorModel { IMX455, IMX571, IMX533, IMX294 }`
   and `SimulatorFilter { L, R, G, B, Ha5, Ha3, OIII5, OIII3, SII5, SII3 }`, each `[Description]` +
   `[TypeConverter(typeof(EnumStaticDescriptionConverter))]`.
2. `CameraSimulator/CameraSimulatorOptions.cs` — implement per `InspectorOptions.cs` with all options from the design's
   config table (Focus, Optics, Sensor, Filter, Sky, Catalog, Frame, Aberrations). ASTAP default
   `Path.Combine(Environment.GetFolderPath(SpecialFolder.ProgramFiles), "astap")`.
3. `CameraSimulator/HocusFocusSimulatorCamera.cs` — copy the `SimulatorCamera` member surface; add `IFocuserMediator`;
   make sensor-derived props (`CameraXSize/YSize`, `PixelSizeX/Y`, `BitDepth`, `SensorType`, `ElectronsPerADU`,
   `Gain/Offset`, `Temperature`, `SensorName`) read from the resolved `SensorDefinition` + options. Stub
   `DownloadExposure` to throw "not yet implemented" (fills in Phase 4). Dual constructor (public + `internal` with
   injected `StarFieldCompositor`/`IAstapCatalogReader`).
4. `CameraSimulator/HocusFocusSimulatorCameraProvider.cs` — `[Export(typeof(IEquipmentProvider))] IEquipmentProvider<ICamera>`,
   `[ImportingConstructor]` gets `IProfileService, IExposureDataFactory, IImageDataFactory, ITelescopeMediator,
   IFocuserMediator`; `GetEquipment()` builds one camera reading `HocusFocusPlugin.CameraSimulatorOptions ?? new(...)`.
5. `HocusFocusPlugin.cs` — add static `CameraSimulatorOptions` singleton (lazy-init in ctor), `ResetCameraSimulatorDefaultsCommand`,
   `ChooseAstapPathDiagCommand` (clone folder picker).
6. Options UI — `HocusFocus_CameraSimulator_Options` `DataTemplate` in `OptionsDataTemplates.xaml` (Focus/Optics,
   Sensor/Filter, Sky/Catalog, Aberrations groups) + `_Tooltip` per option; new `TabItem` in `Options.xaml`;
   aberration group `IsEnabled="{Binding EnableAberrations}"`. **Invariant: every persisted option needs a control here.**
7. `StartExposure`/`WaitUntilExposureIsReady`/`Connect`/`Disconnect`/`CameraState` transitions per the template.
   `StartExposure` builds the immutable `RenderRequest` snapshot (focuser+telescope Connected/Position/Coordinates
   J2000, options, filter, optics, focal length from `profile.TelescopeSettings.FocalLength` or override, exposure).
   `DownloadExposure` throws the descriptive focuser/telescope-not-connected errors here.

**Milestone:** builds; camera appears in NINA's camera dropdown; exposing with a disconnected focuser/mount fails with
a clear message. `dotnet build` clean.

## Phase 1 — Sensors, filters, projection (pure, testable)

8. `Sensors/SensorDefinition.cs`, `Sensors/QeCurve.cs`, `Sensors/SensorRegistry.cs` — the 4 sensors with the design's
   datasheet tables (resolution, pixel, bits, full well, QE anchors, RN(gain)+HCG step, dark@ref + 6.5 °C doubling,
   gain law `g_e = FullWell/2^bits · 10^(−g/200)`).
9. `Sensors/FilterDefinition.cs`, `Sensors/FilterRegistry.cs` — λc/Δλ/throughput for all 10 filters.
10. `Rendering/TanProjection.cs` — gnomonic TAN: `(RA/Dec J2000, focalLenMm, pixelµm, rotationDeg, W, H) → (x,y)`,
    pixel scale `206.265·pixel/focal`; reject off-frame (+ PSF margin). Own it (don't bind NINA internals).

**Tests:** projection round-trip < 0.01 px (incl. edges/rotation, off-frame rejection); sensor/filter registry sanity.

## Phase 2 — ASTAP catalog reader

11. `Catalog/CatalogStar.cs` (`Coordinates` J2000, `Magnitude`, optional color).
12. `Catalog/AstapCellGeometry.cs` — pure `find_areas(ra,dec,fov)` → cell ids/filenames; `.1476`/`.290` detection.
13. `Catalog/AstapCatalogReader.cs` (`IAstapCatalogReader`) — open cell files, parse 110-byte header + 5/6-byte records,
    decode RA/Dec/mag per the design, early-out at limiting magnitude; descriptive errors for missing/corrupt files.

**Tests:** decode synthetic byte arrays (known RA/Dec/mag incl. `FF FF FF` mag headers, bright→faint early-out);
`find_areas` returns expected 1–4 cells for known (ra,dec,fov). (Reader I/O tested via a temp fixture file.)

## Phase 3 — Physics: radiometry, PSF, aberration surface, noise

14. `Rendering/RadiometryCalculator.cs` — `StarElectrons`, `SkyElectronsPerPixel`, `DarkElectronsPerPixel` (design §Radiometry/Sky/Noise).
15. `Rendering/DefocusModel.cs` — focuser state + optics → defocus parameter; exposes HFR(x) and the derived
    `range→4×HFR` readout. Single source of truth for the HFR calibration.
16. `Rendering/AberrationSurface.cs` — `zBestFocus(x,y)` paraboloid; maps `TiltAngle/TiltAmount/BackfocusError` →
    `Gx,Gy,K`; `localDefocus(px,py, currentFocuser)`. Disabled ⇒ flat surface (uniform defocus).
17. `Rendering/PsfKernelGenerator.cs` — analytic annulus⊛Gaussian radial LUT → 4×-oversampled sub-pixel-phase kernels,
    normalized Σ=1, returns kernel + measured HFR. Build one per quantized defocus level. Optional `Cv2.Dft` FFT path
    behind a flag.
18. `Rendering/NoiseGenerator.cs` — in-place Poisson (Knuth < 1000, else Gaussian approx) + read noise + e⁻→ADU +
    pedestal + clip; seedable RNG (reuse Box-Muller).
19. `Rendering/StarStamper.cs` — relocate `AddStar`/annulus primitives here as `public`; sub-pixel additive stamp with
    a shared kernel. Update `Tests/Synthetic/*` to reference these.

**Tests:** radiometry monotonicity (↑ exposure/bandwidth/area, ↓ mag; L/Hα-5 ≈93×); PSF HFR calibration
(HFR_min at optimal, √(HFR_min²+(κΔ)²) growth, donut hole ratio→ε, vs Rice closed form <1%); noise stats
(mean & variance match model; photon-transfer slope 1/g_e).

## Phase 4 — Compositor & wire-up

20. `Rendering/StarFieldCompositor.cs` — `RenderRequest → ushort[]`: ASTAP query (FOV from sensor+focal) → project →
    per-star local defocus → quantize → build kernels → tile-bucket stars → `Parallel.ForEach` stamp `kernel·flux`
    into `float[]` accumulator → add sky+dark → `NoiseGenerator` → `ushort[]`. Cancellation-aware; pool buffers.
21. Wire `HocusFocusSimulatorCamera.DownloadExposure` → `Task.Run(() => compositor.Render(snapshot), token)` →
    `CreateImageArrayExposureData`. Out-of-catalog pointing ⇒ starless frame + warning.

**Tests (capstone):** (a) render frame with a fake fixed catalog → `StarDetector.Detect` recovers stars at projected
(x,y) and defocus-model HFR; (b) render a stepped-focuser run with injected tilt/backfocus → sensor/aberration model
recovers `Theta`, `TiltEffectMicrons`, `CurvatureEffectMicrons` within tolerance.

## Phase 5 — Verify

22. `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` — full suite green (project invariant;
    fix causes, never skip).
23. Manual smoke test in NINA (Windows): select the **Hocus Focus** synthetic camera; connect simulator focuser + mount;
    verify (a) HFR V-curve across focuser positions, (b) donuts with correct hole far from focus, (c) narrowband needs
    longer exposures, (d) aberration inspector recovers injected tilt/backfocus, (e) descriptive failure when
    focuser/mount disconnected, (f) resolution/pixel/bit-depth track the selected sensor.

---

## Notes / decisions carried from design review

- Monochrome only; `SensorDefinition.SensorType` + `isBayered` flag is the Bayer seam (add `ColorFilterArray` +
  terminal `ApplyCfa()` later; no rework to PSF/projection/noise).
- Plan **reads** focal length from the profile and does **not** mutate the NINA profile.
- Aberration units: tilt angle = azimuth deg, tilt amount = center→corner focus swing µm, backfocus = corner-vs-center
  curvature offset µm (match inspector reporting). Adjustable if the user prefers degrees / curvature radius mm.
- Transient memory ~370 MB at 61 MP (IMX455) is acceptable; revisit tiled-streaming only if needed.
- Per-star elongation (coma/astigmatism) out of scope — the inspector fits only a best-focus surface, so local-defocus
  is its exact inverse. Corner stars are round donuts of varying size.
- Keep the held-out golden/AF bank honest — the simulator is a separate synthetic source, not part of any optimizer.
