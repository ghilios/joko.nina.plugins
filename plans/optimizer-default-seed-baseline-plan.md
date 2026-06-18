# Optimizer Default Seed + Current-Settings Baseline — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Star Detection Optimization Wizard start optimization from a fully-default star-detection seed while measuring/displaying improvement against the user's current settings; never mutate live options, so rejecting leaves current settings untouched.

**Architecture:** Split the single `LoadedRun.Seed` (today both the optimizer start *and* the displayed baseline) into `Seed` (fully-default params → optimizer start) and `Baseline` (current settings → before-σ/J, "Current" curve, changed-params before-column). The wizard computes a `currentBaselineJ` from the `Baseline` and uses it for the displayed improvement; the optimizer still starts from `Seed`. No `StarDetectionOptions` writes occur until Accept (`ApplyOptimizedSettings`), so reject/cancel/close is automatically a no-op revert.

**Tech Stack:** C# / .NET 8 (WPF class library), NUnit 4 + NSubstitute, CommunityToolkit.Mvvm. Build: `dotnet`; tests via `dotnet test`.

**Reference spec:** `docs/optimizer-default-seed-baseline-design.md`

**Conventions:**
- Commit author/committer email MUST be `322725+ghilios@users.noreply.github.com` (see CLAUDE.md). Use:
  ```bash
  GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
    git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "<msg>"
  ```
- Full test command (run from solution root `/mnt/c/Users/ghili/src/nina.plugins`):
  ```bash
  dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo
  ```
- To run a single test class quickly, add `--filter "FullyQualifiedName~<ClassName>"`.
- Work on branch `ghilios/optimizer-default-seed-baseline` (already created).

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `Joko.NINA.Plugins.HocusFocus/StarDetection/HocusFocusStarDetection.cs` | Modify | Add `BuildDefaultStarDetectorParams()` + `GetDefaultStarDetectorParams(...)`; extract shared `ApplyDetectionImageContext(...)` |
| `Joko.NINA.Plugins.HocusFocus/Interfaces/IHocusFocusStarDetection.cs` | Modify | Declare `GetDefaultStarDetectorParams(...)` on the interface |
| `Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/RunEvaluationLoader.cs` | Modify | `LoadedRun.Baseline` (falls back to `Seed`); loader sets `Seed`=default, `Baseline`=current |
| `Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/StarDetectionOptimizerWizardVM.cs` | Modify | Decouple baseline from seed: `currentBaselineJ`, guard→Baseline, progress/summary/changed-params/currentResult/UseCurrentSettings/Apply |
| `Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectionOptionsTests.cs` | Modify | Drift-guard test for `BuildDefaultStarDetectorParams()` |
| `Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/Optimization/RunEvaluationLoaderTests.cs` | Modify | `LoadedRun.Baseline` fallback unit test |
| `Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/Optimization/StarDetectionOptimizerWizardVMTests.cs` | Modify | New decoupling test (optimizer uses Seed; display uses Baseline) |

---

## Task 1: Default star-detector params (`BuildDefaultStarDetectorParams`)

**Files:**
- Modify: `Joko.NINA.Plugins.HocusFocus/StarDetection/HocusFocusStarDetection.cs` (add method next to `BuildStarDetectorParams`, ~line 333)
- Test: `Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectionOptionsTests.cs` (add a test reusing the existing `Build()` helper)

- [ ] **Step 1: Write the failing drift-guard test**

Add this test to `StarDetectionOptionsTests.cs` (anywhere inside the `StarDetectionOptionsTests` class, e.g. after `DefocusGatesOff_BothDetectorFlagsOff` near line 370). It reuses the file's existing `Build()` helper and asserts the hand-written default params match a `ResetDefaults()`-ed options build field-for-field, so the two default sources can never drift.

```csharp
    [Test]
    public void BuildDefaultStarDetectorParams_MatchesResetDefaultsBuild() {
        // The optimizer's "fully-default" seed must equal what BuildStarDetectorParams produces from a freshly
        // reset options object — for EVERY option-derived field. If a default ever changes in only one place
        // (ResetDefaults or BuildDefaultStarDetectorParams), this test fails loudly.
        var (options, _, _) = Build();
        options.ResetDefaults();
        var fromOptions = HocusFocusStarDetection.BuildStarDetectorParams(options);
        var fromDefault = HocusFocusStarDetection.BuildDefaultStarDetectorParams();

        Assert.Multiple(() => {
            Assert.That(fromDefault.ModelPSF, Is.EqualTo(fromOptions.ModelPSF));
            Assert.That(fromDefault.StarMeasurementNoiseReductionEnabled, Is.EqualTo(fromOptions.StarMeasurementNoiseReductionEnabled));
            Assert.That(fromDefault.PSFFitType, Is.EqualTo(fromOptions.PSFFitType));
            Assert.That(fromDefault.HotpixelFiltering, Is.EqualTo(fromOptions.HotpixelFiltering));
            Assert.That(fromDefault.HotpixelThresholdingEnabled, Is.EqualTo(fromOptions.HotpixelThresholdingEnabled));
            Assert.That(fromDefault.NoiseReductionRadius, Is.EqualTo(fromOptions.NoiseReductionRadius));
            Assert.That(fromDefault.NoiseClippingMultiplier, Is.EqualTo(fromOptions.NoiseClippingMultiplier));
            Assert.That(fromDefault.StarClippingMultiplier, Is.EqualTo(fromOptions.StarClippingMultiplier));
            Assert.That(fromDefault.ContaminationSensitivity, Is.EqualTo(fromOptions.ContaminationSensitivity));
            Assert.That(fromDefault.RejectContaminatedStars, Is.EqualTo(fromOptions.RejectContaminatedStars));
            Assert.That(fromDefault.StructureLayers, Is.EqualTo(fromOptions.StructureLayers));
            Assert.That(fromDefault.DefocusAwareStructure, Is.EqualTo(fromOptions.DefocusAwareStructure));
            Assert.That(fromDefault.StructureLayerBoost, Is.EqualTo(fromOptions.StructureLayerBoost));
            Assert.That(fromDefault.Sensitivity, Is.EqualTo(fromOptions.Sensitivity));
            Assert.That(fromDefault.PeakResponse, Is.EqualTo(fromOptions.PeakResponse));
            Assert.That(fromDefault.MaxDistortion, Is.EqualTo(fromOptions.MaxDistortion));
            Assert.That(fromDefault.DefocusAwareDistortion, Is.EqualTo(fromOptions.DefocusAwareDistortion));
            Assert.That(fromDefault.DefocusAwareCentering, Is.EqualTo(fromOptions.DefocusAwareCentering));
            Assert.That(fromDefault.DefocusDistortionSizeReference, Is.EqualTo(fromOptions.DefocusDistortionSizeReference));
            Assert.That(fromDefault.DefocusDistortionMinFactor, Is.EqualTo(fromOptions.DefocusDistortionMinFactor));
            Assert.That(fromDefault.DefocusCenteringToleranceFactor, Is.EqualTo(fromOptions.DefocusCenteringToleranceFactor));
            Assert.That(fromDefault.StarCenterTolerance, Is.EqualTo(fromOptions.StarCenterTolerance));
            Assert.That(fromDefault.BackgroundBoxExpansion, Is.EqualTo(fromOptions.BackgroundBoxExpansion));
            Assert.That(fromDefault.MinimumStarBoundingBoxSize, Is.EqualTo(fromOptions.MinimumStarBoundingBoxSize));
            Assert.That(fromDefault.MinHFR, Is.EqualTo(fromOptions.MinHFR));
            Assert.That(fromDefault.StructureDilationSize, Is.EqualTo(fromOptions.StructureDilationSize));
            Assert.That(fromDefault.StructureDilationCount, Is.EqualTo(fromOptions.StructureDilationCount));
            Assert.That(fromDefault.AnalysisSamplingSize, Is.EqualTo(fromOptions.AnalysisSamplingSize));
            Assert.That(fromDefault.StoreStructureMap, Is.EqualTo(fromOptions.StoreStructureMap));
            Assert.That(fromDefault.SaveIntermediateFilesPath, Is.EqualTo(fromOptions.SaveIntermediateFilesPath));
            Assert.That(fromDefault.PSFParallelPartitionSize, Is.EqualTo(fromOptions.PSFParallelPartitionSize));
            Assert.That(fromDefault.PSFResolution, Is.EqualTo(fromOptions.PSFResolution));
            Assert.That(fromDefault.PSFGoodnessOfFitThreshold, Is.EqualTo(fromOptions.PSFGoodnessOfFitThreshold));
            Assert.That(fromDefault.UsePSFAbsoluteDeviation, Is.EqualTo(fromOptions.UsePSFAbsoluteDeviation));
            Assert.That(fromDefault.HotpixelThreshold, Is.EqualTo(fromOptions.HotpixelThreshold));
            Assert.That(fromDefault.SaturationThreshold, Is.EqualTo(fromOptions.SaturationThreshold));
            Assert.That(fromDefault.PSFPixelIntegration, Is.EqualTo(fromOptions.PSFPixelIntegration));
            Assert.That(fromDefault.MaxStarEvaluationParallelism, Is.EqualTo(fromOptions.MaxStarEvaluationParallelism));
        });
    }
```

