# σ Consistency (F4, then F3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every σ-based detector threshold use the σ of the image it is actually applied to (F4), recalibrate knob values so effective behavior at defaults is preserved, then decide MeasureStar's τ semantics empirically (F3).

**Architecture:** Two named noise estimates in `StarDetector.Detect` — σ_structure (existing, noise-reduced copy → binarize threshold only) and σ_measure (new, measured directly on `srcImage` in a parallel task → sensitivity gate, clip margins, MeasureStar τ, PSF noise floor, contamination fallback). Per-preset knob recalibration preserves effective behavior. A `TauClipPolicy` seam in `MeasureStar` enables an evidence-driven F3 decision at a mid-plan user checkpoint.

**Tech Stack:** C# / .NET 8.0-windows7.0, NUnit 4, OpenCvSharp, headless `TestApp focus-sweep` for real-data validation.

**Spec:** `docs/sigma-consistency-design.md` (approved). Branch: `ghilios/sigma-consistency` (already created; contains the design doc commit).

**Environment notes (read first):**
- All `dotnet` commands run via WSL interop. Use `rtk dotnet ...` (trust `errors=0`/exit code, not the header word) or `cmd.exe /c "dotnet ..."`. Set Bash timeout to 600000 for build/test.
- Full suite: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
- Every commit must use the noreply identity:
  ```bash
  GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
    git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "..."
  ```
- Never push to `develop`; the PR at the end targets `develop`.

**The three user checkpoints (do not skip):**
- **Checkpoint A** (Task 1): ask the user for paths to saved AutoFocus runs; capture BASELINE focus-sweeps before any production change.
- **Checkpoint B** (Task 8): present the F3 experiment table; the user picks the τ policy.
- **Checkpoint C** (Task 10): present before/after focus-sweep comparison; the user signs off (or constants get nudged and we loop).

---

### Task 1: Checkpoint A — collect AF run paths and capture baseline focus-sweeps

No production code changes in this task. Everything here runs against current code.

**Files:** none modified (TestApp build output only).

- [ ] **Step 1: Ask the user for AF run paths**

Ask the user in chat (verbatim is fine):

> "I need paths to 1–3 saved AutoFocus runs (the folders NINA writes when 'Save AutoFocus images' is on — files named like `001_Frame01_BitDepth16_Bayered0_Focuser12345.fits`). Ideally runs spanning different conditions (different targets / star densities). These will be the before/after evaluation set for the σ changes. Please give Windows paths, e.g. `C:\Users\ghili\...\AutoFocus\2026-06-10_22-15`."

Wait for the answer. Record the paths; they are used again in Task 10.

- [ ] **Step 2: Build TestApp (current code = baseline code)**

Run: `cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"` (timeout 600000)
Expected: exit 0, `0 errors`.

- [ ] **Step 3: Run a baseline focus-sweep per AF run**

For each user-provided path (N = 1, 2, 3...):

```bash
./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe \
  focus-sweep --af-run "<userPathN>" --out "C:\temp\hf-sigma-step3\baseline\runN"
```

Expected: console prints per-position star counts and `Wrote focus_sweep.csv, focus_sweep_summary.txt, focus_sweep_hfr.png`.

- [ ] **Step 4: Sanity-check the baselines**

Read each `C:\temp\hf-sigma-step3\baseline\runN\focus_sweep.csv` (WSL path `/mnt/c/temp/hf-sigma-step3/baseline/runN/focus_sweep.csv`). Confirm: multiple focuser positions, non-zero `StarCount`, a V-shaped `MedianHFR`. If a run looks broken (0 stars everywhere), tell the user and ask for a replacement path before continuing.

No commit (outputs live outside the repo).

---

### Task 2: σ-ratio sanity test (pins the mechanism the recalibration compensates)

**Files:**
- Test: create `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Utility/SigmaConsistencyTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility {

    /// <summary>
    /// Pins the F4 mechanism: a Gaussian blur of the default noise-reduction kernel cuts white-noise σ by a
    /// large factor (≈ the inverse L2 norm of the 2D kernel, ~4-5× for radius 3 / kernel 7), so σ estimated on
    /// the blurred structure-map copy badly understates the σ of the sharp image that star measurement samples.
    /// The recalibration constants in StarDetectionOptions (10→2.0, 2.0→0.4, i.e. ×0.2) assume this factor.
    /// </summary>
    [TestFixture]
    public class SigmaConsistencyTests {

        [Test]
        public void KappaSigma_GaussianBlurredWhiteNoise_SigmaShrinksRoughlyFiveFold() {
            const int size = 512;
            const double noiseSigma = 0.02;
            const int radius = 3; // default NoiseReductionRadius → kernel = 2*3+1 = 7
            using var sharp = SyntheticGaussianStarImage.CreateFlat(size, size, 0.2f);
            SyntheticDefocusedStarImage.AddGaussianNoise(sharp, noiseSigma, seed: 777);
            using var blurred = sharp.Clone();
            CvImageUtility.ConvolveGaussian(blurred, blurred, radius * 2 + 1);

            var sharpEstimate = CvImageUtility.KappaSigmaNoiseEstimate(sharp, clippingMultipler: 4.0);
            var blurredEstimate = CvImageUtility.KappaSigmaNoiseEstimate(blurred, clippingMultipler: 4.0);
            double ratio = sharpEstimate.Sigma / blurredEstimate.Sigma;
            TestContext.WriteLine($"σ_sharp={sharpEstimate.Sigma:F6} σ_blurred={blurredEstimate.Sigma:F6} ratio={ratio:F2}");

            Assert.Multiple(() => {
                Assert.That(sharpEstimate.Sigma, Is.EqualTo(noiseSigma).Within(0.3 * noiseSigma),
                    "kappa-sigma should recover the true white-noise σ on the sharp image");
                Assert.That(ratio, Is.InRange(3.0, 7.0),
                    "blur should understate σ by roughly the factor the recalibration constants assume (~5×)");
            });
        }
    }
}
```

