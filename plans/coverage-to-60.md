# HocusFocus Plugin — Coverage Push to 60%

## Context

After PR #33 the plugin has coverage tooling (coverlet.collector + Cobertura summary in CI) and a baseline of **26.07% line coverage** (2,693 / 10,328 instrumented lines). The Tier 1–3 unit tests built in `plans/comprehensive-unit-tests.md` give us strong coverage of pure-logic utilities, math models, value converters, validation rules, and basic VM construction; but the bulk of the AutoFocus, Inspection, and StarDetection code is at 15–25%.

This plan brings coverage to **~60%** by combining a small number of structural seams with a larger volume of test-only additions. It reuses the test infrastructure that already exists (NSubstitute, synthetic Mat/PSF fixtures in `Tests/Synthetic/`, the dual-constructor MEF pattern, the existing per-VM fixture style).

The missing ~3,500 covered lines come from five workstreams executed in dependency order: shared test infrastructure first, then the highest-ROI options refactor, then the cheap data/algorithm tests, then the expensive VM behavioral tests, then the SensorModel split.

---

## Workstream 1 — `IPluginOptionsAccessor` seam + 5 options test fixtures

**Why:** The five option classes (AutoFocusOptions, InspectorOptions, StarDetectionOptions, StarAnnotatorOptions, TiltAdapterOptions) total **~1,370 missed lines at 0–5% coverage**. Each is a thin wrapper over a concrete `PluginOptionsAccessor`, but `new PluginOptionsAccessor(profileService, guid)` in the constructor blocks unit testing without a real NINA profile. Introduce an interface seam so a fake accessor can be injected.

### Changes

**Production code** (mechanical refactor, ~5 files):
1. New `Interfaces/IPluginOptionsAccessor.cs` exposing the typed get/set surface used today (`GetValueInt32/Double/Boolean/String`, `GetValueEnum<T>`, and their `Set` counterparts).
2. New `Utility/PluginOptionsAccessorAdapter.cs` — wraps a concrete `PluginOptionsAccessor` and implements `IPluginOptionsAccessor`. One line per method.
3. For each of the five options classes:
   - Add a second constructor `internal MyOptions(IProfileService profileService, IPluginOptionsAccessor accessor)` that bypasses the `PluginOptionsAccessor.GetAssemblyGuid` lookup.
   - Keep the existing `[ImportingConstructor]` constructor; have it call into the new ctor with `new PluginOptionsAccessorAdapter(new PluginOptionsAccessor(profileService, guid.Value))`.
   - Replace the `private readonly PluginOptionsAccessor optionsAccessor;` field with `private readonly IPluginOptionsAccessor optionsAccessor;`.