- [ ] **Step 2: Run the test to verify it fails (does not compile yet)**

Run:
```bash
dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~StarDetectionOptionsTests.BuildDefaultStarDetectorParams_MatchesResetDefaultsBuild"
```
Expected: BUILD FAILS — `HocusFocusStarDetection` does not contain `BuildDefaultStarDetectorParams`.

- [ ] **Step 3: Implement `BuildDefaultStarDetectorParams()`**

In `HocusFocusStarDetection.cs`, immediately after the closing brace of `BuildStarDetectorParams` (currently line 333), add the method below. It mirrors `BuildStarDetectorParams` exactly, substituting each option read with the literal default from `StarDetectionOptions.ResetDefaults()`. (The Step-1 test guards these literals against `ResetDefaults`.)

```csharp
        /// <summary>
        /// The "fully-default" detector params — the analogue of <see cref="BuildStarDetectorParams"/> for an
        /// options object at <c>StarDetectionOptions.ResetDefaults()</c>. Every option-derived field is at its
        /// documented default. Used as the Optimization Wizard's seed so the search starts from a clean,
        /// reproducible point regardless of the user's current settings. Image-dependent fields (PixelScale,
        /// Region) and the auto-focus overrides are layered on by <see cref="GetDefaultStarDetectorParams"/>.
        /// The literals here are kept in lockstep with ResetDefaults by
        /// StarDetectionOptionsTests.BuildDefaultStarDetectorParams_MatchesResetDefaultsBuild.
        /// </summary>
        internal static StarDetectorParams BuildDefaultStarDetectorParams() {
            return new StarDetectorParams() {
                ModelPSF = true,
                StarMeasurementNoiseReductionEnabled = false,
                PSFFitType = StarDetectorPSFFitType.Moffat_40,
                HotpixelFiltering = true,
                HotpixelThresholdingEnabled = true,
                NoiseReductionRadius = 3,
                NoiseClippingMultiplier = 4.0,
                StarClippingMultiplier = 2.0,
                ContaminationSensitivity = 5.0,
                RejectContaminatedStars = true,
                StructureLayers = 4,
                DefocusAwareStructure = false,
                StructureLayerBoost = 0,
                Sensitivity = 2.0,
                PeakResponse = 0.75,
                MaxDistortion = 0.5,
                DefocusAwareDistortion = false,
                DefocusAwareCentering = false,
                DefocusDistortionSizeReference = 30.0,
                DefocusDistortionMinFactor = 0.25,
                DefocusCenteringToleranceFactor = 2.0,
                StarCenterTolerance = 0.3,
                BackgroundBoxExpansion = 3,
                MinimumStarBoundingBoxSize = 5,
                MinHFR = 1.2,
                StructureDilationSize = 3,
                StructureDilationCount = 0,
                AnalysisSamplingSize = 1.0f,
                StoreStructureMap = false,
                SaveIntermediateFilesPath = string.Empty,
                PSFParallelPartitionSize = 100,
                PSFResolution = 10,
                PSFGoodnessOfFitThreshold = 0.9,
                UsePSFAbsoluteDeviation = false,
                HotpixelThreshold = 0.001d,
                SaturationThreshold = 0.99d,
                PSFPixelIntegration = false,
                MaxStarEvaluationParallelism = 0
            };
        }
```

Note: `StarDetectorPSFFitType` is already in scope in this file (used by `BuildStarDetectorParams`). If the build reports it unresolved, confirm the existing `using` for the `StarDetector` enums is present (it is — `BuildStarDetectorParams` references `options.PSFFitType` of that type).

- [ ] **Step 4: Run the test to verify it passes**

Run:
```bash
dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~StarDetectionOptionsTests.BuildDefaultStarDetectorParams_MatchesResetDefaultsBuild"
```
Expected: PASS. If any field assertion fails, the literal in `BuildDefaultStarDetectorParams` disagrees with `ResetDefaults` — fix that single literal to match `ResetDefaults` and re-run.

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/HocusFocusStarDetection.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectionOptionsTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat: add BuildDefaultStarDetectorParams with ResetDefaults drift guard

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Default seed with image context (`GetDefaultStarDetectorParams`)

**Files:**
- Modify: `Joko.NINA.Plugins.HocusFocus/StarDetection/HocusFocusStarDetection.cs:335-365` (`GetStarDetectorParams` — extract shared helper, add default variant)
- Modify: `Joko.NINA.Plugins.HocusFocus/Interfaces/IHocusFocusStarDetection.cs:35` (declare the new method)