- [ ] **Step 2: Run it — it should PASS against current code (it tests CvImageUtility, not the detector)**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter FullyQualifiedName~SigmaConsistencyTests` (timeout 600000)
Expected: PASS. Note the printed ratio — if it is far from 5 (e.g. 3.2), remember it for Task 10's interpretation of count shifts.

- [ ] **Step 3: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Utility/SigmaConsistencyTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Pin sigma understatement factor of noise-reduction blur (F4 mechanism)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Synthetic star field helper + cross-change pin test

This test is the **compensation guard**: written and passing BEFORE the σ switch, it must STILL pass after Task 4 (honest σ + recalibrated knobs), proving effective behavior at defaults is preserved.

**Files:**
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Synthetic/SyntheticStarField.cs`
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/SigmaConsistencyDetectorTests.cs`

- [ ] **Step 1: Write the star-field helper**

```csharp
using OpenCvSharp;
using System;
using Size = OpenCvSharp.Size;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Synthetic {

    /// <summary>
    /// Builds multi-star synthetic fields for full-pipeline StarDetector.Detect tests. Stars are additive
    /// Gaussians on a flat background; noise is seeded (deterministic). Peak values are chosen by callers to
    /// sit clearly above or below the gates under test, so small σ-estimate differences cannot flip outcomes.
    /// </summary>
    internal static class SyntheticStarField {

        public static Mat CreateFlat(int width, int height, float background) {
            var mat = new Mat(new Size(width, height), MatType.CV_32F);
            mat.SetTo(new Scalar(background));
            return mat;
        }

        /// <summary>Adds a Gaussian star (peak above background) at (cx, cy) with the given σ in pixels.</summary>
        public static void AddStar(Mat mat, double cx, double cy, double sigma, double peak) {
            int rad = (int)Math.Ceiling(5.0 * sigma);
            double inv = 1.0 / (2.0 * sigma * sigma);
            int width = mat.Width, height = mat.Height;
            unsafe {
                var data = (float*)mat.DataPointer;
                for (int y = Math.Max(0, (int)cy - rad); y <= Math.Min(height - 1, (int)cy + rad); ++y) {
                    for (int x = Math.Max(0, (int)cx - rad); x <= Math.Min(width - 1, (int)cx + rad); ++x) {
                        double dx = x - cx, dy = y - cy;
                        data[y * width + x] += (float)(peak * Math.Exp(-(dx * dx + dy * dy) * inv));
                    }
                }
            }
        }
    }
}
```

- [ ] **Step 2: Write the pin test**

```csharp
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using System.Threading;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Full-pipeline tests for the F4 fix (two named σ estimates) and its recalibration. The pin test below
    /// is written BEFORE the σ switch and must keep passing AFTER it: old code at old defaults and new code at
    /// recalibrated defaults must detect the same star set (effective thresholds preserved by design).
    /// </summary>
    [TestFixture]
    public class SigmaConsistencyDetectorTests {
        private const int Size = 256;
        private const double NoiseSigma = 0.02;
        private const double Background = 0.05;
        private const double StarSigmaPx = 2.5;
        private const int Seed = 424242;

        // 12 bright stars (peaks 20-40× noise σ: decisively above the sensitivity gate in both the old
        // effective regime (~2σ_sharp) and the recalibrated honest regime (2.0σ_sharp)).
        private static readonly (double x, double y, double peak)[] BrightStars = {
            (30, 30, 0.80), (90, 30, 0.70), (150, 30, 0.60), (210, 30, 0.50),
            (30, 90, 0.80), (90, 90, 0.70), (150, 90, 0.60), (210, 90, 0.50),
            (30, 150, 0.45), (90, 150, 0.45), (150, 150, 0.40), (210, 150, 0.40),
        };

        // 8 ultra-faint stars (peak 1.5× noise σ: above the structure-map binarize threshold — which sits at
        // ~0.8σ_n because it legitimately uses the smoothed-image σ — but decisively below the sensitivity
        // gate in both regimes, so they pin the gate, not structure detection).
        private static readonly (double x, double y)[] FaintStars = {
            (30, 210), (60, 210), (90, 210), (120, 210),
            (150, 210), (180, 210), (210, 210), (240, 210),
        };

        internal static OpenCvSharp.Mat BuildField() {
            var mat = SyntheticStarField.CreateFlat(Size, Size, (float)Background);
            foreach (var (x, y, peak) in BrightStars) SyntheticStarField.AddStar(mat, x, y, StarSigmaPx, peak);
            foreach (var (x, y) in FaintStars) SyntheticStarField.AddStar(mat, x, y, StarSigmaPx, 1.5 * NoiseSigma);
            SyntheticDefocusedStarImage.AddGaussianNoise(mat, NoiseSigma, Seed);
            return mat;
        }

        internal static StarDetectorParams MismatchPathParams() => new StarDetectorParams {
            // The F4 mismatch path: a noise-reduction radius is set but measurement noise reduction is off,
            // so the structure copy is blurred while the sharp srcImage is what gets measured. All other
            // values stay at class defaults so this test tracks the default knob recalibration.
            StarMeasurementNoiseReductionEnabled = false,
            NoiseReductionRadius = 3,
        };

        [Test]
        public void Detect_DefaultKnobsOnMismatchPath_DetectsExactlyTheBrightStars() {
            using var image = BuildField();
            var detector = new StarDetector(new AlglibAPI());
            var result = detector.Detect(image, MismatchPathParams(), null, CancellationToken.None)
                .GetAwaiter().GetResult();
            TestContext.WriteLine($"detected={result.DetectedStars.Count} lowSensitivity={result.Metrics.LowSensitivity}");
            Assert.That(result.DetectedStars.Count, Is.EqualTo(12),
                "default knobs should accept all 12 bright stars and reject all 8 ultra-faint ones — " +
                "this count must be identical before and after the σ switch + recalibration");
        }
    }
}
```

- [ ] **Step 3: Run it against CURRENT code**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter FullyQualifiedName~SigmaConsistencyDetectorTests` (timeout 600000)
Expected: PASS with `detected=12`. If the count differs (e.g. a faint star sneaks through or a bright one is rejected), adjust the star peaks (bright up / faint down) until the count is exactly 12 and stable — the peaks were chosen with wide margins so this should not be needed. Do NOT proceed with a flaky count.

- [ ] **Step 4: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Synthetic/SyntheticStarField.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/SigmaConsistencyDetectorTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Add cross-change pin test for default-knob detection behavior (F4 guard)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Two σs in Detect + recalibrated knob values (one atomic, suite-green change)

The σ switch and the recalibration MUST land together: honest σ with old knob values changes behavior ~5× (the pin test would fail between commits).

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetector.cs` (~:225-234, ~:259, :301, :309, result construction ~:320-360)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/IStarDetector.cs` (:207, :213-218, :244-245, `HocusFocusStarDetectorResult` :432)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionOptions.cs` (InitializeOptions :154/:158, ConfigureSimpleSettings :58-127, ResetDefaults :201/:205)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectionOptionsTests.cs` (:141-186)
- Modify: `Joko.NINA.Plugins/TestApp/FocusSweepDiagnosticRunner.cs` (PositionAccum, Accumulate, WriteCsv)
- Test: extend `SigmaConsistencyDetectorTests.cs`

- [ ] **Step 1: Write the failing tests (RED)**

Add to `SigmaConsistencyDetectorTests`:

