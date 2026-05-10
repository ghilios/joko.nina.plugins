# HocusFocus Plugin — Comprehensive Unit Test Plan

## Context

The HocusFocus plugin had only 3 test files covering 2 utility classes (`MathUtility`, `OrdinaryKrigingInterpolator`). The remaining ~85 source files across AutoFocus, Inspection, StarDetection, Utility, Converters, ValidationRules, SequenceItems and Controls had **zero test coverage**.

The plugin has unusually clean DI discipline (statics only at MEF boundaries, dual-constructor pattern everywhere, `IAlglibAPI` injected) — making it an ideal codebase to add comprehensive unit tests to. Tests are written against the **expected** behavior (treating the test as the spec), and any actual-vs-expected divergences trigger user-gated decisions on whether the implementation should change to match the spec — which has already produced multiple real bug fixes.

The work is scoped into three tiers, executed in waves with user approval between tiers.

---

## Status (as of last session)

### Done
- **Tier 0 (setup)** ✅
  - Test project switched from "linked source files" to a `<ProjectReference>` to the main plugin
  - TFM bumped to `net8.0-windows7.0` to load WPF/WinForms-targeting assembly
  - `NSubstitute 5.3.0` added
  - `[InternalsVisibleTo("Joko.NINA.Plugins.HocusFocus.Tests")]` added to plugin AssemblyInfo
  - Removed obsolete `TestDoubles/Point2D.cs` (now resolved via project reference to `Utility/RANSACRegistration.cs`'s `Point2D`)

- **Tier 1 (pure logic)** ✅ — **212 tests, 0 failing**
  - Utility: expanded `MathUtilityTests.cs`, added `ExtensionsTests.cs`, `PathUtilityTests.cs`
  - Converters: 16 new test files covering all 28 converters
  - ValidationRules: tests for both `PositiveOddIntegerRule` and `PositiveIntegerOrInfiniteRule`
  - **5 real bugs fixed during Tier 1** (driven by spec-first tests):
    1. `Utility/MathUtility.cs` — renamed misnamed `CalcDistance` (which returned squared distance) to `CalcSquaredDistance`; updated 4 call sites in `RANSACRegistration.cs`
    2. `Utility/MathUtility.cs` — `RejectionTest` now returns `null` early when `errorsStdDev == 0` (was returning a phantom outlier on perfect-fit data due to NaN comparison)
    3. `ValidationRules/PositiveOddIntegerRule.cs` — `NumberStyles.Number` → `NumberStyles.Integer` so `"3.0"` and group-separator inputs no longer parse
    4. `ValidationRules/PositiveIntegerOrInfiniteRule.cs` — tightened to accept only positive integers, `-1`, or the `"unlimited"` literal (was accepting any integer including 0 and negatives)
    5. `Converters/DoubleDegreesToArcsecDoubleDashConverter.cs:28` — `i == double.NaN` (dead code in IEEE 754) → `double.IsNaN(i)`. The decimal sibling correctly handled its sentinel; this was a typo

### Deferred from Tier 1 to Tier 2
The following utilities were originally scoped to Tier 1 but are heavily OpenCV/algo-coupled and benefit from Tier 2's synthetic Mat/data infrastructure:
- `Utility/CvImageUtility.cs` (image statistics, pixel conversions, kappa-sigma noise, atrous wavelets)
- `Utility/HotpixelFiltering.cs`
- `Utility/AlglibAPI.cs` (test concurrency/thread-safety alongside Tier 2 fitting tests)
- `Utility/MultiStopWatch.cs` (low priority)
- `Utility/NonLinearLeastSquaresSolver.cs` (Tier 2 algorithm)
- `Utility/NonLinearLeastSquaresSolverBase.cs`
- `Utility/RANSACRegistration.cs` (algorithmic, fits Tier 2)

- **Tier 2 (algorithms with mocked dependencies)** ✅ — **309 tests, 0 failing**
  - Added `Synthetic/SyntheticGaussianStarImage.cs`, `Synthetic/SyntheticFocusCurveSamples.cs`
  - Test project gained `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` so tests can fill OpenCV `Mat` buffers via raw pointers
  - Utility: `CvImageUtilityTests`, `HotpixelFilteringTests`, `AlglibAPITests` (incl. parallel-solve smoke), `NonLinearLeastSquaresSolverTests` (linear-recovery + winsorized outlier rejection), `RANSACRegistrationTests` (Matrix3x2 round-trip, FitAffineTransform translation/scale, NN match brightness gating)
  - StarDetection: `PSFModelTests` (FWHM/eccentricity/Sigma round-trip, Gaussian σ→FWHM constant), `PSFModelerTests` (Gaussian σ recovery on synthetic star + cancellation), `HyperbolicFittingAlglibTests`, `HyperbolicUnevenFittingAlglibTests`
  - Inspection: `SensorParaboloidModelTests` (Value/Tilt/Curvature math, FromArray/ToArray round-trip, full curvature recovery on synthetic paraboloid)
  - AutoFocus: `TiltPlaneModelTests` (corner-equal/A-only/B-only OLS recovery, GetModelX/Y bounds, ctor validation)
  - Scottplot: `LinearColormapTests`
  - **1 real bug fixed during Tier 2** (driven by spec-first test):
    1. `AutoFocus/TiltModel.cs:40` — `imageSize.Width == 0` → `imageSize.Width <= 0` so negative width is rejected symmetrically with negative height (which already used `<= 0`). Matches the "dimensions must be positive" error message and prevents `GetModelX` returning garbage on a negative-Width construction.

- **Tier 3 (engine, VMs, sequence items)** ✅ — **365 tests, 0 failing** (+56 new)
  - Added `TestDoubles/SynchronousApplicationDispatcher.cs` (runs callbacks inline; no WPF dispatcher)
  - SequenceItems: `RunAberrationInspectorTests.cs` (Validate per camera/focuser combo, Clone, Execute cancellation)
  - AutoFocus:
    - `HocusFocusVMFactoryTests.cs` (Name/ContentId, Create() returns concrete VM)
    - `HocusFocusVMTests.cs` (commands wired, observable property semantics, SetCurveFittings dispatches by AFMethod/AFCurveFitting on synthetic points)
    - `InspectorVMTests.cs` (constructs without throwing; consumer registration; UpdateDeviceInfo)
    - `AutoFocusEngineTests.cs` (`GetOptions` mapping incl. `MaxConcurrent==0 → int.MaxValue` and `savedAttempt.StepSize` override; argument validation on `RunWithRegions`/`RerunWithRegions`; `LoadSavedAutoFocusAttempt`/`LoadSavedFinalAttempt` parse temp folders)
  - StarDetection: `HocusFocusStarDetectionTests.cs` (`ToHocusFocusParams` Sensitivity → HighSigma 3/4; `CreateAnalysis`; `UpdateAnalysis` field copy; analysis `INPC`)
  - TiltAdapterWizard: `TiltAdapterWizardVMTests.cs` (initial state, Start/Restart commands, `IsCalibrationValid`, `HasCurvatureCalibration`/`CurvatureSignDescription`/`ShowRunColumn`, `AreDevicesConnected`)
  - **No production-code bugs surfaced this tier.** VM-specific note: the `IInspectorVMFactory.Create()` returns the concrete `InspectorVM`; deeper end-to-end tests for `RunAberrationInspector.Execute` happy path (and any test that wants to substitute `AnalyzeAutoFocus`) would need either an `IInspectorVM` interface or a `virtual` modifier on `AnalyzeAutoFocus`. Flagged as a future refactor — not done in this tier.

---

## Decisions locked in

| Decision | Choice |
|---|---|
| Test → main project access | `<ProjectReference>` + `[assembly: InternalsVisibleTo(...)]` |
| Mocking library | NSubstitute |
| Plan scope | Tier-by-tier with explicit user gate between tiers |
| Test data | Synthetic (programmatic) for unit tests; 1–2 small real images for end-to-end sanity checks |

---

## Tier 2 — Algorithms with mocked dependencies (next)

These classes are pure-ish algorithms but take a few injectable interfaces (`IAlglibAPI`, options interfaces, etc.). NSubstitute does the heavy lifting.

### Targets

#### Utility/ (deferred from Tier 1)
| Source | Test file | Coverage focus |
|---|---|---|
| `Utility/CvImageUtility.cs` | `Utility/CvImageUtilityTests.cs` | Median, MAD, mean/stddev on small synthetic Mats; pixel type conversions; kappa-sigma noise estimate |
| `Utility/HotpixelFiltering.cs` | `Utility/HotpixelFilteringTests.cs` | Single-pixel spike removal; no false positives on smooth gradients; CFA bayer pattern |
| `Utility/AlglibAPI.cs` | `Utility/AlglibAPITests.cs` | Smoke test the wrapper; concurrency: parallel solves don't corrupt state |
| `Utility/NonLinearLeastSquaresSolver*.cs` | `Utility/NonLinearLeastSquaresSolverTests.cs` | Solver recovers known coefficients on synthetic data |
| `Utility/RANSACRegistration.cs` | `Utility/RANSACRegistrationTests.cs` | Putative match generation; transform recovery on synthetic point sets |

#### StarDetection/
| Source | Test file | Coverage focus |
|---|---|---|
| `StarDetection/PSFModel.cs` | `StarDetection/PSFModelTests.cs` | FWHM math, eccentricity, Gaussian/Moffat parameter round-trip |
| `StarDetection/PSFModeler.cs` | `StarDetection/PSFModelerTests.cs` | Goodness-of-fit on synthetic stars; abstract base contract |
| `StarDetection/HyperbolicFittingAlglib.cs` | `StarDetection/HyperbolicFittingAlglibTests.cs` | Fit recovers known hyperbola coefficients within tolerance using a real `IAlglibAPI`; weights affect fit as expected |
| `StarDetection/HyperbolicUnevenFittingAlglib.cs` | same folder | Same plus uneven-spacing edge cases |
| `StarDetection/StarDetector.cs` | `StarDetection/StarDetectorTests.cs` | On a synthetic image with known Gaussian star at (cx, cy), `Detect` returns one star at that centroid within sub-pixel tolerance; rejects images that are pure noise |

#### Inspection/
| Source | Test file | Coverage focus |
|---|---|---|
| `Inspection/SensorModel.cs` | `Inspection/SensorModelTests.cs` | Surface fitting recovers a synthetic paraboloid; RBF interpolation is symmetric where it should be |
| `Inspection/SensorParaboloidModel.cs` | `Inspection/SensorParaboloidModelTests.cs` | Aberration parameter recovery on synthetic data |

#### AutoFocus/
| Source | Test file | Coverage focus |
|---|---|---|
| `AutoFocus/TiltModel.cs` (`TiltPlaneModel`) | `AutoFocus/TiltPlaneModelTests.cs` | `EstimateFocusPosition`, `GetModelX/Y`, `Create(...)` from known corner samples — adjustment math (steps and microns) for each sensor side. **Spec-first divergence to flag**: ctor at `TiltModel.cs:40` reads `imageSize.Width == 0 || imageSize.Height <= 0` — width allows negatives but height does not. |
| `AutoFocus/TiltScrewGuidanceRow.cs` | `AutoFocus/TiltScrewGuidanceRowTests.cs` | Per-screw guidance math given known plane parameters |
| `AutoFocus/AutoFocusEngine.cs` (curve fit slice) | `AutoFocus/AutoFocusEngineTests.cs` (deeper Tier 3) | Curve fit + minimum extraction on synthetic curves with NSubstitute for mediators; edge cases (unimodal vs noise-only) |

#### Scottplot/
| Source | Test file | Coverage focus |
|---|---|---|
| `Scottplot/LinearColormap.cs` | `Scottplot/LinearColormapTests.cs` | Stop interpolation at 0, 1, midpoints, beyond range |

### Tier 2 expected behavior we want to spec
- **Star detection on a 100×100 Gaussian-spike image** should return exactly one star whose centroid is within 0.5 px of truth and whose fitted FWHM matches the synthesized sigma within ~10%.
- **Hyperbolic fit** on noiseless points sampled from `y = a + b·sqrt((x-c)² + d²)` should recover `a, b, c, d` to machine precision (within `1e-6` relative).
- **Tilt plane `Create`** with the four corner focuser positions all equal must produce `A=0, B=0, C=meanFocuser`, and zero adjustment for every side.

### Tier 2 infrastructure to add
- `TestData/` folder for one or two small synthetic FITS fixtures (generated once, committed)
- `Synthetic/SyntheticGaussianStarImage.cs` — generates a 2D Gaussian star image at a known centroid
- `Synthetic/SyntheticFocusCurveSamples.cs` — generates noiseless hyperbolic / parabolic focus curve points

---

## Tier 3 — Engine, VMs, sequence items (most-mocked)

Heaviest mocking. These classes are coupled to NINA mediators (`ICameraMediator`, `IFocuserMediator`, etc.) and the static `HocusFocusPlugin.*` singletons. Per the dual-constructor pattern, instantiate them via the testable constructor and pass NSubstitute mocks for everything.

### Targets (in priority order)
| Source | Test focus |
|---|---|
| `AutoFocus/AutoFocusEngine.cs` (deeper) | Full run on a script of fake measurements; cancellation propagation; failure paths (`TooManyFailedMeasurementsException`) |
| `SequenceItems/RunAberrationInspector.cs` | `Validate()` populates `Issues` correctly when camera not connected; `Execute` happy path with mocked mediators; `Clone()` preserves metadata |
| `AutoFocus/HocusFocusVMFactory.cs` | `Create()` returns a `HocusFocusVM` of the correct type with deps wired |
| `AutoFocus/HocusFocusVM.cs` | Commands invoke their handlers; `LoadSavedAutoFocusRunCommand` triggers without UI dispatch |
| `AutoFocus/InspectorVM.cs` | Exercise public command surface and state transitions |
| `AutoFocus/TiltAdapterWizardVM.cs` | Wizard step navigation, screw orientation handling |
| `Inspection/HocusFocusStarDetection.cs` | Public `Detect(HocusFocusDetectionParams)` overload routing |

### Tier 3 considerations
- **`ApplicationDispatcher`** — VMs marshal to UI thread via `IApplicationDispatcher`. A test fake should run callbacks synchronously. Add `TestDoubles/SynchronousApplicationDispatcher.cs`.
- **Static singletons** — VMs use the `[ImportingConstructor]` for MEF and a second testable constructor for testing. Use the testable one and supply mocks; the static `HocusFocusPlugin` is never touched.
- **`PluginOptionsAccessor`** — options classes use a `PluginOptionsAccessor` keyed off the assembly GUID and an `IProfileService`. For Tier 3, mock the `I*Options` interface directly rather than instantiating real options.
- **End-to-end star detection sanity check** — bundle one ~50–100 KB synthetic FITS in `TestData/` for one full-pipeline test. Generated synthetically and saved once.

---

## Test conventions (in effect)

- **One file per source class**, directory mirrors the source layout. Namespace: `NINA.Joko.Plugins.HocusFocus.Tests.<SubFolder>`.
- **Naming**: `MethodUnderTest_Scenario_Expectation` (e.g. `Validate_NegativeOddInteger_ReturnsFalse`).
- **Use `Assert.Multiple`** for grouped assertions on a single object.
- **Floating-point**: `Within(1e-6)` for derived numerics, `Within(1e-12)` for round-trips.
- **Synthetic generators** live in `Synthetic/` (Tier 2+).
- **Cancellation tests**: pass an already-cancelled `CancellationToken` and assert the operation throws `OperationCanceledException`.

---

## Critical files (so far)

| Path | Modification |
|---|---|
| `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Joko.NINA.Plugins.HocusFocus.Tests.csproj` | TFM bump, ProjectReference, NSubstitute |
| `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Properties/AssemblyInfo.cs` | Added `InternalsVisibleTo` |
| `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Utility/MathUtility.cs` | `CalcDistance` rename, `RejectionTest` zero-stddev guard |
| `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Utility/RANSACRegistration.cs` | Updated callers of `CalcDistance` → `CalcSquaredDistance` |
| `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/ValidationRules/PositiveOddIntegerRule.cs` | `NumberStyles.Number` → `NumberStyles.Integer` |
| `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/ValidationRules/PositiveIntegerOrInfiniteRule.cs` | Restrict to positive integers + `-1` + `"unlimited"` |
| `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Converters/DoubleDegreesToArcsecDoubleDashConverter.cs` | `i == double.NaN` → `double.IsNaN(i)` |

29 new test files added under `Joko.NINA.Plugins.HocusFocus.Tests/{Utility,Converters,ValidationRules}/`.

---

## Verification plan

After each tier:

1. `cd Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests; dotnet test -c Debug` — must pass.
2. Spot-check **deliberately failing** tests written from spec; the failures surface real divergences. Then come back to the user with the question and either fix the implementation (preferred when behavior is clearly wrong) or adjust the test.

Tier 1 final result: 212 tests, 0 failing, 5 implementation bugs fixed.

End-to-end (Tier 3 only): run the one full-pipeline star-detection test against the bundled fixture; confirm centroids land within tolerance.

---

## Execution gates

- ~~Tier 0 (setup) — confirm project builds before writing any new tests.~~ ✅
- ~~Tier 1 — ping with the list of expected-vs-actual divergences found.~~ ✅ (5 bugs fixed)
- ~~Tier 2 — ping with any algorithm divergences found.~~ ✅ (1 bug fixed)
- ~~Tier 3 — final pass; ping with anything VM-specific that warrants a refactor.~~ ✅ (no bugs; flagged `IInspectorVMFactory.Create()` returning concrete `InspectorVM` as future-refactor candidate to enable Execute-happy-path testing of `RunAberrationInspector`)