There is no clean unit-test seam for the image-context layering (it needs a real `IRenderedImage` + profile pixel scale). It is verified by: (a) the shared helper means the default variant uses the *same* image-context code as the production `GetStarDetectorParams` (already covered by existing detection tests), and (b) the full build + suite at the end. Tracked via Step 4 build check.

- [ ] **Step 1: Extract the shared image-context helper and refactor `GetStarDetectorParams`**

Replace the current `GetStarDetectorParams` (lines 335-365) with the refactor below. Behavior of `GetStarDetectorParams` is unchanged — the pixel-scale calc, Region, AF overrides, and the non-AF `SaveIntermediateImages = false` side effect are preserved exactly; only the pure image-context layering is extracted so the default variant can reuse it.

Old:
```csharp
        public StarDetectorParams GetStarDetectorParams(IRenderedImage image, StarDetectionRegion starDetectionRegion, bool isAutoFocus) {
            var binning = Math.Max(image.RawImageData.MetaData.Camera.BinX, 1);
            var pixelScale = MathUtility.ArcsecPerPixel(profileService.ActiveProfile.CameraSettings.PixelSize, profileService.ActiveProfile.TelescopeSettings.FocalLength) * binning;
            if (double.IsNaN(pixelScale)) {
                if (!pixelScaleWarningShown) {
                    pixelScaleWarningShown = true;
                    Notification.ShowWarning("Pixel Scale is NaN. Make sure pixel size and focal length are set in Options.");
                }
                Logger.Warning("Pixel Scale is NaN. Make sure pixel size and focal length are set in Options.");
            }

            var detectorParams = BuildStarDetectorParams(starDetectionOptions);
            detectorParams.PixelScale = pixelScale;
            detectorParams.Region = starDetectionRegion;

            // For AutoFocus, don't save intermediate data or model PSFs
            if (isAutoFocus) {
                detectorParams.SaveIntermediateFilesPath = string.Empty;
                detectorParams.ModelPSF = false;
                // Design decision (accuracy analysis F1): the TooFlat gate (StarDetector rejects candidates whose
                // median >= PeakResponse*peak) is intentionally left ACTIVE during AutoFocus. It can reject bright,
                // heavily-defocused flat-top/donut stars, but relaxing it here risks admitting flat noise blobs, and
                // PeakResponse is also reused in the sensitivity (NormalizedBrightness) calc so loosening it has side
                // effects. Revisit with a real defocus dataset (TestApp focus-sweep) if AF star counts drop at sweep
                // extremes.
            } else {
                // Only save intermediate images for 1 detection. Doing this again should require the user to pick it again
                starDetectionOptions.SaveIntermediateImages = false;
            }
            return detectorParams;
        }
```

New:
```csharp
        public StarDetectorParams GetStarDetectorParams(IRenderedImage image, StarDetectionRegion starDetectionRegion, bool isAutoFocus) {
            var detectorParams = BuildStarDetectorParams(starDetectionOptions);
            ApplyDetectionImageContext(detectorParams, image, starDetectionRegion, isAutoFocus);
            if (!isAutoFocus) {
                // Only save intermediate images for 1 detection. Doing this again should require the user to pick it again.
                starDetectionOptions.SaveIntermediateImages = false;
            }
            return detectorParams;
        }

        /// <summary>
        /// The Optimization Wizard's seed: the fully-default detector params (<see cref="BuildDefaultStarDetectorParams"/>)
        /// with the SAME image-dependent fields + auto-focus overrides as <see cref="GetStarDetectorParams"/> layered on
        /// (PixelScale, Region, ModelPSF=false, SaveIntermediateFilesPath=""). Read-only with respect to options — it
        /// never touches <c>starDetectionOptions</c>. <paramref name="isAutoFocus"/> is expected to be true for the
        /// wizard's replay path.
        /// </summary>
        public StarDetectorParams GetDefaultStarDetectorParams(IRenderedImage image, StarDetectionRegion starDetectionRegion, bool isAutoFocus) {
            var detectorParams = BuildDefaultStarDetectorParams();
            ApplyDetectionImageContext(detectorParams, image, starDetectionRegion, isAutoFocus);
            return detectorParams;
        }

        /// <summary>
        /// Layers the image-dependent fields (PixelScale from the profile × binning, Region) and the auto-focus
        /// overrides (ModelPSF=false, no intermediate-file save) onto an already-built params bundle. Pure with
        /// respect to options — shared by <see cref="GetStarDetectorParams"/> and
        /// <see cref="GetDefaultStarDetectorParams"/> so the two can never diverge in how they compute pixel scale or
        /// apply the AF overrides.
        /// </summary>
        private void ApplyDetectionImageContext(StarDetectorParams detectorParams, IRenderedImage image, StarDetectionRegion starDetectionRegion, bool isAutoFocus) {
            var binning = Math.Max(image.RawImageData.MetaData.Camera.BinX, 1);
            var pixelScale = MathUtility.ArcsecPerPixel(profileService.ActiveProfile.CameraSettings.PixelSize, profileService.ActiveProfile.TelescopeSettings.FocalLength) * binning;
            if (double.IsNaN(pixelScale)) {
                if (!pixelScaleWarningShown) {
                    pixelScaleWarningShown = true;
                    Notification.ShowWarning("Pixel Scale is NaN. Make sure pixel size and focal length are set in Options.");
                }
                Logger.Warning("Pixel Scale is NaN. Make sure pixel size and focal length are set in Options.");
            }

            detectorParams.PixelScale = pixelScale;
            detectorParams.Region = starDetectionRegion;

            // For AutoFocus, don't save intermediate data or model PSFs.
            if (isAutoFocus) {
                detectorParams.SaveIntermediateFilesPath = string.Empty;
                detectorParams.ModelPSF = false;
                // Design decision (accuracy analysis F1): the TooFlat gate (StarDetector rejects candidates whose
                // median >= PeakResponse*peak) is intentionally left ACTIVE during AutoFocus. It can reject bright,
                // heavily-defocused flat-top/donut stars, but relaxing it here risks admitting flat noise blobs, and
                // PeakResponse is also reused in the sensitivity (NormalizedBrightness) calc so loosening it has side
                // effects. Revisit with a real defocus dataset (TestApp focus-sweep) if AF star counts drop at sweep
                // extremes.
            }
        }
```

- [ ] **Step 2: Declare `GetDefaultStarDetectorParams` on the interface**

In `Joko.NINA.Plugins.HocusFocus/Interfaces/IHocusFocusStarDetection.cs`, add the new method declaration directly under the existing `GetStarDetectorParams` declaration (line 35):

Old:
```csharp
        StarDetectorParams GetStarDetectorParams(IRenderedImage image, StarDetectionRegion starDetectionRegion, bool isAutoFocus);
```

New:
```csharp
        StarDetectorParams GetStarDetectorParams(IRenderedImage image, StarDetectionRegion starDetectionRegion, bool isAutoFocus);

        /// <summary>The Optimization Wizard's seed: fully-default detector params with the same image-context +
        /// auto-focus overrides as <see cref="GetStarDetectorParams"/>. Read-only with respect to options.</summary>
        StarDetectorParams GetDefaultStarDetectorParams(IRenderedImage image, StarDetectionRegion starDetectionRegion, bool isAutoFocus);
```