```csharp
        [Test]
        public void Detect_MismatchPath_ExposesHonestMeasurementSigmaAndSmallerStructureSigma() {
            using var image = BuildField();
            var detector = new StarDetector(new AlglibAPI());
            var result = detector.Detect(image, MismatchPathParams(), null, CancellationToken.None)
                .GetAwaiter().GetResult();
            TestContext.WriteLine($"σ_measure={result.MeasurementNoiseSigma:F6} σ_structure={result.StructureNoiseSigma:F6}");
            Assert.Multiple(() => {
                Assert.That(result.MeasurementNoiseSigma, Is.EqualTo(NoiseSigma).Within(0.4 * NoiseSigma),
                    "measurement σ must be estimated on the sharp srcImage (the image MeasureStar samples)");
                Assert.That(result.StructureNoiseSigma, Is.LessThan(0.5 * result.MeasurementNoiseSigma),
                    "structure σ comes from the blurred copy and must be much smaller on this path");
            });
        }

        [Test]
        public void Detect_ConsistentPaths_ReuseStructureSigmaAsMeasurementSigma() {
            using var image = BuildField();
            var detector = new StarDetector(new AlglibAPI());

            // Path 1: measurement noise reduction ON → srcImage is the blurred image → identical estimates.
            var nrOn = new StarDetectorParams { StarMeasurementNoiseReductionEnabled = true, NoiseReductionRadius = 3 };
            var r1 = detector.Detect(image.Clone(), nrOn, null, CancellationToken.None).GetAwaiter().GetResult();
            // Path 2: no noise reduction at all → no blur anywhere → identical estimates.
            var noNr = new StarDetectorParams { StarMeasurementNoiseReductionEnabled = false, NoiseReductionRadius = 0 };
            var r2 = detector.Detect(image.Clone(), noNr, null, CancellationToken.None).GetAwaiter().GetResult();

            Assert.Multiple(() => {
                Assert.That(r1.MeasurementNoiseSigma, Is.EqualTo(r1.StructureNoiseSigma),
                    "with measurement NR on, the images are identical — the estimate must be reused, not recomputed");
                Assert.That(r2.MeasurementNoiseSigma, Is.EqualTo(r2.StructureNoiseSigma),
                    "with radius 0 the images are identical — the estimate must be reused, not recomputed");
            });
        }

        [Test]
        public void Detect_SensitivityGate_UsesMeasurementSigma() {
            // Stars at 5× noise σ: under the OLD code the gate at 10 was effectively ~2σ_sharp (smoothed σ is
            // ~5× small), so 5σ stars passed. With honest σ, Sensitivity=10 means a true 10σ and they must be
            // rejected. This test FAILS before the fix and PASSES after.
            var mat = SyntheticStarField.CreateFlat(Size, Size, (float)Background);
            var positions = new (double x, double y)[] { (40, 40), (120, 40), (200, 40), (40, 120), (120, 120) };
            foreach (var (x, y) in positions) SyntheticStarField.AddStar(mat, x, y, StarSigmaPx, 5.0 * NoiseSigma);
            SyntheticDefocusedStarImage.AddGaussianNoise(mat, NoiseSigma, Seed);
            using var image = mat;

            var p = MismatchPathParams();
            p.Sensitivity = 10.0; // explicit: probe the honest meaning of "10σ"
            var detector = new StarDetector(new AlglibAPI());
            var result = detector.Detect(image, p, null, CancellationToken.None).GetAwaiter().GetResult();
            TestContext.WriteLine($"detected={result.DetectedStars.Count} lowSensitivity={result.Metrics.LowSensitivity}");
            Assert.Multiple(() => {
                Assert.That(result.DetectedStars.Count, Is.EqualTo(0),
                    "5σ stars must fail an honest 10σ sensitivity gate");
                Assert.That(result.Metrics.LowSensitivity, Is.GreaterThanOrEqualTo(positions.Length),
                    "the faint stars must be rejected specifically by the sensitivity gate");
            });
        }
```

- [ ] **Step 2: Run them — expect RED for the right reasons**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter FullyQualifiedName~SigmaConsistencyDetectorTests` (timeout 600000)
Expected: **compile error** — `MeasurementNoiseSigma`/`StructureNoiseSigma` do not exist on `HocusFocusStarDetectorResult`. That is the red state for tests 1-2. (Test 3 would fail with `detected=5` once it compiles.)

- [ ] **Step 3: Add the result fields**

In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/IStarDetector.cs`, inside `HocusFocusStarDetectorResult` (line ~432), add:

```csharp
        // Noise σ estimated on the noise-reduced structure-map source; drives only the binarize threshold.
        public double StructureNoiseSigma { get; set; }

        // Noise σ estimated on the image actually sampled for star measurement; drives the sensitivity gate,
        // clip margins, MeasureStar τ, the PSF noise floor, and the contamination fallback. Equal to
        // StructureNoiseSigma when the two images are identical (no noise reduction, or measurement noise
        // reduction enabled).
        public double MeasurementNoiseSigma { get; set; }
```

- [ ] **Step 4: Compute σ_measure in Detect and switch the consumers**

In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetector.cs`, immediately AFTER the existing `noiseReducedNoiseEstimateTask` block (ends line ~234), add:

```csharp
                // F4 (σ consistency): thresholds applied to the image actually sampled must use that image's σ.
                // The estimate above is computed on the (possibly blurred) structure-map source; when a noise
                // reduction radius is set but measurement noise reduction is off, srcImage was never blurred and
                // its white-noise σ is ~4-5× larger. Measure it directly on srcImage (rather than applying an
                // analytic kernel factor) so correlated real-camera noise and hotpixel filtering are accounted
                // for automatically. srcImage is read-only from here until ScanStars, so the concurrent read is safe.
                var measurementImageDiffers = p.NoiseReductionRadius > 0 && !noiseReductionApplied;
                var measurementNoiseEstimateTask = measurementImageDiffers
                    ? Task.Run(() => {
                        var result = CvImageUtility.KappaSigmaNoiseEstimate(srcImage, clippingMultipler: p.NoiseClippingMultiplier);
                        var trace = $"Measurement Image K-Sigma Noise Estimate: {result.Sigma}, Background Mean: {result.BackgroundMean}, NumIterations={result.NumIterations}";
                        Logger.Trace(trace);
                        MaybeSaveIntermediateText(trace, p, "02-ksigma-estimate-measurement.txt");
                        return result;
                    })
                    : null;
```

Then, at the existing await (line ~259), extend:

```csharp
                var noiseReducedImageNoise = await noiseReducedNoiseEstimateTask;
                var measurementImageNoise = measurementNoiseEstimateTask != null
                    ? await measurementNoiseEstimateTask
                    : noiseReducedImageNoise;