4. Add `[InternalsVisibleTo("Joko.NINA.Plugins.HocusFocus.Tests")]` if the new ctor needs to stay internal (already present per the prior plan's Tier 0).

**Test infrastructure**:
5. New `Tests/TestDoubles/InMemoryPluginOptionsAccessor.cs` — `IPluginOptionsAccessor` backed by a `Dictionary<string, object>`. ~40 lines. Reusable.

**Tests** (one fixture per options class):
6. `Tests/AutoFocus/AutoFocusOptionsTests.cs`
7. `Tests/AutoFocus/InspectorOptionsTests.cs`
8. `Tests/StarDetection/StarDetectionOptionsTests.cs`
9. `Tests/StarDetection/StarAnnotatorOptionsTests.cs`
10. `Tests/TiltAdapterWizard/TiltAdapterOptionsTests.cs`

Each fixture covers:
- Per-property round-trip (set value, read back, assert persisted to the in-memory store).
- `RaisePropertyChanged` fires with the correct property name on each setter.
- `ResetDefaults()` resets every property back to the documented default.
- `ProfileChanged` event re-runs `InitializeOptions` (mock `IProfileService`, raise the event, assert values reload).

**Expected coverage gain: ~1,200–1,300 lines.**

### Critical files

| Path | Change |
|---|---|
| `Joko.NINA.Plugins.HocusFocus/Interfaces/IPluginOptionsAccessor.cs` | New |
| `Joko.NINA.Plugins.HocusFocus/Utility/PluginOptionsAccessorAdapter.cs` | New |
| `Joko.NINA.Plugins.HocusFocus/AutoFocus/AutoFocusOptions.cs` | Add ctor, swap field type |
| `Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorOptions.cs` | Add ctor, swap field type |
| `Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionOptions.cs` | Add ctor, swap field type |
| `Joko.NINA.Plugins.HocusFocus/StarDetection/StarAnnotatorOptions.cs` | Add ctor, swap field type |
| `Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltAdapterOptions.cs` | Add ctor, swap field type |
| `Tests/TestDoubles/InMemoryPluginOptionsAccessor.cs` | New |
| `Tests/{AutoFocus,StarDetection,TiltAdapterWizard}/*OptionsTests.cs` | New (5 files) |

---

## Workstream 2 — Test-only data-class coverage

**Why:** Several plain data classes are at 0% but contain trivial setters/`ToString`/equality logic. Cheapest possible coverage.

### Targets

| File | Class(es) | Missed lines |
|---|---|---|
| `Interfaces/IStarDetector.cs` | `RatioRect` | 72 |
| `AutoFocus/AutoFocusEngine.cs` | nested `AutoFocusRegionState` | 70 |
| `AutoFocus/AutoFocusEngine.cs` | nested `CurveFittingResult` | 51 |
| `Controls/SurfacePlotModel.cs` | nested `SurfacePlotRenderer.Bounds` | 63 |
| `AutoFocus/HocusFocusReport.cs` | data-bag properties only (skip `GenerateReport` static) | 30–40 |
| `Inspection/SensorModelAberrationResult.cs` | data properties (~240 lines, but most is property boilerplate) | ~150 reachable |

### Tests

- `Tests/Interfaces/RatioRectTests.cs` — construction, equality if any, percentage→pixel scaling helpers.
- `Tests/AutoFocus/AutoFocusEngineDataClassTests.cs` — exercise nested types' constructors and property setters.
- `Tests/Controls/SurfacePlotBoundsTests.cs` — Bounds construction / contains-checks (assuming pure value type).
- `Tests/AutoFocus/HocusFocusReportTests.cs` — property INPC + serialization round-trip via Newtonsoft (the class is `[JsonObject]`).
- `Tests/Inspection/SensorModelAberrationResultTests.cs` — property INPC for each `RaisePropertyChanged` setter; `ToString` if present.

Pattern reused from the new `SensorTiltModelTests`/`SensorTiltHistoryModelTests` in PR #33 — straight property round-trips and `[TestCase]` over property names via reflection.

**Expected coverage gain: ~250–400 lines.**

---

## Workstream 3 — Synthetic-input tests for already-tested algorithms

**Why:** Several algorithm files have partial coverage but lots of unexercised branches. The synthetic test fixtures (`Tests/Synthetic/SyntheticGaussianStarImage.cs`, `SyntheticFocusCurveSamples.cs`) already exist — extend them and feed more inputs through.

### Targets

| File | Current | Missed | Strategy |
|---|---|---|---|
| `Utility/CvImageUtility.cs` | 27% | 361 | Already has `CvImageUtilityTests`. Add cases for: kappa-sigma noise on synthetic noisy Mats, atrous wavelet decomposition (assert energy preservation), debayer paths with synthetic Bayer patterns, downsample edge cases |
| `Utility/RANSACRegistration.cs` | 32% | 234 + 95 (StarTriangle) | Already has `RANSACRegistrationTests`. Add cases for: `StarTriangle` similarity comparison, NN matching with rotation/scale, fit failure paths (degenerate point sets) |
| `StarDetection/PSFModeler.cs` | 0% | 79 | Synthetic Gaussian peaks of varying SNR; `MoffatPSFAlglibType` and `GaussianPSFAlglibType` recovery tests beyond the existing single round-trip |
| `StarDetection/PSFModelTypeAlglibBase.cs` | 47% | 96 | Drive both PSF subtypes through their full Fit/Eval lifecycle |
| `StarDetection/HyperbolicFittingAlglib.cs` | 62% | 62 | Add boundary cases (insufficient points, near-flat curves, extreme asymmetry) |
| `StarDetection/MoffatPSFAlglibType.cs` | 0% | 77 | New file — synthetic Moffat-shaped peaks with known parameters |

### Tests

Extend existing fixtures in place rather than creating new files where possible:
- Augment `Tests/Utility/CvImageUtilityTests.cs`
- Augment `Tests/Utility/RANSACRegistrationTests.cs`
- Augment `Tests/StarDetection/PSFModelerTests.cs` and add `Tests/StarDetection/MoffatPSFAlglibTypeTests.cs`
- Augment `Tests/StarDetection/HyperbolicFittingAlglibTests.cs` with boundary cases

**Expected coverage gain: ~500–700 lines.**

---

## Workstream 4 — Behavioral tests for partially-covered VMs

**Why:** `InspectorVM` (836 missed at 10%), `HocusFocusVM` (206 missed at 46%), `TiltAdapterWizardVM` (144 missed at 50%), `StarDetectionResultsVM` (72 missed at 0%), and `HocusFocusStarAnnotator` (231 missed at 0%) all have construction-only tests today. The mock setup is the bottleneck: each VM needs ~10 mediators stubbed. Build a shared fixture, then write deeper tests.

### Foundation: shared fixture

11. New `Tests/TestDoubles/MediatorBundle.cs` (or `VmFixtureBase.cs`) — exposes preconfigured `Substitute.For<…>` instances for every mediator (`ICameraMediator`, `IFocuserMediator`, `IFilterWheelMediator`, `IGuiderMediator`, `IImagingMediator`, `ITelescopeMediator`, `IApplicationStatusMediator`) plus `IProfileService`, `IImageDataFactory`, `IPluggableBehaviorSelector<…>`. Helpers like `WithCameraConnected()`, `WithFocuserPosition(int)`, `WithFilterWheel(string)`. ~80 lines, replaces ~30 lines of setup per existing VM test.

Refactor `HocusFocusVMTests`, `InspectorVMTests`, `TiltAdapterWizardVMTests` to use the bundle (no behavioral changes — a mechanical sweep before adding new tests).

### New tests per VM

12. `Tests/AutoFocus/InspectorVMBehavioralTests.cs`:
    - `RunFullInspection` happy path: mediators report connected, an `AutoFocusResult` is produced, tilt history is populated.
    - Slew/return commands: `ITelescopeMediator.SlewToCoordinatesAsync` is invoked with correct args.
    - Update-device-info handlers: `UpdateDeviceInfo` flips observable properties (`IsFocuserConnected`, etc.).
    - `ClearAnalyses` resets observable collections; `CanExecute` reflects "analysis running" state.
    - Cancellation: an in-flight inspection respects `CancellationToken`.

13. `Tests/AutoFocus/HocusFocusVMBehavioralTests.cs`:
    - `LoadSavedAutoFocusRunCommand` parses a synthetic report folder and populates the VM (extends existing `LoadData` helper test).
    - `StartAutoFocusCommand` validates prerequisites and invokes the engine factory; failure surfaces in `Issues`.

14. `Tests/TiltAdapterWizard/TiltAdapterWizardBehavioralTests.cs`:
    - Step transitions: `Start` → calibration → `Restart`.
    - Validation: `IsCalibrationValid` matches the documented rules per step.
    - Screw correction math: feed known tilt input, assert recommended adjustments.

15. `Tests/StarDetection/StarDetectionResultsVMTests.cs`:
    - `INPC` for each property; result-list reset; selected-result behavior.

16. `Tests/StarDetection/HocusFocusStarAnnotatorTests.cs`:
    - `Annotate` with a synthetic star list produces an annotated `BitmapSource` (assert dimensions / non-empty pixels — the actual drawing logic is hard to assert, but smoke-testing the orchestration covers the bulk of the lines).
    - Skip the actual GDI rendering path if it requires a desktop session — gate with `[Platform(Include="Win")]` if needed.

**Expected coverage gain: ~700–1,000 lines** (highly variable by how deep we drive each VM).

---

## Workstream 5 — Split `Inspection/SensorModel.cs`

**Why:** This single file is 701 lines / 691 missed (1.4%). It mixes pure aberration math, async orchestration over mediators, and observable-collection mutations. Extracting the math into a pure calculator unlocks the bulk of the lines for cheap unit tests.

### Refactor

17. New `Inspection/SensorAberrationCalculator.cs` (or `SensorAberrationAnalyzer.cs`) — pure class with no NINA dependencies:
    - Inputs: collection of `SensorParaboloidDataPoint` (or equivalent), focuser positions, image size, configuration.
    - Outputs: `SensorModelAnalysisResult` (already exists) or a new richer result struct.
    - Methods: `Analyze(...)`, `EstimateBackfocusDelta(...)`, `EstimateTilt(...)` — whatever the math sections of `SensorModel` already do, lifted verbatim.

18. `Inspection/SensorModel.cs` becomes a thin coordinator:
    - Holds the `IInspectorOptions`, mediators, observable collections.
    - Delegates all math to the calculator.
    - Async orchestration (run inspection → call calculator → push to observable collections).

### Tests

19. `Tests/Inspection/SensorAberrationCalculatorTests.cs`:
    - Synthetic data points where the answer is known (perfect paraboloid → zero tilt; pure tilt → known A/B; mixed → both).
    - Edge cases: too few points, NaN inputs, degenerate geometry.
    - Reuse `SyntheticFocusCurveSamples` and the existing `SensorParaboloidModelTests` patterns.

20. (Optional) `Tests/Inspection/SensorModelTests.cs` — light coordinator tests with mocked calculator, asserting orchestration semantics (calls calculator, pushes results to observable collections, raises INPC).

**Expected coverage gain: ~400–500 lines** (most of the calculator + INPC path of the coordinator).

---

## Execution order and ROI

| Order | Workstream | Effort | Lines |
|---|---|---|---|
| 1 | WS-1 Options seam + 5 fixtures | medium refactor + ~1 day tests | ~1,250 |
| 2 | WS-2 Data-class tests | low | ~300 |
| 3 | WS-3 Synthetic-input algorithm tests | low–medium | ~600 |
| 4 | WS-4 Shared mediator fixture + VM behavioral tests | high | ~800 |
| 5 | WS-5 SensorModel calculator extraction | medium refactor + tests | ~450 |
| | **Total** | | **~3,400 → ~58%** |

If we land ~3,400 covered lines we hit **~58%**. If WS-3 or WS-4 over-deliver (likely on WS-3 if synthetic fixtures generalize well), we clear 60%. If we fall short, the cheapest gap-filler is more property-by-property tests in WS-2 and more synthetic boundary cases in WS-3.

Each workstream is independently shippable as its own PR. Order matters for landing risk:
- WS-1 first because it's the highest-ROI single change.
- WS-2 and WS-3 can land in parallel (no shared files).
- WS-4 last among the test-only workstreams because the shared fixture refactor touches existing fixtures and is best done after WS-1's test patterns settle.
- WS-5 can slot in any time after WS-1 — its risk is in the production refactor of `SensorModel.cs`.

---

## Critical files (consolidated)

**Production refactor (WS-1, WS-5):**
- `Joko.NINA.Plugins.HocusFocus/Interfaces/IPluginOptionsAccessor.cs` (new)
- `Joko.NINA.Plugins.HocusFocus/Utility/PluginOptionsAccessorAdapter.cs` (new)
- `Joko.NINA.Plugins.HocusFocus/AutoFocus/AutoFocusOptions.cs`
- `Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorOptions.cs`
- `Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionOptions.cs`
- `Joko.NINA.Plugins.HocusFocus/StarDetection/StarAnnotatorOptions.cs`
- `Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltAdapterOptions.cs`
- `Joko.NINA.Plugins.HocusFocus/Inspection/SensorModel.cs` (split)
- `Joko.NINA.Plugins.HocusFocus/Inspection/SensorAberrationCalculator.cs` (new)

**Test infrastructure:**
- `Tests/TestDoubles/InMemoryPluginOptionsAccessor.cs` (new)
- `Tests/TestDoubles/MediatorBundle.cs` (new)

**New test fixtures (WS-1 through WS-5):**
See per-workstream sections above (~14 new test files, plus extensions to ~6 existing fixtures).

**Reusable assets to lean on (already in place):**
- `Tests/Synthetic/SyntheticGaussianStarImage.cs` — synthetic Mat fixtures
- `Tests/Synthetic/SyntheticFocusCurveSamples.cs` — synthetic focus-curve data
- `Tests/TestDoubles/SynchronousApplicationDispatcher.cs` — inline dispatcher for VM tests
- The dual-constructor MEF pattern in every options/VM class — extend the testable ctor to take `IPluginOptionsAccessor`

---

## Verification

For each workstream PR:

1. `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests --collect "XPlat Code Coverage" --settings Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/coverlet.runsettings` passes locally.
2. PR description quotes the before/after coverage % for the touched files (read directly from the Cobertura XML or the run-summary table).
3. The CI run summary (post-PR-#33) renders a coverage delta — confirm it ticked up by the expected amount.
4. After the final workstream lands, run `reportgenerator` locally on the merged-branch coverage XML and confirm overall line coverage ≥ 60%.

If overall coverage stalls below 60% after all five workstreams, follow-ups (in order):
- More property-level tests against the new options seam (each `*Options.cs` typically has 30+ properties; the WS-1 fixtures might only cover a subset by `[TestCaseSource]`).
- Deeper VM scenario tests (cancellation paths, error-flow `Issues` population).
- Re-evaluate whether to take Workstream 6 from the original analysis (denominator tightening) — explicitly excluding `HocusFocusPlugin.cs` and pure-render `Controls/` is defensible if 60% is still out of reach.