- [ ] **Step 3: Build to verify it compiles**

Run:
```bash
dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Joko.NINA.Plugins.HocusFocus.csproj -c Debug --nologo
```
Expected: BUILD SUCCEEDED. (Any test double / fake of `IHocusFocusStarDetection` in the test project would now need the new member — none exists today; if the test build later flags one, implement it by delegating to `BuildDefaultStarDetectorParams` + the same image context.)

- [ ] **Step 4: Run the affected detection tests to confirm no regression**

Run:
```bash
dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~HocusFocusStarDetectionTests"
```
Expected: PASS (the `GetStarDetectorParams` refactor preserved behavior).

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/HocusFocusStarDetection.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/IHocusFocusStarDetection.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat: add GetDefaultStarDetectorParams + shared image-context helper

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: `LoadedRun.Baseline` + loader wiring

**Files:**
- Modify: `Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/RunEvaluationLoader.cs:34-38` (`LoadedRun`) and `:147-181` (`LoadSavedRunAsync` seed/return)
- Test: `Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/Optimization/RunEvaluationLoaderTests.cs` (add `LoadedRun.Baseline` fallback unit test)

- [ ] **Step 1: Write the failing `LoadedRun.Baseline` fallback test**

Add this test to `RunEvaluationLoaderTests.cs` (inside its test class). It pins the contract that `Baseline` falls back to `Seed` when not separately set, and is independent when set — the behavior the wizard and existing tests rely on.

```csharp
    [Test]
    public void LoadedRun_Baseline_FallsBackToSeed_WhenNotSet() {
        var seed = new StarDetectorParams { Sensitivity = 3.0 };
        var run = new LoadedRun { Seed = seed };
        Assert.That(run.Baseline, Is.SameAs(seed), "Baseline defaults to Seed when not provided");

        var baseline = new StarDetectorParams { Sensitivity = 9.0 };
        run.Baseline = baseline;
        Assert.Multiple(() => {
            Assert.That(run.Baseline, Is.SameAs(baseline), "an explicitly set Baseline is independent of Seed");
            Assert.That(run.Seed, Is.SameAs(seed), "setting Baseline does not change Seed");
        });
    }
```

If `RunEvaluationLoaderTests.cs` does not already `using NINA.Joko.Plugins.HocusFocus.Interfaces;` (for `StarDetectorParams`), add it.

- [ ] **Step 2: Run the test to verify it fails (does not compile)**

Run:
```bash
dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~RunEvaluationLoaderTests.LoadedRun_Baseline_FallsBackToSeed_WhenNotSet"
```
Expected: BUILD FAILS — `LoadedRun` has no `Baseline`.

- [ ] **Step 3: Add `Baseline` to `LoadedRun`**

In `RunEvaluationLoader.cs`, replace the `LoadedRun` class (lines 34-38):

Old:
```csharp
    public sealed class LoadedRun {
        public RunEvaluationData Data { get; set; }
        public StarDetectorParams Seed { get; set; }
        public AutoFocusEngineOptions AfOptions { get; set; }
    }
```

New:
```csharp
    public sealed class LoadedRun {
        public RunEvaluationData Data { get; set; }

        /// <summary>The params the optimizer starts its search from. In production this is the fully-DEFAULT
        /// detector params (see <see cref="IHocusFocusStarDetection.GetDefaultStarDetectorParams"/>), carrying
        /// PixelScale + Region + ModelPSF=false.</summary>
        public StarDetectorParams Seed { get; set; }

        private StarDetectorParams baseline;

        /// <summary>The user's CURRENT settings — the displayed "before" baseline (σ, cost J, the "Current" curve,
        /// and the changed-parameters before-column). Defaults to <see cref="Seed"/> when not separately provided,
        /// so a caller/test that sets only <see cref="Seed"/> keeps the legacy "the seed is the baseline" behavior.</summary>
        public StarDetectorParams Baseline {
            get => baseline ?? Seed;
            set => baseline = value;
        }

        public AutoFocusEngineOptions AfOptions { get; set; }
    }
```

- [ ] **Step 4: Wire the loader to set the default Seed + current-settings Baseline**

In `LoadSavedRunAsync`, the seed is built at line 149. Replace:

Old:
```csharp
            // Seed = AF-base detector params for the first image (PixelScale + Region + ModelPSF=false). This is
            // the bundle the optimizer starts from and tunes.
            var seed = detection.GetStarDetectorParams(firstImage, region, isAutoFocus: true);
```

New:
```csharp
            // Seed = fully-DEFAULT AF-base detector params for the first image (PixelScale + Region + ModelPSF=false):
            // the optimizer starts its search from a clean, reproducible default regardless of the user's current
            // settings. Baseline = the user's CURRENT settings, kept for the displayed "before" comparison (σ, cost J,
            // the "Current" curve). Both carry the same image context; neither writes to options.
            var seed = detection.GetDefaultStarDetectorParams(firstImage, region, isAutoFocus: true);
            var baseline = detection.GetStarDetectorParams(firstImage, region, isAutoFocus: true);
```

Then update the return (lines 177-181):

Old:
```csharp
            return new LoadedRun {
                Data = data,
                Seed = seed,
                AfOptions = afOptions
            };
```

New:
```csharp
            return new LoadedRun {
                Data = data,
                Seed = seed,
                Baseline = baseline,
                AfOptions = afOptions
            };
```

- [ ] **Step 5: Run the test to verify it passes**

Run:
```bash
dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~RunEvaluationLoaderTests"
```
Expected: PASS (the new fallback test + the existing loader tests).

- [ ] **Step 6: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/RunEvaluationLoader.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/Optimization/RunEvaluationLoaderTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat: split LoadedRun into default Seed + current-settings Baseline

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Wizard — measure improvement vs current settings

**Files:**
- Modify: `Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/StarDetectionOptimizerWizardVM.cs`
- Test: `Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/Optimization/StarDetectionOptimizerWizardVMTests.cs`

This task has several interdependent edits in one file; they are applied together and validated by the suite at the end. All line numbers are anchors as of the current file.

- [ ] **Step 1: Write the failing decoupling test**

Add a factory + test to `StarDetectionOptimizerWizardVMTests.cs`. The factory builds a `LoadedRun` with a DEFAULT-like `Seed` (Sensitivity=2) and a DISTINCT `Baseline` (Sensitivity=8). The test asserts the optimizer started from `Seed` (its raw `Result.ChangedVariables` before-value is 2) while the displayed `Summary.ChangedParameters` before-value is the `Baseline` (8) — proving the decoupling.

Add this factory next to `GoodRun` (after line 90):