```

Switch the two sharp-image consumers (the σ flows from these two arguments to the sensitivity gate `:818`, `clipMargin` `:1128`, `MeasureStar` `:625/:848`, and the contamination fallback `:1114` automatically):

- Line ~301: `var stars = ScanStars(srcImage, structureMap, p, measurementImageNoise.Sigma, metrics, token);`
- Line ~309: `await ModelPSF(srcImage, measurementImageNoise.Sigma, stars, p, metrics, token);`

The binarize threshold (line ~260) keeps `noiseReducedImageNoise.Sigma` — do not touch it.

Locate the `new HocusFocusStarDetectorResult { ... }` construction near the end of `Detect` (after the metrics trace, ~line 320-360) and add to its initializer:

```csharp
                    StructureNoiseSigma = noiseReducedImageNoise.Sigma,
                    MeasurementNoiseSigma = measurementImageNoise.Sigma,
```

- [ ] **Step 5: Recalibrate the params class defaults**

In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/IStarDetector.cs`:

Line ~206-207 — mirror the options default so the class-default bundle is self-consistent (sharp measurement + honest σ + compensated knobs):

```csharp
        // If this is true, then the source image used for star measurement has the noise reduction settings applied to it. Otherwise, noise reduction is done only on the structure map
        public bool StarMeasurementNoiseReductionEnabled { get; set; } = false;
```

Line ~217-218:

```csharp
        // Number of measurement-image noise standard deviations above the local background median to filter star
        // candidate pixels out from star consideration and HFR analysis. σ is measured on the image actually
        // sampled (F4), so the default compensates for the removed ~5× understatement (was 2.0 against a smoothed σ)
        public double StarClippingMultiplier { get; set; } = 0.4;
```

Line ~244-245:

```csharp
        // Sensitivity is the minimum value of a star's brightness (with the background n subtracted out) above the
        // noise floor (s - b)/n, with n measured on the image actually sampled (F4). Smaller values increase
        // sensitivity. The default compensates for the removed ~5× σ understatement (was 10.0 against a smoothed σ)
        public double Sensitivity { get; set; } = 2.0;
```

- [ ] **Step 6: Recalibrate StarDetectionOptions**

In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionOptions.cs`:

`InitializeOptions` (lines ~154, ~158) — new-profile fallbacks:

```csharp
            starClippingMultiplier = optionsAccessor.GetValueDouble("StarClippingMultiplier", 0.4);
```
```csharp
            brightnessSensitivity = optionsAccessor.GetValueDouble("BrightnessSensitivity", 2.0);
```

`ResetDefaults` (lines ~201, ~205):

```csharp
            StarClippingMultiplier = 0.4;
```
```csharp
            BrightnessSensitivity = 2.0;
```

`ConfigureSimpleSettings` — replace the body between `HotpixelFiltering = ...` (line 63) and `MinStarBoundingBoxSize = 5;` (line 96) with:

```csharp
            HotpixelFiltering = Simple_NoiseLevel != NoiseLevelEnum.None;
            // F4: σ-based knobs are honest multiples of the measured image's σ. The old code measured σ on a
            // blurred copy (~5× understated) on the Low/Typical path only, so the same knob value used to mean
            // ~5× different effective thresholds across noise presets. sensitivityScale compensates per preset
            // so each preset's EFFECTIVE behavior is unchanged: ×0.2 where the mismatch existed, ×1 where σ was
            // already honest (None: no blur; High: measurement noise reduction blurs the measured image itself).
            double sensitivityScale = 1.0;
            switch (Simple_NoiseLevel) {
                case NoiseLevelEnum.None:
                    StarMeasurementNoiseReductionEnabled = false;
                    NoiseReductionRadius = 0;
                    break;

                case NoiseLevelEnum.Low:
                    StarMeasurementNoiseReductionEnabled = false;
                    NoiseReductionRadius = 3;
                    sensitivityScale = 0.2;
                    break;

                case NoiseLevelEnum.Typical:
                    StarMeasurementNoiseReductionEnabled = false;
                    NoiseReductionRadius = 3;
                    sensitivityScale = 0.2;
                    break;

                case NoiseLevelEnum.High:
                    StarMeasurementNoiseReductionEnabled = true;
                    NoiseReductionRadius = 5;
                    break;
            }
            NoiseClippingMultiplier = 4; // structure-map path: σ_structure is unchanged by F4, so no rescale
            StarClippingMultiplier = 2 * sensitivityScale;
            StructureLayers = 4;
            BrightnessSensitivity = 10.0 * sensitivityScale;
            if (Simple_FocusRange == FocusRangeEnum.WideRange) {
                StructureLayers += 1;
                // As we get further from focus, we want to be more sensitive as the chance for bad data
                // increases. BrightnessSensitivity is a threshold where SMALLER = more sensitive, so we LOWER it.
                BrightnessSensitivity -= 2.0 * sensitivityScale;
            }

            MinStarBoundingBoxSize = 5;
```

And in the `LongFocalLength` branch (line ~102-108), scale its delta the same way:

```csharp
            } else if (Simple_PixelScale == PixelScaleEnum.LongFocalLength) {
                StructureLayers += 1;
                MinStarBoundingBoxSize += 1;
                // Longer focal length spreads star flux over more pixels, so we want to be more sensitive.
                // BrightnessSensitivity is a threshold where SMALLER = more sensitive, so we LOWER it.
                BrightnessSensitivity -= 2.0 * sensitivityScale;
            }
```

NOTE: `sensitivityScale` must therefore be in scope for the pixel-scale block — it is a local declared at the top of the method (it is; the switch and the pixel-scale block are in the same method body).

Validation safety: worst case Low/Typical + WideRange + LongFocalLength = 2.0 − 0.4 − 0.4 = 1.2 ≥ 0; None path worst case = 10 − 2 − 2 = 6 ≥ 0. Neither trips the non-negative `ArgumentException`.

- [ ] **Step 7: Update the step-2 options tests and add per-preset coverage**

In `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectionOptionsTests.cs`, update the two `..._IncreasesSensitivity` tests (lines ~141-186): the assertion blocks become

```csharp
            Assert.Multiple(() => {
                Assert.That(typical.BrightnessSensitivity, Is.EqualTo(2.0));
                Assert.That(longFl.BrightnessSensitivity, Is.EqualTo(1.6));
                Assert.That(longFl.BrightnessSensitivity, Is.LessThan(typical.BrightnessSensitivity));
            });
```

(and identically `wide.BrightnessSensitivity` → `1.6` in the WideRange test). Extend each test's comment with one line: `// Values are honest σ multiples after the F4 recalibration (10→2.0 baseline, deltas ×0.2).`