```csharp
    // A run whose optimizer SEED (default) and displayed BASELINE (current settings) differ, so a test can prove
    // the optimizer starts from Seed while the summary's "before" column reflects Baseline.
    private static LoadedRun SeedBaselineSplitRun(
        string id = "split", double optSensitivity = 10.0, int seedSensitivity = 2, int baselineSensitivity = 8) {
        var data = new RunEvaluationData(id, NineFrames(), OptimizableDetect(optSensitivity), NewAlglib(), DefaultFitConfig());
        return new LoadedRun {
            Data = data,
            Seed = new StarDetectorParams { Sensitivity = seedSensitivity, StarClippingMultiplier = 2.0 },
            Baseline = new StarDetectorParams { Sensitivity = baselineSensitivity, StarClippingMultiplier = 2.0 },
            AfOptions = new AutoFocusEngineOptions { AutoFocusStepSize = DefaultStepSize, AutoFocusInitialOffsetSteps = 4 }
        };
    }
```

Add this test (after `Start_ChangedParametersRow_CarriesSeedAndBestValues`, ~line 326):

```csharp
    [Test]
    public async Task Start_OptimizerStartsFromSeed_ButSummaryBaselineIsCurrentSettings() {
        // The optimizer must start from the (default) Seed, while the displayed improvement/changed-parameters
        // "before" column must reflect the user's CURRENT settings (Baseline). Seed.Sensitivity=2 (default),
        // Baseline.Sensitivity=8 (current); both optimize toward 10.
        var vm = NewVM(LoaderReturning(SeedBaselineSplitRun()));
        vm.SourcePaths[0] = @"C:\run1";

        await vm.StartAsync(CancellationToken.None);

        // The optimizer's own record (Result.ChangedVariables) starts from the Seed (2).
        var rawSensitivity = vm.Result.ChangedVariables
            .FirstOrDefault(c => c.Name == nameof(StarDetectorParams.Sensitivity));
        Assert.That(rawSensitivity.Name, Is.EqualTo(nameof(StarDetectorParams.Sensitivity)),
            "the optimizer changed Sensitivity from its seed");
        Assert.That(rawSensitivity.SeedValue, Is.EqualTo(2.0).Within(1e-9), "the optimizer started from the default Seed (2)");

        // The displayed summary's "before" column reflects the current-settings Baseline (8).
        var displayedSensitivity = vm.Summary.ChangedParameters
            .FirstOrDefault(r => r.Name == nameof(StarDetectorParams.Sensitivity));
        Assert.That(displayedSensitivity, Is.Not.Null, "the summary shows the Sensitivity change vs current settings");
        Assert.Multiple(() => {
            Assert.That(displayedSensitivity.SeedValue, Is.EqualTo(8.0).Within(1e-9),
                "the summary 'before' value is the user's current setting (8), not the default seed (2)");
            Assert.That(Math.Abs(displayedSensitivity.OptimizedValue - 10.0),
                Is.LessThan(Math.Abs(8.0 - 10.0)), "optimized Sensitivity is closer to the optimum than current");
        });
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run:
```bash
dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~StarDetectionOptimizerWizardVMTests.Start_OptimizerStartsFromSeed_ButSummaryBaselineIsCurrentSettings"
```
Expected: FAIL — today `Summary.ChangedParameters` is computed from `res.ChangedVariables` (seed=2), so `displayedSensitivity.SeedValue` is 2.0, not 8.0.

- [ ] **Step 3: Add the objective-constants field + `currentBaselineJ` + `ComputeBaselineJAsync` helper**

Find the field block near the top of the `StarDetectionOptimizerWizardVM` class (where private fields like `cts`, `running`, `loadedRunFolders` live). Add these two fields and the helper. A good place for the helper is next to `AnalyzeWithProgressAsync` (~line 1158). Add the fields with the other private fields:

```csharp
        // The objective the optimizer uses by default (StarDetectionOptimizer ctor defaults to new ObjectiveConstants()).
        // The wizard computes the current-settings baseline J with the SAME constants so it is comparable to BestJ.
        private readonly ObjectiveConstants objectiveConstants = new ObjectiveConstants();

        // J of the user's CURRENT settings (Baseline), evaluated on the loaded runs. The displayed "before" for the
        // improvement readouts (summary SeedJ + live ProgressSeedJ). Set in StartAsync (and the re-optimize path)
        // before the optimize + summary steps.
        private double currentBaselineJ;
```

Add the helper method (e.g. immediately after `AnalyzeWithProgressAsync`):

```csharp
        /// <summary>
        /// Computes the objective J of the user's CURRENT settings (each run's <see cref="LoadedRun.Baseline"/>) on the
        /// loaded runs, using the same <see cref="ObjectiveConstants"/> and JRun/JTotal aggregation the optimizer uses,
        /// so the value is directly comparable to <see cref="OptimizationResult.BestJ"/>. This is the displayed
        /// "before" baseline for the improvement readouts. Cheap after the seed guard warmed the early-detection
        /// contexts (the late stage re-runs against the warmed cache).
        /// </summary>
        private async Task<double> ComputeBaselineJAsync(IReadOnlyList<LoadedRun> runs, CancellationToken token) {
            // Use a SINGLE current-settings bundle (runs[0].Baseline) across every run, exactly as the optimizer's
            // evaluator applies one StarDetectorParams across all runs — so this J is directly comparable to BestJ.
            var baseline = runs[0].Baseline;
            var perRunJ = new List<double>(runs.Count);
            for (var i = 0; i < runs.Count; i++) {
                token.ThrowIfCancellationRequested();
                var eval = await runs[i].Data.EvaluateAndFitAsync(baseline, token).ConfigureAwait(true);
                perRunJ.Add(OptimizationObjective.JRun(eval.Metrics, objectiveConstants));
            }
            return OptimizationObjective.JTotal(perRunJ, objectiveConstants);
        }
```

- [ ] **Step 4: Seed guard evaluates the Baseline (current settings)**

In `SeedFitIsUsableAsync` (line ~1131), change the evaluated bundle from `r.Seed` to `r.Baseline` so the "usable at your current settings" guard keeps its meaning and warms the current-settings contexts:

Old:
```csharp
            var results = await AnalyzeWithProgressAsync(runs, r => r.Seed, token).ConfigureAwait(true);
```

New:
```csharp
            var results = await AnalyzeWithProgressAsync(runs, r => r.Baseline, token).ConfigureAwait(true);
```

- [ ] **Step 5: Compute `currentBaselineJ`, warm the default seed, in `StartAsync`**

In `StartAsync`, after the seed guard passes (line 964-966) and before the optimize/UseCurrentSettings branch (line ~972), insert the baseline-J computation. Replace:

Old:
```csharp
                // 2. Seed guard — evaluate the seed once; bail with a clear error on a degenerate run.
                if (!await SeedFitIsUsableAsync(loadedRuns, token).ConfigureAwait(true)) {
                    return; // ErrorMessage set by the guard
                }

                // 3. Optimize — off the UI thread, progress marshaled back. OptimizeAsync always returns a