Then add two new tests after them (same `Build()` pattern as the file's other tests):

```csharp
        [Test]
        public void SimpleMode_NoiseLevelNone_KeepsUncompensatedSensitivity() {
            // The None preset never blurred the structure copy, so its σ was already honest — its knob values
            // must NOT be compensated (they would become ~5× more permissive than today).
            var (options, _, _) = Build();
            options.UseAdvanced = false;
            options.Simple_NoiseLevel = NoiseLevelEnum.None;
            options.Simple_PixelScale = PixelScaleEnum.Typical;
            options.Simple_FocusRange = FocusRangeEnum.Typical;
            Assert.Multiple(() => {
                Assert.That(options.BrightnessSensitivity, Is.EqualTo(10.0));
                Assert.That(options.StarClippingMultiplier, Is.EqualTo(2.0));
            });
        }

        [Test]
        public void SimpleMode_NoiseLevelHigh_KeepsUncompensatedSensitivity() {
            // High blurs the measured image itself (measurement noise reduction on), so σ was already
            // consistent — no compensation.
            var (options, _, _) = Build();
            options.UseAdvanced = false;
            options.Simple_NoiseLevel = NoiseLevelEnum.High;
            options.Simple_PixelScale = PixelScaleEnum.Typical;
            options.Simple_FocusRange = FocusRangeEnum.Typical;
            Assert.Multiple(() => {
                Assert.That(options.BrightnessSensitivity, Is.EqualTo(10.0));
                Assert.That(options.StarClippingMultiplier, Is.EqualTo(2.0));
            });
        }
```

(If `NoiseLevelEnum` is not already `using`-visible in the test file, add the needed `using NINA.Joko.Plugins.HocusFocus.StarDetection;` — it should already be present for the other enums.)

- [ ] **Step 8: Expose both σs in the focus-sweep CSV**

In `Joko.NINA.Plugins/TestApp/FocusSweepDiagnosticRunner.cs`:

In `PositionAccum` (line ~54-59), add:

```csharp
            public readonly List<double> MeasurementSigmas = new List<double>();
            public readonly List<double> StructureSigmas = new List<double>();
```

In `Accumulate` (locate the method that copies `result.Metrics` counters into the accum), add:

```csharp
            accum.MeasurementSigmas.Add(result.MeasurementNoiseSigma);
            accum.StructureSigmas.Add(result.StructureNoiseSigma);
```

In `WriteCsv` (line ~201): append `,MedianMeasurementSigma,MedianStructureSigma` to the header string, and append the two medians to each row:

```csharp
                var (mSig, _) = MedianMad(r.MeasurementSigmas);
                var (sSig, _) = MedianMad(r.StructureSigmas);
```

and add `, F(mSig), F(sSig)` to the end of the `string.Join` argument list. (New columns are appended LAST so baseline CSVs from Task 1 still align column-for-column on the shared prefix.)

- [ ] **Step 9: Run the full suite — expect GREEN, including the Task-3 pin test**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` (timeout 600000)
Expected: all pass. The critical ones:
- `Detect_DefaultKnobsOnMismatchPath_DetectsExactlyTheBrightStars` still passes (compensation works);
- the three new tests pass;
- `MeasureStar_UnderstatedSigma_InflatesHfrUnderNoise` still passes (it feeds σ explicitly).
If the pin test fails with a count below 12, the compensation under-corrects (real factor < assumed): check the Task-2 ratio printout and adjust the compensated constants (e.g. 2.0 → `10/ratio` rounded to one decimal; 0.4 → `2/ratio`) consistently across IStarDetector.cs, StarDetectionOptions.cs, and the options tests — then re-run.

- [ ] **Step 10: Commit**

```bash
git add -A Joko.NINA.Plugins
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Use measured-image sigma for measurement thresholds; recalibrate knobs (F4)

Two named estimates: sigma_structure (noise-reduced copy, binarize threshold
only) and sigma_measure (srcImage as sampled, computed in a parallel task and
reused when the images are identical). Sensitivity gate, clip margins,
MeasureStar tau, PSF noise floor, and contamination fallback now use
sigma_measure. Defaults and the Low/Typical simple presets are scaled by 0.2
(10->2.0, 2.0->0.4, preset deltas scaled) so effective behavior at defaults is
preserved; None/High presets were already consistent and keep their values.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Update the two knob tooltips

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml` (lines ~432, ~437-441)

- [ ] **Step 1: Replace `StarClippingMultiplier_Tooltip` (line ~432)**

```xml
    <TextBlock x:Key="StarClippingMultiplier_Tooltip" Text="Number of noise standard deviations above the local background median to filter star candidate pixels out from star consideration and HFR analysis. The noise level is measured on the image actually used for star measurement, so this value means the same thing regardless of noise-reduction settings. Decrease this if there are a large number of stars excluded for being degenerate and you can see many clear dim stars that are not detected." />
```

- [ ] **Step 2: Replace the `BrightnessSensitivity_Tooltip` content (lines ~437-441)**

```xml
    <TextBlock x:Key="BrightnessSensitivity_Tooltip">
        Given a star with a normalized brightness measure of<Bold>s</Bold>
        , a local background of<Bold>b</Bold>
        , and a background noise level of<Bold>n</Bold>
        , star sensitivity is measured as the ratio of the star brightness above the background relative to the background noise<Bold>(s-b)/n</Bold>
        . The noise level<Bold>n</Bold>
        is measured on the image actually used for star measurement, so this value is an honest multiple of the real noise and means the same thing regardless of noise-reduction settings. The default of 2 works well; if you find a large number of rejections due to low sensitivity, smaller values increase sensitivity</TextBlock>
```

- [ ] **Step 3: Build to validate the XAML, then commit**

Run: `rtk dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` (timeout 600000)
Expected: exit 0, `errors=0`.

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Update sigma-knob tooltips for honest measured-image semantics (F4)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: TauClipPolicy seam in MeasureStar (behavior-identical refactor)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/IStarDetector.cs` (enum above `StarDetectorParams`, property near `StarClippingMultiplier`)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetector.cs` (`MeasureStar` inner loop, :637-644)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/MeasureStarBiasTests.cs` (pin the F3-mechanism tests to `SubtractTau`)

- [ ] **Step 1: Add the enum and property**

In `IStarDetector.cs`, directly above `public class StarDetectorParams` (line ~195):

```csharp
    // How the τ clip (StarClippingMultiplier × measurement-image σ) is applied inside MeasureStar's flux sum.
    // SubtractTau subtracts τ from every surviving pixel (legacy soft-threshold; biases HFR low on stars with a
    // radial gradient — accuracy analysis F3 — but suppresses one-sided noise at large radii more aggressively).
    // GateOnly uses τ purely as an inclusion gate, matching the convention of the iterative centroid and the
    // star-parameter computation. The production default is chosen empirically — see
    // docs/sigma-consistency-design.md §3.
    public enum TauClipPolicy {
        SubtractTau,
        GateOnly
    }
```

Inside `StarDetectorParams`, after the `StarClippingMultiplier` property (line ~218):

```csharp
        // See TauClipPolicy. Applies only inside MeasureStar; the centroid and star-parameter clip sites are
        // gate-only by construction.
        public TauClipPolicy HfrTauPolicy { get; set; } = TauClipPolicy.SubtractTau;
```

- [ ] **Step 2: Refactor the MeasureStar inner loop**

In `StarDetector.cs` `MeasureStar`, replace lines ~637-644:

```csharp
                    var background = backgroundPlane.ValueAt(x, y);
                    var value = CvImageUtility.BilinearSamplePixelValue(srcImage, y: y, x: x) - background - noiseThreshold;
                    if (value > 0.0f) {
                        // Apply partial-pixel weighting at the aperture boundary (linear interpolation)
                        var apertureWeight = 1.0 - Math.Max(0.0, distance - (apertureRadius - 0.5));
                        totalWeightedDistance += apertureWeight * value * distance;
                        totalBrightness += apertureWeight * value;
                    }
```

with:

```csharp
                    var background = backgroundPlane.ValueAt(x, y);
                    var flux = CvImageUtility.BilinearSamplePixelValue(srcImage, y: y, x: x) - background;
                    if (flux > noiseThreshold) {
                        // SubtractTau preserves the legacy soft-threshold (flux − τ): identical gating, but wing
                        // pixels lose relative weight, biasing HFR low on radial gradients (F3). GateOnly keeps
                        // the full flux of surviving pixels, matching the centroid's gate-only convention.
                        var value = p.HfrTauPolicy == TauClipPolicy.SubtractTau ? flux - noiseThreshold : flux;
                        // Apply partial-pixel weighting at the aperture boundary (linear interpolation)
                        var apertureWeight = 1.0 - Math.Max(0.0, distance - (apertureRadius - 0.5));
                        totalWeightedDistance += apertureWeight * value * distance;
                        totalBrightness += apertureWeight * value;
                    }
```

(`flux − τ > 0` ⇔ `flux > τ`, so `SubtractTau` is bit-for-bit the old behavior.)

- [ ] **Step 3: Pin the F3-mechanism tests to SubtractTau**

In `MeasureStarBiasTests.cs`, the four tests that measure the subtraction mechanism must keep measuring it even after a possible default flip in Task 9. In `MeasureStar_UniformDisk_SoftThresholdBarelyChangesHfr`, `MeasureStar_GaussianSoftThreshold_BiasesHfrDownward`, `MeasureStar_GaussianSoftThreshold_RelativeBiasGrowsForFainterStars`, and `MeasureStar_UnderstatedSigma_InflatesHfrUnderNoise`, add `HfrTauPolicy = TauClipPolicy.SubtractTau` to every `new StarDetectorParams { ... }` initializer that has a non-zero `StarClippingMultiplier` (e.g. `new StarDetectorParams { AnalysisSamplingSize = 1.0f, StarClippingMultiplier = 1.0, HfrTauPolicy = TauClipPolicy.SubtractTau }`). Add `using NINA.Joko.Plugins.HocusFocus.Interfaces;` if not already present (it is, line 1).

- [ ] **Step 4: Run the full suite — behavior-identical refactor must be all green**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` (timeout 600000)
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add -A Joko.NINA.Plugins
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Add TauClipPolicy seam to MeasureStar (behavior-identical, F3 prep)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: F3 experiment — τ-policy comparison matrix

**Files:**
- Test: create `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/TauPolicyExperimentTests.cs`
- Create: `docs/sigma-consistency-f3-results.md` (the captured table)

- [ ] **Step 1: Write the experiment fixture**

```csharp
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// F3 decision evidence: compares τ policies for MeasureStar on synthetic shapes with known HFR ground
    /// truth under seeded noise. For each (policy, multiplier) cell it reports HFR bias (mean − truth) and
    /// noise (std across seeds). subtract@0.4σ is the status-quo effective behavior after the F4 recalibration;
    /// the gate-only candidates trade the radial-gradient bias against one-sided noise admission at large radii.
    /// Assertions are deliberately loose — the printed table is the product (captured into
    /// docs/sigma-consistency-f3-results.md for the decision gate).
    /// </summary>
    [TestFixture]
    public class TauPolicyExperimentTests {
        private const int Size = 81;
        private const double Cx = 40.0, Cy = 40.0;
        private const double NoiseSigmaTrue = 0.02; // honest σ of the measurement image, passed to MeasureStar
        private const int SeedBase = 9000;
        private const int Seeds = 10;

        private static readonly (string label, TauClipPolicy policy, double multiplier)[] Cells = {
            ("subtract@0.4σ", TauClipPolicy.SubtractTau, 0.4),
            ("gate@0.4σ", TauClipPolicy.GateOnly, 0.4),
            ("gate@1.0σ", TauClipPolicy.GateOnly, 1.0),
            ("gate@2.0σ", TauClipPolicy.GateOnly, 2.0),
        };

        private static Star NewStar() => new Star {
            Center = new Point2d(Cx, Cy),
            StarBoundingBox = new Rect(0, 0, Size, Size),
            Background = 0.0
        };

        private static (double bias, double std) MeasureCell(
                Func<Mat> imageFactory, double truth, TauClipPolicy policy, double multiplier) {
            var detector = new StarDetector(new AlglibAPI());
            var p = new StarDetectorParams {
                AnalysisSamplingSize = 1.0f,
                StarClippingMultiplier = multiplier,
                HfrTauPolicy = policy
            };
            var hfrs = new List<double>();
            for (int s = 0; s < Seeds; ++s) {
                using var image = imageFactory();
                SyntheticDefocusedStarImage.AddGaussianNoise(image, NoiseSigmaTrue, SeedBase + s);
                var star = NewStar();
                if (detector.MeasureStar(image, star, p, NoiseSigmaTrue)) {
                    hfrs.Add(star.HFR);
                }
            }
            Assert.That(hfrs, Has.Count.EqualTo(Seeds), "every seed must produce a measurable HFR");
            double mean = hfrs.Average();
            double std = Math.Sqrt(hfrs.Sum(h => (h - mean) * (h - mean)) / (hfrs.Count - 1));
            return (mean - truth, std);
        }

        private static void RunMatrix(string shapeLabel, Func<Mat> imageFactory, double truth) {
            TestContext.WriteLine($"--- {shapeLabel} (truth HFR={truth:F3}, noise σ={NoiseSigmaTrue}) ---");
            TestContext.WriteLine($"{"cell",-16} {"bias",10} {"std",10}");
            foreach (var (label, policy, multiplier) in Cells) {
                var (bias, std) = MeasureCell(imageFactory, truth, policy, multiplier);
                TestContext.WriteLine($"{label,-16} {bias,10:F4} {std,10:F4}");
                Assert.Multiple(() => {
                    Assert.That(bias, Is.InRange(-5.0, 5.0), $"{shapeLabel}/{label}: bias should be bounded");
                    Assert.That(std, Is.LessThan(3.0), $"{shapeLabel}/{label}: seed noise should be bounded");
                });
            }
        }

        [Test]
        public void TauPolicyMatrix_BrightGaussian() {
            const double sigma = 5.0, peak = 1.0;
            RunMatrix("bright gaussian (peak 50σn)",
                () => SyntheticGaussianStarImage.Create(Size, Size, Cx, Cy, sigma, sigma, peak, 0.0),
                HfrGroundTruth.Gaussian(sigma));
        }

        [Test]
        public void TauPolicyMatrix_FaintGaussian() {
            const double sigma = 5.0, peak = 0.16; // 8× noise σ — the faint-star regime AF cares about
            RunMatrix("faint gaussian (peak 8σn)",
                () => SyntheticGaussianStarImage.Create(Size, Size, Cx, Cy, sigma, sigma, peak, 0.0),
                HfrGroundTruth.Gaussian(sigma));
        }

        [Test]
        public void TauPolicyMatrix_FaintDonut() {
            const double inner = 8.0, outer = 14.0, peak = 0.12; // 6× noise σ defocused annulus
            RunMatrix("faint donut (peak 6σn)",
                () => SyntheticDefocusedStarImage.CreateAnnulus(Size, Size, Cx, Cy, inner, outer, peak, 0.0),
                HfrGroundTruth.Annulus(inner, outer));
        }
    }
}
```

- [ ] **Step 2: Run the experiment**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter FullyQualifiedName~TauPolicyExperimentTests` (timeout 600000)
Expected: PASS (loose bounds), with three printed tables. If a cell trips a bound, that itself is decision evidence — widen the bound only if the value is plausible (e.g. gate@0.4σ noise inflation on the faint donut), and note it.