```

New:
```csharp
                // 2. Seed guard — evaluate the current settings once; bail with a clear error on a degenerate run.
                if (!await SeedFitIsUsableAsync(loadedRuns, token).ConfigureAwait(true)) {
                    return; // ErrorMessage set by the guard
                }

                // 2b. Baseline J = the user's CURRENT settings' objective, the displayed "before" for improvement.
                //     Computed once here (cheap: the guard just warmed the current-settings contexts).
                currentBaselineJ = await ComputeBaselineJAsync(loadedRuns, token).ConfigureAwait(true);
                ProgressSeedJ = currentBaselineJ;

                // 3. Optimize — off the UI thread, progress marshaled back. OptimizeAsync always returns a
```

Then, in the `else` (optimize) branch, warm the DEFAULT seed contexts before optimizing so the optimizer's first evaluation does not stall the progress bar (mirrors the re-optimize path's warm-up; all cache hits when the user's settings already equal the defaults). Replace:

Old:
```csharp
                } else {
                    CurrentStep = WizardStep.Optimize;
                    optimizeResult = await OptimizeAsync(loadedRuns, token).ConfigureAwait(true);
                    optimized = true;
                }
```

New:
```csharp
                } else {
                    CurrentStep = WizardStep.Optimize;
                    // Warm the DEFAULT-seed early contexts (the optimizer starts here) so the bar moves during the
                    // first build instead of sitting on the optimizer's seed evaluation.
                    await AnalyzeWithProgressAsync(loadedRuns, r => r.Seed, token).ConfigureAwait(true);
                    optimizeResult = await OptimizeAsync(loadedRuns, token).ConfigureAwait(true);
                    optimized = true;
                }
```

- [ ] **Step 6: "Use current settings" mode applies the Baseline (not the default Seed)**

In the `OptimizeMode == WizardOptimizeMode.UseCurrentSettings` branch (lines 974-984), the "no optimization" result must carry the user's CURRENT settings and the baseline J. Replace:

Old:
```csharp
                if (OptimizeMode == WizardOptimizeMode.UseCurrentSettings) {
                    SetProgress("Using current settings (no optimization)", 0, 0);
                    optimizeResult = new OptimizationResult {
                        BestParams = loadedRuns[0].Seed,
                        SeedJ = 0.0,
                        BestJ = 0.0,
                        Evaluations = 0,
                        ImprovedOverSeed = false,
                        ChangedVariables = new List<(string Name, double SeedValue, double BestValue)>()
                    };
                    optimized = false;
```

New:
```csharp
                if (OptimizeMode == WizardOptimizeMode.UseCurrentSettings) {
                    SetProgress("Using current settings (no optimization)", 0, 0);
                    optimizeResult = new OptimizationResult {
                        BestParams = loadedRuns[0].Baseline,
                        SeedJ = currentBaselineJ,
                        BestJ = currentBaselineJ,
                        Evaluations = 0,
                        ImprovedOverSeed = false,
                        ChangedVariables = new List<(string Name, double SeedValue, double BestValue)>()
                    };
                    optimized = false;
```

- [ ] **Step 7: "Current" variant result uses the Baseline + baseline J**

In `StartAsync`, the `currentResult` block (lines 997-1004). Replace:

Old:
```csharp
                currentResult = new OptimizationResult {
                    BestParams = loadedRuns[0].Seed,
                    SeedJ = optimizeResult.SeedJ,
                    BestJ = optimizeResult.SeedJ,
                    Evaluations = 0,
                    ImprovedOverSeed = false,
                    ChangedVariables = new List<(string Name, double SeedValue, double BestValue)>()
                };
```

New:
```csharp
                currentResult = new OptimizationResult {
                    BestParams = loadedRuns[0].Baseline,
                    SeedJ = currentBaselineJ,
                    BestJ = currentBaselineJ,
                    Evaluations = 0,
                    ImprovedOverSeed = false,
                    ChangedVariables = new List<(string Name, double SeedValue, double BestValue)>()
                };
```

- [ ] **Step 8: Live progress shows improvement vs the current-settings baseline**

In `OptimizeAsync` (the progress callback, line ~1189), the seed J displayed during the search must be the current-settings baseline, not the optimizer's (default) seed J. Replace:

Old:
```csharp
            var progress = new Progress<OptimizationProgress>(p => {
                ProgressCurrent = p.Evaluations;
                ProgressTotal = p.MaxEvaluations;
                ProgressBestJ = p.BestJ;
                ProgressSeedJ = p.SeedJ;
                Phase = FriendlyPhase(p.Phase);
            });
```

New:
```csharp
            var progress = new Progress<OptimizationProgress>(p => {
                ProgressCurrent = p.Evaluations;
                ProgressTotal = p.MaxEvaluations;
                ProgressBestJ = p.BestJ;
                // The displayed baseline is the user's CURRENT settings (currentBaselineJ), not the optimizer's
                // default seed J (p.SeedJ), so the live "improved ~X%" reads vs current and matches the summary.
                ProgressSeedJ = currentBaselineJ;
                Phase = FriendlyPhase(p.Phase);
            });
```

- [ ] **Step 9: `BuildSummaryAsync` — before-σ/J + changed-params vs current settings**

Replace the body of `BuildSummaryAsync` from the `var seed = ...` line through the `var summary = ...` assignment (lines 1220-1268). Key changes: evaluate `Baseline` for the "before" σ and the "Current" curve; `SeedJ = currentBaselineJ`; compute the changed-parameters table as current(Baseline) → optimized(BestParams) over the curated variable set.

Old:
```csharp
            var seed = runs[0].Seed;
            var changed = res.ChangedVariables
                .Select(c => new ChangedParameterRow { Name = c.Name, SeedValue = c.SeedValue, OptimizedValue = c.BestValue })
                .ToList();

            // σ(focus) before/after, averaged over the runs (the first run also yields the representative best fit
            // AND the curves we plot — seed = "Current", best = "Optimized"; both carry the scatter points + fit).
            double seedSigmaSum = 0.0, bestSigmaSum = 0.0;
            var seedSigmaCount = 0; var bestSigmaCount = 0;
            AlglibHyperbolicFitting representativeBestFit = null;
            OptimizationCurve currentCurveLocal = null, optimizedCurveLocal = null;
            for (var i = 0; i < runs.Count; i++) {
                token.ThrowIfCancellationRequested();
                var seedEval = await runs[i].Data.EvaluateAndFitAsync(seed, token).ConfigureAwait(true);
                var bestEval = await runs[i].Data.EvaluateAndFitAsync(res.BestParams, token).ConfigureAwait(true);
                if (double.IsFinite(seedEval.Metrics.SigmaFocus)) { seedSigmaSum += seedEval.Metrics.SigmaFocus; seedSigmaCount++; }
                if (double.IsFinite(bestEval.Metrics.SigmaFocus)) { bestSigmaSum += bestEval.Metrics.SigmaFocus; bestSigmaCount++; }
                if (i == 0) {
                    representativeBestFit = bestEval.BestFit;
                    currentCurveLocal = new OptimizationCurve {
                        Label = "Current", Points = seedEval.Points, Fit = seedEval.BestFit,
                        FrameStarCounts = seedEval.Metrics.FrameStarCounts,
                        FrameFocuserPositions = seedEval.Metrics.FrameFocuserPositions
                    };
                    optimizedCurveLocal = new OptimizationCurve {
                        Label = "Optimized", Points = bestEval.Points, Fit = bestEval.BestFit,
                        FrameStarCounts = bestEval.Metrics.FrameStarCounts,
                        FrameFocuserPositions = bestEval.Metrics.FrameFocuserPositions
                    };
                }
            }

            var currentStepSize = runs[0].AfOptions?.AutoFocusStepSize ?? DefaultCurrentStepSize;
            var currentOffsetSteps = runs[0].AfOptions?.AutoFocusInitialOffsetSteps ?? DefaultCurrentOffsetSteps;
            var recommendation = StepSizeRecommender.Recommend(representativeBestFit, currentStepSize, GetFocuserMaxStep());

            var summary = new OptimizationSummary {
                ChangedParameters = changed,
                SeedJ = res.SeedJ,
                BestJ = res.BestJ,
                SeedSigmaFocus = seedSigmaCount > 0 ? seedSigmaSum / seedSigmaCount : double.NaN,
                BestSigmaFocus = bestSigmaCount > 0 ? bestSigmaSum / bestSigmaCount : double.NaN,
                RunCount = runs.Count,
                RecommendedStepSize = recommendation.StepSize,
                RecommendedOffsetSteps = recommendation.OffsetSteps,
                CurrentStepSize = currentStepSize,
                CurrentOffsetSteps = currentOffsetSteps,
                ImprovedOverSeed = res.ImprovedOverSeed
            };
            return (summary, currentCurveLocal, optimizedCurveLocal);
```

New:
```csharp
            // The displayed "before" is the user's CURRENT settings (Baseline), NOT the optimizer's default seed.
            var baseline = runs[0].Baseline;

            // Changed-parameters table = current (Baseline) -> optimized (BestParams) over the curated knobs, so it is
            // exactly the diff the user would accept onto their live settings (consistent with the σ/J improvement,
            // which is also measured vs current). This intentionally replaces res.ChangedVariables (default -> best).
            var changed = new List<ChangedParameterRow>();
            foreach (var v in OptimizerVariable.CreateCuratedSet()) {
                var before = v.Read(baseline);
                var after = v.Read(res.BestParams);
                if (Math.Abs(before - after) > 1e-9) {
                    changed.Add(new ChangedParameterRow { Name = v.Name, SeedValue = before, OptimizedValue = after });
                }
            }

            // σ(focus) before/after, averaged over the runs (the first run also yields the representative best fit
            // AND the curves we plot — baseline = "Current", best = "Optimized"; both carry the scatter points + fit).
            double baselineSigmaSum = 0.0, bestSigmaSum = 0.0;
            var baselineSigmaCount = 0; var bestSigmaCount = 0;
            AlglibHyperbolicFitting representativeBestFit = null;
            OptimizationCurve currentCurveLocal = null, optimizedCurveLocal = null;
            for (var i = 0; i < runs.Count; i++) {
                token.ThrowIfCancellationRequested();
                var baselineEval = await runs[i].Data.EvaluateAndFitAsync(baseline, token).ConfigureAwait(true);
                var bestEval = await runs[i].Data.EvaluateAndFitAsync(res.BestParams, token).ConfigureAwait(true);
                if (double.IsFinite(baselineEval.Metrics.SigmaFocus)) { baselineSigmaSum += baselineEval.Metrics.SigmaFocus; baselineSigmaCount++; }
                if (double.IsFinite(bestEval.Metrics.SigmaFocus)) { bestSigmaSum += bestEval.Metrics.SigmaFocus; bestSigmaCount++; }
                if (i == 0) {
                    representativeBestFit = bestEval.BestFit;
                    currentCurveLocal = new OptimizationCurve {
                        Label = "Current", Points = baselineEval.Points, Fit = baselineEval.BestFit,
                        FrameStarCounts = baselineEval.Metrics.FrameStarCounts,
                        FrameFocuserPositions = baselineEval.Metrics.FrameFocuserPositions
                    };
                    optimizedCurveLocal = new OptimizationCurve {
                        Label = "Optimized", Points = bestEval.Points, Fit = bestEval.BestFit,
                        FrameStarCounts = bestEval.Metrics.FrameStarCounts,
                        FrameFocuserPositions = bestEval.Metrics.FrameFocuserPositions
                    };
                }
            }

            var currentStepSize = runs[0].AfOptions?.AutoFocusStepSize ?? DefaultCurrentStepSize;
            var currentOffsetSteps = runs[0].AfOptions?.AutoFocusInitialOffsetSteps ?? DefaultCurrentOffsetSteps;
            var recommendation = StepSizeRecommender.Recommend(representativeBestFit, currentStepSize, GetFocuserMaxStep());

            var summary = new OptimizationSummary {
                ChangedParameters = changed,
                SeedJ = currentBaselineJ,
                BestJ = res.BestJ,
                SeedSigmaFocus = baselineSigmaCount > 0 ? baselineSigmaSum / baselineSigmaCount : double.NaN,
                BestSigmaFocus = bestSigmaCount > 0 ? bestSigmaSum / bestSigmaCount : double.NaN,
                RunCount = runs.Count,
                RecommendedStepSize = recommendation.StepSize,
                RecommendedOffsetSteps = recommendation.OffsetSteps,
                CurrentStepSize = currentStepSize,
                CurrentOffsetSteps = currentOffsetSteps,
                ImprovedOverSeed = res.ImprovedOverSeed
            };
            return (summary, currentCurveLocal, optimizedCurveLocal);
```

Also update the `BuildSummaryAsync` doc-comment line (1213-1216) wording "re-evaluated at seed and best" → "re-evaluated at the current settings (baseline) and best" (cosmetic; keeps the comment honest).

- [ ] **Step 10: Re-optimize (feedback) path sets `currentBaselineJ` too**

In `ReOptimizeWithLabelsAsync`, after the warm-up `AnalyzeWithProgressAsync(reloaded, ...)` (line ~1640) and before `OptimizeAsync(reloaded, ...)` (line ~1643), compute the baseline J for the reloaded runs so the feedback summary's "before" is also the user's current settings. Replace:

Old:
```csharp
                // Warm + report the per-frame early contexts for the params the optimizer will start from (re-optimize
                // skips the seed-guard, so this is the parity warm-up that makes the bar move during the initial build).
                await AnalyzeWithProgressAsync(reloaded, _ => seedOverride ?? reloaded[0].Seed, token).ConfigureAwait(true);

                // Re-run the optimize + summary pipeline over the labeled runs (warm-started when we have a recommendation).
                var optimizeResult = await OptimizeAsync(reloaded, token, seedOverride, variablesOverride).ConfigureAwait(true);
```

New:
```csharp
                // Warm + report the per-frame early contexts for the params the optimizer will start from (re-optimize
                // skips the seed-guard, so this is the parity warm-up that makes the bar move during the initial build).
                await AnalyzeWithProgressAsync(reloaded, _ => seedOverride ?? reloaded[0].Seed, token).ConfigureAwait(true);

                // Baseline J for the reloaded runs (current settings) — the displayed "before" for the feedback summary,
                // keeping the improvement measured vs the user's current settings on this path too.
                currentBaselineJ = await ComputeBaselineJAsync(reloaded, token).ConfigureAwait(true);

                // Re-run the optimize + summary pipeline over the labeled runs (warm-started when we have a recommendation).
                var optimizeResult = await OptimizeAsync(reloaded, token, seedOverride, variablesOverride).ConfigureAwait(true);
```

- [ ] **Step 11: `Apply` records the displayed (vs-current) before/after J**

In `Apply` (lines 1339-1344), record the summary's baseline/best J (the vs-current numbers the user saw) in the persisted snapshot + log, instead of the optimizer's default-seed J. Replace:

Old:
```csharp
            var dto = OptimizedStarDetectionSettings.FromParams(
                result.BestParams, summary.RunCount, result.SeedJ, result.BestJ,
                summary.RecommendedStepSize, summary.RecommendedOffsetSteps);

            starDetectionOptions.ApplyOptimizedSettings(dto);
            Logger.Info($"Applied optimized star-detection settings (J {result.SeedJ:F3} -> {result.BestJ:F3}, {summary.RunCount} run(s))");
```

New:
```csharp
            // Record the displayed (vs current-settings) before/after J in the snapshot + log, so the persisted record
            // matches the improvement the user saw. summary.SeedJ is the current-settings baseline J; summary.BestJ is
            // the optimizer's best (== result.BestJ).
            var dto = OptimizedStarDetectionSettings.FromParams(
                result.BestParams, summary.RunCount, summary.SeedJ, summary.BestJ,
                summary.RecommendedStepSize, summary.RecommendedOffsetSteps);

            starDetectionOptions.ApplyOptimizedSettings(dto);
            Logger.Info($"Applied optimized star-detection settings (J {summary.SeedJ:F3} -> {summary.BestJ:F3}, {summary.RunCount} run(s))");
```

- [ ] **Step 12: Run the new decoupling test to verify it passes**

Run:
```bash
dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~StarDetectionOptimizerWizardVMTests.Start_OptimizerStartsFromSeed_ButSummaryBaselineIsCurrentSettings"
```
Expected: PASS.

- [ ] **Step 13: Run the full wizard + options test classes to confirm no regression**

Run:
```bash
dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~StarDetectionOptimizerWizardVMTests|FullyQualifiedName~StarDetectionOptionsTests|FullyQualifiedName~RunEvaluationLoaderTests"
```
Expected: ALL PASS. In particular these existing tests must stay green (they rely on `Baseline` falling back to `Seed` in the in-memory factories, so the before-values equal the seed values they assert): `Start_ChangedParametersRow_CarriesSeedAndBestValues`, `Start_GoodRun_PopulatesSummaryModel`, `Apply_CallsApplyOptimizedSettingsWithCuratedValuesAndMetadata`, `Start_UseCurrentSettings_*`, `SelectingCurrentVariant_*`.

If `Apply_CallsApplyOptimizedSettingsWithCuratedValuesAndMetadata` fails on `dto.BaselineJ`/`dto.FinalJ`: confirm Step 11 used `summary.SeedJ`/`summary.BestJ` (in the in-memory tests these equal `result.SeedJ`/`result.BestJ` because Baseline==Seed, so the assertions still hold).

- [ ] **Step 14: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/StarDetectionOptimizerWizardVM.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/Optimization/StarDetectionOptimizerWizardVMTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat: optimize from default seed, show improvement vs current settings

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Run the entire suite**

Run:
```bash
dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo
```
Expected: BUILD SUCCEEDED and ALL tests PASS (0 failed). If any fail, fix the underlying cause (do not skip/ignore tests).

- [ ] **Step 2: Confirm no stray options mutation**

Verify by reading the diff that the only write to `IStarDetectionOptions`/`StarDetectionOptions` settings on the wizard path is still `ApplyOptimizedSettings` inside `Apply` (called only from `Accept`). Run:
```bash
git diff develop...HEAD -- Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/
```
Expected: no new setter calls on options outside `Apply`; `GetDefaultStarDetectorParams` is read-only.

- [ ] **Step 3: Manual verification notes (in NINA, performed by the user later)**

These cannot be unit-tested; record them for the PR description:
- Open the wizard with **non-default** current star-detection settings; run a replay optimization. The search starts from defaults; the summary's "before" σ/cost + changed-parameters table read against your **current** settings.
- **Reject** (Close): live star-detection options are unchanged (no `ApplyOptimizedSettings`).
- **Accept**: the optimized snapshot is applied (`UseOptimizedSettings = true`).
- If your current settings were already better than the search-from-default result, the summary honestly shows little/no improvement (σ "unchanged" or worse); rejecting keeps your current settings.

- [ ] **Step 4: Finish the branch**

Use the `superpowers:finishing-a-development-branch` skill to choose how to integrate (open a PR to `develop` — never push to `develop` directly, per CLAUDE.md).

---

## Self-Review Notes (for the implementer)

- **Spec coverage:** default seed (Task 1+2+3), save/measure current baseline (Task 4 Steps 3,5,9,10), display improvement vs current (Task 4 Steps 8,9,11), revert-on-reject = automatic/no options mutation (verified Task 5 Step 2), changed-params current→optimized (Task 4 Step 9), drift guard (Task 1), feedback path keeps warm-start but baseline-vs-current (Task 4 Step 10), "Use current settings" applies current not default (Task 4 Step 6). All covered.
- **Type consistency:** `Baseline` (LoadedRun) get/set; `currentBaselineJ` (double) + `objectiveConstants` (ObjectiveConstants) fields; `ComputeBaselineJAsync(IReadOnlyList<LoadedRun>, CancellationToken) -> Task<double>`; `OptimizationObjective.JRun(RunEvaluationMetrics, ObjectiveConstants)` and `JTotal(IReadOnlyList<double>, ObjectiveConstants)`; `OptimizerVariable.Read(StarDetectorParams) -> double` and `CreateCuratedSet()`; `BuildDefaultStarDetectorParams()` / `GetDefaultStarDetectorParams(IRenderedImage, StarDetectionRegion, bool)`. Consistent across tasks.
- **Behavioral note (intended):** with the seed at default, `BestJ` can be below the current-settings baseline; the improvement readouts clamp negatives and the user rejects to keep current. Do NOT add a guard that forces best ≥ current — out of scope.