If the per-cell numbers are not visible in rtk's output, re-run the filter via `cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo --filter FullyQualifiedName~TauPolicyExperimentTests --logger \"console;verbosity=detailed\""` to capture the TestContext tables.

- [ ] **Step 3: Capture the tables into `docs/sigma-consistency-f3-results.md`**

Create the file with: a one-paragraph header (what was measured, link to design §3), the three tables verbatim, and a short "Reading the table" note (bias closest to 0 wins on accuracy; std materially above subtract@0.4σ's is the noise-admission penalty; the faint donut row is the AF-critical regime).

- [ ] **Step 4: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/TauPolicyExperimentTests.cs \
        docs/sigma-consistency-f3-results.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Add tau-policy experiment matrix and captured results (F3 evidence)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: Checkpoint B — F3 decision gate (user picks the τ policy)

- [ ] **Step 1: Present the evidence and ask**

Show the user the three tables from `docs/sigma-consistency-f3-results.md` plus a recommendation derived from them (pick the cell with near-zero bias on the faint Gaussian AND faint donut whose std does not materially exceed subtract@0.4σ's; if gate-only's noise penalty at 0.4σ is large, prefer gate@1.0σ or gate@2.0σ). Ask the user to choose one of:

1. `gate@0.4σ` — gate-only, keep recalibrated multiplier
2. `gate@1.0σ` — gate-only, raise multiplier to 1.0
3. `gate@2.0σ` — gate-only, raise multiplier to 2.0
4. `subtract@0.4σ` — keep legacy subtraction, close F3 as won't-fix

Wait for the answer. Record it in `docs/sigma-consistency-f3-results.md` under a `## Decision` heading (one line: chosen cell + date + one-sentence rationale).

---

### Task 9: Implement the F3 winner

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/IStarDetector.cs`
- Modify (option 2/3 only): `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionOptions.cs`, `StarDetectionOptionsTests.cs`, `SigmaConsistencyDetectorTests.cs`
- Modify: `docs/sigma-consistency-f3-results.md` (already updated in Task 8)

Exactly ONE of the following branches applies. All branches end with the same suite run + commit steps.

**If the user chose `gate@0.4σ` (option 1):**

- [ ] Flip the default in `IStarDetector.cs`: `public TauClipPolicy HfrTauPolicy { get; set; } = TauClipPolicy.GateOnly;` and update the enum's doc comment last sentence to: `// The production default is GateOnly, chosen empirically — see docs/sigma-consistency-f3-results.md.`

**If the user chose `gate@1.0σ` or `gate@2.0σ` (option 2/3 — L = 1.0 or 2.0):**

- [ ] Flip the default to `GateOnly` as in option 1.
- [ ] Set `StarClippingMultiplier` to L everywhere it was recalibrated to 0.4, since the level was chosen against the honest σ and applies uniformly (this intentionally also changes the None/High preset τ — the empirically better level wins; say so at Checkpoint C):
  - `IStarDetector.cs` class default: `public double StarClippingMultiplier { get; set; } = <L>;` (update its comment's "compensates" sentence to "chosen empirically — see docs/sigma-consistency-f3-results.md").
  - `StarDetectionOptions.cs` `InitializeOptions`: `starClippingMultiplier = optionsAccessor.GetValueDouble("StarClippingMultiplier", <L>);`
  - `StarDetectionOptions.cs` `ResetDefaults`: `StarClippingMultiplier = <L>;`
  - `StarDetectionOptions.cs` `ConfigureSimpleSettings`: replace `StarClippingMultiplier = 2 * sensitivityScale;` with `StarClippingMultiplier = <L>; // uniform honest τ level, chosen empirically (F3)`.
  - Update the two `..._KeepsUncompensatedSensitivity` tests' `StarClippingMultiplier` expectation from `2.0` to `<L>` (and rename them `..._KeepsUncompensatedBrightnessSensitivity` since only the sensitivity knob remains uncompensated).
- [ ] Re-verify the Task-3 pin test still passes (τ change can shift HFR slightly but the count pins gate behavior, not τ): if `Detect_DefaultKnobsOnMismatchPath_DetectsExactlyTheBrightStars` fails, inspect which rejection counter moved (likely `TooLowHFR` or `HFRAnalysisFailed`) and re-tune the field's faint-star peak or document the new expected count with a comment explaining the τ-level change — do not silently change the expectation.

**If the user chose `subtract@0.4σ` (option 4):**

- [ ] No production change. In `StarDetector.cs`, extend the comment inside the `MeasureStar` clip block with one line: `// Decision (F3): subtraction retained deliberately — see docs/sigma-consistency-f3-results.md.`

**All branches:**

- [ ] **Run the full suite**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` (timeout 600000)
Expected: all pass (`TauPolicyExperimentTests` is policy-explicit per cell, so it is unaffected by the default flip; `MeasureStarBiasTests` was pinned to `SubtractTau` in Task 6).

- [ ] **Commit**

```bash
git add -A Joko.NINA.Plugins docs/sigma-consistency-f3-results.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Apply empirically chosen tau policy for MeasureStar (F3)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

(Adjust the subject to name the chosen policy, e.g. "Switch MeasureStar tau to gate-only at 1.0 sigma (F3)".)

---

### Task 10: Checkpoint C — after-sweeps, before/after comparison, sign-off

- [ ] **Step 1: Rebuild TestApp with the changed code**

Run: `cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"` (timeout 600000)
Expected: exit 0, `0 errors`.

- [ ] **Step 2: Run the after focus-sweeps (same AF runs as Task 1)**

```bash
./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe \
  focus-sweep --af-run "<userPathN>" --out "C:\temp\hf-sigma-step3\after\runN"
```

**Important:** the sweep loads the user's real NINA profile. If the user's profile uses simple mode, the recalibrated presets apply automatically. If it uses advanced mode with hand-tuned values, the after-sweep reflects the semantics change — note which case applies (the sweep's summary prints the params used).

- [ ] **Step 3: Build the comparison**

For each run, read `baseline/runN/focus_sweep.csv` and `after/runN/focus_sweep.csv` and produce a per-position table: `FocuserPosition | StarCount before→after | MedianHFR before→after | LowSensitivity before→after | TooLowHFR before→after`. Also note the after-CSV's `MedianMeasurementSigma / MedianStructureSigma` ratio (the real-data mismatch factor).

- [ ] **Step 4: Present to the user and ask for sign-off**

Show the tables plus a one-paragraph reading: in-focus and defocused star counts should be roughly preserved (within ~±20%); the V-curve (MedianHFR vs position) should keep its shape with the minimum at the same position; HFR absolute values may shift slightly if a gate-only τ policy was chosen. Ask: **"Sign off, or nudge the compensation constants?"**

- [ ] **Step 5 (only if counts shifted materially): nudge and loop**

If star counts at defaults dropped (real mismatch factor < assumed 5) or grew materially: compute the corrected scale from the after-CSV σ ratio (`scale = 1/ratio`), update consistently — `sensitivityScale = 0.2` in `ConfigureSimpleSettings`, the `2.0`/`0.4` defaults in `IStarDetector.cs` + `InitializeOptions` + `ResetDefaults`, and the options-test expectations (`2.0`/`1.6` and the clip values) — re-run the full suite, re-run Step 2-4, and re-present. Commit the nudge as:

```bash
git add -A Joko.NINA.Plugins
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Tune F4 compensation constants from real-data focus-sweep evidence

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 11: Roadmap housekeeping, full suite, push, PR

**Files:**
- Modify: `docs/star-detection-hfr-autofocus-accuracy-analysis.md` (§10 table, lines ~283-284)

- [ ] **Step 1: Update §10**

Replace row 2 (currently `🟡 Planned`):

```markdown
| 2. Defocus robustness (F1/F2) | ✅ Done (PR #47, merged) | WideRange sensitivity direction fixed (F2 — 10→8); TooFlat intentionally kept active during AF, documented (F1); LongFocalLength preset also made more sensitive (tuning). StructureLayers-from-HFR deferred. Plan: `defocus-robustness-plan.md`. |
```

Replace row 3 (currently `⬜ Not started`):

```markdown
| 3. σ consistency (F4, then F3) | 🟡 In review | Honest measured-image σ for all measurement-side thresholds; per-preset knob recalibration preserves effective behavior; τ semantics decided empirically (see `sigma-consistency-f3-results.md`). Plans: `sigma-consistency-{design,plan}.md`. |
```

- [ ] **Step 2: Final full suite**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` (timeout 600000)
Expected: all pass. If anything fails, fix the cause before pushing — never skip or mark expected-to-fail.

- [ ] **Step 3: Commit, push, open the PR**

```bash
git add docs/star-detection-hfr-autofocus-accuracy-analysis.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Update roadmap progress for steps 2 and 3

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
git push -u origin ghilios/sigma-consistency
```

Then create the PR (use the actual chosen F3 policy in the second bullet; attach or summarize the Task 10 before/after tables in the Verification section):

```bash
gh pr create --base develop --title "Sigma consistency (step 3): honest measurement-image sigma (F4), empirical tau semantics (F3)" --body "$(cat <<'EOF'
Step 3 of the star-detection accuracy analysis (σ consistency, findings F4 then F3). Design: `docs/sigma-consistency-design.md`; F3 evidence: `docs/sigma-consistency-f3-results.md`.

## Changes
- **F4 — honest σ (behavior-preserving at defaults):** the sensitivity gate, clip margins, MeasureStar τ, PSF noise floor, and contamination fallback now use σ measured on the image actually sampled (second parallel kappa-sigma estimate, reused when the images are identical). The structure-map binarize threshold keeps the smoothed-image σ.
- **Recalibration:** defaults and the Low/Typical simple presets scaled ×0.2 (BrightnessSensitivity 10→2.0, StarClippingMultiplier 2.0→0.4, preset deltas scaled) so effective behavior is preserved; None/High presets were already consistent and keep their values. Both σs are exposed on the detector result, in trace, and as new focus-sweep CSV columns.
- **F3 — τ semantics (empirical):** MeasureStar's τ policy decided from a synthetic bias/variance matrix (subtract vs gate-only at 0.4/1.0/2.0σ) — see the results file for the table and decision.

## Advanced-mode users (release note)
BrightnessSensitivity and StarClippingMultiplier are now honest multiples of the measured image's noise σ. Hand-tuned advanced-mode values set with noise reduction enabled (and measurement noise reduction off) should be divided by ~5 to keep prior behavior. Simple-mode presets are compensated automatically.

## Verification
- Full unit suite passes, including a cross-change pin test proving default-knob detection behavior is unchanged, σ-threading tests, and the τ-policy matrix.
- Before/after `TestApp focus-sweep` on real saved AF runs: star counts per position and V-curve shape preserved (tables reviewed at the in-plan checkpoint).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

(If `gh pr create` hits the Projects-classic GraphQL error seen on this repo, retry via `gh api -X POST repos/:owner/:repo/pulls -f title=... -f head=ghilios/sigma-consistency -f base=develop -F body=@/tmp/pr-body.md`.)

- [ ] **Step 4: Report back**

Tell the user: PR number/URL, the F3 decision applied, the final compensation constants, and the location of the before/after evidence.

---

## Self-review notes (already applied)

- **Spec coverage:** design §1 → Task 4 (two σs, skip condition, consumers, result fields, trace); §2 → Task 4 steps 5-7 + Task 5 (tooltips) + Task 10 step 5 (constants arbiter); §3 → Tasks 6-9 (seam, matrix, gate, winner); §4 → Tasks 2-4, 7 (σ-ratio, pin, threading, matrix); §5 → Tasks 1, 10 (user-provided AF runs, before/after, sign-off); §6 → Task 11 (+ release note in PR body).
- **Pin-test integrity:** the pin test is written against pre-change code (Task 3) and re-validated after the atomic σ+recalibration change (Task 4 step 9) and after the F3 winner (Task 9) — count changes must be explained, never silently re-pinned.
- **Type consistency:** `TauClipPolicy`/`HfrTauPolicy` (Tasks 6, 7, 9), `MeasurementNoiseSigma`/`StructureNoiseSigma` (Tasks 4, 10), `sensitivityScale` (Tasks 4, 10) are used with identical names throughout.
